param apiManagementServiceName string
param fhirBaseUrl string
param smartAuthFunctionBaseUrl string

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

output fhirBackendId string = fhirBackend.id
output smartAuthFunctionBackendId string = smartAuthFunctionBackend.id
