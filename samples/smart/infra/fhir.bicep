param createWorkspace bool
param createFhirService bool
param workspaceName string
param fhirServiceName string
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
  }

  tags: appTags
}

resource fhirExisting 'Microsoft.HealthcareApis/workspaces/fhirservices@2021-06-01-preview' existing = if (!createFhirService) {
  name: '${newOrExistingWorkspaceName}/${fhirServiceName}'
}

output fhirId string = createFhirService ? fhir.id : fhirExisting.id
