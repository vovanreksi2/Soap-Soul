// Production infrastructure: App Service (Linux, .NET 10) + Azure SQL + Blob Storage + Key Vault.
// The app reaches all of them with one user-assigned managed identity. The only secrets (third-party API keys)
// live in Key Vault; they are set by hand (infra/set-secrets.sh), never by the template or the pipeline.
// Deployed by .github/workflows/deploy.yml before every app deployment (idempotent). See docs/deploy/azure.md.

targetScope = 'resourceGroup'

@description('Base name used in resource names.')
param name string = 'soapandsoul'

param location string = resourceGroup().location

@description('Web app name (becomes <name>.azurewebsites.net). Must be globally unique.')
param webAppName string = 'app-${name}-${uniqueString(resourceGroup().id)}'

@description('Resource id of an existing Linux App Service plan to reuse. Empty creates a new plan.')
param appServicePlanId string = ''

@description('SKU of the new App Service plan (ignored when reusing one). B1 or higher supports Always On.')
param appServicePlanSku string = 'B1'

@description('Azure SQL database SKU. Basic (5 DTU, 2 GB) is enough for a single user.')
param sqlDatabaseSku object = {
  name: 'Basic'
  tier: 'Basic'
}

var suffix = uniqueString(resourceGroup().id)
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}'
  location: location
}

// --- Azure SQL ---------------------------------------------------------------------------------------

// Entra-only authentication. The app identity is the server's Entra admin: the app applies EF migrations
// at startup, and Azure SQL cannot create database users declaratively.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${name}-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: identity.name
      sid: identity.properties.clientId
      tenantId: tenant().tenantId
    }
  }
}

// Lets App Service reach the server; access still requires an Entra token.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: name
  location: location
  sku: sqlDatabaseSku
  properties: {
    requestedBackupStorageRedundancy: 'Local'
  }
}

// --- Blob Storage ------------------------------------------------------------------------------------

// No public blobs and no account keys: photos are served by the app, which signs in with the identity.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('st${replace(name, '-', '')}${suffix}', 24)
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'images'
  properties: {
    publicAccess: 'None'
  }
}

resource imagesAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(imagesContainer.id, identity.id, storageBlobDataContributor)
  scope: imagesContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// --- Key Vault ---------------------------------------------------------------------------------------

// Secrets the app loads into configuration at startup: "Llm--ApiKey" → Llm:ApiKey, "Mcp--ApiKey" → Mcp:ApiKey.
// RBAC only; the app identity can read secrets, nothing else.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('kv${replace(name, '-', '')}${suffix}', 24)
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// --- App Service -------------------------------------------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = if (empty(appServicePlanId)) {
  name: 'asp-${name}'
  location: location
  kind: 'linux'
  sku: {
    name: appServicePlanSku
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  // The app reads Key Vault at startup, so its access must exist first.
  dependsOn: [
    keyVaultAccess
  ]
  properties: {
    serverFarmId: empty(appServicePlanId) ? plan.id : appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      appSettings: [
        { name: 'Database__Provider', value: 'SqlServer' }
        {
          name: 'ConnectionStrings__Default'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabase.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
        }
        { name: 'Images__Provider', value: 'AzureBlob' }
        { name: 'Images__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'Images__Container', value: imagesContainer.name }
        { name: 'Images__ManagedIdentityClientId', value: identity.properties.clientId }
        { name: 'KeyVault__Uri', value: keyVault.properties.vaultUri }
        { name: 'KeyVault__ManagedIdentityClientId', value: identity.properties.clientId }
        // Voice drafts and MCP stay off until their keys are in Key Vault.
        { name: 'Llm__Provider', value: 'Anthropic' }
      ]
    }
  }
}

// Deployments use the GitHub OIDC identity, so FTP and Kudu basic-auth credentials stay off.
resource ftpCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: webApp
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: webApp
  name: 'scm'
  properties: {
    allow: false
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output sqlServerName string = sqlServer.name
output storageAccountName string = storage.name
output keyVaultName string = keyVault.name
