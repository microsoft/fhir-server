param apiManagementServiceName string

resource tenantIdNamedValue 'Microsoft.ApiManagement/service/policyFragments@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/set-oid-header-from-token'
  properties: {
    format: 'rawxml'
    value: loadTextContent('./fragments/set-oid-header-from-token.xml')
  }
}
