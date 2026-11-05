@minLength(2)
@maxLength(32)
param containerAppName string
param location string = resourceGroup().location
param managedEnvironmentId string
param userAssignedIdentities object
param keyVaultName string
param keyVaultRoleName string
param ownershipTags object

@secure()
param configuration object

@secure()
param template object

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: ownershipTags
  identity: {
    type: 'SystemAssigned,UserAssigned'
    userAssignedIdentities: userAssignedIdentities
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    configuration: configuration
    template: template
  }
}

resource vault 'Microsoft.KeyVault/vaults@2022-07-01' existing = {
  name: keyVaultName
}

resource appSystemIdentityVaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: keyVaultRoleName
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output containerAppId string = app.id
output keyVaultRoleId string = appSystemIdentityVaultReader.id
