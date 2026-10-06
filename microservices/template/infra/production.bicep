targetScope = 'resourceGroup'

param location string
param names object
param databaseAdministratorLogin string
@secure()
param databaseAdministratorPassword string
param deployApplications bool = false
param gatewayImage string = ''
param membersImage string = ''
param projectsImage string = ''
param registryServer string = ''
@secure()
param gatewaySigningPrivateKeyPem string = ''
@secure()
param gatewayPublishedPublicKeys object = {}
@secure()
param membersDatabaseConnectionString string = ''
@secure()
param projectsDatabaseConnectionString string = ''
param authenticationAuthority string = ''
param authenticationAudience string = ''
param authenticationTenantId string = ''
param authenticationClientId string = ''
param otlpEndpoint string = ''
param otlpProtocol string = 'grpc'
param regionShortName string
param deployedSystem string
param deployedEnvironment string
param deployedCloud string
param tags object = {}

resource identities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for name in [
  names.gatewayIdentityName
  names.membersIdentityName
  names.projectsIdentityName
]: {
  name: name
  location: location
  tags: tags
}]

module database './database.bicep' = {
  name: 'database'
  params: {
    location: location
    serverName: names.databaseServerName
    membersDatabaseName: names.membersDatabaseName
    projectsDatabaseName: names.projectsDatabaseName
    administratorLogin: databaseAdministratorLogin
    administratorPassword: databaseAdministratorPassword
    tags: tags
  }
}

module messaging './messaging.bicep' = {
  name: 'messaging'
  params: {
    location: location
    namespaceName: names.serviceBusName
    topicName: names.topicName
    subscriptionName: names.subscriptionName
    tags: tags
  }
}

module idempotency './idempotency.bicep' = {
  name: 'idempotency'
  params: {
    location: location
    cosmosAccountName: names.cosmosAccountName
    databaseId: names.idempotencyDatabaseName
    tags: tags
  }
}

module access './idempotency-access.bicep' = {
  name: 'idempotency-access'
  params: {
    cosmosAccountName: names.cosmosAccountName
    databaseId: names.idempotencyDatabaseName
    principalId: identities[1].properties.principalId
  }
  dependsOn: [
    idempotency
  ]
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = if ('TEMPLATE_TELEMETRY_EXPORTERS' != 'otlp') {
  name: names.applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.containerEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

var gatewayIssuer = 'https://${names.gatewayName}.${environment.properties.defaultDomain}'
var commonEnvironment = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'Gateway__Issuer'
    value: gatewayIssuer
  }
  {
    name: 'DeployedEnvironment__Region'
    value: location
  }
  {
    name: 'DeployedEnvironment__RegionShortName'
    value: regionShortName
  }
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
]
var telemetryEnvironment = concat(('TEMPLATE_TELEMETRY_EXPORTERS' != 'azure-monitor' ? [
  {
    name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
    value: otlpEndpoint
  }
  {
    name: 'OTEL_EXPORTER_OTLP_PROTOCOL'
    value: otlpProtocol
  }
] : []), ('TEMPLATE_TELEMETRY_EXPORTERS' != 'otlp' ? [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: insights!.properties.ConnectionString
  }
] : []))
var authenticationEnvironment = 'TEMPLATE_AUTH_PROVIDER' == 'entra' ? [
  {
    name: 'Authentication__TenantId'
    value: authenticationTenantId
  }
  {
    name: 'Authentication__ClientId'
    value: authenticationClientId
  }
] : [
  {
    name: 'Authentication__Authority'
    value: authenticationAuthority
  }
  {
    name: 'Authentication__Audience'
    value: authenticationAudience
  }
]
var publicKeys = items(gatewayPublishedPublicKeys)
var publishedSecrets = [for key in publicKeys: {
  name: 'published-${key.key}'
  value: key.value
}]
var publishedEnvironment = [for (key, index) in publicKeys: {
  name: 'Gateway__PublishedKeyPaths__${index}'
  value: '/keys/published-${key.key}.pem'
}]
var publishedVolumeItems = [for key in publicKeys: {
  secretRef: 'published-${key.key}'
  path: 'published-${key.key}.pem'
}]

module gateway './container-app.bicep' = if (deployApplications) {
  name: 'gateway'
  params: {
    location: location
    name: names.gatewayName
    environmentId: environment.id
    identityId: identities[0].id
    image: gatewayImage
    externalIngress: true
    registryServer: registryServer
    tags: tags
    settings: {
      secrets: concat([
        {
          name: 'active-key'
          value: gatewaySigningPrivateKeyPem
        }
      ], publishedSecrets)
      environment: concat(commonEnvironment, telemetryEnvironment, authenticationEnvironment, [
        {
          name: 'Gateway__SigningKeyPath'
          value: '/keys/active.pem'
        }
        {
          name: 'services__members__https__0'
          value: 'https://${names.membersName}.internal.${environment.properties.defaultDomain}'
        }
        {
          name: 'services__projects__https__0'
          value: 'https://${names.projectsName}.internal.${environment.properties.defaultDomain}'
        }
      ], publishedEnvironment)
      volumeMounts: [
        {
          volumeName: 'signing'
          mountPath: '/keys'
        }
      ]
      volumes: [
        {
          name: 'signing'
          storageType: 'Secret'
          secrets: concat([
            {
              secretRef: 'active-key'
              path: 'active.pem'
            }
          ], publishedVolumeItems)
        }
      ]
    }
  }
}

module members './container-app.bicep' = if (deployApplications) {
  name: 'members'
  params: {
    location: location
    name: names.membersName
    environmentId: environment.id
    identityId: identities[1].id
    image: membersImage
    registryServer: registryServer
    tags: tags
    settings: {
      secrets: [
        {
          name: 'database'
          value: membersDatabaseConnectionString
        }
        {
          name: 'messaging'
          value: messaging.outputs.membersConnectionString
        }
      ]
      environment: concat(commonEnvironment, telemetryEnvironment, [
        {
          name: 'AZURE_CLIENT_ID'
          value: identities[1].properties.clientId
        }
        {
          name: 'DeployedEnvironment__Service'
          value: 'mbr'
        }
        {
          name: 'ConnectionStrings__membersdb'
          secretRef: 'database'
        }
        {
          name: 'ConnectionStrings__messaging'
          secretRef: 'messaging'
        }
        {
          name: 'Idempotency__Store'
          value: 'Cosmos'
        }
        {
          name: 'Idempotency__Cosmos__Endpoint'
          value: idempotency.outputs.endpoint
        }
        {
          name: 'Idempotency__Cosmos__DatabaseId'
          value: names.idempotencyDatabaseName
        }
        {
          name: 'Idempotency__Cosmos__ContainerId'
          value: 'idempotency'
        }
      ])
    }
  }
  dependsOn: [
    access
    database
  ]
}

module projects './container-app.bicep' = if (deployApplications) {
  name: 'projects'
  params: {
    location: location
    name: names.projectsName
    environmentId: environment.id
    identityId: identities[2].id
    image: projectsImage
    registryServer: registryServer
    tags: tags
    settings: {
      secrets: [
        {
          name: 'database'
          value: projectsDatabaseConnectionString
        }
        {
          name: 'messaging'
          value: messaging.outputs.projectsConnectionString
        }
      ]
      environment: concat(commonEnvironment, telemetryEnvironment, [
        {
          name: 'DeployedEnvironment__Service'
          value: 'prj'
        }
        {
          name: 'ConnectionStrings__projectsdb'
          secretRef: 'database'
        }
        {
          name: 'ConnectionStrings__messaging'
          secretRef: 'messaging'
        }
      ])
    }
  }
  dependsOn: [
    database
  ]
}

output gatewayUrl string = gatewayIssuer
output databaseServerFqdn string = database.outputs.serverFqdn
