param createWorkspace bool
param createFhirService bool
param workspaceName string
param fhirServiceName string
param exportStoreName string
param tenantId string
param location string
param appTags object = {}

var loginURL = environment().authentication.loginEndpoint
var authority = '${loginURL}${tenantId}'
var audience = 'https://${workspaceName}-${fhirServiceName}.fhir.azurehealthcareapis.com'

resource healthWorkspace 'Microsoft.HealthcareApis/workspaces@2021-06-01-preview' = if (createWorkspace) {
  name: workspaceName
  location: location
  tags: appTags
}

resource healthWorkspaceExisting 'Microsoft.HealthcareApis/workspaces@2021-06-01-preview' existing = if (!createWorkspace) {
  name: workspaceName
}
var newOrExistingWorkspaceName = createWorkspace ? healthWorkspace.name : healthWorkspaceExisting.name

resource fhir 'Microsoft.HealthcareApis/workspaces/fhirservices@2021-06-01-preview' = if (createFhirService) {
  name: '${newOrExistingWorkspaceName}/${fhirServiceName}'
  location: location
  kind: 'fhir-R4'

  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    authenticationConfiguration: {
      authority: authority
      audience: audience
      smartProxyEnabled: false
    }
    exportConfiguration: {
      storageAccountName: exportStorageAccount.name
    }
  }

  tags: appTags
}

@description('FHIR Export required linked storage account')
resource exportStorageAccount 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: exportStoreName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: appTags
}

module exportFhirRoleAssignment './identity.bicep'= {
  name: 'fhirExportRoleAssignment'
  params: {
    principalId: createFhirService ? fhir.identity.principalId : fhirExisting.identity.principalId
    fhirId: createFhirService ? fhir.id : fhirExisting.id
    roleType: 'storageBlobContributor'
  }
}

resource fhirExisting 'Microsoft.HealthcareApis/workspaces/fhirservices@2021-06-01-preview' existing = if (!createFhirService) {
  name: '${newOrExistingWorkspaceName}/${fhirServiceName}'
}

output fhirId string = createFhirService ? fhir.id : fhirExisting.id
output fhirIdentity string = createFhirService ? fhir.identity.principalId : fhirExisting.identity.principalId
output exportStorageUrl string = exportStorageAccount.properties.primaryEndpoints.blob
