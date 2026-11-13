$ErrorActionPreference = 'Stop'
$groupId = '/subscriptions/test/resourceGroups/msh-fhir-pr-42-123'
$appId = "$groupId/providers/Microsoft.App/containerApps/f123-123-r4-sql"
$databaseId = "$groupId/providers/Microsoft.Sql/servers/f123-123/databases/FHIRR4"
$foreignId = "$groupId/providers/Microsoft.App/containerApps/f123-123-r5-sql"
$global:ResourceIdCleanupFixture = @{
    Deleted = [Collections.Generic.List[string]]::new()
    Resources = @($appId, $databaseId, $foreignId)
    LoseResponseFor = $null
}

function Start-Sleep { param($Seconds) }
function Get-AzResourceGroup {
    [CmdletBinding()] param($Name)
    return @{ ResourceId = $groupId }
}
function Get-AzResource {
    [CmdletBinding()] param($ResourceGroupName, $ResourceType)
    return @($global:ResourceIdCleanupFixture.Resources |
        Where-Object { -not $ResourceType -or $_ -like "*/providers/$ResourceType/*" } |
        ForEach-Object { [pscustomobject]@{ ResourceId = $_; Name = $_.Split('/')[-1]; Tags = @{} } })
}
function Get-AzResourceGroupDeployment {
    [CmdletBinding()] param($ResourceGroupName, $Name)
    throw 'Cleanup must not depend on a separate variant deployment.'
}
function Remove-AzResource {
    [CmdletBinding()] param($ResourceId, [switch]$Force)
    if ($global:ResourceIdCleanupFixture.Resources -notcontains $ResourceId) { throw "ResourceNotFound: $ResourceId" }
    $global:ResourceIdCleanupFixture.Deleted.Add($ResourceId)
    $global:ResourceIdCleanupFixture.Resources = @($global:ResourceIdCleanupFixture.Resources | Where-Object { $_ -ne $ResourceId })
    if ($global:ResourceIdCleanupFixture.LoseResponseFor -eq $ResourceId) {
        $global:ResourceIdCleanupFixture.LoseResponseFor = $null
        throw 'The delete response was lost after the service accepted it.'
    }
}
$cleanupArgs = @{
    Version = 'R4'; ResourceGroupName = 'msh-fhir-pr-42-123'
    DeploymentEnvironmentName = 'f123-123'; PrNumber = '42'; BuildId = '123'
}
try {
    $failure = $null
    $foreignBuildArgs = @{} + $cleanupArgs
    $foreignBuildArgs.BuildId = '999'
    try { & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @foreignBuildArgs } catch { $failure = $_ }
    if ($failure.Exception.Message -notlike '*current PR build*' -or $global:ResourceIdCleanupFixture.Deleted.Count -ne 0) {
        throw 'Cleanup must reject another build before any deletion.'
    }

    $global:ResourceIdCleanupFixture.LoseResponseFor = $appId
    & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @cleanupArgs
    if (($global:ResourceIdCleanupFixture.Deleted -join ',') -cne (@($appId, $databaseId) -join ',')) {
        throw "Cleanup must delete only the SQL app, then the shared version database, once each. Deleted: $($global:ResourceIdCleanupFixture.Deleted -join ',')"
    }
    if ($global:ResourceIdCleanupFixture.Resources -notcontains $foreignId) { throw 'Cleanup must not delete another version app.' }

    & "$PSScriptRoot/Cleanup-PrTestResources.ps1" @cleanupArgs
    if ($global:ResourceIdCleanupFixture.Deleted.Count -ne 2) { throw 'A cleanup retry must not delete an already absent resource.' }
    Write-Host 'PASS cleanup ownership, app-then-database order, lost delete response, and idempotent retry.'
} finally {
    Remove-Variable ResourceIdCleanupFixture -Scope Global
}
