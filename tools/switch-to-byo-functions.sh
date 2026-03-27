#!/bin/bash

# Setup Azure Function App with Flex Consumption Plan
# This script creates an Azure Function App with:
# - Flex Consumption hosting plan
# - System Assigned Managed Identity
# - Dedicated Storage Account accessed via Managed Identity

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Azure Function App Setup (Flex Consumption) ===${NC}\n"

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Get the project root (parent of tools directory)
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Load existing configuration if available
CONFIG_FILE="${SCRIPT_DIR}/.azure-config"
if [ -f "$CONFIG_FILE" ]; then
    source "$CONFIG_FILE"
    echo -e "${GREEN}Loaded existing configuration from ${CONFIG_FILE}${NC}"
else
    echo -e "${RED}Configuration file not found. Please run setup-azure-resources.sh first.${NC}"
    exit 1
fi

# 1. Ask for Function App name (default based on project name)
DEFAULT_FUNCTION_NAME="fun-${PROJECT_NAME}"
read -p "Enter Function App name [${DEFAULT_FUNCTION_NAME}]: " FUNCTION_APP_NAME
FUNCTION_APP_NAME=${FUNCTION_APP_NAME:-$DEFAULT_FUNCTION_NAME}

# 2. Ask for Function Storage Account name (default based on function app name)
# Storage account names must be 3-24 characters, lowercase letters and numbers only
FUNCTION_STORAGE_DEFAULT=$(echo "${FUNCTION_APP_NAME}" | tr -d '-' | cut -c1-20)st
read -p "Enter Function Storage Account name [${FUNCTION_STORAGE_DEFAULT}]: " FUNCTION_STORAGE_ACCOUNT
FUNCTION_STORAGE_ACCOUNT=${FUNCTION_STORAGE_ACCOUNT:-$FUNCTION_STORAGE_DEFAULT}
# Ensure it meets storage account naming requirements
FUNCTION_STORAGE_ACCOUNT=$(echo "$FUNCTION_STORAGE_ACCOUNT" | tr '[:upper:]' '[:lower:]' | tr -d '-' | tr -d '_')

echo -e "\n${GREEN}=== Configuration Summary ===${NC}"
echo -e "Project Name:           ${PROJECT_NAME}"
echo -e "Resource Group:         ${RESOURCE_GROUP}"
echo -e "Location:               ${LOCATION}"
echo -e "Function App:           ${FUNCTION_APP_NAME}"
echo -e "Function Storage:       ${FUNCTION_STORAGE_ACCOUNT}"
echo ""

read -p "Proceed with Function App creation? (y/n): " CONFIRM
if [[ ! $CONFIRM =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}Function App creation cancelled${NC}"
    exit 0
fi

echo -e "\n${GREEN}=== Checking Azure CLI Authentication ===${NC}"
if ! az account show &>/dev/null; then
    echo -e "${YELLOW}Not logged in to Azure. Please login...${NC}"
    AZURE_CORE_ONLY_SHOW_ERRORS=true az login
    echo -e "${YELLOW}Logged in successfully${NC}"
else
    echo -e "${GREEN}Already authenticated to Azure${NC}"
fi

echo -e "\n${GREEN}=== Creating Function Storage Account ===${NC}"
if az storage account show --name "$FUNCTION_STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    echo -e "${YELLOW}Storage account '${FUNCTION_STORAGE_ACCOUNT}' already exists${NC}"
else
    az storage account create \
        --name "$FUNCTION_STORAGE_ACCOUNT" \
        --resource-group "$RESOURCE_GROUP" \
        --location "$LOCATION" \
        --sku Standard_LRS \
        --kind StorageV2 \
        --allow-blob-public-access false \
        -o none
    echo -e "${GREEN}✓ Storage account created${NC}"
    
    # Wait for storage account to be fully provisioned
    echo -ne "${YELLOW}Waiting for storage account to be fully provisioned (15s)... ${NC}"
    for i in {1..3}; do
        echo -n "."
        sleep 5
    done
    echo -e " ${GREEN}done${NC}"
fi

echo -e "\n${GREEN}=== Creating Function App (Flex Consumption) ===${NC}"
if az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    echo -e "${YELLOW}Function App '${FUNCTION_APP_NAME}' already exists${NC}"
    FUNCTION_EXISTS=true
else
    az functionapp create \
        --name "$FUNCTION_APP_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --storage-account "$FUNCTION_STORAGE_ACCOUNT" \
        --runtime dotnet-isolated \
        --runtime-version 10 \
        --functions-version 4 \
        --flexconsumption-location "$LOCATION" \
        -o none
    
    echo -e "${GREEN}✓ Function App created${NC}"
    FUNCTION_EXISTS=false
    
    # Wait for function app to be fully provisioned
    echo -ne "${YELLOW}Waiting for Function App to be fully provisioned (20s)... ${NC}"
    for i in {1..4}; do
        echo -n "."
        sleep 5
    done
    echo -e " ${GREEN}done${NC}"
fi

echo -e "\n${GREEN}=== Enabling System Assigned Managed Identity ===${NC}"
PRINCIPAL_ID=$(az functionapp identity assign \
    --name "$FUNCTION_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query principalId -o tsv)

if [ -n "$PRINCIPAL_ID" ]; then
    echo -e "${GREEN}✓ Managed Identity enabled (Principal ID: ${PRINCIPAL_ID})${NC}"
else
    echo -e "${RED}Failed to enable Managed Identity${NC}"
    exit 1
fi

# Wait for identity to propagate
echo -ne "${YELLOW}Waiting for Managed Identity to propagate (30s)... ${NC}"
for i in {1..6}; do
    echo -n "."
    sleep 5
done
echo -e " ${GREEN}done${NC}"

echo -e "\n${GREEN}=== Configuring Storage Account Access via Managed Identity ===${NC}"

# Get the storage account resource ID
STORAGE_RESOURCE_ID=$(az storage account show \
    --name "$FUNCTION_STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --query id -o tsv)

# Assign Storage Blob Data Owner role to the Function App's managed identity
echo -e "${YELLOW}Assigning 'Storage Blob Data Owner' role...${NC}"
if az role assignment create \
    --assignee "$PRINCIPAL_ID" \
    --role "Storage Blob Data Owner" \
    --scope "$STORAGE_RESOURCE_ID" \
    -o none 2>/dev/null; then
    echo -e "${GREEN}✓ Storage Blob Data Owner role assigned${NC}"
    ROLE_ASSIGNMENT_SUCCESS=true
else
    echo -e "${RED}✗ Could not assign role automatically (likely Conditional Access policy)${NC}"
    ROLE_ASSIGNMENT_SUCCESS=false
fi

# Assign Storage Queue Data Contributor role to the Function App's managed identity
echo -e "${YELLOW}Assigning 'Storage Queue Data Contributor' role...${NC}"
if az role assignment create \
    --assignee "$PRINCIPAL_ID" \
    --role "Storage Queue Data Contributor" \
    --scope "$STORAGE_RESOURCE_ID" \
    -o none 2>/dev/null; then
    echo -e "${GREEN}✓ Storage Queue Data Contributor role assigned${NC}"
else
    echo -e "${RED}✗ Could not assign role automatically${NC}"
    ROLE_ASSIGNMENT_SUCCESS=false
fi

# Assign Storage Table Data Contributor role to the Function App's managed identity
echo -e "${YELLOW}Assigning 'Storage Table Data Contributor' role...${NC}"
if az role assignment create \
    --assignee "$PRINCIPAL_ID" \
    --role "Storage Table Data Contributor" \
    --scope "$STORAGE_RESOURCE_ID" \
    -o none 2>/dev/null; then
    echo -e "${GREEN}✓ Storage Table Data Contributor role assigned${NC}"
else
    echo -e "${RED}✗ Could not assign role automatically${NC}"
    ROLE_ASSIGNMENT_SUCCESS=false
fi

# If role assignments failed, provide manual instructions
if [ "$ROLE_ASSIGNMENT_SUCCESS" = false ]; then
    echo -e "\n${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${YELLOW}⚠️  MANUAL ACTION REQUIRED: Assign Storage Roles${NC}"
    echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "\n${RED}Role assignments failed (likely due to Conditional Access policies).${NC}"
    echo -e "${YELLOW}You must manually assign roles in the Azure Portal:${NC}\n"
    echo -e "${GREEN}Option 1: Azure Portal (Easiest)${NC}"
    echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo -e "1. Go to Storage Account:"
    echo -e "   ${GREEN}https://portal.azure.com/#@/resource${STORAGE_RESOURCE_ID}${NC}"
    echo -e "\n2. Click 'Access Control (IAM)' in the left menu"
    echo -e "\n3. Click '+ Add' → 'Add role assignment'"
    echo -e "\n4. Assign these THREE roles to the Function App's Managed Identity:"
    echo -e "   ${GREEN}Role:${NC} Storage Blob Data Owner"
    echo -e "   ${GREEN}Assign access to:${NC} Managed Identity"
    echo -e "   ${GREEN}Members:${NC} Select '${FUNCTION_APP_NAME}'"
    echo -e "   Click 'Review + assign'"
    echo -e "\n   ${GREEN}Role:${NC} Storage Queue Data Contributor"
    echo -e "   ${GREEN}Assign access to:${NC} Managed Identity"
    echo -e "   ${GREEN}Members:${NC} Select '${FUNCTION_APP_NAME}'"
    echo -e "   Click 'Review + assign'"
    echo -e "\n   ${GREEN}Role:${NC} Storage Table Data Contributor"
    echo -e "   ${GREEN}Assign access to:${NC} Managed Identity"
    echo -e "   ${GREEN}Members:${NC} Select '${FUNCTION_APP_NAME}'"
    echo -e "   Click 'Review + assign'"
    echo -e "\n${GREEN}Option 2: Azure CLI (After re-authentication)${NC}"
    echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo -e "Run these commands after logging in with appropriate scope:\n"
    echo -e "${YELLOW}az logout${NC}"
    echo -e "${YELLOW}az login --scope https://graph.microsoft.com//.default${NC}\n"
    echo -e "${YELLOW}az role assignment create \\${NC}"
    echo -e "${YELLOW}  --assignee \"${PRINCIPAL_ID}\" \\${NC}"
    echo -e "${YELLOW}  --role \"Storage Blob Data Owner\" \\${NC}"
    echo -e "${YELLOW}  --scope \"${STORAGE_RESOURCE_ID}\"${NC}\n"
    echo -e "${YELLOW}az role assignment create \\${NC}"
    echo -e "${YELLOW}  --assignee \"${PRINCIPAL_ID}\" \\${NC}"
    echo -e "${YELLOW}  --role \"Storage Queue Data Contributor\" \\${NC}"
    echo -e "${YELLOW}  --scope \"${STORAGE_RESOURCE_ID}\"${NC}\n"
    echo -e "${YELLOW}az role assignment create \\${NC}"
    echo -e "${YELLOW}  --assignee \"${PRINCIPAL_ID}\" \\${NC}"
    echo -e "${YELLOW}  --role \"Storage Table Data Contributor\" \\${NC}"
    echo -e "${YELLOW}  --scope \"${STORAGE_RESOURCE_ID}\"${NC}\n"
    echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${RED}⚠️  The Function App will NOT work until these roles are assigned!${NC}"
    echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}\n"
    
    read -p "Have you assigned the roles manually? Continue anyway? (y/n): " CONTINUE_ANYWAY
    if [[ ! $CONTINUE_ANYWAY =~ ^[Yy]$ ]]; then
        echo -e "${RED}Setup cancelled. Please assign the roles and re-run this script.${NC}"
        exit 1
    fi
fi

# Copy environment variables from Static Web App to Function App
echo -e "\n${GREEN}=== Copying Environment Variables from Static Web App ===${NC}"

# Get environment variables from Static Web App
SWA_SETTINGS=$(az staticwebapp appsettings list \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query "properties" -o json)

if [ "$SWA_SETTINGS" != "{}" ] && [ "$SWA_SETTINGS" != "null" ]; then
    # Convert JSON to space-separated key=value pairs for az functionapp config
    SETTINGS_ARRAY=()
    while IFS= read -r key; do
        value=$(echo "$SWA_SETTINGS" | jq -r --arg key "$key" '.[$key]')
        SETTINGS_ARRAY+=("${key}=${value}")
        echo -e "  ${YELLOW}Copying:${NC} ${key}"
    done < <(echo "$SWA_SETTINGS" | jq -r 'keys[]')
    
    if [ ${#SETTINGS_ARRAY[@]} -gt 0 ]; then
        # Set the environment variables on the Function App
        az functionapp config appsettings set \
            --name "$FUNCTION_APP_NAME" \
            --resource-group "$RESOURCE_GROUP" \
            --settings "${SETTINGS_ARRAY[@]}" \
            -o none
        echo -e "${GREEN}✓ Copied ${#SETTINGS_ARRAY[@]} environment variable(s) from Static Web App${NC}"
    fi
else
    echo -e "${YELLOW}No environment variables found in Static Web App${NC}"
fi

# Configure the Function App to use Managed Identity for storage
echo -e "\n${GREEN}=== Configuring Function App to use Managed Identity ===${NC}"

# For Managed Identity, we need to set the credential type and all service URIs
# Reference: https://learn.microsoft.com/en-us/azure/azure-functions/functions-identity-based-connections-tutorial
az functionapp config appsettings set \
    --name "$FUNCTION_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --settings \
        "AzureWebJobsStorage__accountName=${FUNCTION_STORAGE_ACCOUNT}" \
        "AzureWebJobsStorage__credential=managedidentity" \
        "AzureWebJobsStorage__blobServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.blob.core.windows.net" \
        "AzureWebJobsStorage__queueServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.queue.core.windows.net" \
        "AzureWebJobsStorage__tableServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.table.core.windows.net" \
    -o none

echo -e "${GREEN}✓ Function App configured to use Managed Identity for storage${NC}"
echo -e "  ${YELLOW}Settings configured:${NC}"
echo -e "    AzureWebJobsStorage__accountName=${FUNCTION_STORAGE_ACCOUNT}"
echo -e "    AzureWebJobsStorage__credential=managedidentity"
echo -e "    AzureWebJobsStorage__blobServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.blob.core.windows.net"
echo -e "    AzureWebJobsStorage__queueServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.queue.core.windows.net"
echo -e "    AzureWebJobsStorage__tableServiceUri=https://${FUNCTION_STORAGE_ACCOUNT}.table.core.windows.net"

# Get Function App details
echo -e "\n${GREEN}=== Retrieving Function App Details ===${NC}"
FUNCTION_HOSTNAME=$(az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" --query "defaultHostName" -o tsv)

# Flex Consumption apps may take time to provision the hostname
if [ -z "$FUNCTION_HOSTNAME" ]; then
    echo -e "${YELLOW}Hostname not available yet. Waiting for Function App to be fully provisioned...${NC}"
    sleep 30
    FUNCTION_HOSTNAME=$(az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" --query "defaultHostName" -o tsv)
fi

if [ -z "$FUNCTION_HOSTNAME" ]; then
    echo -e "${YELLOW}Warning: Hostname still not available. It may take a few minutes to provision.${NC}"
    FUNCTION_HOSTNAME="<will-be-available-soon>"
else
    echo -e "${GREEN}✓ Function hostname: ${FUNCTION_HOSTNAME}${NC}"
fi

# Get deployment credentials for GitHub Actions (Flex Consumption uses publish profile via REST API)
echo -e "\n${GREEN}=== Retrieving Deployment Credentials ===${NC}"
echo -e "${YELLOW}Note: Flex Consumption plans require publish profile from Azure Portal/API${NC}"

# Get subscription ID for REST API call
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Get publish profile using Azure REST API (same as Portal "Get publish profile" button)
PUBLISH_PROFILE=$(az rest \
    --method post \
    --uri "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.Web/sites/${FUNCTION_APP_NAME}/publishxml?api-version=2022-03-01" \
    --headers "Content-Type=application/json" \
    --output tsv 2>/dev/null)

if [ -z "$PUBLISH_PROFILE" ] || [ "$PUBLISH_PROFILE" = "null" ]; then
    echo -e "${RED}✗ Could not retrieve publish profile automatically${NC}"
    echo -e "\n${YELLOW}Please retrieve it manually from Azure Portal:${NC}"
    echo -e "1. Go to: ${GREEN}https://portal.azure.com/#@/resource/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.Web/sites/${FUNCTION_APP_NAME}${NC}"
    echo -e "2. Click '${GREEN}Get publish profile${NC}' button at the top"
    echo -e "3. Save the downloaded file and copy its contents\n"
    read -p "Press Enter after you have the publish profile ready..."
    echo -e "\n${YELLOW}Please paste the publish profile content (press Ctrl+D when done):${NC}"
    PUBLISH_PROFILE=$(cat)
fi

echo -e "${GREEN}✓ Publish profile retrieved${NC}"

echo -e "\n${GREEN}=== Function App Creation Complete ===${NC}"
echo -e "\n${GREEN}Resource Details:${NC}"
echo -e "  Function App:           ${FUNCTION_APP_NAME}"
echo -e "  Function URL:           ${GREEN}https://${FUNCTION_HOSTNAME}${NC}"
echo -e "  Function Storage:       ${FUNCTION_STORAGE_ACCOUNT}"
echo -e "  Managed Identity:       ${GREEN}Enabled${NC}"
echo -e "  Principal ID:           ${PRINCIPAL_ID}"

# Update configuration file
cat >> "$CONFIG_FILE" << EOF

# Azure Function App Configuration
# Added: $(date)
FUNCTION_APP_NAME=${FUNCTION_APP_NAME}
FUNCTION_STORAGE_ACCOUNT=${FUNCTION_STORAGE_ACCOUNT}
FUNCTION_HOSTNAME=${FUNCTION_HOSTNAME}
PRINCIPAL_ID=${PRINCIPAL_ID}
EOF

echo -e "\n${GREEN}Configuration updated in: ${CONFIG_FILE}${NC}"

# Switch workflow files to BYO (Bring Your Own) Functions approach
echo -e "\n${GREEN}=== Configuring GitHub Actions Workflows ===${NC}"

WORKFLOWS_DIR="${PROJECT_ROOT}/.github/workflows"
COMBINED_WORKFLOW="${WORKFLOWS_DIR}/azure-static-web-apps.yml"
FRONTEND_WORKFLOW="${WORKFLOWS_DIR}/azure-static-web-apps-frontend.yml"
API_WORKFLOW="${WORKFLOWS_DIR}/azure-functions-byo.yml"

# Disable the combined workflow (if it exists and is enabled)
if [ -f "$COMBINED_WORKFLOW" ]; then
    mv "$COMBINED_WORKFLOW" "${COMBINED_WORKFLOW}.disabled"
    echo -e "${GREEN}✓ Disabled combined workflow (azure-static-web-apps.yml)${NC}"
fi

# Enable the frontend workflow (remove .disabled if present)
if [ -f "${FRONTEND_WORKFLOW}.disabled" ]; then
    mv "${FRONTEND_WORKFLOW}.disabled" "$FRONTEND_WORKFLOW"
    echo -e "${GREEN}✓ Enabled frontend workflow (azure-static-web-apps-frontend.yml)${NC}"
elif [ -f "$FRONTEND_WORKFLOW" ]; then
    echo -e "${YELLOW}Frontend workflow already enabled${NC}"
fi

# Enable the API workflow (remove .disabled if present)
if [ -f "${API_WORKFLOW}.disabled" ]; then
    mv "${API_WORKFLOW}.disabled" "$API_WORKFLOW"
    echo -e "${GREEN}✓ Enabled API workflow (azure-functions-byo.yml)${NC}"
elif [ -f "$API_WORKFLOW" ]; then
    echo -e "${YELLOW}API workflow already enabled${NC}"
fi

echo -e "${GREEN}✓ Workflow configuration complete${NC}"

# Update workflow files with actual resource names
echo -e "\n${GREEN}=== Updating Workflow Configuration ===${NC}"

# Update API workflow with resource names
if [ -f "$API_WORKFLOW" ]; then
    sed -i "s/AZURE_FUNCTIONAPP_NAME: '.*'/AZURE_FUNCTIONAPP_NAME: '${FUNCTION_APP_NAME}'/" "$API_WORKFLOW"
    sed -i "s/AZURE_RESOURCE_GROUP: '.*'/AZURE_RESOURCE_GROUP: '${RESOURCE_GROUP}'/" "$API_WORKFLOW"
    echo -e "${GREEN}✓ Updated azure-functions-byo.yml with your resource names${NC}"
fi

# GitHub Secret Setup Instructions
if git -C "$PROJECT_ROOT" remote get-url origin &> /dev/null; then
    REPO_PATH=$(git -C "$PROJECT_ROOT" remote get-url origin | sed 's/.*github\.com[:/]\(.*\)\(\.git\)\?$/\1/' | sed 's/\.git$//')
    
    echo -e "\n${GREEN}=== GitHub Secret Setup Required ===${NC}"
    echo -e "${YELLOW}IMPORTANT: Add this secret to GitHub before deploying:${NC}\n"
    echo -e "${YELLOW}Secret Name:${NC} ${GREEN}AZURE_FUNCTIONAPP_PUBLISH_PROFILE${NC}"
    echo -e "\n${YELLOW}Steps to add the secret:${NC}"
    echo -e "1. Go to: ${GREEN}https://github.com/${REPO_PATH}/settings/secrets/actions/new${NC}"
    echo -e "2. Name: ${GREEN}AZURE_FUNCTIONAPP_PUBLISH_PROFILE${NC}"
    echo -e "3. Value: Copy the publish profile below"
    echo -e "4. Click 'Add secret'\n"
    echo -e "${YELLOW}Publish Profile (copy everything below):${NC}"
    echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo "$PUBLISH_PROFILE"
    echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
fi

# Get subscription ID for portal URLs
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Check for uncommitted changes
echo -e "\n${GREEN}=== Checking Git Status ===${NC}"
if git -C "$PROJECT_ROOT" diff --quiet .github/workflows/ && git -C "$PROJECT_ROOT" diff --cached --quiet .github/workflows/; then
    echo -e "${GREEN}✓ Workflow files are committed${NC}"
    WORKFLOWS_COMMITTED=true
else
    echo -e "${YELLOW}⚠️  Workflow files have uncommitted changes${NC}"
    echo -e "${YELLOW}The workflows must be committed and pushed to GitHub before they can run.${NC}\n"
    WORKFLOWS_COMMITTED=false
    
    echo -e "${YELLOW}Files changed:${NC}"
    git -C "$PROJECT_ROOT" status --short .github/workflows/
    echo ""
    
    read -p "Do you want to commit and push the workflow changes now? (y/n): " COMMIT_NOW
    if [[ $COMMIT_NOW =~ ^[Yy]$ ]]; then
        git -C "$PROJECT_ROOT" add .github/workflows/
        git -C "$PROJECT_ROOT" commit -m "Switch to BYO Functions: Enable separate API and frontend workflows"
        
        echo -e "${YELLOW}Pushing to GitHub...${NC}"
        if git -C "$PROJECT_ROOT" push; then
            echo -e "${GREEN}✓ Workflows committed and pushed to GitHub${NC}"
            WORKFLOWS_COMMITTED=true
            sleep 5  # Give GitHub a moment to register the workflows
        else
            echo -e "${RED}✗ Failed to push. Please push manually before triggering workflows.${NC}"
            WORKFLOWS_COMMITTED=false
        fi
    else
        echo -e "${YELLOW}Skipping commit. You'll need to commit and push manually:${NC}"
        echo -e "${YELLOW}git add .github/workflows/${NC}"
        echo -e "${YELLOW}git commit -m \"Switch to BYO Functions\"${NC}"
        echo -e "${YELLOW}git push${NC}\n"
    fi
fi

# Trigger the frontend workflow to remove managed functions
echo -e "\n${GREEN}=== Deploying Frontend (Removing Managed Functions) ===${NC}"
echo -e "${YELLOW}The frontend workflow needs to run to remove the managed function from the Static Web App.${NC}"
echo -e "${YELLOW}This will allow proper linking to the new BYO Function App.${NC}\n"

if [ "$WORKFLOWS_COMMITTED" = false ]; then
    echo -e "${RED}⚠️  Cannot trigger workflow - changes not committed to GitHub${NC}"
    echo -e "${YELLOW}Please commit and push the workflow files first, then trigger manually:${NC}"
    echo -e "https://github.com/${REPO_PATH}/actions/workflows/azure-static-web-apps-frontend.yml"
else
    read -p "Do you want to trigger the frontend deployment now? (y/n): " TRIGGER_DEPLOY
    if [[ $TRIGGER_DEPLOY =~ ^[Yy]$ ]]; then
        # Check if gh CLI is available
        if command -v gh &> /dev/null; then
            echo -e "${YELLOW}Triggering frontend workflow...${NC}"
            gh workflow run azure-static-web-apps-frontend.yml --repo "${REPO_PATH}" 2>/dev/null && \
                echo -e "${GREEN}✓ Frontend workflow triggered successfully${NC}" || \
                echo -e "${RED}Failed to trigger workflow. You may need to authenticate with: gh auth login${NC}"
        else
            echo -e "${RED}GitHub CLI (gh) not found. Please trigger the workflow manually:${NC}"
            echo -e "https://github.com/${REPO_PATH}/actions/workflows/azure-static-web-apps-frontend.yml"
        fi
    else
        echo -e "${YELLOW}You can trigger the frontend workflow manually later:${NC}"
        echo -e "https://github.com/${REPO_PATH}/actions/workflows/azure-static-web-apps-frontend.yml"
    fi
fi

# Display final summary
echo -e "\n${GREEN}==================================================${NC}"
echo -e "${GREEN}✅ Function App Created Successfully${NC}"
echo -e "${GREEN}==================================================${NC}"
echo -e "${GREEN}Function App URL:${NC} https://${FUNCTION_HOSTNAME}"
echo -e "${GREEN}Function App Name:${NC} ${FUNCTION_APP_NAME}"
echo -e "${GREEN}Resource Group:${NC} ${RESOURCE_GROUP}\n"

echo -e "${RED}⚠️  REQUIRED NEXT STEPS ⚠️${NC}\n"

echo -e "${YELLOW}STEP 1: Add GitHub Secret${NC}"
echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "Go to: ${GREEN}https://github.com/${REPO_PATH}/settings/secrets/actions/new${NC}"
echo -e "Secret name: ${GREEN}AZURE_FUNCTIONAPP_PUBLISH_PROFILE${NC}"
echo -e "Secret value: ${YELLOW}(Copy the publish profile shown above)${NC}\n"

echo -e "${YELLOW}STEP 2: Wait for Frontend Deployment${NC}"
echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "Monitor here: ${GREEN}https://github.com/${REPO_PATH}/actions${NC}"
echo -e "Wait for the frontend workflow to complete (removes managed functions)\n"

echo -e "${YELLOW}STEP 3: Link Function App in Azure Portal (CRITICAL)${NC}"
echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "1. Go to Static Web App backends page:"
echo -e "   ${GREEN}https://portal.azure.com/#@/resource/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.Web/staticSites/${SWA_NAME}/apisList${NC}"
echo -e "2. ${RED}Unlink${NC} the existing backend (if any)"
echo -e "3. Click ${GREEN}'Link'${NC} and configure:"
echo -e "   • Backend type: ${GREEN}Function App${NC}"
echo -e "   • Function App: ${GREEN}${FUNCTION_APP_NAME}${NC}"
echo -e "   • Region: ${GREEN}${LOCATION}${NC}\n"

echo -e "${YELLOW}STEP 4: Deploy API${NC}"
echo -e "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "After completing steps 1-3, deploy your API:"
echo -e "• Push to main branch, or"
echo -e "• Trigger manually: ${GREEN}https://github.com/${REPO_PATH}/actions/workflows/azure-functions-byo.yml${NC}\n"

echo -e "${GREEN}==================================================${NC}\n"
