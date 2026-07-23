// Static OIDC discovery and JWKS host for remote SMART E2E tests.

var storageAccountName = 'fhirsmart${uniqueString(resourceGroup().id)}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: resourceGroup().location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: true
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

#disable-next-line BCP081
resource staticWebsite 'Microsoft.Storage/storageAccounts/staticWebsite@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    enabled: true
    indexDocument: 'index.html'
  }
}

output issuer string = storageAccount.properties.primaryEndpoints.web
output storageAccountName string = storageAccount.name
