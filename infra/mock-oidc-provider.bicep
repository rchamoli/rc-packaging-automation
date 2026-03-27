// ──────────────────────────────────────────────────────────────────────
// Mock OIDC Provider — Azure Container App + Container Registry
//
// Deploys a lightweight Node.js OIDC identity provider used by
// Azure Static Web Apps for demo/mock authentication.
//
// Usage:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infra/mock-oidc-provider.bicep \
//     --parameters appName=mock-oidc location=<region>
//
// Outputs:
//   fqdn — the public URL to configure in staticwebapp.config.swa.json
//          as the wellKnownOpenIdConfiguration endpoint.
// ──────────────────────────────────────────────────────────────────────

@description('Base name for all resources (e.g. "mock-oidc")')
param appName string = 'mock-oidc'

@description('Azure region for deployment')
param location string = resourceGroup().location

@description('Container image to deploy (set by CI/CD pipeline)')
param containerImage string = ''

@description('ISSUER URL override. Leave empty to auto-detect from the Container App FQDN.')
param issuerUrl string = ''

@description('Optional admin key to protect /manage/* endpoints. Leave empty for open access.')
@secure()
param adminKey string = ''

@description('Cache TTL in seconds for per-client user lists (default: 300)')
param usersCacheTtlSeconds int = 300

// ── Variables ──────────────────────────────────────────────────────────
var acrName = replace('${appName}acr', '-', '')
var envName = '${appName}-env'
var appServiceName = '${appName}-app'
var storageAccountName = replace('${appName}store', '-', '')

// ── Azure Container Registry ──────────────────────────────────────────
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// ── Log Analytics Workspace (required by Container Apps Environment) ──
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ────────────────────────────────────────
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}
// ── Storage Account (multi-tenant user store) ─────────────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

resource usersContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'oidc-users'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}
// ── Container App ─────────────────────────────────────────────────────
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appServiceName
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 80
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
        {
          name: 'storage-connection-string'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
        }
        {
          name: 'admin-key'
          value: adminKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mock-oidc-provider'
          image: !empty(containerImage) ? containerImage : 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'PORT'
              value: '80'
            }
            {
              name: 'ISSUER'
              value: !empty(issuerUrl) ? issuerUrl : ''
            }
            {
              name: 'AZURE_STORAGE_CONNECTION_STRING'
              secretRef: 'storage-connection-string'
            }
            {
              name: 'USERS_CONTAINER'
              value: 'oidc-users'
            }
            {
              name: 'USERS_CACHE_TTL'
              value: string(usersCacheTtlSeconds)
            }
            {
              name: 'ADMIN_KEY'
              secretRef: 'admin-key'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────

@description('The FQDN of the Container App (use as OIDC provider URL)')
output fqdn string = containerApp.properties.configuration.ingress.fqdn

@description('The full OIDC discovery URL for SWA config')
output wellKnownUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}/.well-known/openid-configuration'

@description('ACR login server (for docker push)')
output acrLoginServer string = acr.properties.loginServer

@description('Container App name (for az containerapp update)')
output containerAppName string = containerApp.name

@description('Container Apps Environment name')
output environmentName string = containerAppsEnv.name

@description('Storage account name (for user management)')
output storageAccountName string = storageAccount.name

@description('Blob container name for user data')
output usersContainerName string = usersContainer.name
