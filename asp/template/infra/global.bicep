// Global stack — the cloud-singleton resources, deployed ONCE for the whole cloud.
//
// In the Trellis Azure naming convention these resources have region-less names (e.g.
// tdo-sql-prod-nhm4y), so every region computes the SAME name and connects to the SAME instance.
// deploy.ps1 creates the region-less resource group (rg-tdo-prod) and deploys this stack into it
// in the first wave; later region waves reference these by name and never recreate them.
//
// Names are passed in from the Trellis.ResourceNaming.Azure convention (see deploy/names) — this
// template never invents a name.

targetScope = 'resourceGroup'

@description('Azure region for the global resources (and the resource group metadata).')
param location string

@description('SQL logical server name (convention: tdo-sql-prod-<hash>).')
param sqlServerName string

@description('SQL database name (convention: tdo-sqldb-prod).')
param sqlDatabaseName string

@description('Microsoft Entra principal that becomes the SQL server administrator (object id).')
param sqlAdminObjectId string

@description('Display name / UPN of the Entra SQL administrator (shown in the portal).')
param sqlAdminLogin string

@allowed([ 'User', 'Group', 'Application' ])
@description('Principal type of the Entra SQL administrator.')
param sqlAdminPrincipalType string = 'User'

@description('Tags applied to every resource.')
param tags object = {}

// Entra-only authentication (no SQL logins / passwords). Each region's App Service authenticates
// with its managed identity; grant those identities database access as a documented post-step.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: sqlAdminPrincipalType
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Lets App Service instances in any region reach the server. For a hardened deployment replace this
// with a private endpoint per region; that is out of scope for this naming example.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

@description('The SQL server FQDN used in connection strings — matches the convention SqlServerFqdn().')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('The database name, echoed for the regional stacks.')
output sqlDatabaseName string = database.name
