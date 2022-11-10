param apiManagementServiceName string
param fhirBaseUrl string
param smartAuthFunctionBaseUrl string
param exportFunctionBaseUrl string
param contextStaticAppBaseUrl string

resource fhirBackend 'Microsoft.ApiManagement/service/backends@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/fhir'
  properties: {
    url: fhirBaseUrl
    protocol: 'http'
  }
}

resource smartAuthFunctionBackend 'Microsoft.ApiManagement/service/backends@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/smartAuth'
  properties: {
    url: smartAuthFunctionBaseUrl
    protocol: 'http'
  }
}

resource exportFunctionBackend 'Microsoft.ApiManagement/service/backends@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/export'
  properties: {
    url: exportFunctionBaseUrl
    protocol: 'http'
  }
}

resource contextStaticAppBackend 'Microsoft.ApiManagement/service/backends@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/contextStaticApp'
  properties: {
    url: contextStaticAppBaseUrl
    protocol: 'http'
  }
}

output fhirBackendId string = fhirBackend.id
output smartAuthFunctionBackendId string = smartAuthFunctionBackend.id
output exportFunctionBackendId string = exportFunctionBackend.id
output contextStaticAppBaseUrlId string = contextStaticAppBackend.id
