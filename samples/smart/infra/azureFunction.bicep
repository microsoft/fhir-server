param storageAccountName string
param appServiceName string
param functionAppName string
param appInsightsInstrumentationKey string
param appInsightsConnectionString string
param location string
param functionSettings object = {}
param appTags object = {}

@description('Azure Function required linked storage account')
resource funcStorageAccount 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: appTags
}

@description('App Service used to run Azure Function')
resource hostingPlan 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: appServiceName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'S1'
  }
  properties: {}
  tags: appTags
}

@description('Azure Function used to run toolkit compute')
resource functionApp 'Microsoft.Web/sites@2021-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'

  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    httpsOnly: true
    enabled: true
    serverFarmId: hostingPlan.id
    clientAffinityEnabled: false
    siteConfig: {
      alwaysOn:true
    }
  }

  tags: union(appTags, {
    'azd-service-name': 'SMARTProxy'
  })
}

resource fhirProxyAppSettings 'Microsoft.Web/sites/config@2020-12-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: union({
    AzureWebJobsStorage: 'DefaultEndpointsProtocol=https;AccountName=${funcStorageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${funcStorageAccount.listKeys().keys[0].value}'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    APPINSIGHTS_INSTRUMENTATIONKEY: appInsightsInstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
    SCM_DO_BUILD_DURING_DEPLOYMENT: 'true'
  }, functionSettings)
}

resource funcTableService 'Microsoft.Storage/storageAccounts/tableServices@2022-05-01' = {
  name: 'default'
  parent: funcStorageAccount
}

resource symbolicname 'Microsoft.Storage/storageAccounts/tableServices/tables@2022-05-01' = {
  name: 'jwksBackendService'
  parent: funcTableService
}

output functionAppName string = functionAppName
output functionAppPrincipalId string = functionApp.identity.principalId
output hostName string = functionApp.properties.defaultHostName
