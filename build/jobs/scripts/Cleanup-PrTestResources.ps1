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

$resourceGroup = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction Stop
$resourceGroupId = $resourceGroup.ResourceId
$environmentName = $DeploymentEnvironmentName.ToLowerInvariant()
$versionSuffix = if ($Version -eq 'Stu3') { '' } else { "-$($Version.ToLowerInvariant())" }
$appName = "$environmentName$versionSuffix-sql"

$resources = @(Get-AzResource -ResourceGroupName $ResourceGroupName -ErrorAction Stop)
$targets = @(
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
