targetScope = 'resourceGroup'

param location string
param cosmosAccountName string
param databaseId string
param containerId string = 'idempotency'
param tags object = {}

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true
    minimalTlsVersion: 'Tls12'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: account
  name: databaseId
  properties: {
    resource: {
      id: databaseId
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: containerId
  properties: {
    resource: {
      id: containerId
      partitionKey: {
        paths: [
          '/scope'
        ]
        kind: 'Hash'
      }
      // Enable per-item TTL; reservations must not expire through the background sweep.
      defaultTtl: -1
    }
  }
}

output endpoint string = account.properties.documentEndpoint
