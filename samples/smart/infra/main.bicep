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

@description('Audience for SMART scopes in Azure Active Directory')
param smartAudience string = 'https://fhir.azurehealthcareapis.com'

@description('Name of the owner of the API Management resource')
param apimPublisherName string

@description('Email of the owner of the API Management resource')
param apimPublisherEmail string

@description('Client ID for single principal based JWKS backend auth')
param testBackendClientId string

@secure()
@description('Client Secret for single principal based JWKS backend auth')
param testBackendClientSecret string

@description('JWKS URL for single principal based JWKS backend auth')
param testBackendClientJwks string

@description('Resource group to deploy sample in.')
resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: '${name}-rg'
  location: location
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
    keyVaultWriterPrincipals: [principalId]
    smartAudience: smartAudience
    apimPublisherName: apimPublisherName
    apimPublisherEmail: apimPublisherEmail
    testBackendClientId: testBackendClientId
    testBackendClientSecret: testBackendClientSecret
    testBackendClientJwks: testBackendClientJwks
  }
}

// These map to user secrets for local execution of the program
output LOCATION string = location
output SmartFhirEndpoint string = template.outputs.SmartFhirEndpoint
output TenantId string = template.outputs.TenantId
