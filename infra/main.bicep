// Production infrastructure: App Service (Linux, .NET 10) + Azure SQL (free offer) + Blob Storage + Key Vault.
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

@description('SKU of the App Service plan: F1 (free) or B1 and higher. When appServicePlanId is set, set this to that plan SKU.')
param appServicePlanSku string = 'F1'

@description('Auto-pause delay of the free serverless database in minutes (15 or more). Every minute online counts against the free monthly vCore seconds.')
@minValue(15)
param sqlAutoPauseDelay int = 15

var suffix = uniqueString(resourceGroup().id)
// The free plan has no Always On, unloads the app after 20 idle minutes and allows 60 CPU minutes a day.
var freePlan = toUpper(appServicePlanSku) == 'F1'
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

// Azure SQL free offer: serverless General Purpose with 100,000 vCore seconds and 32 GB free per month. When the
// allowance runs out the database pauses until the next month instead of billing. An existing database cannot be
// converted to the free offer, so this is a new database; the app copies the old one into it on first start.
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: '${name}-db'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: sqlAutoPauseDelay
    minCapacity: any(json('0.5'))
    requestedBackupStorageRedundancy: 'Local'
  }
}

// The previous Basic-tier database, kept unchanged until the move to the free one is confirmed: it is the import
// source (Database__ImportFrom) and the rollback. Remove it, the setting and DatabaseImport together.
resource sqlLegacyDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: name
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    requestedBackupStorageRedundancy: 'Local'
  }
}

func sqlConnectionString(server string, database string, identityClientId string) string =>
  'Server=tcp:${server},1433;Database=${database};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;Connect Timeout=60'

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
      // The package also holds SoapAndSoul.Client.runtimeconfig.json; without an explicit command the platform
      // cannot tell which assembly to start and serves its default page instead.
      appCommandLine: 'dotnet SoapAndSoul.Api.dll'
      alwaysOn: !freePlan
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      // With one instance the health check cannot fail over anything; on the free plan it would also keep the app
      // loaded and spend its CPU quota. The deployment smoke test calls /healthz either way.
      healthCheckPath: freePlan ? null : '/healthz'
      appSettings: [
        { name: 'Database__Provider', value: 'SqlServer' }
        {
          name: 'ConnectionStrings__Default'
          value: sqlConnectionString(sqlServer.properties.fullyQualifiedDomainName, sqlDatabase.name, identity.properties.clientId)
        }
        {
          name: 'Database__ImportFrom'
          value: sqlConnectionString(sqlServer.properties.fullyQualifiedDomainName, sqlLegacyDatabase.name, identity.properties.clientId)
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
