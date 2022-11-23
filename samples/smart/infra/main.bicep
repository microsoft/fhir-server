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

@description('Audience for SMART scopes in Azure Active Directory. Leave blank to use the PaaS Service URL.')
param smartAudience string = ''

@description('Name of the owner of the API Management resource')
param apimPublisherName string

@description('Email of the owner of the API Management resource')
param apimPublisherEmail string

param contextAadApplicationId string

@description('Tags for all Azure resources in the solution')
var appTags = {
  AppID: 'fhir-smart-onc-g10-sample'
  'azd-env-name': name
}

@description('Resource group to deploy sample in.')
resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: '${name}-rg'
  location: location
  tags: appTags
}

@description('Core deployment used to house the resource group scoped resources.')
module template 'core.bicep'= {
  name: 'main'
  scope: resourceGroup
  params: {
    prefixName: name
    workspaceName: '${replace(name, '-', '')}ahds'
    fhirServiceName: 'fhirdata'
    location: location
    fhirContributorPrincipals: [principalId]
    fhirSMARTPrincipals: [principalId]
    keyVaultWriterPrincipals: [principalId]
    smartAudience: smartAudience
    apimPublisherName: apimPublisherName
    apimPublisherEmail: apimPublisherEmail
    appTags: appTags
  }
}

// These map to user secrets for local execution of the program
output LOCATION string = location
output FhirServerUrl string = template.outputs.FhirServerUrl
output ExportStorageAccountUrl string = template.outputs.ExportStorageAccountUrl
output ApiManagementHostName string = template.outputs.ApiManagementHostName
output BackendServiceKeyVaultStore string = template.outputs.BackendServiceKeyVaultStore
output Audience string = template.outputs.Audience
output TenantId string = template.outputs.TenantId

output REACT_APP_AAD_APP_CLIENT_ID string = contextAadApplicationId
output REACT_APP_AAD_APP_TENANT_ID string = template.outputs.TenantId
output REACT_APP_API_BASE_URL string = 'https://${template.outputs.ApiManagementHostName}/smart'
