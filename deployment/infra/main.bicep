@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the sku')
param skuName string = 'Standard_E2d_v5'

@description('# of nodes')
@minValue(2)
@maxValue(1000)
param skuCapacity int = 2

var suffix = uniqueString(resourceGroup().id)

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: 'eventHub${suffix}'
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
    name: 'kustoHub'
    properties: {
      messageRetentionInDays: 2
      partitionCount: 32
    }

    resource emitterSend 'authorizationRules' = {
      name: 'emitter-send'
      properties: {
        rights: [
          'Send'
        ]
      }
    }

    resource kustoConsumerGroup 'consumergroups' = {
      name: 'kustoConsumerGroup'
      properties: {}
    }
  }
}

resource cluster 'Microsoft.Kusto/clusters@2022-02-01' = {
  name: 'kusto${suffix}'
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
    name: 'telemetry'
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

resource registry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: 'registry${suffix}'
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

resource appEnvironment 'Microsoft.App/managedEnvironments@2022-10-01' = {
  name: 'appEnv${suffix}'
  location: location
  sku: {
    name: 'Consumption'
  }
  properties: {
    zoneRedundant: false
  }
}

resource containerFetchingIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'containerFetchingId-${suffix}'
  location: location
}

//  We also need to authorize the user identity to pull container images from the registry
resource userIdentityRbacAuthorization 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerFetchingIdentity.id, registry.id, 'rbac')
  scope: registry

  properties: {
    description: 'Giving AcrPull RBAC to identity'
    principalId: containerFetchingIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource app 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'emitter-app-${suffix}'
  location: location
  dependsOn: [
    userIdentityRbacAuthorization
  ]
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${containerFetchingIdentity.id}': {}
    }
  }
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          identity: containerFetchingIdentity.id
          server: registry.properties.loginServer
        }
      ]
    }
    environmentId: appEnvironment.id
    template: {
      containers: [
        {
          image: '${registry.name}.azurecr.io/kusto/eh-max-ingestion:latest'
          name: 'eh-max-ingestion'
          resources: {
            cpu: '2'
            memory: '4Gi'
          }
          env: [
            {
              name: 'EVENT_HUB_CONN_STRING'
              value: eventHubNamespace::eventHub::emitterSend.listKeys().primaryConnectionString
            }
            {
              name: 'THREAD_COUNT'
              value: '4'
            }
            {
              name: 'NETWORK_QUEUE_DEPTH'
              value: '25'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
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
var roleAssignmentName = guid(eventHubRoleAssignmentName, eventHubNamespace::eventHub.name, dataReceiverId, cluster.name)

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
