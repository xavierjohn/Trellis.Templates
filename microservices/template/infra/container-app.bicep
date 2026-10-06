targetScope = 'resourceGroup'

param location string
param name string
param environmentId string
param identityId string
param image string
param externalIngress bool = false
param registryServer string = ''
@secure()
param settings object
param tags object = {}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: empty(registryServer) ? [] : [
        {
          server: registryServer
          identity: identityId
        }
      ]
      secrets: settings.secrets
    }
    template: {
      containers: [
        {
          name: 'app'
          image: image
          env: settings.environment
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          volumeMounts: settings.?volumeMounts ?? []
        }
      ]
      volumes: settings.?volumes ?? []
      scale: {
        // Keep the Projects Service Bus consumer and Members outbox relay running without HTTP traffic.
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
