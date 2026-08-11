param environmentName string
param location string = resourceGroup().location

@description('Name of the Log Analytics workspace to create and attach to the ACA environment for centralized log storage.')
param logAnalyticsWorkspaceName string = '${environmentName}-law'

@description('Retention period (in days) for Log Analytics data.')
param logAnalyticsRetentionInDays int = 30

module logAnalytics 'modules/log-analytics.bicep' = {
  name: '${environmentName}-log-analytics'
  params: {
    workspaceName: logAnalyticsWorkspaceName
    location: location
    retentionInDays: logAnalyticsRetentionInDays
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.outputs.customerId
        sharedKey: logAnalytics.outputs.primarySharedKey
      }
    }
  }
}

output logAnalyticsWorkspaceId string = logAnalytics.outputs.id
output logAnalyticsWorkspaceCustomerId string = logAnalytics.outputs.customerId
