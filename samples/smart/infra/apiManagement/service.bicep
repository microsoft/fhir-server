param apiManagementServiceName string
param location string
param sku string
param skuCount int
param publisherName string
param publisherEmail string
param appInsightsInstrumentationKey string

resource apim 'Microsoft.ApiManagement/service@2021-12-01-preview' = {
  name: apiManagementServiceName
  location: location
  sku: {
    name: sku
    capacity: skuCount
  }

  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }

  resource apimLogger 'loggers' = {
    name: 'appinsights'
    properties: {
      loggerType: 'applicationInsights'
      credentials: {
        appInsightsInstrumentationKey: appInsightsInstrumentationKey
        instrumentationKey: appInsightsInstrumentationKey
      }
      isBuffered: true
    }
  }

  resource apimDiagnostics 'diagnostics' = {
    name: 'applicationinsights'
    properties: {
      alwaysLog: 'allErrors'
      httpCorrelationProtocol: 'W3C'
      logClientIp: true
      loggerId: apimLogger.id
      sampling: {
        samplingType: 'fixed'
        percentage: 100
      }
      frontend: {
        request: {
          dataMasking: {
            queryParams: [
              {
                value: '*'
                mode: 'Hide'
              }
            ]
          }
        }
      }
      backend: {
        request: {
          dataMasking: {
            queryParams: [
              {
                value: '*'
                mode: 'Hide'
              }
            ]
          }
        }
      }
    }
  }
}

output name string = apim.name
output serviceLoggerId string = apim::apimLogger.id
