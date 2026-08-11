// Shared monitoring module for Application Insights with existing Log Analytics workspace.

// ──────────────────────────────────────────────
// Parameters
// ──────────────────────────────────────────────

@description('Name of the Azure Container App the monitoring resources belong to.')
param containerAppName string

@description('Name of the Container Apps Environment the Container App runs in.')
param containerAppsEnvironmentName string

@description('Azure region for the monitoring resources.')
param location string = resourceGroup().location

@description('Name of the existing Log Analytics workspace backing Application Insights.')
param logAnalyticsWorkspaceName string = '${toLower(containerAppsEnvironmentName)}-law'

@description('Name of the Application Insights component.')
param applicationInsightsName string = 'AppInsights-${toLower(containerAppName)}'

// ──────────────────────────────────────────────
// Resources
// ──────────────────────────────────────────────

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}

// ──────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────

output applicationInsightsName string = applicationInsights.name
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsWorkspaceCustomerId string = logAnalyticsWorkspace.properties.customerId
