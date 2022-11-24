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

@description('ClientId for the context static app registration for this FHIR Service (you must create this)')
param contextAadApplicationId string

param appTags object

@description('Name of the Log Analytics workspace to deploy or use. Leave blank to skip deployment')
param logAnalyticsName string = '${prefixName}-la'

@description('Location to deploy resources')
param location string = resourceGroup().location

@description('ID of principals to give FHIR Contributor role assignment on the FHIR service')
param fhirContributorPrincipals array = []

@description('ID of principals to give FHIR SMART role assignment to on the FHIR service')
param fhirSMARTPrincipals array = []

@description('ID of principals to give KeyVault Writer access to')
param keyVaultWriterPrincipals array = []

@description('Tenant ID where resources are deployed')
var tenantId  = subscription().tenantId

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
var aadCustomOperationsAppServiceName = '${prefixName}-aad-as'

@description('Name for the Function App to deploy the SDK custom operations to.')
var aadCustomOperationsFunctionAppName = '${prefixName}-aad-func'

@description('Name for the storage account needed for the Function App')
var aadCustomOperationsFuncStorName = '${prefixNameCleanShort}aadfuncsa'

var fhirUrl = 'https://${workspaceName}-${fhirServiceName}.fhir.azurehealthcareapis.com'

var aadCustomOperationsFunctionParams = {
  AZURE_FhirServerUrl: fhirUrl
  AZURE_ApiManagementHostName: '${apimName}.azure-api.net'
  AZURE_InstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
  AZURE_TenantId: tenantId
  AZURE_Audience: length(smartAudience) > 0 ? smartAudience : fhirUrl
  AZURE_BackendServiceKeyVaultStore: backendServiceVaultName
  AZURE_ContextAppClientId: contextAadApplicationId
}

@description('Deploy Azure Function to run SDK custom operations')
module aadCustomOperationFunction './azureFunction.bicep'= {
  name: 'aadCustomOperationFunction'
  params: {
    appServiceName: aadCustomOperationsAppServiceName
    functionAppName: aadCustomOperationsFunctionAppName
    storageAccountName: aadCustomOperationsFuncStorName
    location: location
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    functionSettings: aadCustomOperationsFunctionParams
    appTags: appTags
    azdServiceName: 'auth'
    deployJwksTable: true
    siteConfig: {
      cors: {
        allowedOrigins: [
          contextStaticWebApp.outputs.uri
        ]
      }
    }
  }
}

@description('Name for the App Service used to host the Export Custom Operation Function App.')
var exportCustomOperationsAppServiceName = '${prefixName}-exp-as'

@description('Name for the Function App to deploy the Export Custom Operations to')
var exportCustomOperationsFunctionAppName = '${prefixName}-exp-func'

@description('Name for the storage account needed for the Function App')
var exportCustomOperationsFuncStorName = '${prefixNameCleanShort}expfuncsa'

var exportCustomOperationsFunctionParams = {
  AZURE_FhirServerUrl: fhirUrl
  AZURE_ExportStorageAccountUrl: 'https://${exportStoreName}.blob.${environment().suffixes.storage}'
  AZURE_ApiManagementHostName: '${apimName}.azure-api.net'
  AZURE_InstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
  AZURE_TenantId: tenantId
  Azure_Audience: length(smartAudience) > 0 ? smartAudience : fhirUrl
  AZURE_BackendServiceKeyVaultStore: backendServiceVaultName
}

@description('Deploy Azure Function to run export custom operations')
module exportCustomOperationFunction './azureFunction.bicep'= {
  name: 'exportCustomOperationFunction'
  params: {
    appServiceName: exportCustomOperationsAppServiceName
    functionAppName: exportCustomOperationsFunctionAppName
    storageAccountName: exportCustomOperationsFuncStorName
    location: location
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    functionSettings: exportCustomOperationsFunctionParams
    azdServiceName: 'export'
    appTags: appTags
  }
}

// #TODO - remove once SMART Scopes are properly working
@description('Setup identity connection between FHIR and the function app')
module functionFhirIdentity './identity.bicep'= {
  name: 'fhirIdentity-function-contributor'
  params: {
    fhirId: fhir.outputs.fhirId
    principalId: aadCustomOperationFunction.outputs.functionAppPrincipalId
    roleType: 'fhirContributor'
  }
}

@description('Setup identity connection between FHIR and the given contributors')
module fhirContributorIdentities './identity.bicep' =  [for principalId in  fhirContributorPrincipals: {
  name: 'fhirIdentity-${principalId}-fhirContrib'
  params: {
    fhirId: fhir.outputs.fhirId
    principalId: principalId
    principalType: 'User'
    roleType: 'fhirContributor'
  }
}]

@description('Setup identity connection between FHIR and the given SMART users')
module fhirSMARTIdentities './identity.bicep' =  [for principalId in  fhirSMARTPrincipals: {
  name: 'fhirIdentity-${principalId}-fhirSmart'
  params: {
    fhirId: fhir.outputs.fhirId
    principalId: principalId
    principalType: 'User'
    roleType: 'fhirSmart'
  }
}]

@description('Setup identity connection between Export functon app and export storage account')
module exportFhirRoleAssignment './identity.bicep'= {
  name: 'fhirExportRoleAssignment'
  params: {
    principalId: exportCustomOperationFunction.outputs.functionAppPrincipalId
    fhirId: fhir.outputs.fhirId
    roleType: 'storageBlobContributor'
  }
}

var apimName = '${prefixName}-apim'

@description('Deploy Azure API Management for the FHIR gateway')
module apim './apiManagement.bicep'= {
  name: 'apiManagementDeploy'
  params: {
    apiManagementServiceName: apimName
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
    location: location
    fhirBaseUrl: fhirUrl
    smartAuthFunctionBaseUrl: 'https://${aadCustomOperationFunction.outputs.hostName}/api'
    exportFunctionBaseUrl: 'https://${exportCustomOperationFunction.outputs.hostName}/api'
    contextStaticAppBaseUrl: contextStaticWebApp.outputs.uri
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
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
    readerObjectIds: [ aadCustomOperationFunction.outputs.functionAppPrincipalId ]
  }
}

var authorizeStaticWebAppName = '${prefixName}-contextswa'
@description('Static web app for authorize UI')
module contextStaticWebApp './staticWebApp.bicep' = {
  name: 'staticWebAppDeploy'
  params: {
    staticWebAppName: authorizeStaticWebAppName
    location: location
    appTags: union(appTags, {
      'azd-service-name': 'context'
    })
  }
}

output FhirServiceId string = fhir.outputs.fhirId
output ApiManagementHostName string = apim.outputs.apimHostName
output ExportStorageAccountUrl string =  'https://${exportStoreName}.blob.${environment().suffixes.storage}'
output FhirServerUrl string = fhirUrl
output TenantId string = tenantId
output Audience string = length(smartAudience) > 0 ? smartAudience : fhirUrl
output BackendServiceKeyVaultStore string = backendServiceVaultName
