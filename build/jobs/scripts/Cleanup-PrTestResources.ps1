param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stu3', 'R4', 'R5')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string] $DeploymentEnvironmentName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+$')]
    [string] $PrNumber,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+$')]
    [string] $BuildId
)

$ErrorActionPreference = 'Stop'

if ($ResourceGroupName -ne "msh-fhir-pr-$PrNumber-$BuildId") {
    throw "Cleanup is restricted to the current PR build's resource group."
}
if ($DeploymentEnvironmentName -notmatch "^f[a-z0-9]+-$BuildId$") {
    throw "Deployment environment must belong to the current PR build."
}

. "$PSScriptRoot/../../../release/scripts/PowerShell/Invoke-WithRetry.ps1"
. "$PSScriptRoot/ResourceIdProfile.ps1"

$resourceGroup = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction Stop
$resourceGroupId = $resourceGroup.ResourceId
$environmentName = $DeploymentEnvironmentName.ToLowerInvariant()
$versionSuffix = if ($Version -eq 'Stu3') { '' } else { "-$($Version.ToLowerInvariant())" }
$appName = "$environmentName$versionSuffix-sql"
$variantName = Get-LongResourceIdAppName $Version PR $BuildId
$variantId = "$resourceGroupId/providers/Microsoft.App/containerApps/$variantName"
$variant = @(Get-AzResource -ResourceGroupName $ResourceGroupName -ResourceType 'Microsoft.App/containerApps' -ErrorAction Stop | Where-Object Name -EQ $variantName)
if ($variant.Count -gt 0 -and ($variant[0].Tags['buildId'] -ne $BuildId -or $variant[0].Tags['baselineApp'] -ne $appName -or $variant[0].Tags['resourceIdProfile'] -ne 'Long128')) {
    throw 'Variant ownership does not match the current PR build and baseline.'
}
$deployment = Get-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -Name $variantName -ErrorAction Stop
$roleId = [string]$deployment.Outputs.keyVaultRoleId.Value
if (-not $roleId.StartsWith("$resourceGroupId/providers/Microsoft.KeyVault/vaults/", [StringComparison]::OrdinalIgnoreCase) -or
    -not $roleId.EndsWith("/providers/Microsoft.Authorization/roleAssignments/$(Get-LongResourceIdRoleName $variantId)", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The variant deployment does not identify its owned vault reader.'
}

$resources = @(Get-AzResource -ResourceGroupName $ResourceGroupName -ErrorAction Stop)
$role = Get-AzRoleAssignment -Scope ($roleId -replace '/providers/Microsoft.Authorization/roleAssignments/[^/]+$', '') -ErrorAction Stop |
    Where-Object RoleAssignmentId -EQ $roleId
if ($role) {
    Invoke-WithRetry -OperationName "Delete variant vault reader" -ScriptBlock {
        Remove-AzResource -ResourceId $roleId -Force -ErrorAction Stop
    }
}
$targets = @(
    $variantId
    "$resourceGroupId/providers/Microsoft.App/containerApps/$appName"
    "$resourceGroupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIR$Version"
)

foreach ($resourceId in $targets) {
    if ($resources.ResourceId -notcontains $resourceId) {
        Write-Host "Already absent: $resourceId"
        continue
    }

    Write-Host "Deleting stage-owned resource: $resourceId"
    Invoke-WithRetry -OperationName "Delete $resourceId" -ScriptBlock {
        Remove-AzResource -ResourceId $resourceId -Force -ErrorAction Stop
    }
}
