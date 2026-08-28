<#
.SYNOPSIS
    Runs E2E tests against a baseline (main branch) and a local branch FHIR server
    deployed as Azure Container Apps, then compares results.

.DESCRIPTION
    This tool enables A/B testing of FHIR server changes by:
    1. Pulling the latest CI-produced Docker image for main (tagged 'master')
    2. Building a Docker image from the current local branch
    3. Deploying both images as separate Azure Container Apps (auth disabled)
    4. Running the E2E test suite against each endpoint
    5. Comparing .trx results and producing a diff report

    Authorization is disabled on both services to simplify setup.

.EXAMPLE
    ./Invoke-ABTest.ps1 -FhirVersion R4 -DataStore SqlServer -Subscription "my-sub"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Stu3', 'R4', 'R4B', 'R5')]
    [string] $FhirVersion = 'R4',

    [Parameter(Mandatory = $false)]
    [ValidateSet('SqlServer', 'CosmosDb')]
    [string] $DataStore = 'SqlServer',

    [Parameter(Mandatory = $true)]
    [string] $Subscription,

    [Parameter(Mandatory = $false)]
    [string] $Location = 'westus2',

    [Parameter(Mandatory = $true)]
    [string] $ResourceGroupPrefix,

    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string] $ContainerRegistry = 'healthplatformregistry.azurecr.io',

    [Parameter(Mandatory = $false)]
    [string] $BaselineTag = 'master',

    [Parameter(Mandatory = $false)]
    [string] $CategoryFilter = '',

    [Parameter(Mandatory = $false)]
    [switch] $OnlyShortTests,

    [Parameter(Mandatory = $false)]
    [switch] $SkipCleanup,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 20)]
    [int] $Iterations = 1,

    [Parameter(Mandatory = $false)]
    [string] $TestDllPath,

    [Parameter(Mandatory = $false)]
    [ValidateSet('BaselineImageBranch', 'SameImageProvider')]
    [string] $ComparisonMode = 'BaselineImageBranch',

    [Parameter(Mandatory = $false)]
    [ValidateSet('E2E', 'Bundle', 'Import')]
    [string] $Workload = 'E2E',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $ControlDefaultProvider = 'Firely',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $ControlImportProvider = 'Firely',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $ControlFhirPathProvider = 'Firely',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $TreatmentDefaultProvider = 'Firely',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $TreatmentImportProvider = 'Firely',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $TreatmentFhirPathProvider = 'Ignixa',

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 100000)]
    [int] $BundleCount = 100,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 500)]
    [int] $BundleSize = 100,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 128)]
    [int] $Concurrency = 4,

    [Parameter(Mandatory = $false)]
    [ValidateRange(0, 20)]
    [int] $WarmupIterations = 1,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 20)]
    [int] $MeasuredIterations = 3,

    [Parameter(Mandatory = $false)]
    [uri[]] $ImportInputUrl,

    [Parameter(Mandatory = $false)]
    [uri] $ImportStorageAccountUri,

    [Parameter(Mandatory = $false)]
    [string] $ImportStorageAccountResourceId,

    [Parameter(Mandatory = $false)]
    [string[]] $ImportResourceType = @('Patient'),

    [Parameter(Mandatory = $false)]
    [string[]] $ImportSearchProbe,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, [long]::MaxValue)]
    [long] $ImportExpectedResourceCount,

    [Parameter(Mandatory = $false)]
    [uri[]] $ImportWarmupInputUrl,

    [Parameter(Mandatory = $false)]
    [string[]] $ImportWarmupResourceType,

    [Parameter(Mandatory = $false)]
    [string[]] $ImportWarmupSearchProbe,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, [long]::MaxValue)]
    [long] $ImportWarmupExpectedResourceCount,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 360)]
    [int] $ImportTimeoutMinutes = 120,

    [Parameter(Mandatory = $false)]
    [ValidateRange(0.1, 60)]
    [double] $ImportPollIntervalSeconds = 1,

    [Parameter(Mandatory = $false)]
    [switch] $ParallelWorkloads,

    [Parameter(Mandatory = $false)]
    [switch] $DryRun,

    [Parameter(Mandatory = $false)]
    [string] $PlanOutputPath,

    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $false)]
    [switch] $ValidateCleanupOnFailure
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# ─────────────────────────────────────────────────────────────────────────────
# Resolve paths
# ─────────────────────────────────────────────────────────────────────────────

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

$scriptsDir = $PSScriptRoot

function Get-DeploymentEnvironmentVariables {
    param([System.Collections.IDictionary] $Providers)

    $settings = @()
    if ($ComparisonMode -eq 'SameImageProvider') {
        $settings += @(
            "FhirServer__CoreFeatures__FhirSdkProvider__Default=$($Providers.Default)",
            "FhirServer__CoreFeatures__FhirSdkProvider__Import=$($Providers.Import)",
            "FhirServer__CoreFeatures__FhirSdkProvider__FhirPath=$($Providers.FhirPath)"
        )
    }
    if ($Workload -eq 'Import') {
        $settings += @(
            'FhirServer__Operations__Import__Enabled=true',
            "FhirServer__Operations__IntegrationDataStore__StorageAccountUri=$($ImportStorageAccountUri.AbsoluteUri)"
        )
    }
    return $settings
}

function ConvertTo-SanitizedUriString {
    param([uri] $Uri)

    if (-not $Uri) {
        return $null
    }

    return $Uri.GetLeftPart([System.UriPartial]::Path)
}

function ConvertTo-SanitizedProbeDefinition {
    param(
        [string] $Probe,
        [string] $ExpectedResourceType
    )

    $questionMark = $Probe.IndexOf('?')
    $path = $Probe.Substring(0, $questionMark)
    $parameterNames = @(
        $Probe.Substring($questionMark + 1) -split '&' |
            ForEach-Object { [uri]::UnescapeDataString(($_ -split '=', 2)[0]) }
    )

    return [ordered]@{
        resourceType = $ExpectedResourceType
        path = $path
        parameterNames = $parameterNames
    }
}

function Assert-ImportManifest {
    param(
        [string] $ParameterPrefix,
        [uri[]] $InputUrl,
        [string[]] $ResourceType,
        [string[]] $SearchProbe,
        [uri] $StorageAccountUri
    )

    if (-not $InputUrl -or -not $ResourceType -or -not $SearchProbe) {
        throw "-${ParameterPrefix}InputUrl, -${ParameterPrefix}ResourceType, and -${ParameterPrefix}SearchProbe are required."
    }
    if (-not $StorageAccountUri.IsAbsoluteUri -or $StorageAccountUri.Query) {
        throw '-ImportStorageAccountUri must be an absolute URI without a query string.'
    }
    if ($ResourceType.Count -ne $InputUrl.Count) {
        throw "-${ParameterPrefix}ResourceType must contain one entry per -${ParameterPrefix}InputUrl."
    }

    foreach ($input in $InputUrl) {
        if (-not $input.IsAbsoluteUri) {
            throw "-${ParameterPrefix}InputUrl values must be absolute URIs."
        }
        if ($input.Query) {
            throw "-${ParameterPrefix}InputUrl '$($input.GetLeftPart([System.UriPartial]::Path))' must not contain a query string."
        }
        if (-not [string]::Equals($input.Scheme, $StorageAccountUri.Scheme, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($input.IdnHost, $StorageAccountUri.IdnHost, [StringComparison]::OrdinalIgnoreCase)) {
            throw "-${ParameterPrefix}InputUrl scheme and host must match -ImportStorageAccountUri (expected $($StorageAccountUri.Scheme)://$($StorageAccountUri.IdnHost))."
        }
    }

    $distinctTypes = @($ResourceType | Sort-Object -Unique)
    if ($SearchProbe.Count -ne $distinctTypes.Count) {
        throw "-${ParameterPrefix}SearchProbe must contain one indexed query per distinct resource type ($($distinctTypes -join ', '))."
    }
    for ($index = 0; $index -lt $distinctTypes.Count; $index++) {
        $type = $distinctTypes[$index]
        $probe = $SearchProbe[$index]
        if ($probe -notmatch "^$([regex]::Escape($type))\?(.+)$") {
            throw "-${ParameterPrefix}SearchProbe[$index] must be a relative $type query."
        }
        $queryParameters = @($Matches[1] -split '&')
        if (-not ($queryParameters | Where-Object { $_ -match '^[^_=][^=]*=.+$' })) {
            throw "-${ParameterPrefix}SearchProbe[$index] must include a deterministic indexed search parameter; type-only and _count-only probes are not allowed."
        }
    }
}

function Get-DeploymentCommandPlan {
    param(
        [string] $AppName,
        [string] $Image
    )

    $create = @(
        'az', 'containerapp', 'create',
        '--name', $AppName,
        '--resource-group', $ResourceGroupName,
        '--environment', $acaEnvironmentName,
        '--image', $Image,
        '--registry-server', $ContainerRegistry,
        '--registry-identity', '<acr-sql-uami-resource-id>',
        '--user-assigned', '<acr-sql-uami-resource-id>',
        '--system-assigned'
    )
    $role = @(
        'az', 'role', 'assignment', 'create',
        '--assignee-object-id', "<system-principal-id:$AppName>",
        '--assignee-principal-type', 'ServicePrincipal',
        '--role', 'Storage Blob Data Contributor',
        '--scope', $ImportStorageAccountResourceId
    )

    return [ordered]@{
        create = $create -join ' '
        systemPrincipalLookup = "az containerapp show --name $AppName --resource-group $ResourceGroupName --query identity.principalId --output tsv"
        importStorageRoleAssignment = if ($Workload -eq 'Import') { $role -join ' ' } else { $null }
        rolePropagationWaitSeconds = if ($Workload -eq 'Import') { 30 } else { 0 }
    }
}

function Invoke-ProtectedRun {
    param(
        [scriptblock] $Operation,
        [scriptblock] $Cleanup
    )

    $operationError = $null
    $cleanupError = $null
    try {
        & $Operation
    } catch {
        $operationError = $_
    } finally {
        try {
            & $Cleanup
        } catch {
            $cleanupError = $_
        }
    }

    if ($operationError) {
        if ($cleanupError) {
            Write-Warning "Cleanup also failed: $($cleanupError.Exception.Message)"
        }
        throw $operationError
    }
    if ($cleanupError) {
        throw $cleanupError
    }
}

function Invoke-RunCleanup {
    param(
        [System.Collections.IDictionary] $State,
        [switch] $Skip,
        [string] $ResourceGroup,
        [string] $Registry,
        [string] $Image,
        [scriptblock] $CommandInvoker
    )

    if ($Skip) {
        Write-Host "`n⚠ Skipping cleanup. Remember to delete resource group '$ResourceGroup' and image '$Image' manually." -ForegroundColor Yellow
        return
    }

    Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
    Write-Host "│ Cleanup Azure resources                                     │" -ForegroundColor Yellow
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

    if (-not $CommandInvoker) {
        $CommandInvoker = {
            param([string[]] $CommandArguments)
            & az @CommandArguments
            if ($LASTEXITCODE -ne 0) {
                throw "az $($CommandArguments -join ' ') failed with exit code $LASTEXITCODE."
            }
        }
    }

    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    if ($State.ResourceGroupCreated) {
        Write-Host "`n► Deleting resource group: $ResourceGroup"
        try {
            & $CommandInvoker -CommandArguments @('group', 'delete', '--name', $ResourceGroup, '--yes')
        } catch {
            $cleanupErrors.Add($_.Exception.Message)
        }
    }
    if ($State.BranchImagePushed) {
        Write-Host "`n► Removing branch image tag from registry..."
        try {
            & $CommandInvoker -CommandArguments @(
                'acr', 'repository', 'delete',
                '--name', $Registry,
                '--image', $Image,
                '--yes'
            )
        } catch {
            $cleanupErrors.Add($_.Exception.Message)
        }
    }

    if ($cleanupErrors.Count -gt 0) {
        throw "Cleanup failed: $($cleanupErrors -join '; ')"
    }
}

$providerParameterNames = @(
    'ControlDefaultProvider',
    'ControlImportProvider',
    'ControlFhirPathProvider',
    'TreatmentDefaultProvider',
    'TreatmentImportProvider',
    'TreatmentFhirPathProvider'
)
if ($ComparisonMode -eq 'BaselineImageBranch' -and
    ($providerParameterNames | Where-Object { $PSBoundParameters.ContainsKey($_) })) {
    throw 'Provider parameters require -ComparisonMode SameImageProvider.'
}
if ($Workload -eq 'Import') {
    if ($DataStore -ne 'SqlServer') {
        throw 'The Import workload requires -DataStore SqlServer.'
    }
    if (-not $ImportInputUrl) {
        throw '-ImportInputUrl is required for the Import workload.'
    }
    if (-not $ImportStorageAccountUri -or [string]::IsNullOrWhiteSpace($ImportStorageAccountResourceId)) {
        throw '-ImportStorageAccountUri and -ImportStorageAccountResourceId are required for the Import workload.'
    }
    if (-not $PSBoundParameters.ContainsKey('ImportExpectedResourceCount')) {
        throw '-ImportExpectedResourceCount is required for the Import workload.'
    }
    Assert-ImportManifest `
        -ParameterPrefix 'Import' `
        -InputUrl $ImportInputUrl `
        -ResourceType $ImportResourceType `
        -SearchProbe $ImportSearchProbe `
        -StorageAccountUri $ImportStorageAccountUri
    if (-not $PSBoundParameters.ContainsKey('MeasuredIterations')) {
        $MeasuredIterations = 1
    }
    if (-not $PSBoundParameters.ContainsKey('WarmupIterations')) {
        $WarmupIterations = 0
    }
    if ($WarmupIterations -gt 0) {
        if (-not $PSBoundParameters.ContainsKey('ImportWarmupExpectedResourceCount')) {
            throw '-ImportWarmupExpectedResourceCount is required when Import warm-up iterations are requested.'
        }
        Assert-ImportManifest `
            -ParameterPrefix 'ImportWarmup' `
            -InputUrl $ImportWarmupInputUrl `
            -ResourceType $ImportWarmupResourceType `
            -SearchProbe $ImportWarmupSearchProbe `
            -StorageAccountUri $ImportStorageAccountUri
        $measuredInputSet = [Collections.Generic.HashSet[string]]::new(
            [string[]]@($ImportInputUrl.AbsoluteUri),
            [StringComparer]::OrdinalIgnoreCase)
        if ($ImportWarmupInputUrl | Where-Object { $measuredInputSet.Contains($_.AbsoluteUri) }) {
            throw 'Import warm-up and measured input URLs must not collide.'
        }
    } elseif ($ImportWarmupInputUrl -or $ImportWarmupResourceType -or $ImportWarmupSearchProbe -or
              $PSBoundParameters.ContainsKey('ImportWarmupExpectedResourceCount')) {
        throw 'Import warm-up corpus parameters require -WarmupIterations greater than zero.'
    }
} elseif ($PSBoundParameters.ContainsKey('ImportInputUrl') -or
          $PSBoundParameters.ContainsKey('ImportStorageAccountUri') -or
          $PSBoundParameters.ContainsKey('ImportStorageAccountResourceId') -or
          $PSBoundParameters.ContainsKey('ImportExpectedResourceCount') -or
          $PSBoundParameters.ContainsKey('ImportResourceType') -or
          $PSBoundParameters.ContainsKey('ImportSearchProbe') -or
          $PSBoundParameters.ContainsKey('ImportWarmupInputUrl') -or
          $PSBoundParameters.ContainsKey('ImportWarmupResourceType') -or
          $PSBoundParameters.ContainsKey('ImportWarmupSearchProbe') -or
          $PSBoundParameters.ContainsKey('ImportWarmupExpectedResourceCount') -or
          $PSBoundParameters.ContainsKey('ImportPollIntervalSeconds')) {
    throw 'Import input parameters require -Workload Import.'
}
if ($Workload -eq 'E2E' -and
    ($PSBoundParameters.ContainsKey('WarmupIterations') -or
     $PSBoundParameters.ContainsKey('MeasuredIterations') -or
     $ParallelWorkloads)) {
    throw 'Warm-up, measured iteration, and workload parallelism parameters require an ingestion workload.'
}
if ($ValidateCleanupOnFailure -and -not $DryRun) {
    throw '-ValidateCleanupOnFailure requires -DryRun.'
}

# ─────────────────────────────────────────────────────────────────────────────
# Generate unique names
# ─────────────────────────────────────────────────────────────────────────────

$runId = (Get-Date -Format 'yyyyMMddHHmmss')
$branchName = (git rev-parse --abbrev-ref HEAD 2>$null) ?? 'local'
$shortSha = (git rev-parse --short HEAD 2>$null) ?? 'unknown'

$outputDir = if ($OutputDirectory) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    Join-Path $repoRoot "ab-test-results/$runId"
}
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if (-not $ResourceGroupName) {
    $ResourceGroupName = "$ResourceGroupPrefix-abtest-$runId"
}

$baselineAppName = "fhir-baseline-$runId".ToLowerInvariant()
$branchAppName = "fhir-branch-$runId".ToLowerInvariant()
$acaEnvironmentName = "fhir-abtest-env-$runId".ToLowerInvariant()

# Truncate ACA environment name to 32 chars max
if ($acaEnvironmentName.Length -gt 32) {
    $acaEnvironmentName = $acaEnvironmentName.Substring(0, 32).TrimEnd('-')
}

$baselineImage = "$ContainerRegistry/$($FhirVersion.ToLower())_fhir-server:$BaselineTag"
$branchImageTag = "abtest-$runId-$shortSha"
$branchImage = "$ContainerRegistry/$($FhirVersion.ToLower())_fhir-server:$branchImageTag"
if ($ComparisonMode -eq 'SameImageProvider') {
    $baselineImage = $branchImage
}

$controlProviders = [ordered]@{
    Default = $ControlDefaultProvider
    Import = $ControlImportProvider
    FhirPath = $ControlFhirPathProvider
}
$treatmentProviders = [ordered]@{
    Default = $TreatmentDefaultProvider
    Import = $TreatmentImportProvider
    FhirPath = $TreatmentFhirPathProvider
}
if ($ComparisonMode -eq 'BaselineImageBranch') {
    $controlProviders = [ordered]@{ Default = 'Firely'; Import = 'Firely'; FhirPath = 'Firely' }
    $treatmentProviders = [ordered]@{ Default = 'Firely'; Import = 'Firely'; FhirPath = 'Firely' }
}
$comparisonLabel = if ($ComparisonMode -eq 'SameImageProvider') {
    'same-image provider comparison'
} else {
    'baseline-image branch comparison'
}
$controlE2eLabel = 'Baseline'
$treatmentE2eLabel = 'Branch'
$controlReportLabel = "control (image=$baselineImage; Default=$($controlProviders.Default); Import=$($controlProviders.Import); FhirPath=$($controlProviders.FhirPath))"
$treatmentReportLabel = "treatment (image=$branchImage; Default=$($treatmentProviders.Default); Import=$($treatmentProviders.Import); FhirPath=$($treatmentProviders.FhirPath))"
[string[]] $importProbeTypes = if ($Workload -eq 'Import') { @($ImportResourceType | Sort-Object -Unique) } else { @() }
[string[]] $importWarmupProbeTypes = if ($Workload -eq 'Import' -and $WarmupIterations -gt 0) {
    @($ImportWarmupResourceType | Sort-Object -Unique)
} else {
    @()
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " FHIR Server A/B Test Runner" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host " FHIR Version:    $FhirVersion"
Write-Host " Data Store:      $DataStore"
Write-Host " Branch:          $branchName ($shortSha)"
Write-Host " Baseline Image:  $baselineImage"
Write-Host " Branch Image:    $branchImage"
Write-Host " Comparison:      $comparisonLabel"
Write-Host " Workload:        $Workload"
Write-Host " Control SDK:     Default=$($controlProviders.Default), Import=$($controlProviders.Import), FhirPath=$($controlProviders.FhirPath)"
Write-Host " Treatment SDK:   Default=$($treatmentProviders.Default), Import=$($treatmentProviders.Import), FhirPath=$($treatmentProviders.FhirPath)"
Write-Host " Resource Group:  $ResourceGroupName"
Write-Host " Location:        $Location"
Write-Host ""

$plan = [ordered]@{
    comparison = $comparisonLabel
    comparisonMode = $ComparisonMode
    workload = $Workload
    outputDirectory = $outputDir
    execution = if ($Workload -eq 'E2E' -or $ParallelWorkloads) { 'parallel' } else { 'sequential' }
    reporting = [ordered]@{
        e2eCsvLabels = [ordered]@{ control = $controlE2eLabel; treatment = $treatmentE2eLabel }
        metadataFile = 'run-metadata.json'
    }
    control = [ordered]@{
        image = $baselineImage
        providers = $controlProviders
        deploymentSettings = @(Get-DeploymentEnvironmentVariables -Providers $controlProviders)
        deploymentCommands = Get-DeploymentCommandPlan -AppName $baselineAppName -Image $baselineImage
    }
    treatment = [ordered]@{
        image = $branchImage
        providers = $treatmentProviders
        deploymentSettings = @(Get-DeploymentEnvironmentVariables -Providers $treatmentProviders)
        deploymentCommands = Get-DeploymentCommandPlan -AppName $branchAppName -Image $branchImage
    }
    workloadPlan = [ordered]@{
        warmupIterations = if ($Workload -eq 'E2E') { 0 } else { $WarmupIterations }
        measuredIterations = if ($Workload -eq 'E2E') { $Iterations } else { $MeasuredIterations }
        bundleCount = if ($Workload -eq 'Bundle') { $BundleCount } else { $null }
        bundleSize = if ($Workload -eq 'Bundle') { $BundleSize } else { $null }
        concurrency = if ($Workload -eq 'Bundle') { $Concurrency } else { $null }
        importInput = if ($Workload -eq 'Import') {
            @($ImportInputUrl | ForEach-Object -Begin { $index = 0 } -Process {
                [ordered]@{
                    type = $ImportResourceType[$index++]
                    url = ConvertTo-SanitizedUriString -Uri $_
                }
            })
        } else { @() }
        importSearchProbes = if ($Workload -eq 'Import') {
            @($ImportSearchProbe | ForEach-Object -Begin { $index = 0 } -Process {
                ConvertTo-SanitizedProbeDefinition -Probe $_ -ExpectedResourceType $importProbeTypes[$index++]
            })
        } else { @() }
        importExpectedResourceCount = if ($Workload -eq 'Import') { $ImportExpectedResourceCount } else { $null }
        importWarmupInput = if ($Workload -eq 'Import' -and $WarmupIterations -gt 0) {
            @($ImportWarmupInputUrl | ForEach-Object -Begin { $index = 0 } -Process {
                [ordered]@{
                    type = $ImportWarmupResourceType[$index++]
                    url = ConvertTo-SanitizedUriString -Uri $_
                }
            })
        } else { @() }
        importWarmupSearchProbes = if ($Workload -eq 'Import' -and $WarmupIterations -gt 0) {
            @($ImportWarmupSearchProbe | ForEach-Object -Begin { $index = 0 } -Process {
                ConvertTo-SanitizedProbeDefinition -Probe $_ -ExpectedResourceType $importWarmupProbeTypes[$index++]
            })
        } else { @() }
        importWarmupExpectedResourceCount = if ($Workload -eq 'Import' -and $WarmupIterations -gt 0) { $ImportWarmupExpectedResourceCount } else { $null }
        importStorageAccountUri = if ($Workload -eq 'Import') { ConvertTo-SanitizedUriString -Uri $ImportStorageAccountUri } else { $null }
    }
}

$metadata = [ordered]@{
    schemaVersion = 1
    comparisonMode = $ComparisonMode
    comparison = $comparisonLabel
    images = [ordered]@{ control = $baselineImage; treatment = $branchImage }
    providers = [ordered]@{ control = $controlProviders; treatment = $treatmentProviders }
    workload = $Workload
    parameters = $plan.workloadPlan
}
$metadata | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $outputDir 'run-metadata.json') -Encoding utf8

if ($PlanOutputPath) {
    $plan | ConvertTo-Json -Depth 8 | Set-Content -Path $PlanOutputPath -Encoding utf8
    Write-Host " Plan:            $PlanOutputPath"
}
if ($DryRun) {
    $plan | ConvertTo-Json -Depth 8
    if ($ValidateCleanupOnFailure) {
        $cleanupEvidence = [System.Collections.Generic.List[string]]::new()
        $mockState = @{ ResourceGroupCreated = $true; BranchImagePushed = $true }
        $mockInvoker = {
            param([string[]] $CommandArguments)
            $cleanupEvidence.Add($CommandArguments -join ' ')
            if ($CommandArguments[0] -eq 'group') {
                throw 'Simulated resource-group cleanup failure.'
            }
        }
        $forcedFailure = $null
        try {
            Invoke-ProtectedRun -Operation {
                throw 'Forced workload failure.'
            } -Cleanup {
                Invoke-RunCleanup `
                    -State $mockState `
                    -ResourceGroup 'mock-resource-group' `
                    -Registry 'mock-registry' `
                    -Image 'mock-image:tag' `
                    -CommandInvoker $mockInvoker
            }
        } catch {
            $forcedFailure = $_
        }
        if ($forcedFailure.Exception.Message -ne 'Forced workload failure.' -or
            $cleanupEvidence.Count -ne 2 -or
            $cleanupEvidence[0] -notmatch '^group delete ' -or
            $cleanupEvidence[1] -notmatch '^acr repository delete ') {
            throw 'Cleanup-on-failure validation failed.'
        }
        Write-Host 'Cleanup-on-failure validation passed; cleanup ran and the original workload failure was preserved.' -ForegroundColor Green
    }
    Write-Host 'Dry run complete; no Docker or Azure commands were executed.' -ForegroundColor Green
    return
}

$runState = @{ ResourceGroupCreated = $false; BranchImagePushed = $false }
Invoke-ProtectedRun -Operation {
# ─────────────────────────────────────────────────────────────────────────────
# Step 1: Pull baseline image / Build branch image
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 1: Prepare Docker images                               │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

Write-Host "`n► Logging into container registry..."
az acr login --name ($ContainerRegistry -replace '\.azurecr\.io$', '')
if ($LASTEXITCODE -ne 0) { throw "Failed to login to ACR" }

if ($ComparisonMode -eq 'BaselineImageBranch') {
    Write-Host "`n► Pulling baseline image: $baselineImage"
    docker pull $baselineImage
    if ($LASTEXITCODE -ne 0) { throw "Failed to pull baseline image. Ensure the '$BaselineTag' tag exists." }
}

Write-Host "`n► Building branch image from local source..."
docker buildx build `
    --tag $branchImage `
    --file "$repoRoot/build/docker/Dockerfile" `
    --platform linux/amd64 `
    --build-arg FHIR_VERSION=$FhirVersion `
    --build-arg ASSEMBLY_VER="0.0.1" `
    --load `
    $repoRoot
if ($LASTEXITCODE -ne 0) { throw "Failed to build branch Docker image" }

Write-Host "`n► Pushing branch image to registry..."
docker push $branchImage
if ($LASTEXITCODE -ne 0) { throw "Failed to push branch image to registry" }
$runState.BranchImagePushed = $true

# ─────────────────────────────────────────────────────────────────────────────
# Step 2: Provision Azure infrastructure
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 2: Provision Azure infrastructure                      │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

Write-Host "`n► Setting subscription..."
az account set --subscription $Subscription
if ($LASTEXITCODE -ne 0) { throw "Failed to set subscription" }

Write-Host "`n► Creating resource group: $ResourceGroupName"
az group create --name $ResourceGroupName --location $Location --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to create resource group" }
$runState.ResourceGroupCreated = $true

# Deploy ACA managed environment
Write-Host "`n► Creating ACA managed environment: $acaEnvironmentName"
az containerapp env create `
    --name $acaEnvironmentName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to create ACA environment" }

# Deploy SQL Server if needed
$sqlServerName = $null
$sqlManagedIdentityName = $null
$sqlManagedIdentityClientId = $null
if ($DataStore -eq 'SqlServer') {
    $sqlServerName = "fhir-abtest-sql-$runId".ToLowerInvariant()
    $sqlManagedIdentityName = "$sqlServerName-uami"

    # Create user-assigned managed identity (used as AAD-only admin for SQL)
    Write-Host "`n► Creating managed identity: $sqlManagedIdentityName"
    $identityJson = az identity create `
        --name $sqlManagedIdentityName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Failed to create managed identity" }

    $identity = $identityJson | ConvertFrom-Json
    $sqlManagedIdentityClientId = $identity.clientId
    $identityPrincipalId = $identity.principalId
    $identityTenantId = $identity.tenantId

    # Deploy SQL Server using the ARM template (AAD-only auth, no local passwords)
    Write-Host "`n► Creating SQL Server (Entra ID-only auth): $sqlServerName"
    $sqlTemplateFile = Join-Path $repoRoot "samples/templates/default-sqlServer.json"
    az deployment group create `
        --resource-group $ResourceGroupName `
        --name "$sqlServerName-deploy" `
        --template-file $sqlTemplateFile `
        --parameters `
            sqlServerName=$sqlServerName `
            sqlAdministratorLogin=$identityPrincipalId `
            sqlAdministratorSid=$identityPrincipalId `
            sqlAdministratorTenantId=$identityTenantId `
            sqlServerPrincipalType=User `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to create SQL Server" }

    # Allow Azure services through firewall
    Write-Host "  Adding firewall rule: AllowAzureServices"
    az sql server firewall-rule create `
        --resource-group $ResourceGroupName `
        --server $sqlServerName `
        --name AllowAzureServices `
        --start-ip-address 0.0.0.0 `
        --end-ip-address 0.0.0.0 `
        --output none

    # Create databases for both instances
    foreach ($dbName in @("FHIRBaseline$FhirVersion", "FHIRBranch$FhirVersion")) {
        Write-Host "  Creating database: $dbName"
        az sql db create `
            --resource-group $ResourceGroupName `
            --server $sqlServerName `
            --name $dbName `
            --edition GeneralPurpose `
            --compute-model Serverless `
            --family Gen5 `
            --capacity 2 `
            --output none
        if ($LASTEXITCODE -ne 0) { throw "Failed to create database $dbName" }
    }
}

# Ensure we have a managed identity for ACR pull (reuse SQL UAMI or create a new one for Cosmos)
if (-not $sqlManagedIdentityName) {
    $sqlManagedIdentityName = "fhir-abtest-acr-$runId".ToLowerInvariant()
    Write-Host "`n► Creating managed identity for ACR pull: $sqlManagedIdentityName"
    $identityJson = az identity create `
        --name $sqlManagedIdentityName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --output json
    if ($LASTEXITCODE -ne 0) { throw "Failed to create managed identity for ACR" }
    $identity = $identityJson | ConvertFrom-Json
    $sqlManagedIdentityClientId = $identity.clientId
    $identityPrincipalId = $identity.principalId
}

$subscriptionId = az account show --query id -o tsv
$registryNameShort = $ContainerRegistry -replace '\.azurecr\.io$', ''
$acrResourceId = az acr show --name $registryNameShort --query id -o tsv
$uamiResourceId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$sqlManagedIdentityName"

Write-Host "`n► Assigning AcrPull role to managed identity on container registry..."
az role assignment create `
    --assignee-object-id $identityPrincipalId `
    --assignee-principal-type ServicePrincipal `
    --role AcrPull `
    --scope $acrResourceId `
    --output none 2>$null

# Wait for role assignment propagation
Write-Host "  Waiting 30s for role propagation..."
Start-Sleep -Seconds 30

# ─────────────────────────────────────────────────────────────────────────────
# Step 3: Deploy Container Apps (auth disabled)
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 3: Deploy Container Apps                               │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

function Deploy-FhirContainerApp {
    param(
        [string] $AppName,
        [string] $Image,
        [string] $DatabaseName,
        [System.Collections.IDictionary] $Providers
    )


    $envVars = @(
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true",
        "FhirServer__Security__Enabled=false",
        "FhirServer__Security__EnableAadSmartOnFhirProxy=false",
        "FhirServer__Security__Authentication__Authority=invalid",
        "FhirServer__Security__Authentication__Audience=invalid"
    )
    $envVars += Get-DeploymentEnvironmentVariables -Providers $Providers

    if ($DataStore -eq 'SqlServer') {
        $connStr = "Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=$DatabaseName;Persist Security Info=False;Authentication=Active Directory Managed Identity;User Id=$sqlManagedIdentityClientId;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
        $envVars += @(
            "DataStore=SqlServer",
            "SqlServer__ConnectionString=$connStr",
            "SqlServer__Initialize=true",
            "SqlServer__SchemaOptions__AutomaticUpdatesEnabled=true",
            "SqlServer__AllowDatabaseCreation=true"
        )
    } else {
        $envVars += @(
            "DataStore=CosmosDb",
            "CosmosDb__Host=https://${AppName}.documents.azure.com:443/",
            "CosmosDb__DatabaseId=health",
            "CosmosDb__InitialDatabaseThroughput=1000"
        )
    }

    Write-Host "`n► Deploying container app: $AppName (image: $Image)"

    $createArgs = @(
        'containerapp', 'create',
        '--name', $AppName,
        '--resource-group', $ResourceGroupName,
        '--environment', $acaEnvironmentName,
        '--image', $Image,
        '--registry-server', $ContainerRegistry,
        '--registry-identity', $uamiResourceId,
        '--user-assigned', $uamiResourceId,
        '--system-assigned',
        '--target-port', '8080',
        '--ingress', 'external',
        '--min-replicas', '2',
        '--max-replicas', '2',
        '--cpu', '1.0',
        '--memory', '2.0Gi',
        '--env-vars'
    )
    $createArgs += $envVars
    $createArgs += @('--output', 'none')

    & az @createArgs
    if ($LASTEXITCODE -ne 0) { throw "Failed to deploy container app: $AppName" }

    if ($Workload -eq 'Import') {
        $systemPrincipalId = az containerapp show `
            --name $AppName `
            --resource-group $ResourceGroupName `
            --query 'identity.principalId' `
            --output tsv
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($systemPrincipalId)) {
            throw "Failed to resolve system-assigned principal for container app: $AppName"
        }

        Write-Host "  Assigning Storage Blob Data Contributor to $AppName system identity..."
        az role assignment create `
            --assignee-object-id $systemPrincipalId `
            --assignee-principal-type ServicePrincipal `
            --role 'Storage Blob Data Contributor' `
            --scope $ImportStorageAccountResourceId `
            --output none 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to assign import storage role to container app: $AppName"
        }

        Write-Host '  Waiting 30s for import storage role propagation...'
        Start-Sleep -Seconds 30
    }

    # Get the FQDN
    $fqdn = az containerapp show `
        --name $AppName `
        --resource-group $ResourceGroupName `
        --query "properties.configuration.ingress.fqdn" `
        --output tsv
    if ([string]::IsNullOrWhiteSpace($fqdn)) { throw "Could not get FQDN for $AppName" }

    return "https://$fqdn"
}

$baselineDbName = if ($DataStore -eq 'SqlServer') { "FHIRBaseline$FhirVersion" } else { $null }
$branchDbName = if ($DataStore -eq 'SqlServer') { "FHIRBranch$FhirVersion" } else { $null }

$baselineUrl = Deploy-FhirContainerApp -AppName $baselineAppName -Image $baselineImage -DatabaseName $baselineDbName -Providers $controlProviders
$branchUrl = Deploy-FhirContainerApp -AppName $branchAppName -Image $branchImage -DatabaseName $branchDbName -Providers $treatmentProviders

Write-Host "`n  Baseline URL: $baselineUrl" -ForegroundColor Green
Write-Host "  Branch URL:   $branchUrl" -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# Step 4: Health checks
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 4: Health checks                                       │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

function Wait-ForHealthy {
    param(
        [string] $Url,
        [string] $Label,
        [int] $TimeoutMinutes = 7
    )

    $healthUrl = "$Url/health/check"
    $timeout = (Get-Date).AddMinutes($TimeoutMinutes)
    $consecutiveSuccess = 0
    $requiredSuccess = 3

    Write-Host "`n► Waiting for $Label to become healthy: $healthUrl"

    do {
        Start-Sleep -Seconds 10
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -eq 200) {
                $consecutiveSuccess++
                Write-Host "  ✓ Health check passed ($consecutiveSuccess/$requiredSuccess)"
            } else {
                $consecutiveSuccess = 0
                Write-Host "  ✗ Status: $($response.StatusCode)"
            }
        } catch {
            $consecutiveSuccess = 0
            Write-Host "  ✗ Error: $($_.Exception.Message)"
        }
    } while ($consecutiveSuccess -lt $requiredSuccess -and (Get-Date) -lt $timeout)

    if ($consecutiveSuccess -lt $requiredSuccess) {
        throw "$Label failed to become healthy within $TimeoutMinutes minutes"
    }

    Write-Host "  $Label is healthy!" -ForegroundColor Green
}

Wait-ForHealthy -Url $baselineUrl -Label "Baseline"
Wait-ForHealthy -Url $branchUrl -Label "Branch"

# ─────────────────────────────────────────────────────────────────────────────
# Step 5: Run E2E tests
# ─────────────────────────────────────────────────────────────────────────────

if ($Workload -eq 'E2E') {
Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 5: Run E2E tests                                       │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

# Build E2E tests if no DLL path provided
if (-not $TestDllPath) {
    $testProject = Join-Path $repoRoot "test/Microsoft.Health.Fhir.$FhirVersion.Tests.E2E/Microsoft.Health.Fhir.$FhirVersion.Tests.E2E.csproj"
    $testOutputDir = Join-Path $outputDir "testbin"

    Write-Host "`n► Building E2E test project..."
    dotnet build $testProject -c Release -o $testOutputDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Failed to build E2E test project" }

    $TestDllPath = Join-Path $testOutputDir "Microsoft.Health.Fhir.$FhirVersion.Tests.E2E.dll"
}

if (-not (Test-Path $TestDllPath)) {
    throw "E2E test DLL not found at: $TestDllPath"
}

$baselineTrx = Join-Path $outputDir "baseline.trx"
$branchTrx = Join-Path $outputDir "branch.trx"

# Build shared filter once
$filterParts = @()

# No storage account is currently setup, so tests that require it will be skipped.
$filterParts += 'Category!=Export'
$filterParts += 'Category!=ExportDataValidation'
$filterParts += 'Category!=ExportLongRunning'
$filterParts += 'Category!=Import'

if ($OnlyShortTests) {
    # These take a long time to complete
    $filterParts += 'Category!=ReindexOperation'
    $filterParts += 'Category!=IndexAndReindex'
    $filterParts += 'Category!=BulkDelete'
    $filterParts += 'Category!=BulkUpdate'
}

if ($DataStore -eq 'SqlServer') {
    $filterParts += 'FullyQualifiedName~SqlServer'
} elseif ($DataStore -eq 'CosmosDb') {
    $filterParts += 'FullyQualifiedName~CosmosDb'
}
if ($CategoryFilter) {
    $filterParts += $CategoryFilter
}
$testFilter = if ($filterParts.Count -gt 0) { $filterParts -join '&' } else { $null }

if ($Iterations -gt 1) {
    Write-Host "`n► Running E2E tests ($Iterations iterations each, in parallel)..."
} else {
    Write-Host "`n► Running E2E tests in parallel against both services..."
}
if ($testFilter) { Write-Host "  Filter: $testFilter" }

# Script block that runs inside each parallel job
$testJob = {
    param($DllPath, $Url, $Label, $TrxDir, $ResultsFile, $Filter, $FhirVer, $Iterations)

    $env:TestEnvironmentUrl = $Url
    $env:TestEnvironmentUrl_Sql = $Url
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_$FhirVer", $Url)
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_${FhirVer}_Sql", $Url)

    if (-not (Test-Path $TrxDir)) {
        New-Item -ItemType Directory -Path $TrxDir -Force | Out-Null
    }

    $allTrxFiles = @()

    for ($i = 1; $i -le $Iterations; $i++) {
        $iterDir = Join-Path $TrxDir "iter-$i"
        New-Item -ItemType Directory -Path $iterDir -Force | Out-Null

        $testArgs = @($DllPath, '--report-trx', '--results-directory', $iterDir)
        if ($Filter) {
            $testArgs += @('--filter', $Filter)
        }

        & dotnet @testArgs 2>&1 | Out-Null
        $exitCode = $LASTEXITCODE

        $trxFile = Get-ChildItem -Path $iterDir -Filter "*.trx" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($trxFile) {
            $destName = "${Label}-iter${i}.trx"
            $destPath = Join-Path $TrxDir $destName
            Copy-Item $trxFile.FullName -Destination $destPath -Force
            $allTrxFiles += $destPath
        }
    }

    # Copy the last iteration as the "primary" TRX (for backward compat)
    if ($allTrxFiles.Count -gt 0) {
        Copy-Item $allTrxFiles[-1] -Destination $ResultsFile -Force
    }

    return @{
        ExitCode  = $exitCode
        Label     = $Label
        TrxFiles  = $allTrxFiles
    }
}

$baselineTrxDir = Join-Path $outputDir "baseline-results"
$branchTrxDir = Join-Path $outputDir "branch-results"

$baselineJob = Start-Job -ScriptBlock $testJob -ArgumentList $TestDllPath, $baselineUrl, "baseline", $baselineTrxDir, $baselineTrx, $testFilter, $FhirVersion, $Iterations
$branchJob = Start-Job -ScriptBlock $testJob -ArgumentList $TestDllPath, $branchUrl, "branch", $branchTrxDir, $branchTrx, $testFilter, $FhirVersion, $Iterations

# Monitor both jobs with progress updates
$startTime = Get-Date
$completedJobs = @{}

while ($completedJobs.Count -lt 2) {
    Start-Sleep -Seconds 60
    $elapsed = (Get-Date) - $startTime

    foreach ($job in @(@{Name='baseline'; Job=$baselineJob}, @{Name='branch'; Job=$branchJob})) {
        if ($completedJobs.ContainsKey($job.Name)) { continue }

        if ($job.Job.State -eq 'Completed' -or $job.Job.State -eq 'Failed') {
            $completedJobs[$job.Name] = $true
            Write-Host ("  ✓ {0} tests finished [{1:hh\:mm\:ss}]" -f $job.Name, $elapsed) -ForegroundColor Green
        }
    }

    if ($completedJobs.Count -lt 2) {
        $running = @('baseline', 'branch') | Where-Object { -not $completedJobs.ContainsKey($_) }
        Write-Host ("  [{0:hh\:mm\:ss}] Still running: {1}" -f $elapsed, ($running -join ', ')) -ForegroundColor DarkGray
    }
}

$baselineResult = Receive-Job -Job $baselineJob -Wait
$branchResult = Receive-Job -Job $branchJob -Wait
Remove-Job -Job $baselineJob, $branchJob -Force

$totalDuration = (Get-Date) - $startTime
Write-Host ("`n  Both test runs completed in {0:hh\:mm\:ss}" -f $totalDuration) -ForegroundColor Cyan
if ($Iterations -gt 1) {
    Write-Host "  ($Iterations iterations per side)" -ForegroundColor Cyan
}

# Show exit codes
if ($baselineResult.ExitCode -ne 0) {
    Write-Host "  ⚠ Baseline tests exited with code $($baselineResult.ExitCode)" -ForegroundColor Yellow
}
if ($branchResult.ExitCode -ne 0) {
    Write-Host "  ⚠ Branch tests exited with code $($branchResult.ExitCode)" -ForegroundColor Yellow
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 6: Compare results and generate report
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
Write-Host "│ Step 6: Generate comparison report                          │" -ForegroundColor Yellow
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

# Collect all TRX files for multi-iteration averaging
$baselineTrxPaths = Get-ChildItem -Path $baselineTrxDir -Filter "*.trx" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
$branchTrxPaths = Get-ChildItem -Path $branchTrxDir -Filter "*.trx" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName

# Fall back to single file if directory listing fails
if (-not $baselineTrxPaths) { $baselineTrxPaths = @($baselineTrx) }
if (-not $branchTrxPaths) { $branchTrxPaths = @($branchTrx) }

& "$scriptsDir/Compare-TestResults.ps1" `
    -BaselineTrxPaths $baselineTrxPaths `
    -BranchTrxPaths $branchTrxPaths `
    -OutputPath (Join-Path $outputDir "comparison-report.md") `
    -BaselineLabel $controlE2eLabel `
    -BranchLabel $treatmentE2eLabel

@"

## Run provenance

- **Comparison:** $comparisonLabel
- **Control image:** ``$baselineImage``
- **Treatment image:** ``$branchImage``
- **Control providers:** Default=$($controlProviders.Default), Import=$($controlProviders.Import), FhirPath=$($controlProviders.FhirPath)
- **Treatment providers:** Default=$($treatmentProviders.Default), Import=$($treatmentProviders.Import), FhirPath=$($treatmentProviders.FhirPath)
"@ | Add-Content -Path (Join-Path $outputDir 'comparison-report.md') -Encoding utf8

& "$scriptsDir/Export-DetailedCsv.ps1" `
    -BaselineTrxPaths $baselineTrxPaths `
    -BranchTrxPaths $branchTrxPaths `
    -OutputPath (Join-Path $outputDir "detailed-results.csv") `
    -BaselineLabel $controlE2eLabel `
    -BranchLabel $treatmentE2eLabel
} else {
    Write-Host "`n┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
    Write-Host "│ Step 5: Run ingestion workload                              │" -ForegroundColor Yellow
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow

    $workloadArgs = @{
        ControlUrl = $baselineUrl
        TreatmentUrl = $branchUrl
        Workload = $Workload
        OutputDirectory = $outputDir
        ComparisonLabel = $comparisonLabel
        ComparisonMode = $ComparisonMode
        ControlImage = $baselineImage
        TreatmentImage = $branchImage
        ControlProviders = $controlProviders
        TreatmentProviders = $treatmentProviders
        WarmupIterations = $WarmupIterations
        MeasuredIterations = $MeasuredIterations
        BundleCount = $BundleCount
        BundleSize = $BundleSize
        Concurrency = $Concurrency
        ImportInputUrl = $ImportInputUrl
        ImportResourceType = $ImportResourceType
        ImportSearchProbe = $ImportSearchProbe
        ImportExpectedResourceCount = $ImportExpectedResourceCount
        ImportWarmupInputUrl = $ImportWarmupInputUrl
        ImportWarmupResourceType = $ImportWarmupResourceType
        ImportWarmupSearchProbe = $ImportWarmupSearchProbe
        ImportWarmupExpectedResourceCount = $ImportWarmupExpectedResourceCount
        ImportTimeoutMinutes = $ImportTimeoutMinutes
        ImportPollIntervalSeconds = $ImportPollIntervalSeconds
        ControlLabel = $controlReportLabel
        TreatmentLabel = $treatmentReportLabel
        Parallel = $ParallelWorkloads
    }
    & "$scriptsDir/Invoke-IngestionWorkload.ps1" @workloadArgs
}

} -Cleanup {
    Invoke-RunCleanup `
        -State $runState `
        -Skip:$SkipCleanup `
        -ResourceGroup $ResourceGroupName `
        -Registry ($ContainerRegistry -replace '\.azurecr\.io$', '') `
        -Image "$($FhirVersion.ToLower())_fhir-server:$branchImageTag"
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " A/B Test Complete" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host " Results directory: $outputDir"
$comparisonReportName = if ($Workload -eq 'E2E') { 'comparison-report.md' } else { 'ingestion-comparison.md' }
Write-Host " Comparison report: $(Join-Path $outputDir $comparisonReportName)"
Write-Host ""
