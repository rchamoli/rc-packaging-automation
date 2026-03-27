#!/usr/bin/env bash
# ============================================================================
# swa-start.sh — Build, start, and verify SWA CLI + Azure Functions + OIDC
#
# This is the single entry point for starting the full dev environment in
# headless/CI contexts (e.g. GitHub Actions agent sessions).
#
# Starts: frontend TS build, .NET API, SWA proxy, Azurite, mock OIDC provider.
#
# Usage:
#   ./scripts/swa-start.sh              # Start (build + launch + wait)
#   ./scripts/swa-start.sh --restart    # Stop → rebuild → start → wait
#   ./scripts/swa-start.sh --stop       # Stop only
#
# Options:
#   --timeout <seconds>    Readiness timeout (default: 120)
#   --interval <seconds>   Poll interval (default: 3)
#   --skip-build           Skip dotnet build (use existing binaries)
#
# Exit codes:
#   0 = SWA fully ready (start/restart) or stopped (stop)
#   1 = Build failed
#   2 = All retries exhausted — services not ready
# ============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

TIMEOUT=120
INTERVAL=3
SKIP_BUILD=false
MAX_RETRIES=3
MODE="start"    # start | restart | stop

# ── Parse arguments ──────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    --restart)    MODE="restart"; shift ;;
    --stop)       MODE="stop"; shift ;;
    --timeout)    TIMEOUT="$2"; shift 2 ;;
    --interval)   INTERVAL="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=true; shift ;;
    --retries)    MAX_RETRIES="$2"; shift 2 ;;
    *) echo "❌ Unknown option: $1"; exit 1 ;;
  esac
done

SWA_URL="http://127.0.0.1:4280"
API_URL="http://127.0.0.1:7071"
OIDC_URL="http://127.0.0.1:4200"

# ── Helper functions ─────────────────────────────────────────────────
log()  { echo "[$( date +%H:%M:%S )] $*"; }
fail() { log "❌ $*"; exit 1; }

stop_swa() {
  log "Stopping SWA, Azurite, and OIDC provider..."
  pkill -TERM -f 'swa start' 2>/dev/null || true
  pkill -TERM -f 'func host start' 2>/dev/null || true
  pkill -TERM -f 'azurite' 2>/dev/null || true
  pkill -TERM -f 'mock-oidc-provider' 2>/dev/null || true
  # Wait for ports to free up
  local wait=0
  while (( wait < 15 )); do
    if ! lsof -i :4280 -t >/dev/null 2>&1 && \
       ! lsof -i :7071 -t >/dev/null 2>&1 && \
       ! lsof -i :10000 -t >/dev/null 2>&1; then
      log "  Ports cleared"
      return 0
    fi
    # After 5s of graceful waiting, force-kill the specific leftover PIDs
    if (( wait == 5 )); then
      log "  Ports still held — sending SIGKILL to remaining holders..."
      lsof -i :4280 -t 2>/dev/null | xargs -r kill -9 2>/dev/null || true
      lsof -i :7071 -t 2>/dev/null | xargs -r kill -9 2>/dev/null || true
      lsof -i :10000 -t 2>/dev/null | xargs -r kill -9 2>/dev/null || true
      lsof -i :10001 -t 2>/dev/null | xargs -r kill -9 2>/dev/null || true
      lsof -i :10002 -t 2>/dev/null | xargs -r kill -9 2>/dev/null || true
    fi
    sleep 1
    wait=$((wait + 1))
  done
  log "  ⚠️  Ports may still be in use after 15s — will attempt start anyway"
}

build_all() {
  if $SKIP_BUILD; then
    log "Skipping builds (--skip-build)"
    return 0
  fi

  # Frontend: install deps + compile TypeScript
  log "Installing root npm dependencies..."
  if ! npm ci --prefix "$REPO_ROOT" 2>&1; then
    fail "npm ci failed"
  fi
  log "Building frontend TypeScript..."
  if ! npm run build:frontend --prefix "$REPO_ROOT"; then
    fail "Frontend TypeScript build failed"
  fi
  log "  Frontend build succeeded"

  # Backend: .NET Azure Functions
  log "Building Azure Functions..."
  if ! dotnet build api/api.csproj \
       /property:GenerateFullPaths=true \
       /consoleloggerparameters:NoSummary; then
    fail "dotnet build failed — cannot start SWA"
  fi
  log "  .NET build succeeded"

  # OIDC provider: install deps
  log "Installing mock-oidc-provider dependencies..."
  if ! npm ci --prefix "$REPO_ROOT/mock-oidc-provider" 2>&1; then
    fail "mock-oidc-provider npm ci failed"
  fi
  log "  OIDC provider deps installed"
}

start_oidc() {
  log "Starting mock OIDC provider in background..."
  cd "$REPO_ROOT/mock-oidc-provider"
  npx tsx --watch server.ts > /tmp/mock-oidc.log 2>&1 &
  OIDC_PID=$!
  cd "$REPO_ROOT"
  log "  OIDC PID: $OIDC_PID"

  sleep 2
  if ! kill -0 "$OIDC_PID" 2>/dev/null; then
    log "❌ OIDC provider exited immediately. Last output:"
    tail -20 /tmp/mock-oidc.log 2>/dev/null || true
    return 1
  fi
  return 0
}

start_swa() {
  log "Starting SWA CLI in background..."
  swa start > /tmp/swa-start.log 2>&1 &
  SWA_PID=$!
  log "  SWA PID: $SWA_PID"

  # Give it a moment to crash fast if misconfigured
  sleep 2
  if ! kill -0 "$SWA_PID" 2>/dev/null; then
    log "❌ SWA process exited immediately. Last output:"
    tail -30 /tmp/swa-start.log 2>/dev/null || true
    return 1
  fi
  return 0
}

wait_for_ready() {
  log "Waiting for services (timeout: ${TIMEOUT}s, poll: ${INTERVAL}s)..."
  local elapsed=0
  local swa_ready=false
  local functions_ready=false
  local api_ready=false
  local oidc_ready=false

  while (( elapsed < TIMEOUT )); do
    # Check SWA proxy
    if ! $swa_ready && curl -sf -o /dev/null --max-time 3 "${SWA_URL}/" 2>/dev/null; then
      swa_ready=true
      log "  ✅ SWA proxy responding (${elapsed}s)"
    fi

    # Check Functions host
    if ! $functions_ready && curl -sf -o /dev/null --max-time 3 "${API_URL}/" 2>/dev/null; then
      functions_ready=true
      log "  ✅ Functions host responding (${elapsed}s)"
    fi

    # Check OIDC provider (discovery + health)
    if ! $oidc_ready; then
      if curl -sf -o /dev/null --max-time 3 "${OIDC_URL}/.well-known/openid-configuration" 2>/dev/null && \
         curl -sf -o /dev/null --max-time 3 "${OIDC_URL}/health" 2>/dev/null; then
        oidc_ready=true
        log "  ✅ OIDC provider responding (${elapsed}s)"
      fi
    fi

    # Check API routing through proxy (end-to-end)
    if $swa_ready && $functions_ready && ! $api_ready; then
      local status
      status=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "${SWA_URL}/api/SampleBackendFunction" 2>/dev/null) || status="000"
      if [[ "$status" != "000" && "$status" != "502" && "$status" != "503" ]]; then
        api_ready=true
        log "  ✅ API routing through proxy works (${elapsed}s)"
      fi
    fi

    # All ready?
    if $swa_ready && $functions_ready && $api_ready && $oidc_ready; then
      echo ""
      log "🟢 All services ready after ${elapsed}s"
      # Brief stabilization delay — lets SWA proxy fully wire up auth routing
      # to the OIDC provider, preventing 500s on immediate /.auth/login calls.
      sleep 2
      log "   SWA:       ${SWA_URL}"
      log "   Functions: ${API_URL}"
      log "   OIDC:      ${OIDC_URL}"
      return 0
    fi

    sleep "$INTERVAL"
    elapsed=$((elapsed + INTERVAL))

    # Progress every 15s
    if (( elapsed % 15 == 0 )); then
      log "  ⏳ ${elapsed}s — SWA:$(if $swa_ready; then echo '✓'; else echo '✗'; fi) Functions:$(if $functions_ready; then echo '✓'; else echo '✗'; fi) API:$(if $api_ready; then echo '✓'; else echo '✗'; fi) OIDC:$(if $oidc_ready; then echo '✓'; else echo '✗'; fi)"
    fi

    # Check SWA process is still alive
    if ! kill -0 "$SWA_PID" 2>/dev/null; then
      echo ""
      log "❌ SWA process died during startup. Last output:"
      tail -30 /tmp/swa-start.log 2>/dev/null || true
      return 1
    fi
  done

  echo ""
  log "🔴 Timeout after ${TIMEOUT}s"
  log "   SWA proxy:      $(if $swa_ready; then echo 'UP'; else echo 'DOWN'; fi)"
  log "   Functions host:  $(if $functions_ready; then echo 'UP'; else echo 'DOWN'; fi)"
  log "   API via proxy:   $(if $api_ready; then echo 'UP'; else echo 'DOWN'; fi)"
  log "   OIDC provider:   $(if $oidc_ready; then echo 'UP'; else echo 'DOWN'; fi)"
  return 1
}

# ── Attempt a single start cycle (returns 0 on success, 1 on failure) ─
attempt_start() {
  stop_swa
  start_oidc || return 1
  start_swa || return 1
  wait_for_ready || return 1
  return 0
}

# ── Main ─────────────────────────────────────────────────────────────
case "$MODE" in
  stop)
    stop_swa
    log "Done"
    ;;
  start|restart)
    build_all
    for attempt in $(seq 1 "$MAX_RETRIES"); do
      log "━━━ Attempt ${attempt}/${MAX_RETRIES} ━━━"
      if attempt_start; then
        exit 0
      fi
      if (( attempt < MAX_RETRIES )); then
        log "⚠️  Attempt ${attempt} failed — retrying in 3s..."
        sleep 3
      fi
    done
    echo ""
    log "❌ All ${MAX_RETRIES} attempts failed. Troubleshooting:"
    echo "  lsof -i :4280        # SWA proxy"
    echo "  lsof -i :7071        # Functions host"
    echo "  lsof -i :10000-10002 # Azurite"
    echo "  tail -50 /tmp/swa-start.log"
    exit 2
    ;;
esac
