param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stu3', 'R4', 'R5')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('sql', 'cosmos')]
    [string] $DataStore,

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
$appName = "$environmentName$versionSuffix"
if ($DataStore -eq 'sql') {
    $appName += '-sql'
}

$resources = @(Get-AzResource -ResourceGroupName $ResourceGroupName -ErrorAction Stop)
$targets = @("$resourceGroupId/providers/Microsoft.App/containerApps/$appName")

if ($DataStore -eq 'sql') {
    $targets += "$resourceGroupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIR$Version"
} else {
    $nspId = "$resourceGroupId/providers/Microsoft.Network/networkSecurityPerimeters/nsp-$ResourceGroupName"
    if ($resources.ResourceId -contains $nspId) {
        $resources += @(Get-AzResource -ResourceId "$nspId/resourceAssociations" -ApiVersion '2023-08-01-preview' -ErrorAction Stop)
    }
    $targets += "$nspId/resourceAssociations/cosmosdb-$appName-association"
    $targets += "$resourceGroupId/providers/Microsoft.DocumentDB/databaseAccounts/$appName"
}

foreach ($resourceId in $targets) {
    if ($resources.ResourceId -notcontains $resourceId) {
        Write-Host "Already absent: $resourceId"
        continue
    }

    $removeParameters = @{
        ResourceId = $resourceId
        Force = $true
        ErrorAction = 'Stop'
    }
    if ($resourceId -like '*/resourceAssociations/*') {
        $removeParameters.ApiVersion = '2023-08-01-preview'
    }

    Write-Host "Deleting stage-owned resource: $resourceId"
    Invoke-WithRetry -OperationName "Delete $resourceId" -ScriptBlock {
        Remove-AzResource @removeParameters
    }
}
