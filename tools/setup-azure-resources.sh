#!/bin/bash

# Setup Azure Resources for Agentic Software Development
# This script creates Azure resources for the project using Azure CLI

set -e

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Get repository name as default project name
REPO_NAME=$(basename "$(git rev-parse --show-toplevel 2>/dev/null)" 2>/dev/null || echo "agentic-software-development")

echo -e "${GREEN}=== Azure Resource Setup ===${NC}\n"

# 1. Ask for project name
read -p "Enter project name [${REPO_NAME}]: " PROJECT_NAME
PROJECT_NAME=${PROJECT_NAME:-$REPO_NAME}
# Convert to lowercase and replace spaces/underscores with hyphens
PROJECT_NAME=$(echo "$PROJECT_NAME" | tr '[:upper:]' '[:lower:]' | tr '_' '-' | tr ' ' '-')

echo -e "\n${YELLOW}Project Name: ${PROJECT_NAME}${NC}"

# 2. Ask for resource group name (default based on project name)
DEFAULT_RESOURCE_GROUP="rg-${PROJECT_NAME}"
read -p "Enter resource group name [${DEFAULT_RESOURCE_GROUP}]: " RESOURCE_GROUP
RESOURCE_GROUP=${RESOURCE_GROUP:-$DEFAULT_RESOURCE_GROUP}

# 3. Ask for Static Web App name (default based on project name)
DEFAULT_SWA_NAME="${PROJECT_NAME}-swa"
read -p "Enter Static Web App name [${DEFAULT_SWA_NAME}]: " SWA_NAME
SWA_NAME=${SWA_NAME:-$DEFAULT_SWA_NAME}

# 4. Ask for Storage Account name (default based on project name)
# Storage account names must be 3-24 characters, lowercase letters and numbers only
STORAGE_DEFAULT=$(echo "${PROJECT_NAME}" | tr -d '-' | cut -c1-20)st
read -p "Enter Storage Account name [${STORAGE_DEFAULT}]: " STORAGE_ACCOUNT
STORAGE_ACCOUNT=${STORAGE_ACCOUNT:-$STORAGE_DEFAULT}
# Ensure it meets storage account naming requirements
STORAGE_ACCOUNT=$(echo "$STORAGE_ACCOUNT" | tr '[:upper:]' '[:lower:]' | tr -d '-' | tr -d '_')

# Ask for Azure location
read -p "Enter Azure location [westeurope]: " LOCATION
LOCATION=${LOCATION:-westeurope}

echo -e "\n${GREEN}=== Configuration Summary ===${NC}"
echo -e "Project Name:       ${PROJECT_NAME}"
echo -e "Resource Group:     ${RESOURCE_GROUP}"
echo -e "Static Web App:     ${SWA_NAME}"
echo -e "Storage Account:    ${STORAGE_ACCOUNT}"
echo -e "Location:           ${LOCATION}"
echo ""

read -p "Proceed with resource creation? (y/n): " CONFIRM
if [[ ! $CONFIRM =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}Resource creation cancelled${NC}"
    exit 0
fi

echo -e "\n${GREEN}=== Checking Azure CLI Authentication ===${NC}"
if ! az account show &>/dev/null; then
    echo -e "${YELLOW}Not logged in to Azure. Please login...${NC}"
    # Disable the subscription selector to avoid double prompts
    az config set core.login_experience_v2=off
    az login
    
    echo -e "${YELLOW}Logged in successfully${NC}"
else
    echo -e "${GREEN}Already authenticated to Azure${NC}"
fi

# Get list of subscriptions
echo -e "\n${GREEN}=== Select Azure Subscription ===${NC}"
SUBSCRIPTIONS=$(az account list --query "[].{name:name, id:id, isDefault:isDefault}" -o tsv)

if [ -z "$SUBSCRIPTIONS" ]; then
    echo -e "${RED}No subscriptions found. Please check your Azure account.${NC}"
    exit 1
fi

# Display subscriptions with numbers
echo "Available subscriptions:"
COUNTER=1
declare -a SUB_IDS
declare -a SUB_NAMES
while IFS=$'\t' read -r name id isDefault; do
    if [ "$isDefault" = "True" ]; then
        echo -e "  ${GREEN}${COUNTER}) ${name} (${id}) [DEFAULT]${NC}"
        DEFAULT_SUB_NUM=$COUNTER
    else
        echo "  ${COUNTER}) ${name} (${id})"
    fi
    SUB_IDS[$COUNTER]=$id
    SUB_NAMES[$COUNTER]=$name
    ((COUNTER++))
done <<< "$SUBSCRIPTIONS"

# Ask user to select subscription
if [ -n "$DEFAULT_SUB_NUM" ]; then
    read -p "Select subscription number [${DEFAULT_SUB_NUM}]: " SUB_CHOICE
    SUB_CHOICE=${SUB_CHOICE:-$DEFAULT_SUB_NUM}
else
    read -p "Select subscription number: " SUB_CHOICE
fi

# Validate selection
if [ -z "${SUB_IDS[$SUB_CHOICE]}" ]; then
    echo -e "${RED}Invalid subscription selection${NC}"
    exit 1
fi

# Set the subscription
SELECTED_SUB_ID=${SUB_IDS[$SUB_CHOICE]}
SELECTED_SUB_NAME=${SUB_NAMES[$SUB_CHOICE]}

az account set --subscription "$SELECTED_SUB_ID"
echo -e "${GREEN}Using subscription: ${SELECTED_SUB_NAME}${NC}\n"

echo -e "${GREEN}=== Registering Required Resource Providers ===${NC}"
# New subscriptions may not have these providers registered
PROVIDERS=("Microsoft.Storage" "Microsoft.Web")
for PROVIDER in "${PROVIDERS[@]}"; do
    STATUS=$(az provider show --namespace "$PROVIDER" --query "registrationState" -o tsv 2>/dev/null || echo "NotRegistered")
    if [ "$STATUS" != "Registered" ]; then
        echo -e "${YELLOW}Registering ${PROVIDER}...${NC}"
        az provider register --namespace "$PROVIDER" --wait
        echo -e "${GREEN}✓ ${PROVIDER} registered${NC}"
    else
        echo -e "${GREEN}✓ ${PROVIDER} already registered${NC}"
    fi
done
echo ""

echo -e "${GREEN}=== Creating Resource Group ===${NC}"
if az group show --name "$RESOURCE_GROUP" &>/dev/null; then
    echo -e "${YELLOW}Resource group '${RESOURCE_GROUP}' already exists${NC}"
else
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" -o none
    echo -e "${GREEN}✓ Resource group created${NC}"
fi

echo -e "\n${GREEN}=== Creating Storage Account ===${NC}"
if az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    echo -e "${YELLOW}Storage account '${STORAGE_ACCOUNT}' already exists${NC}"
else
    echo -e "${YELLOW}Creating storage account '${STORAGE_ACCOUNT}'...${NC}"
    az storage account create \
        --name "$STORAGE_ACCOUNT" \
        --resource-group "$RESOURCE_GROUP" \
        --location "$LOCATION" \
        --sku Standard_LRS \
        --kind StorageV2 \
        --allow-blob-public-access false
    
    # Verify the storage account was created
    echo -e "${YELLOW}Verifying storage account creation...${NC}"
    for i in {1..12}; do
        if az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
            echo -e "${GREEN}✓ Storage account created${NC}"
            break
        fi
        if [ $i -eq 12 ]; then
            echo -e "${RED}Failed to verify storage account creation${NC}"
            exit 1
        fi
        echo -e "${YELLOW}Waiting for storage account to be available (attempt $i/12)...${NC}"
        sleep 5
    done
fi

# Get storage account connection string
STORAGE_CONNECTION_STRING=$(az storage account show-connection-string \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --query connectionString -o tsv)

echo -e "\n${GREEN}=== Creating Static Web App ===${NC}"
if az staticwebapp show --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    echo -e "${YELLOW}Static Web App '${SWA_NAME}' already exists${NC}"
    SWA_EXISTS=true
else
    az staticwebapp create \
        --name "$SWA_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --location "$LOCATION" \
        --sku Standard \
        -o none
    
    echo -e "${GREEN}✓ Static Web App created${NC}"
    SWA_EXISTS=false
fi

# Get Static Web App details
SWA_HOSTNAME=$(az staticwebapp show --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" --query "defaultHostname" -o tsv)
SWA_API_KEY=$(az staticwebapp secrets list --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" --query "properties.apiKey" -o tsv)

# Set Storage connection string as environment variable on Static Web App
echo -e "\n${GREEN}=== Configuring Static Web App Environment Variables ===${NC}"
az staticwebapp appsettings set \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --setting-names STORAGE="$STORAGE_CONNECTION_STRING" \
    -o none
echo -e "${GREEN}✓ Environment variable 'STORAGE' configured${NC}"

echo -e "\n${GREEN}=== Resource Creation Complete ===${NC}"
echo -e "\n${GREEN}Resource Details:${NC}"
echo -e "  Resource Group:     ${RESOURCE_GROUP}"
echo -e "  Static Web App:     ${SWA_NAME}"
echo -e "  SWA URL:            ${GREEN}https://${SWA_HOSTNAME}${NC}"
echo -e "  Storage Account:    ${STORAGE_ACCOUNT}"

# Generate recommended password for new Static Web Apps
if [ "$SWA_EXISTS" = false ]; then
    RECOMMENDED_PASSWORD="$(openssl rand -base64 16 | tr -d "=+/" | cut -c1-15)!"
fi

# Save configuration to file
CONFIG_FILE="${SCRIPT_DIR}/.azure-config"
cat > "$CONFIG_FILE" << EOF
# Azure Resource Configuration
# Generated: $(date)

PROJECT_NAME=${PROJECT_NAME}
RESOURCE_GROUP=${RESOURCE_GROUP}
SWA_NAME=${SWA_NAME}
STORAGE_ACCOUNT=${STORAGE_ACCOUNT}
LOCATION=${LOCATION}
SWA_HOSTNAME=${SWA_HOSTNAME}
SWA_API_KEY=${SWA_API_KEY}
EOF

# Append password to config only if it was generated (new SWA)
if [ -n "$RECOMMENDED_PASSWORD" ]; then
    echo "SWA_PASSWORD=${RECOMMENDED_PASSWORD}" >> "$CONFIG_FILE"
fi

echo -e "\n${GREEN}Configuration saved to: ${CONFIG_FILE}${NC}"

# GitHub Secret Setup Instructions
if git remote get-url origin &> /dev/null; then
    REPO_PATH=$(git remote get-url origin | sed 's/.*github\.com[:/]\(.*\)\(\.git\)\?$/\1/' | sed 's/\.git$//')
    echo -e "\n${GREEN}=== GitHub Secret Setup ===${NC}"
    echo -e "To enable GitHub Actions deployment, add the following secret:"
    echo -e "\n${YELLOW}Secret Name:${NC} AZURE_STATIC_WEB_APPS_API_TOKEN"
    echo -e "${YELLOW}Secret Value:${NC} ${SWA_API_KEY}"
    echo -e "\n${YELLOW}Set it here:${NC}"
    echo -e "https://github.com/${REPO_PATH}/settings/secrets/actions/new"
fi

# Display final summary with URL at the very end
echo -e "\n${GREEN}==================================================${NC}"
echo -e "${GREEN}Access your Static Web App at:${NC}"
echo -e "${GREEN}https://${SWA_HOSTNAME}${NC}"
if [ "$SWA_EXISTS" = false ]; then
    SWA_RESOURCE_ID="/subscriptions/${SELECTED_SUB_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.Web/staticSites/${SWA_NAME}"
    echo -e "\n${YELLOW}To enable password protection, visit:${NC}"
    echo -e "https://portal.azure.com/#@/resource${SWA_RESOURCE_ID}/configurations"
    echo -e "\nThen select 'Protect staging environments only' or 'Protect both staging and production'"
    echo -e "and set a password. Recommended password: ${YELLOW}${RECOMMENDED_PASSWORD}${NC}"
fi
echo -e "${GREEN}==================================================${NC}\n"
