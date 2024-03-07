/**
 * Module for deploying an AKS cluster.
 *
 * Inputs:
 * - actionGroupId: Action Group Id for alerts
 * - admnistratorObjectIds: The Managed Identity for the AKS Cluster
 * - dnsResourceGroupName: DNS resource group name
 * - location: Location for all resources
 * - logAnalyticWorkspaceId: Log Analytic Workspace Id to use for diagnostics
 * - logAnalyticWorkspaceResourceId: Log Analytic Workspace Resource Id to use for diagnostics
 * - networkingResourceGroupName: Networking resource group name
 * - privateDnsZones: Private DNS Zones for private endpoint
 * - resourceSuffix: Resource suffix for all resources
 * - subnetId: Subnet Id for private endpoint
 * - subnetIdPrivateEndpoint: Subnet Id for private endpoint
 * - tags: Tags for all resources
 * - timestamp: Timestamp for nested deployments
 *
 * Locals:
 * - alerts: Metric alerts for the resource
 * - logs: The Resource logs to enable
 * - name: The Resource Name
 * - serviceType: The Resource Service Type token
 *
 * Outputs:
 * - oidcIssuerUrl: AKS OIDC Issuer URL
 *
 * Resources:
 * - main: The AKS Cluster
 * - diagnostics: Diagnostic settings for the resource
 * - uai: The Managed Identity for the AKS Cluster
 * - dnsRoleAssignment: Role assignment for DNS
 * - netRoleAssignment: Role assignment for networking
 * - metricAlerts: Resource for configuring the Key Vault metric alerts
 * - privateEndpoint: Private endpoint for App Configuration
 * - subnetRoleAssignment: Role assignment for subnet
 *
 * Nested Modules:
 * - dnsRoleAssignment: Role assignment for DNS
 * - netRoleAssignment: Role assignment for networking
 * - metricAlerts: Resource for configuring the Key Vault metric alerts
 * - privateEndpoint: Private endpoint for App Configuration
 * - subnetRoleAssignment: Role assignment for subnet
 */

/** Inputs **/
@description('Action Group Id for alerts')
param actionGroupId string

@description('The Managed Identity for the AKS Cluster')
param admnistratorObjectIds array

@description('DNS resource group name')
param dnsResourceGroupName string

@description('Location for all resources')
param location string

@description('Log Analytic Workspace Id to use for diagnostics')
param logAnalyticWorkspaceId string

@description('Log Analytic Workspace Resource Id to use for diagnostics')
param logAnalyticWorkspaceResourceId string

@description('Networking resource group name')
param networkingResourceGroupName string

@description('Private DNS Zones for private endpoint')
param privateDnsZones array

@description('Resource suffix for all resources')
param resourceSuffix string

@description('Subnet Id for private endpoint')
param subnetId string

@description('Subnet Id for private endpoint')
param subnetIdPrivateEndpoint string

@description('Tags for all resources')
param tags object

@description('Timestamp for nested deployments')
param timestamp string = utcNow()

/** Outputs **/
@description('AKS Cluster Name')
output name string = main.name

@description('AKS OIDC Issuer URL')
output oidcIssuerUrl string = main.properties.oidcIssuerProfile.issuerURL

/** Locals **/
var name = '${serviceType}-${resourceSuffix}'
var serviceType = 'aks'

var alerts = [
  {
    description: 'Node CPU utilization greater than 95% for 1 hour'
    evaluationFrequency: 'PT5M'
    metricName: 'node_cpu_usage_percentage'
    name: 'node-cpu'
    operator: 'GreaterThan'
    severity: 3
    threshold: 95
    timeAggregation: 'Average'
    windowSize: 'PT5M'
  }
  {
    description: 'Node memory utilization greater than 95% for 1 hour'
    evaluationFrequency: 'PT5M'
    metricName: 'node_memory_working_set_percentage'
    name: 'node-memory'
    operator: 'GreaterThan'
    severity: 3
    threshold: 100
    timeAggregation: 'Average'
    windowSize: 'PT5M'
  }
]

var logs = [
  'cloud-controller-manager'
  'cluster-autoscaler'
  'csi-azuredisk-controller'
  'csi-azurefile-controller'
  'csi-snapshot-controller'
  'guard'
  'kube-apiserver'
  'kube-audit'
  'kube-audit-admin'
  'kube-controller-manager'
  'kube-scheduler'
]

/** Resources **/
resource main 'Microsoft.ContainerService/managedClusters@2023-01-02-preview' = {
  name: name
  location: location
  tags: tags
  dependsOn: [
    dnsRoleAssignment
  ]

  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uai.id}': {}
    }
  }

  sku: {
    name: 'Basic'
    tier: 'Paid'
  }

  properties: {
    enableRBAC: true
    fqdnSubdomain: name
    nodeResourceGroup: 'mrg-${name}'
    disableLocalAccounts: true
    workloadAutoScalerProfile: {}

    aadProfile: {
      managed: true
      adminGroupObjectIDs: admnistratorObjectIds
      enableAzureRBAC: true
      tenantID: subscription().tenantId
    }

    addonProfiles: {
      azureKeyvaultSecretsProvider: {
        enabled: true
        config: {
          enableSecretRotation: 'true'
          rotationPollInterval: '2m'
        }
      }

      azurepolicy: {
        config: { version: 'v2' }
        enabled: true
      }

      ingressApplicationGateway: {
        enabled: false
      }

      omsagent: {
        enabled: true
        config: {
          logAnalyticsWorkspaceResourceID: logAnalyticWorkspaceId
          useAADAuth: 'true'
        }
      }
    }
    agentPoolProfiles: [
      {
        count: 1
        enableAutoScaling: true
        maxCount: 3
        minCount: 1
        mode: 'System'
        name: 'system'
        osDiskSizeGB: 1024
        tags: tags
        type: 'VirtualMachineScaleSets'
        vmSize: 'Standard_DS2_v2'
        vnetSubnetID: subnetId

        nodeTaints: [
          'CriticalAddonsOnly=true:NoSchedule'
        ]

        upgradeSettings: {
          maxSurge: '200'
        }
      }
      {
        count: 1
        enableAutoScaling: true
        maxCount: 3
        minCount: 1
        mode: 'User'
        name: 'user'
        osDiskSizeGB: 1024
        tags: tags
        type: 'VirtualMachineScaleSets'
        vmSize: 'Standard_DS2_v2'
        vnetSubnetID: subnetId

        upgradeSettings: {
          maxSurge: '200'
        }
      }
    ]

    apiServerAccessProfile: {
      enablePrivateCluster: true
      enablePrivateClusterPublicFQDN: false
      privateDNSZone: filter(privateDnsZones, (privateDnsZone) => privateDnsZone.key == 'aks')[0].id
    }

    autoUpgradeProfile: { upgradeChannel: 'stable' }

    azureMonitorProfile: {
      metrics: {
        enabled: true
      }
    }

    networkProfile: {
      dnsServiceIP: '10.100.254.1'
      ipFamilies: [ 'IPv4' ]
      loadBalancerSku: 'Standard'
      networkPlugin: 'azure'
      networkPolicy: 'azure'
      outboundType: 'loadBalancer'
      serviceCidr: '10.100.0.0/16'
      serviceCidrs: [ '10.100.0.0/16' ]

      loadBalancerProfile: {
        backendPoolType: 'nodeIPConfiguration'
        managedOutboundIPs: { count: 1 }
      }
    }

    oidcIssuerProfile: { enabled: true }

    privateLinkResources: [
      {
        groupId: 'management'
        name: 'management'
        requiredMembers: [ 'management' ]
        type: 'Microsoft.ContainerService/managedClusters/privateLinkResources'
      }
    ]

    securityProfile: {
      defender: {
        logAnalyticsWorkspaceResourceId: logAnalyticWorkspaceResourceId

        securityMonitoring: {
          enabled: true
        }
      }

      imageCleaner: {
        enabled: false
        intervalHours: 48
      }

      workloadIdentity: { enabled: true }
    }

    servicePrincipalProfile: {
      clientId: 'msi'
    }

    storageProfile: {
      diskCSIDriver: {
        enabled: true
        version: 'v1'
      }

      fileCSIDriver: {
        enabled: true
      }

      snapshotController: {
        enabled: true
      }
    }

  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2017-05-01-preview' = {
  scope: main
  name: 'diag-${serviceType}'
  properties: {
    workspaceId: logAnalyticWorkspaceId
    logs: [for log in logs: {
      category: log
      enabled: true
    }]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource uai 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  location: location
  name: 'uai-${name}'
  tags: tags
}

/** Nested Modules **/
module dnsRoleAssignment 'utility/roleAssignments.bicep' = {
  name: 'dnsra-${resourceSuffix}-${timestamp}'
  scope: resourceGroup(dnsResourceGroupName)
  params: {
    principalId: uai.properties.principalId
    roleDefinitionIds: {
      'Private DNS Zone Contributor': 'b12aa53e-6015-4669-85d0-8515ebb3ae7f'
    }
  }
}

module netRoleAssignment 'utility/roleAssignments.bicep' = {
  name: 'netra-${resourceSuffix}-${timestamp}'
  scope: resourceGroup(networkingResourceGroupName)
  params: {
    principalId: uai.properties.principalId
    roleDefinitionIds: {
      'Network Contributor': '4d97b98b-1d4f-4787-a291-c67834d212e7'
    }
  }
}

@description('Resource for configuring the Key Vault metric alerts.')
module metricAlerts 'utility/metricAlerts.bicep' = {
  name: 'a-${main.name}-${timestamp}'
  params: {
    actionGroupId: actionGroupId
    alerts: alerts
    metricNamespace: 'Microsoft.ContainerService/managedClusters'
    nameSuffix: name
    serviceId: main.id
    tags: tags
  }
}

@description('Private endpoint for App Configuration')
module privateEndpoint 'utility/privateEndpoint.bicep' = {
  name: 'pe-${main.name}-${timestamp}'
  params: {
    groupId: 'management'
    location: location
    privateDnsZones: privateDnsZones
    subnetId: subnetIdPrivateEndpoint
    tags: tags

    service: {
      id: main.id
      name: main.name
    }
  }
}
