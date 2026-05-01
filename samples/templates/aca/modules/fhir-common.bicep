// Common FHIR ACA module — shared resources for both SQL and Cosmos deployments.

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

@description('Datastore-specific environment variables (SQL or Cosmos settings).')
param datastoreEnvVars array = []

@description('User-assigned managed identities object to attach to the container app.')
param userAssignedIdentities object

// ──────────────────────────────────────────────
// Variables
// ──────────────────────────────────────────────

var normalizedAppName = toLower(containerAppName)
var normalizedEnvName = toLower(containerAppsEnvironmentName)
var normalizedKeyVaultName = toLower(keyVaultName)

var isMAG = contains(resourceGroup().location, 'usgov') || contains(resourceGroup().location, 'usdod')

var imageRepositoryName = contains(registryName, 'mcr.')
  ? '${toLower(fhirVersion)}-fhir-server'
  : '${toLower(fhirVersion)}_fhir-server'
var containerImage = '${registryName}/${imageRepositoryName}:${imageTag}'

var blobStorageUri = isMAG ? '.blob.core.usgovcloudapi.net' : '.blob.${environment().suffixes.storage}'
var storageAccountPrefix = substring(replace(normalizedAppName, '-', ''), 0, min(11, length(replace(normalizedAppName, '-', ''))))
var storageAccountName = '${storageAccountPrefix}${uniqueString(resourceGroup().id, normalizedAppName)}'
var storageAccountUri = 'https://${storageAccountName}${blobStorageUri}'

var keyVaultEndpoint = isMAG
  ? 'https://${normalizedKeyVaultName}.vault.usgovcloudapi.net/'
  : 'https://${normalizedKeyVaultName}${environment().suffixes.keyvaultDns}/'

var enableIntegrationStore = enableExport || enableImport

var sharedEnvVars = [
  { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
  { name: 'KeyVault__Endpoint', value: keyVaultEndpoint }
  { name: 'FhirServer__Security__Enabled', value: 'true' }
  { name: 'FhirServer__Security__EnableAadSmartOnFhirProxy', value: 'true' }
  { name: 'FhirServer__Security__Authentication__Authority', value: securityAuthenticationAuthority }
  { name: 'FhirServer__Security__Authentication__Audience', value: securityAuthenticationAudience }
  { name: 'TaskHosting__Enabled', value: 'true' }
  { name: 'TaskHosting__PollingFrequencyInSeconds', value: '1' }
  { name: 'TaskHosting__MaxRunningTaskCount', value: '2' }
  { name: 'FhirServer__CoreFeatures__SearchParameterCacheRefreshIntervalSeconds', value: '2' }
  { name: 'FhirServer__Operations__Export__Enabled', value: enableExport ? 'true' : 'false' }
  { name: 'FhirServer__Operations__Export__StorageAccountUri', value: enableExport ? storageAccountUri : 'null' }
  { name: 'FhirServer__Operations__Import__Enabled', value: enableImport ? 'true' : 'false' }
  { name: 'FhirServer__Operations__IntegrationDataStore__StorageAccountUri', value: enableImport ? storageAccountUri : 'null' }
  { name: 'FhirServer__Operations__ConvertData__Enabled', value: enableConvertData ? 'true' : 'false' }
  { name: 'FhirServer__Operations__ConvertData__ContainerRegistryServers__0', value: enableConvertData ? registryName : 'null' }
  { name: 'FhirServer__Operations__Reindex__Enabled', value: enableReindex ? 'true' : 'false' }
]

var allEnvVars = concat(sharedEnvVars, datastoreEnvVars, additionalEnvVars)

// ──────────────────────────────────────────────
// Resources
// ──────────────────────────────────────────────

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: normalizedAppName
  location: resourceGroup().location
  identity: {
    type: 'SystemAssigned,UserAssigned'
    userAssignedIdentities: userAssignedIdentities
  }
  properties: {
    managedEnvironmentId: resourceId('Microsoft.App/managedEnvironments', normalizedEnvName)
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        transport: 'auto'
      }
      registries: [
        {
          server: registryName
          identity: acrPullUserAssignedManagedIdentityResourceId
        }
      ]
    }
    template: {
      containers: [
        {
          name: normalizedAppName
          image: containerImage
          resources: {
            cpu: json(containerCpu)
            memory: containerMemory
          }
          env: allEnvVars
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/check'
                port: 8080
              }
              initialDelaySeconds: 20
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 6
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/check'
                port: 8080
              }
              initialDelaySeconds: 20
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 6
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
          {
            name: 'cpu-scaling'
            custom: {
              type: 'cpu'
              metadata: {
                type: 'Utilization'
                value: '70'
              }
            }
          }
        ]
      }
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: normalizedKeyVaultName
  location: resourceGroup().location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    accessPolicies: []
    enableRbacAuthorization: true
    enabledForDeployment: false
  }
}

resource keyVaultSecretsOfficerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString('KeyVaultSecretsOfficer', normalizedAppName, normalizedKeyVaultName))
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: containerApp.identity.principalId
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2019-06-01' = if (enableIntegrationStore) {
  name: storageAccountName
  location: resourceGroup().location
  kind: 'Storage'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    #disable-next-line BCP037
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
  }
}

resource storageBlobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableIntegrationStore) {
  name: guid(uniqueString(storageAccountName, normalizedAppName))
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ──────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────

output containerAppName string = containerApp.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output keyVaultName string = keyVault.name
output storageAccountName string = enableIntegrationStore ? storageAccountName : ''
output exportStorageUri string = enableExport ? storageAccountUri : 'null'
output integrationStorageUri string = enableImport ? storageAccountUri : 'null'
