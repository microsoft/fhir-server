@description('The name of the API Management service instance')
param apiManagementServiceName string

@description('Location for API Management service instance.')
param location string = resourceGroup().location
 
@description('The pricing tier of this API Management service')
@allowed(['Developer', 'Standard', 'Premium'])
param sku string = 'Developer'

@description('The instance size of this API Management service.')
@allowed([1, 2])
param skuCount int = 1

@description('The name of the owner of the service')
@minLength(1)
param publisherName string

@description('The email address of the owner of the service')
@minLength(1)
param publisherEmail string

@description('Base URL of the FHIR Service')
param fhirBaseUrl string

@description('Base URL of the SMART Auth Function')
param smartAuthFunctionBaseUrl string

@description('Base URL of the export storage account')
param exportStorageAccountUrl string

@description('Instrumentation key for App Insights used with APIM')
param appInsightsInstrumentationKey string

@description('Core API Management Service Resources')
module apimService 'apiManagement/service.bicep' = {
  name: '${apiManagementServiceName}-service'
  params: {
    apiManagementServiceName: apiManagementServiceName
    location: location
    sku: sku
    skuCount: skuCount
    publisherName: publisherName
    publisherEmail: publisherEmail
    appInsightsInstrumentationKey: appInsightsInstrumentationKey
  }
}

@description('API Management Backends')
module apimBackends 'apiManagement/backends.bicep' = {
  name: '${apiManagementServiceName}-backends'
  params: {
    apiManagementServiceName: apimService.name
    fhirBaseUrl: fhirBaseUrl
    smartAuthFunctionBaseUrl: smartAuthFunctionBaseUrl
    backendStorageUrl: exportStorageAccountUrl
  }
}

@description('Configuration for SMART on FHIR APIs')
module apimSmartApi 'apiManagement/smartApi.bicep' = {
  name: '${apiManagementServiceName}-api-smart'
  params: {
    apiManagementServiceName: apimService.name
    fhirBaseUrl: fhirBaseUrl
    apimServiceLoggerId: apimService.outputs.serviceLoggerId
  }

  dependsOn: [ apimBackends ]
}

@description('API Management Named Values (configuration)')
module apimNamedValues 'apiManagement/namedValues.bicep' = {
  name: '${apiManagementServiceName}-named-values'
  params: {
    apiManagementServiceName: apimService.name
    tenantId: subscription().tenantId
  }
}

@description('API Management Policy Fragments')
module apimFragments 'apiManagement/fragments.bicep' = {
  name: '${apiManagementServiceName}-named-values'
  params: {
    apiManagementServiceName: apimService.name
  }
}

output apimSmartUrl string = 'https://${apimService.name}.azure-api.net/smart'
