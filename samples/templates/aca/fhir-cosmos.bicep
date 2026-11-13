// FHIR Server on ACA with Cosmos DB datastore.

// ──────────────────────────────────────────────
// Parameters
// ──────────────────────────────────────────────

@description('Name of the Azure Container App hosting FHIR.')
param containerAppName string

@description('Name of the Container Apps Environment.')
param containerAppsEnvironmentName string

@description('Name of the Key Vault used by FHIR server.')
@maxLength(24)
param keyVaultName string

@description('FHIR version to deploy.')
@allowed(['Stu3', 'R4', 'R4B', 'R5'])
param fhirVersion string = 'R4'

@description('Container registry server host name.')
param registryName string

@description('Docker image tag.')
param imageTag string = 'latest'

@description('Name of the existing Cosmos DB account.')
param cosmosDbAccountName string

@description('Authority URL for AAD authentication.')
param securityAuthenticationAuthority string = ''

@description('Audience for AAD authentication.')
param securityAuthenticationAudience string = ''

@description('Resource ID of UAMI used to pull from ACR.')
param acrPullUserAssignedManagedIdentityResourceId string

@description('Enable export operations.')
param enableExport bool = true

@description('Enable import operations.')
param enableImport bool = true

@description('Enable ConvertData operations.')
param enableConvertData bool = true

@description('Enable reindex operations.')
param enableReindex bool = true

@description('Minimum number of replicas.')
param minReplicas int = 0

@description('Maximum number of replicas.')
param maxReplicas int = 8

@description('CPU cores per container.')
param containerCpu string = '0.5'

@description('Memory per container.')
param containerMemory string = '1Gi'

@description('Additional environment variables from pipeline.')
param additionalEnvVars array = []

// ──────────────────────────────────────────────
// Variables
// ──────────────────────────────────────────────

var normalizedCosmosDbAccountName = toLower(cosmosDbAccountName)

var userAssignedIdentities = {
  '${acrPullUserAssignedManagedIdentityResourceId}': {}
}

var datastoreEnvVars = [
  { name: 'DataStore', value: 'CosmosDb' }
  { name: 'CosmosDb__UseManagedIdentity', value: 'true' }
  { name: 'CosmosDb__ContinuationTokenSizeLimitInKb', value: '1' }
  { name: 'CosmosDb__UseQueueClientJobs', value: 'true' }
  {
    name: 'FhirServer__ResourceManager__DataStoreResourceId'
    value: resourceId('Microsoft.DocumentDB/databaseAccounts', normalizedCosmosDbAccountName)
  }
]

// ──────────────────────────────────────────────
// Modules
// ──────────────────────────────────────────────

module common 'modules/fhir-common.bicep' = {
  name: '${toLower(containerAppName)}-common'
  params: {
    containerAppName: containerAppName
    containerAppsEnvironmentName: containerAppsEnvironmentName
    keyVaultName: keyVaultName
    fhirVersion: fhirVersion
    registryName: registryName
    imageTag: imageTag
    securityAuthenticationAuthority: securityAuthenticationAuthority
    securityAuthenticationAudience: securityAuthenticationAudience
    acrPullUserAssignedManagedIdentityResourceId: acrPullUserAssignedManagedIdentityResourceId
    enableExport: enableExport
    enableImport: enableImport
    enableConvertData: enableConvertData
    enableReindex: enableReindex
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    containerCpu: containerCpu
    containerMemory: containerMemory
    additionalEnvVars: additionalEnvVars
    datastoreEnvVars: datastoreEnvVars
    userAssignedIdentities: userAssignedIdentities
  }
}

// ──────────────────────────────────────────────
// Cosmos-specific resources
// ──────────────────────────────────────────────

resource existingCosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2021-06-15' existing = {
  name: normalizedCosmosDbAccountName
}

resource cosmosDbHostSecret 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = {
  name: '${toLower(keyVaultName)}/CosmosDb--Host'
  properties: {
    contentType: 'text/plain'
    value: existingCosmosDbAccount.properties.documentEndpoint
  }
}

// ──────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────

output containerAppName string = common.outputs.containerAppName
output containerAppFqdn string = common.outputs.containerAppFqdn
output containerAppUrl string = common.outputs.containerAppUrl
output storageAccountName string = common.outputs.storageAccountName
output exportStorageUri string = common.outputs.exportStorageUri
output integrationStorageUri string = common.outputs.integrationStorageUri
