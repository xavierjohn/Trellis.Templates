// Regional stack — the per-region resources, deployed ONCE PER REGION.
//
// deploy.ps1 loops the region list; for each region it computes the names (which carry the region
// token, e.g. tdo-app-prod-usw3-<hash>) from the Trellis.ResourceNaming.Azure convention, creates
// the regional resource group (rg-tdo-prod-<region-short>), and deploys this stack into it. The names are
// passed in — this template never invents one. Every region connects to the SAME global SQL server
// (its name is region-less): deploy.ps1 supplies that server's FQDN, which the convention computes to
// the same value the global stack provisioned.

targetScope = 'resourceGroup'

@description('Azure region for the regional resources.')
param location string

@description('App Service name (convention: tdo-app-prod-<region-short>-<hash>).')
param appServiceName string

@description('App Service plan name (convention: tdo-plan-prod-<region-short>).')
param appServicePlanName string

@description('User-assigned managed identity name (convention: tdo-id-prod-<region-short>).')
param managedIdentityName string

@description('Log Analytics workspace name (convention: tdo-log-prod-<region-short>).')
param logAnalyticsName string

param applicationInsightsName string
param cosmosAccountName string
param cosmosResourceGroupName string
param cosmosEndpoint string
param idempotencyDatabaseName string

@description('FQDN of the global SQL server the app connects to (convention-computed; identical to the value the global stack provisions).')
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

// The ASP.NET Core hosting environment, derived from the CAF lifecycle word so a -Environment
// override (e.g. test/stage) stays consistent between the resource names and the running app.
var aspNetCoreEnvironments = {
  local: 'Development'
  test: 'Staging'
  stage: 'Staging'
  prod: 'Production'
}
var aspNetCoreEnvironment = aspNetCoreEnvironments[?deployedEnvironment] ?? 'Production'

// The app's identity: used for passwordless SQL access (Active Directory Default). Its client id is
// surfaced to the app so DefaultAzureCredential picks this identity.
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

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

module idempotencyAccess './idempotency-access.bicep' = {
  name: 'idempotency-access-${deployedRegionShortName}'
  scope: resourceGroup(cosmosResourceGroupName)
  params: {
    cosmosAccountName: cosmosAccountName
    databaseId: idempotencyDatabaseName
    principalId: identity.properties.principalId
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
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
  dependsOn: [
    idempotencyAccess
  ]
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspNetCoreEnvironment
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
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: insights.properties.ConnectionString
        }
        {
          name: 'Idempotency__Store'
          value: 'Cosmos'
        }
        {
          name: 'Idempotency__Cosmos__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'Idempotency__Cosmos__DatabaseId'
          value: idempotencyDatabaseName
        }
        {
          name: 'Idempotency__Cosmos__ContainerId'
          value: 'idempotency'
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
