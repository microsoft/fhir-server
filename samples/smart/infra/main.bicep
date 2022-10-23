targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the the environment which is a prefix for all resources')
param name string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''

param smartAudience string

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: '${name}-rg'
  location: location
}

module template 'core.bicep'= {
  name: 'main'
  scope: resourceGroup
  params: {
    prefixName: name
    workspaceName: '${replace(name, '-', '')}ahds'
    fhirServiceName: 'fhirdata'
    location: location
    fhirContributorPrincipals: [principalId]
    smartAudience: smartAudience
  }
}

// These map to user secrets for local execution of the program
output LOCATION string = location
output SmartFhirEndpoint string = template.outputs.SmartFhirEndpoint
output TenantId string = template.outputs.TenantId
