param fhirId string
param principalId string
param principalType string = 'ServicePrincipal'

@description('This is the built-in FHIR Data Contributor role. See https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#fhir-data-contributor')
resource fhirContributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: '5a1fc7df-4bf1-4951-a576-89034ee01acd'
}

resource fhirDataContributorAccess 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  {
  name: guid(fhirId, principalId, fhirContributorRoleDefinition.id)
  properties: {
    roleDefinitionId: fhirContributorRoleDefinition.id
    principalId: principalId
    principalType: principalType
  }
}
