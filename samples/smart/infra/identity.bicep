param fhirId string
param principalId string
param principalType string = 'ServicePrincipal'

@allowed(['fhirContributor', 'storageBlobContributor'])
param roleType string

@description('This is the built-in FHIR Data Contributor role. See https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#fhir-data-contributor')
resource fhirContributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: '5a1fc7df-4bf1-4951-a576-89034ee01acd'
}

@description('This is the built-in Storage Blob Data Contributor role. See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#storage-blob-data-contributor')
resource storageBlobDataControbutorRole 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource fhirDataContributorAccess 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  if (roleType == 'fhirContributor') {
  name: guid(fhirId, principalId, fhirContributorRoleDefinition.id)
  properties: {
    roleDefinitionId: fhirContributorRoleDefinition.id
    principalId: principalId
    principalType: principalType
  }
}

resource storageBlobContributorAccess 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  if (roleType == 'storageBlobContributor') {
  name: guid(fhirId, principalId, storageBlobDataControbutorRole.id)
  properties: {
    roleDefinitionId: storageBlobDataControbutorRole.id
    principalId: principalId
    principalType: principalType
  }
}
