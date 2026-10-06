targetScope = 'resourceGroup'

param location string
param namespaceName string
param topicName string
param subscriptionName string
param tags object = {}

resource messaging 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: messaging
  name: topicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic
  name: subscriptionName
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

resource filter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: subscription
  name: 'MemberInvited'
  properties: {
    filterType: 'CorrelationFilter'
    correlationFilter: {
      label: topicName
    }
  }
}

resource defaultRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: subscription
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '1=0'
    }
  }
}

resource sender 'Microsoft.ServiceBus/namespaces/topics/authorizationRules@2024-01-01' = {
  parent: topic
  name: 'members-send'
  properties: {
    rights: ['Send']
  }
}

resource receiver 'Microsoft.ServiceBus/namespaces/topics/authorizationRules@2024-01-01' = {
  parent: topic
  name: 'projects-listen'
  properties: {
    rights: ['Listen']
  }
}

@secure()
output membersConnectionString string = sender.listKeys().primaryConnectionString
@secure()
output projectsConnectionString string = receiver.listKeys().primaryConnectionString
