param name string
param identityName string
param topicName string
param destinationTopicName string
param eventGridName string
param filterPrefix string = ''
param includedEventTypes array
param advancedFilters array = []

resource eventGridNamespace 'Microsoft.EventGrid/namespaces@2023-12-15-preview' existing = {
  name: eventGridName
}

resource destinationTopic 'Microsoft.EventGrid/namespaces/topics@2023-12-15-preview' existing = {
  name: destinationTopicName
  parent: eventGridNamespace
}

resource topic 'Microsoft.EventGrid/systemTopics@2023-12-15-preview' existing = {
  name: topicName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: identityName
}

resource resourceProviderSub 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2023-12-15-preview' = {
  name: name
  parent: topic
  properties: {
    deliveryWithResourceIdentity: {
      identity: {
        type: 'UserAssigned'
        userAssignedIdentity: identity.id
      }
      destination: {
        endpointType: 'NamespaceTopic'
        properties: {
          resourceId: destinationTopic.id
        }
      }  
    }
    filter: {
      subjectBeginsWith: filterPrefix
      includedEventTypes: includedEventTypes
      enableAdvancedFilteringOnArrays: true
      advancedFilters: advancedFilters
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}
