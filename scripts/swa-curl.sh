#!/usr/bin/env bash
# swa-curl — curl wrapper with SWA authentication pre-injected.
#
# Eliminates the need to manually construct and inject
# StaticWebAppsAuthCookie or x-ms-client-principal headers.
#
# Usage:
#   scripts/swa-curl.sh [--user PERSONA] [curl args...]
#
# Personas (from users.json):
#   packager — Kai Patel          (authenticated)
#   appowner — Lisa van der Berg  (manager + authenticated)
#   qa       — Sam Okoye          (authenticated)
#
# Default persona: packager
#
# Examples:
#   scripts/swa-curl.sh http://127.0.0.1:4280/api/health
#   scripts/swa-curl.sh --user standard http://127.0.0.1:4280/api/my-data
#   scripts/swa-curl.sh --user manager -X POST -d '{}' http://127.0.0.1:4280/api/projects
#   scripts/swa-curl.sh http://127.0.0.1:7071/api/health   # direct Functions port
#
# How it works:
#   - Port 4280 (SWA proxy): injects StaticWebAppsAuthCookie
#   - Port 7071 (Functions direct): injects x-ms-client-principal header
#   - Any other URL: injects the cookie (SWA proxy assumed)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
USERS_JSON="$SCRIPT_DIR/../users.json"

# ── Parse --user flag ────────────────────────────────────────────────
PERSONA="packager"
CURL_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --user)
      PERSONA="$2"
      shift 2
      ;;
    --user=*)
      PERSONA="${1#--user=}"
      shift
      ;;
    *)
      CURL_ARGS+=("$1")
      shift
      ;;
  esac
done

# ── Resolve persona to clientPrincipal ───────────────────────────────
if [[ ! -f "$USERS_JSON" ]]; then
  echo "ERROR: users.json not found at $USERS_JSON" >&2
  exit 1
fi

USER_ID="user-${PERSONA}"

# Extract user data from users.json using node (always available in devcontainer)
PRINCIPAL_JSON=$(node -e "
  const users = require('$USERS_JSON');
  const u = users.find(u => u.userId === '$USER_ID');
  if (!u) { console.error('Unknown persona: $PERSONA (looked for userId=$USER_ID)'); process.exit(1); }
  const p = {
    identityProvider: 'mockoidc',
    userId: u.userId,
    userDetails: u.email,
    userRoles: u.roles,
    claims: [{ typ: 'name', val: u.displayName }]
  };
  process.stdout.write(JSON.stringify(p));
")

PRINCIPAL_B64=$(echo -n "$PRINCIPAL_JSON" | base64 -w0)

# ── Detect target port and inject auth accordingly ───────────────────
# Check if any curl arg targets port 7071 (direct Azure Functions)
USES_7071=false
for arg in "${CURL_ARGS[@]}"; do
  if [[ "$arg" == *":7071"* ]]; then
    USES_7071=true
    break
  fi
done

if [[ "$USES_7071" == "true" ]]; then
  # Direct Functions port — use x-ms-client-principal header
  exec curl -sS --fail-with-body -H "x-ms-client-principal: $PRINCIPAL_B64" "${CURL_ARGS[@]}"
else
  # SWA proxy — use cookie
  exec curl -sS --fail-with-body -b "StaticWebAppsAuthCookie=$PRINCIPAL_B64" "${CURL_ARGS[@]}"
fi
