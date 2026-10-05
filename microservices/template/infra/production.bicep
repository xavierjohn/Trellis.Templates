targetScope = 'resourceGroup'

param location string
param cosmosAccountName string
param logAnalyticsName string
param applicationInsightsName string
param membersPrincipalId string
param databaseId string = 'idempotency'
param containerId string = 'idempotency'
param tags object = {}

module idempotency './idempotency.bicep' = {
  name: 'idempotency'
  params: {
    location: location
    cosmosAccountName: cosmosAccountName
    databaseId: databaseId
    containerId: containerId
    tags: tags
  }
}

module access './idempotency-access.bicep' = {
  name: 'idempotency-access'
  params: {
    cosmosAccountName: cosmosAccountName
    databaseId: databaseId
    containerId: containerId
    principalId: membersPrincipalId
  }
  dependsOn: [
    idempotency
  ]
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
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
    WorkspaceResourceId: workspace.id
  }
}

output cosmosEndpoint string = idempotency.outputs.endpoint
output applicationInsightsConnectionString string = insights.properties.ConnectionString
