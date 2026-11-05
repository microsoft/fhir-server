$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ResourceIdProfile.ps1"
$groupId = '/subscriptions/test/resourceGroups/msh-fhir-pr-42-123'
$variantId = "$groupId/providers/Microsoft.App/containerApps/$(Get-LongResourceIdAppName R4 PR 123)"
$baselineId = "$groupId/providers/Microsoft.App/containerApps/f123-123-r4-sql"
$databaseId = "$groupId/providers/Microsoft.Sql/servers/f123-123/databases/FHIRR4"
$roleId = "$groupId/providers/Microsoft.KeyVault/vaults/f123-r4-sql/providers/Microsoft.Authorization/roleAssignments/$(Get-LongResourceIdRoleName $variantId)"
$global:ResourceIdCleanupFixture = @{
    Deleted = [Collections.Generic.List[string]]::new()
    Resources = @($variantId, $baselineId, $databaseId)
    BuildId = '123'
    RolePresent = $true
}

function Get-AzResourceGroup {
    [CmdletBinding()] param($Name)
    return @{ ResourceId = $groupId }
}
function Get-AzResource {
    [CmdletBinding()] param($ResourceGroupName, $ResourceType)
    if ($ResourceType) {
        if ($global:ResourceIdCleanupFixture.Resources -contains $variantId) {
            return @{ Name = 'f123-r4-l128'; ResourceId = $variantId; Tags = @{ buildId = $global:ResourceIdCleanupFixture.BuildId; baselineApp = 'f123-123-r4-sql'; resourceIdProfile = 'Long128' } }
        }
        return @()
    }
    return @($global:ResourceIdCleanupFixture.Resources | ForEach-Object { @{ ResourceId = $_ } })
}
function Get-AzResourceGroupDeployment {
    [CmdletBinding()] param($ResourceGroupName, $Name)
    return @{ Outputs = @{ keyVaultRoleId = @{ Value = $roleId } } }
}
function Get-AzRoleAssignment {
    [CmdletBinding()] param($Scope)
    if ($global:ResourceIdCleanupFixture.RolePresent) { return @{ RoleAssignmentId = $roleId } }
    return @()
}
function Remove-AzResource {
    [CmdletBinding()] param($ResourceId, [switch]$Force)
    $global:ResourceIdCleanupFixture.Deleted.Add($ResourceId)
    $global:ResourceIdCleanupFixture.Resources = @($global:ResourceIdCleanupFixture.Resources | Where-Object { $_ -ne $ResourceId })
    if ($ResourceId -eq $roleId) { $global:ResourceIdCleanupFixture.RolePresent = $false }
}
$cleanupArgs = @{
    Version = 'R4'; ResourceGroupName = 'msh-fhir-pr-42-123'
    DeploymentEnvironmentName = 'f123-123'; PrNumber = '42'; BuildId = '123'
}
try {
    $global:ResourceIdCleanupFixture.BuildId = '999'
    $failure = $null
    try { & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @cleanupArgs } catch { $failure = $_ }
    if ($null -eq $failure -or $failure.Exception.Message -notlike '*Variant ownership*' -or $global:ResourceIdCleanupFixture.Deleted.Count -ne 0) {
        throw 'Cleanup must reject a variant owned by another build before any deletion.'
    }
    $global:ResourceIdCleanupFixture.BuildId = '123'
    & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @cleanupArgs
    $expected = @($roleId, $variantId, $baselineId, $databaseId)
    if (($global:ResourceIdCleanupFixture.Deleted -join ',') -cne ($expected -join ',')) { throw 'Cleanup must remove variant resources, then baseline, then the database exactly once.' }
    & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @cleanupArgs
    if ($global:ResourceIdCleanupFixture.Deleted.Count -ne 4) { throw 'A cleanup retry must not delete an already absent resource.' }
    Write-Host 'PASS cleanup ownership, variant-first order, single shared database deletion, and idempotent retry.'
} finally {
    Remove-Variable ResourceIdCleanupFixture -Scope Global
}
