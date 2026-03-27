# Azure Resource Setup Tools

This folder contains scripts for setting up and managing Azure resources for the Agentic Software Development project.

## Scripts

### init-local-settings.sh

Creates a default `api/local.settings.json` file with Azurite configuration for local development.

**What it does:**
- Checks if `local.settings.json` already exists
- If not, creates it with Azurite (local storage emulator) connection strings
- Safe to run multiple times - won't overwrite existing files

**Usage:**
```bash
./tools/init-local-settings.sh
```

**Note:** This script runs automatically during dev container creation via `postCreateCommand`.

**Configuration created:**
- `AzureWebJobsStorage`: Uses Azurite local emulator
- `StorageConnectionString`: Uses Azurite local emulator
- `FUNCTIONS_WORKER_RUNTIME`: Set to `dotnet-isolated`

### setup-azure-resources.sh

Interactive script that creates Azure resources using Azure CLI.

**What it does:**
1. Prompts for project name (defaults to repository name)
2. Prompts for resource group name (required)
3. Prompts for Static Web App name (defaults to `{project-name}-swa`)
4. Prompts for Storage Account name (defaults to sanitized project name + "st")
5. Creates the following Azure resources:
   - Resource Group
   - Azure Storage Account (Standard_LRS)
   - Azure Static Web App (Standard tier)

**Usage:**
```bash
./tools/setup-azure-resources.sh
```

**Requirements:**
- Azure CLI (`az`) installed and available
- Authenticated to Azure (script will prompt login if needed)
- Appropriate permissions to create resources in your Azure subscription

**Output:**
- Creates Azure resources
- Displays resource URLs and connection strings
- Saves configuration to `tools/.azure-config`
- Provides next steps for deployment

### switch-to-byo-functions.sh

Interactive script that creates a dedicated Azure Function App with Flex Consumption plan and switches from managed functions to "Bring Your Own" (BYO) Functions architecture.

**What it does:**
1. Loads configuration from `tools/.azure-config` (created by setup-azure-resources.sh)
2. Prompts for Function App name (defaults to `fun-{project-name}`)
3. Prompts for dedicated Function Storage Account name
4. Creates the following Azure resources:
   - Storage Account for Function App
   - Azure Function App (Flex Consumption plan, .NET 8 isolated)
   - System Assigned Managed Identity
   - Role assignments for storage access (Blob, Queue, Table Data roles)
5. Copies environment variables from Static Web App to Function App
6. Configures Function App to use Managed Identity for storage access
7. Switches GitHub Actions workflows:
   - Disables combined workflow (`azure-static-web-apps.yml`)
   - Enables frontend-only workflow (`azure-static-web-apps-frontend.yml`)
   - Enables separate API workflow (`azure-functions-byo.yml`)
8. Retrieves publish profile for GitHub Actions deployment
9. Provides step-by-step instructions to complete the setup

**When to use:**
- When you need more control over your Function App configuration
- When you want to use Flex Consumption plan features
- When you need System Assigned Managed Identity for security
- When you want to scale the API independently from the frontend

**Usage:**
```bash
./tools/switch-to-byo-functions.sh
```

**Requirements:**
- Must run `setup-azure-resources.sh` first
- Azure CLI (`az`) installed and available
- Authenticated to Azure
- Git repository with remote origin (for workflow switching)
- GitHub CLI (`gh`) optional for automatic workflow triggering

**Output:**
- Creates Function App with Flex Consumption plan
- Configures Managed Identity and role assignments
- Switches GitHub Actions workflows
- Provides publish profile for GitHub secret
- Displays step-by-step instructions:
  1. Add `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` secret to GitHub
  2. Wait for frontend deployment to complete
  3. Link Function App in Azure Portal
  4. Deploy API via GitHub Actions

**Architecture Change:**

Before (Managed Functions):
```
┌─────────────────────────────┐
│  Azure Static Web App       │
│  ┌──────────┐  ┌─────────┐  │
│  │ Frontend │  │ Managed │  │
│  │   HTML   │  │   API   │  │
│  └──────────┘  └─────────┘  │
└─────────────────────────────┘
```

After (BYO Functions):
```
┌──────────────────┐       ┌────────────────────────┐
│ Static Web App   │       │  Azure Function App    │
│  ┌──────────┐    │ Link  │  ┌──────────────────┐  │
│  │ Frontend │────┼──────▶│  │ .NET 8 API       │  │
│  └──────────┘    │       │  │ (Flex Consumption)│  │
└──────────────────┘       │  └──────────────────┘  │
                           │  ┌──────────────────┐  │
                           │  │ Managed Identity │  │
                           │  └──────────────────┘  │
                           └────────────────────────┘
```

**Note:** If role assignments fail due to Conditional Access policies, the script provides detailed manual instructions for assigning roles via Azure Portal or Azure CLI.

## Configuration

After running the setup script, a `.azure-config` file will be created with your resource details. This file is gitignored by default.

## Example

```bash
cd /workspaces/Agentic-Software-Development
./tools/setup-azure-resources.sh
```

The script will interactively prompt for:
- Project name [Agentic-Software-Development]
- Resource group name: rg-agentic-dev
- Static Web App name [agentic-software-development-swa]
- Storage Account name [agenticsoftwaredevelopmentst]
- Azure location [eastus]
