// FHIR Server on ACA with SQL Server datastore.

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

@description('Existing SQL server name.')
param sqlServerName string

@description('Schema automatic updates mode.')
@allowed(['auto', 'tool'])
param sqlSchemaAutomaticUpdatesEnabled string = 'auto'

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

var normalizedSqlServerName = toLower(sqlServerName)
var sqlManagedIdentityName = '${normalizedSqlServerName}-uami'
var sqlDatabaseName = 'FHIR${fhirVersion}'

var sqlManagedIdentityResourceId = resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', sqlManagedIdentityName)

var userAssignedIdentities = {
  '${sqlManagedIdentityResourceId}': {}
  '${acrPullUserAssignedManagedIdentityResourceId}': {}
}

var datastoreEnvVars = [
  { name: 'DataStore', value: 'SqlServer' }
  { name: 'SqlServer__Initialize', value: 'true' }
  {
    name: 'SqlServer__SchemaOptions__AutomaticUpdatesEnabled'
    value: sqlSchemaAutomaticUpdatesEnabled == 'auto' ? 'true' : 'false'
  }
  { name: 'SqlServer__DeleteAllDataOnStartup', value: 'false' }
  { name: 'SqlServer__AllowDatabaseCreation', value: 'true' }
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
// SQL-specific resources
// ──────────────────────────────────────────────

resource existingSqlServer 'Microsoft.Sql/servers@2021-11-01' existing = {
  name: normalizedSqlServerName
}

resource existingSqlUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' existing = {
  name: sqlManagedIdentityName
}

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = {
  name: '${toLower(keyVaultName)}/SqlServer--ConnectionString'
  properties: {
    contentType: 'text/plain'
    value: 'Server=tcp:${existingSqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;Authentication=ActiveDirectoryMSI;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;User Id=${existingSqlUami.properties.clientId};'
  }
}

// ──────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────

output containerAppName string = common.outputs.containerAppName
output containerAppFqdn string = common.outputs.containerAppFqdn
output containerAppUrl string = common.outputs.containerAppUrl
output exportStorageUri string = common.outputs.exportStorageUri
output integrationStorageUri string = common.outputs.integrationStorageUri
