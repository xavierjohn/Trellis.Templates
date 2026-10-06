targetScope = 'resourceGroup'

param location string
param serverName string
param membersDatabaseName string
param projectsDatabaseName string
param administratorLogin string
@secure()
param administratorPassword string
param tags object = {}

@allowed(['postgres', 'sqlserver'])
param provider string = 'TEMPLATE_DATABASE_PROVIDER'

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = if (provider == 'sqlserver') {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlAccess 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (provider == 'sqlserver') {
  parent: sql
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabases 'Microsoft.Sql/servers/databases@2023-08-01-preview' = [for databaseName in [membersDatabaseName, projectsDatabaseName]: if (provider == 'sqlserver') {
  parent: sql
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}]

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (provider == 'postgres') {
  name: serverName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
  }
}

resource postgresAccess 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (provider == 'postgres') {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource postgresDatabases 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = [for databaseName in [membersDatabaseName, projectsDatabaseName]: if (provider == 'postgres') {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}]

output serverFqdn string = provider == 'postgres'
  ? postgres!.properties.fullyQualifiedDomainName
  : sql!.properties.fullyQualifiedDomainName
