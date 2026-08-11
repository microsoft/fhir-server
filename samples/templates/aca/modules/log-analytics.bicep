@description('Name of the Log Analytics workspace.')
param workspaceName string

@description('Azure region for the Log Analytics workspace.')
param location string = resourceGroup().location

@description('Retention period in days.')
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

output name string = workspace.name
output id string = workspace.id
output customerId string = workspace.properties.customerId

@secure()
output primarySharedKey string = workspace.listKeys().primarySharedKey
