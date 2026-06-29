// Regional stack — the per-region resources, deployed ONCE PER REGION.
//
// deploy.ps1 loops the region list; for each region it computes the names (which carry the region
// token, e.g. tdo-app-prod-usw3-<hash>) from the Trellis.ResourceNaming.Azure convention, creates
// the regional resource group (rg-tdo-prod-<region>), and deploys this stack into it. The names are
// passed in — this template never invents one. Every region connects to the SAME global SQL server
// (its name is region-less), so its FQDN is passed through from the global stack's output.

targetScope = 'resourceGroup'

@description('Azure region for the regional resources.')
param location string

@description('App Service name (convention: tdo-app-prod-<region>-<hash>).')
param appServiceName string

@description('User-assigned managed identity name (convention: tdo-id-prod-<region>).')
param managedIdentityName string

@description('Key Vault name (convention: tdo-kv-prod-<region>-<hash>).')
param keyVaultName string

@description('Log Analytics workspace name (convention: tdo-log-prod-<region>).')
param logAnalyticsName string

@description('FQDN of the global SQL server the app connects to (from the global stack).')
param sqlServerFqdn string

@description('Database name on the global SQL server.')
param sqlDatabaseName string

@description('Deployed-environment values surfaced to the app as DeployedEnvironment:* settings.')
param deployedSystem string
param deployedEnvironment string
param deployedCloud string
param deployedRegion string
param deployedRegionShortName string
param deployedScope string = 'Shared'

@description('Tags applied to every resource.')
param tags object = {}

// Built-in role: Key Vault Secrets User.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// The app's identity: used for passwordless SQL access (Active Directory Default) and Key Vault
// references. Its client id is surfaced to the app so DefaultAzureCredential picks this identity.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
  tags: tags
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
  }
}

// Grant the app identity read access to secrets (RBAC, not access policies).
resource keyVaultGrant 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  // App Service plans are not DNS-scoped and are not in the convention catalog; derive the name from
  // the (convention-derived) app name so the pair stays together.
  name: '${appServiceName}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        // DeployedEnvironment:* — the single source the service binds for SLI region + resource
        // naming. These MUST match the values the names were computed from, or the running service
        // would resolve different names than were provisioned.
        {
          name: 'DeployedEnvironment__System'
          value: deployedSystem
        }
        {
          name: 'DeployedEnvironment__Environment'
          value: deployedEnvironment
        }
        {
          name: 'DeployedEnvironment__Cloud'
          value: deployedCloud
        }
        {
          name: 'DeployedEnvironment__Region'
          value: deployedRegion
        }
        {
          name: 'DeployedEnvironment__RegionShortName'
          value: deployedRegionShortName
        }
        {
          name: 'DeployedEnvironment__Scope'
          value: deployedScope
        }
        // Tells Active Directory Default / DefaultAzureCredential which user-assigned identity to use.
        {
          name: 'AZURE_CLIENT_ID'
          value: identity.properties.clientId
        }
        // Passwordless SQL via the app's managed identity. Requires the Acl to use the SqlServer EF
        // provider (the sample ships with SQLite for local dev) and the identity to be granted a
        // database user — see deploy/README.md.
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
        }
      ]
    }
  }
}

// Ship App Service platform logs + metrics to the regional workspace (infra-only; no app change).
resource appDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: app
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

@description('The public URL of the regional App Service.')
output appUrl string = 'https://${app.properties.defaultHostName}'

@description('The app identity principal id — grant it a SQL database user (see deploy/README.md).')
output appIdentityPrincipalId string = identity.properties.principalId
