@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the cluster')
param clusterName string = 'kusto${uniqueString(resourceGroup().id)}'

@description('Name of the sku')
param skuName string = 'Standard_E2d_v5'

@description('# of nodes')
@minValue(2)
@maxValue(1000)
param skuCapacity int = 2

@description('Name of the database')
param databaseName string = 'telemetry'

@description('Name of Event Hub\'s namespace')
param eventHubNamespaceName string = 'eventHub${uniqueString(resourceGroup().id)}'

@description('Name of Event Hub')
param eventHubName string = 'kustoHub'

@description('Name of registry')
param registryName string = 'registry${uniqueString(resourceGroup().id)}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    anonymousPullEnabled: false
    dataEndpointEnabled: false
    policies: {
      azureADAuthenticationAsArmPolicy: {
        status: 'enabled'
      }
      retentionPolicy: {
        status: 'disabled'
      }
      softDeletePolicy: {
        status: 'disabled'
      }
    }
    publicNetworkAccess: 'enabled'
    zoneRedundancy: 'disabled'
  }
}

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: eventHubNamespaceName
  location: location
  sku: {
    capacity: 1
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    isAutoInflateEnabled: true
    maximumThroughputUnits: 40
  }

  resource eventHub 'eventhubs' = {
    name: eventHubName
    properties: {
      messageRetentionInDays: 2
      partitionCount: 32
    }

    resource kustoConsumerGroup 'consumergroups' = {
      name: 'kustoConsumerGroup'
      properties: {}
    }
  }
}

resource cluster 'Microsoft.Kusto/clusters@2022-02-01' = {
  name: clusterName
  location: location
  sku: {
    name: skuName
    tier: 'Standard'
    capacity: skuCapacity
  }
  identity: {
    type: 'SystemAssigned'
  }

  resource kustoDb 'databases' = {
    name: databaseName
    location: location
    kind: 'ReadWrite'

    resource kustoScript 'scripts' = {
      name: 'db-script'
      properties: {
        scriptContent: loadTextContent('script.kql')
        continueOnErrors: false
      }
    }

    resource eventConnection 'dataConnections' = {
      name: 'eventConnection'
      location: location
      //  Here we need to explicitely declare dependencies
      //  Since we do not use those resources in the event connection
      //  but we do need them to be deployed first
      dependsOn: [
        //  We need the table to be present in the database
        kustoScript
        //  We need the cluster to be receiver on the Event Hub
        clusterEventHubAuthorization
      ]
      kind: 'EventHub'
      properties: {
        compression: 'None'
        consumerGroup: eventHubNamespace::eventHub::kustoConsumerGroup.name
        dataFormat: 'MULTIJSON'
        eventHubResourceId: eventHubNamespace::eventHub.id
        eventSystemProperties: [
          'x-opt-enqueued-time'
        ]
        managedIdentityResourceId: cluster.id
        mappingRuleName: 'DirectJson'
        tableName: 'RawEvents'
      }
    }
  }
}

//  We need to authorize the cluster to read the event hub by assigning the role
//  "Azure Event Hubs Data Receiver"
//  Role list:  https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles
var dataReceiverId = 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
var fullDataReceiverId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dataReceiverId)
var eventHubRoleAssignmentName = '${resourceGroup().id}${cluster.name}${dataReceiverId}${eventHubNamespace::eventHub.name}'
var roleAssignmentName = guid(eventHubRoleAssignmentName, eventHubName, dataReceiverId, clusterName)

resource clusterEventHubAuthorization 'Microsoft.Authorization/roleAssignments@2021-04-01-preview' = {
  name: roleAssignmentName
  //  See https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources
  //  for scope for extension
  scope: eventHubNamespace::eventHub
  properties: {
    description: 'Give "Azure Event Hubs Data Receiver" to the cluster'
    principalId: cluster.identity.principalId
    //  Required in case principal not ready when deploying the assignment
    principalType: 'ServicePrincipal'
    roleDefinitionId: fullDataReceiverId
  }
}