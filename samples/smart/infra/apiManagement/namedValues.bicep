param apiManagementServiceName string
param tenantId string
param contextStaticAppBaseUrl string

resource tenantIdNamedValue 'Microsoft.ApiManagement/service/namedValues@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/tenantId'
  properties: {
    displayName: 'TenantId'
    value: tenantId
  }
}

resource contextStaticAppNamedValue 'Microsoft.ApiManagement/service/namedValues@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/contextStaticAppBaseUrl'
  properties: {
    displayName: 'contextStaticAppBaseUrl'
    value: contextStaticAppBaseUrl
  }
}
