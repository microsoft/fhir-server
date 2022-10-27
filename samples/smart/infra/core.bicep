@description('Prefix for resources deployed by this solution (App Service, Function App, monitoring, etc)')
param prefixName string

var prefixNameClean = replace(prefixName, '-', '')
var prefixNameCleanShort = length(prefixNameClean) > 16 ? substring(prefixNameClean, 0, 16) : prefixNameClean

@description('Do you want to create a new Azure Health Data Services workspace or use an existing one?')
param createWorkspace bool = true

@description('Do you want to create a new FHIR Service or use an existing one?')
param createFhirService bool = true

@description('Name of Azure Health Data Services workspace to deploy or use.')
param workspaceName string

@description('Name of the FHIR service to deloy or use.')
param fhirServiceName string

@description('AAD Audience of app with SMART scopes')
param smartAudience string

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

@description('Name of the Log Analytics workspace to deploy or use. Leave blank to skip deployment')
param logAnalyticsName string = '${prefixName}-la'

@description('Location to deploy resources')
param location string = resourceGroup().location

@description('ID of principals to give FHIR Contributor on the FHIR service')
param fhirContributorPrincipals array = []

@description('ID of principals to give KeyVault Writer access to')
param keyVaultWriterPrincipals array = []

@description('Any custom function app settings')
param functionAppCustomSettings object = {}

@description('Tenant ID where resources are deployed')
var tenantId  = subscription().tenantId

@description('Tags for all Azure resources in the solution')
var appTags = {
    AppID: 'fhir-smart-onc-g10-sample'
  }

@description('Name for the storage account needed for the Function App')
var exportStoreName = '${prefixNameCleanShort}expsa'

@description('Deploy Azure Health Data Services and FHIR service')
module fhir './fhir.bicep'= {
  name: 'fhirDeploy'
  params: {
    createWorkspace: createWorkspace
    createFhirService: createFhirService
    workspaceName: workspaceName
    fhirServiceName: fhirServiceName
    exportStoreName: exportStoreName
    location: location
    tenantId: tenantId
    appTags: appTags
  }
}

@description('Name for app insights resource used to monitor the Function App')
var appInsightsName = '${prefixName}-appins'

@description('Deploy monitoring and logging')
module monitoring './monitoring.bicep'= {
  name: 'monitoringDeploy'
  params: {
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    location: location
    appTags: appTags
  }
}

@description('Name for the App Service used to host the Function App.')
var appServiceName = '${prefixName}-appserv'

@description('Name for the Function App to deploy the SDK custom operations to.')
var functionAppName = '${prefixName}-func'

@description('Name for the storage account needed for the Function App')
var funcStorName = '${prefixNameCleanShort}funcsa'

var fhirUrl = 'https://${workspaceName}-${fhirServiceName}.fhir.azurehealthcareapis.com'

var functionParams = union(
  {
    AZURE_BackendFhirUrl: fhirUrl
    AZURE_SmartFhirEndpoint: 'https://${apimName}.azure-api.net/smart'
    AZURE_InstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    AZURE_TenantId: tenantId
    Azure_Audience: length(smartAudience) > 0 ? smartAudience : fhirUrl
  }, 
  functionAppCustomSettings, 
  length(testBackendClientId) > 0 ? {
    AZURE_TestBackendClientId: testBackendClientId
  } : {},
  length(testBackendClientSecret) > 0 ? {
    AZURE_TestBackendClientSecret: testBackendClientSecret
  } : {},
  length(testBackendClientJwks) > 0 ? {
    AZURE_TestBackendClientJwks: testBackendClientJwks
  } : {}
)

@description('Deploy Azure Function to run SDK custom operations')
module function './azureFunction.bicep'= {
  name: 'functionDeploy'
  params: {
    appServiceName: appServiceName
    functionAppName: functionAppName
    storageAccountName: funcStorName
    location: location
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    functionSettings: functionParams
    appTags: appTags
  }
}

@description('Setup identity connection between FHIR and the function app')
module functionFhirIdentity './identity.bicep'= {
  name: 'fhirIdentity-function'
  params: {
    fhirId: fhir.outputs.fhirId
    principalId: function.outputs.functionAppPrincipalId
    roleType: 'fhirContributor'
  }
}

@description('Setup identity connection between FHIR and the function app')
module specifiedIdentity './identity.bicep' =  [for principalId in  fhirContributorPrincipals: {
  name: 'fhirIdentity-${principalId}'
  params: {
    fhirId: fhir.outputs.fhirId
    principalId: principalId
    principalType: 'User'
    roleType: 'fhirContributor'
  }
}]

var apimName = '${prefixName}-apim'

@description('Deploy Azure API Management for the FHIR gateway')
module apim './apiManagement.bicep'= {
  name: 'apiManagementDeploy'
  params: {
    apiManagementServiceName: apimName
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
    location: location
    fhirBaseUrl: 'https://${workspaceName}-${fhirServiceName}.fhir.azurehealthcareapis.com'
    smartAuthFunctionBaseUrl: 'https://${function.outputs.hostName}/api'
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    exportStorageAccountUrl: fhir.outputs.exportStorageUrl
  }
}

var backendServiceVaultName = '${prefixName}-backkv'
@description('KeyVault to hold backend service principal maps')
module keyVault './keyVault.bicep' = {
  name: 'vaultDeploy'
  params: {
    vaultName: backendServiceVaultName
    location: location
    tenantId: tenantId
    writerObjectIds: keyVaultWriterPrincipals
    readerObjectIds: [ function.outputs.functionAppPrincipalId ]
  }
}

output SmartFhirEndpoint string = apim.outputs.apimSmartUrl
output FhirServiceId string = fhir.outputs.fhirId
output FhirServiceUrl string = 'https://${workspaceName}-${fhirServiceName}.fhir.azurehealthcareapis.com'
output TenantId string = tenantId
