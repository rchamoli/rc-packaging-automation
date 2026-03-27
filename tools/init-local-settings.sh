#!/bin/bash

# Initialize local.settings.json for Azure Functions API
# This script creates a local.settings.json if it doesn't exist
# Safe to run multiple times - won't overwrite existing files

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$SCRIPT_DIR/.."
API_DIR="$WORKSPACE_DIR/api"
LOCAL_SETTINGS_FILE="$API_DIR/local.settings.json"
TEMPLATE_FILE="$API_DIR/local.settings.template.json"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${GREEN}=== Initializing local.settings.json ===${NC}"

# Check if local.settings.json already exists
if [ -f "$LOCAL_SETTINGS_FILE" ]; then
    echo -e "${YELLOW}local.settings.json already exists, skipping creation${NC}"
    exit 0
fi

# Full Azurite connection string for local development
AZURITE_CONNECTION_STRING="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"

# Copy from template setting by setting, expanding values for local development
echo -e "Copying from template and configuring for local development..."

# Read template, remove _comments section, and expand STORAGE to full Azurite connection string
jq --arg azurite "$AZURITE_CONNECTION_STRING" '
    del(._comments) |
    .Values.STORAGE = $azurite
' "$TEMPLATE_FILE" > "$LOCAL_SETTINGS_FILE"

echo -e "${GREEN}✓ Created local.settings.json with Azurite configuration${NC}"
echo -e "  File: $LOCAL_SETTINGS_FILE"
echo -e "\n${YELLOW}Note: This file uses Azurite (local storage emulator)${NC}"
echo -e "To use Azure Storage, update the connection strings in local.settings.json"
