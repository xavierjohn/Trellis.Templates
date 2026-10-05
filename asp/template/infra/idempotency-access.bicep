targetScope = 'resourceGroup'

param cosmosAccountName string
param databaseId string
param containerId string = 'idempotency'
param principalId string

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}

resource access 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: account
  name: guid(account.id, databaseId, containerId, principalId)
  properties: {
    principalId: principalId
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    scope: '${account.id}/dbs/${databaseId}/colls/${containerId}'
  }
}
