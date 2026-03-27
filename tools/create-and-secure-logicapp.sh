#!/usr/bin/env bash
set -euo pipefail

echo "=== Logic App (Consumption) - Create & Secure with Multiple OAuth Policies ==="

# --- Collect inputs ---
read -rp "Resource group name: " RG
read -rp "Azure region (e.g., australiaeast) [australiaeast]: " LOC
LOC=${LOC:-australiaeast}

read -rp "Logic App name: " LA

DEFAULT_TENANT=$(az account show --query tenantId -o tsv 2>/dev/null || echo "")
read -rp "Tenant ID [${DEFAULT_TENANT}]: " TENANT_ID
TENANT_ID=${TENANT_ID:-$DEFAULT_TENANT}

DEFAULT_PROD_APPID="a10b5f61-a91d-4a0f-851f-0768c6a1be78"
DEFAULT_DEV_APPID="db10a624-301b-48f7-a187-36fc6f07967f"

read -rp "Prod Managed Identity clientId [${DEFAULT_PROD_APPID}]: " APPID_PROD
APPID_PROD=${APPID_PROD:-$DEFAULT_PROD_APPID}

read -rp "Dev Managed Identity clientId [${DEFAULT_DEV_APPID}]: " APPID_DEV
APPID_DEV=${APPID_DEV:-$DEFAULT_DEV_APPID}

read -rp "Audience (aud) [https://management.azure.com]: " AUD
AUD=${AUD:-https://management.azure.com}

echo
echo "Resource Group : $RG"
echo "Location       : $LOC"
echo "Logic App      : $LA"
echo "Tenant ID      : $TENANT_ID"
echo "Prod appid     : $APPID_PROD"
echo "Dev appid      : $APPID_DEV"
echo "Audience       : $AUD"
echo

az account show >/dev/null

# --- Create RG if needed ---
echo "➡️  Ensuring resource group exists..."
if az group show -n "$RG" &>/dev/null; then
  EXISTING_LOC=$(az group show -n "$RG" --query "location" -o tsv)
  echo "ℹ️  Resource group already exists in $EXISTING_LOC — reusing it."
else
  az group create -n "$RG" -l "$LOC" >/dev/null
fi

# --- Create minimal Logic App ---
DEF_FILE="$(mktemp)"
cat > "$DEF_FILE" <<'JSON'
{
  "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {},
  "triggers": {
    "manual": {
      "type": "Request",
      "kind": "Http",
      "inputs": { "schema": { "type": "object" } }
    }
  },
  "actions": {
    "Response": {
      "type": "Response",
      "kind": "Http",
      "runAfter": {},
      "inputs": {
        "statusCode": 200,
        "body": { "ok": true }
      }
    }
  },
  "outputs": {}
}
JSON

PROP_FILE="$(mktemp)"
cat > "$PROP_FILE" <<JSON
{
  "definition": $(cat "$DEF_FILE"),
  "parameters": {},
  "state": "Enabled"
}
JSON

echo "➡️  Creating Logic App workflow..."
az resource create \
  -g "$RG" \
  -n "$LA" \
  -l "$LOC" \
  --resource-type "Microsoft.Logic/workflows" \
  --properties @"$PROP_FILE" >/dev/null

# --- Build accessControl block ---
ISSUER="https://sts.windows.net/${TENANT_ID}/"
ACCESS_CONTROL_JSON="$(cat <<EOF
{
  "triggers": {
    "sasAuthenticationPolicy": {
      "state": "Disabled"
    },
    "openAuthenticationPolicies": {
      "policies": {
        "fun-checklists-only": {
          "type": "AAD",
          "claims": [
            { "name": "iss", "value": "${ISSUER}" },
            { "name": "aud", "value": "${AUD}" },
            { "name": "appid", "value": "${APPID_PROD}" }
          ]
        },
        "local-dev": {
          "type": "AAD",
          "claims": [
            { "name": "iss", "value": "${ISSUER}" },
            { "name": "aud", "value": "${AUD}" },
            { "name": "appid", "value": "${APPID_DEV}" }
          ]
        }
      }
    }
  }
}
EOF
)"

echo "➡️  Applying SAS disable + multiple OAuth policies..."
az resource update \
  -g "$RG" \
  -n "$LA" \
  --resource-type "Microsoft.Logic/workflows" \
  --set "properties.accessControl=${ACCESS_CONTROL_JSON}" >/dev/null

echo "➡️  Verification:"
az resource show \
  -g "$RG" \
  -n "$LA" \
  --resource-type "Microsoft.Logic/workflows" \
  --query "{sasPolicy:properties.accessControl.triggers.sasAuthenticationPolicy, policies:properties.accessControl.triggers.openAuthenticationPolicies.policies}" \
  -o json

echo
echo "✅ Done."
echo "Your Logic App now has:"
echo " - SAS disabled"
echo " - OAuth policy 'fun-checklists-only' (Prod MI: ${APPID_PROD})"
echo " - OAuth policy 'local-dev' (Dev MI: ${APPID_DEV})"
echo
echo "Each policy accepts tokens with:"
echo "   iss = ${ISSUER}"
echo "   aud = ${AUD}"
