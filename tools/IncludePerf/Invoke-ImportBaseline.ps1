<#
.SYNOPSIS
    Produces safe, reproducible import baseline reports or compares compatible reports.

.DESCRIPTION
    The default path is an offline plan: it reads a local manifest and writes a report without making
    a network request. Live import execution requires -Execute and an operator-prepared isolated store.
    The script neither creates nor deletes databases, storage, or cloud resources.
#>

[CmdletBinding(DefaultParameterSetName = 'Plan')]
param(
    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $ManifestPath,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $OutputPath,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [ValidateSet('Stu3', 'R4', 'R4B', 'R5')]
    [string] $FhirVersion,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $BinaryIdentity,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $ImportProvider,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $ImportProviderEvidenceSource,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $FhirPathProvider,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $FhirPathProviderEvidenceSource,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [ValidateSet('SqlServer', 'CosmosDb')]
    [string] $Backend,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $EnvironmentIdentity,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $LoadProfile,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [string] $PreparedStoreIdentity,

    [Parameter(ParameterSetName = 'Plan')]
    [Parameter(ParameterSetName = 'Execute')]
    [ValidateRange(1, 1000)]
    [int] $Repetitions = 1,

    [Parameter(ParameterSetName = 'Execute', Mandatory = $true)]
    [switch] $Execute,

    [Parameter(ParameterSetName = 'Execute')]
    [string] $Endpoint,

    [Parameter(ParameterSetName = 'Execute')]
    [string] $StorageBaseUri,

    [Parameter(ParameterSetName = 'Execute')]
    [string] $ErrorContainerName,

    [Parameter(ParameterSetName = 'Execute')]
    [string] $AccessToken,

    [Parameter(ParameterSetName = 'Execute')]
    [ValidateRange(1, 300)]
    [int] $PollSeconds = 10,

    [Parameter(ParameterSetName = 'Execute')]
    [ValidateRange(1, 1440)]
    [int] $TimeoutMinutes = 60,

    [Parameter(ParameterSetName = 'Compare', Mandatory = $true)]
    [string] $BaselineReportPath,

    [Parameter(ParameterSetName = 'Compare', Mandatory = $true)]
    [string] $CandidateReportPath,

    [Parameter(ParameterSetName = 'Compare')]
    [ValidateSet('Import', 'FhirPath')]
    [string] $SelectedCapability = 'Import'
)

$ErrorActionPreference = 'Stop'

if ($null -eq ('System.Net.Http.HttpClientHandler' -as [type])) {
    Add-Type -AssemblyName System.Net.Http
}

function Get-RequiredReportValue {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $current = $Object
    foreach ($segment in $Path.Split('.')) {
        if ($null -eq $current) {
            throw "Report is missing '$Path'."
        }

        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property -or $null -eq $property.Value -or ([string]$property.Value).Length -eq 0) {
            throw "Report is missing '$Path'."
        }

        $current = $property.Value
    }

    return $current
}

function Assert-NonNegativeNumber {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    if ($Value -isnot [byte] -and $Value -isnot [int] -and $Value -isnot [long] -and $Value -isnot [decimal] -and $Value -isnot [double] -and $Value -isnot [float]) {
        throw "'$Path' must be a raw numeric value."
    }

    if ($Value -lt 0) {
        throw "'$Path' must not be negative."
    }
}

function Test-ServerMetric {
    param(
        [Parameter(Mandatory = $true)] $Metric,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $statusProperty = $Metric.PSObject.Properties['Status']
    if ($null -eq $statusProperty -or $null -eq $statusProperty.Value -or ([string]$statusProperty.Value).Length -eq 0) {
        throw "Report is missing '$Path.Status'."
    }

    $status = $statusProperty.Value
    if ($status -notin @('available', 'unavailable')) {
        throw "'$Path.Status' must be 'available' or 'unavailable'."
    }

    $valueProperty = $Metric.PSObject.Properties['Value']
    if ($null -eq $valueProperty) {
        throw "Report is missing '$Path.Value'."
    }

    if ($status -eq 'unavailable') {
        if ($null -ne $valueProperty.Value) {
            throw "'$Path.Value' must be null when '$Path.Status' is unavailable."
        }

        return
    }

    Assert-NonNegativeNumber -Value $valueProperty.Value -Path "$Path.Value"
}

function Test-ImportBaselineReport {
    <#
    .SYNOPSIS
        Validates the stable, lossless report schema before a report can be compared.
    #>
    param([Parameter(Mandatory = $true)] $Report)

    $schemaVersion = Get-RequiredReportValue -Object $Report -Path 'SchemaVersion'
    if ($schemaVersion -ne '1.0') {
        throw "Unsupported report SchemaVersion '$schemaVersion'."
    }

    $corpusHash = Get-RequiredReportValue -Object $Report -Path 'Corpus.Sha256'
    if ($corpusHash -notmatch '^[a-fA-F0-9]{64}$') {
        throw "'Corpus.Sha256' must be a SHA-256 hash."
    }

    foreach ($path in @(
        'Corpus.Identity',
        'Corpus.TotalResources',
        'Corpus.InputFileCount',
        'Corpus.ResourceTypeCounts',
        'FhirVersion',
        'Binary.Identity',
        'Binary.Kind',
        'ProviderConfiguration.Import.EffectiveProvider',
        'ProviderConfiguration.Import.EvidenceSource',
        'ProviderConfiguration.FhirPath.EffectiveProvider',
        'ProviderConfiguration.FhirPath.EvidenceSource',
        'Environment.Backend',
        'Environment.Identity',
        'Environment.LoadProfile',
        'Environment.PreparedStoreIdentity',
        'Execution.Repetitions',
        'Execution.TimingWindow',
        'Execution.Counts',
        'Execution.TerminalOutcome',
        'ServerMetrics')) {
        $null = Get-RequiredReportValue -Object $Report -Path $path
    }

    Assert-NonNegativeNumber -Value (Get-RequiredReportValue -Object $Report -Path 'Corpus.TotalResources') -Path 'Corpus.TotalResources'
    Assert-NonNegativeNumber -Value (Get-RequiredReportValue -Object $Report -Path 'Corpus.InputFileCount') -Path 'Corpus.InputFileCount'
    Assert-NonNegativeNumber -Value (Get-RequiredReportValue -Object $Report -Path 'Execution.Repetitions') -Path 'Execution.Repetitions'
    $resourceTypeCounts = Get-RequiredReportValue -Object $Report -Path 'Corpus.ResourceTypeCounts'
    $resourceTypeCountValues = if ($resourceTypeCounts -is [Collections.IDictionary]) {
        @($resourceTypeCounts.Values)
    }
    else {
        @($resourceTypeCounts.PSObject.Properties | ForEach-Object { $_.Value })
    }
    if ($resourceTypeCountValues.Count -eq 0) {
        throw "'Corpus.ResourceTypeCounts' must include at least one resource type."
    }

    $totalResourceTypeCount = [long]0
    foreach ($resourceTypeCount in $resourceTypeCountValues) {
        Assert-NonNegativeNumber -Value $resourceTypeCount -Path 'Corpus.ResourceTypeCounts'
        $totalResourceTypeCount += $resourceTypeCount
    }
    if ($totalResourceTypeCount -ne $Report.Corpus.TotalResources) {
        throw "'Corpus.ResourceTypeCounts' must sum to 'Corpus.TotalResources'."
    }

    $outcome = Get-RequiredReportValue -Object $Report -Path 'Execution.TerminalOutcome'
    if ($outcome -notin @('Planned', 'Succeeded', 'Failed', 'TimedOut', 'Cancelled')) {
        throw "Unsupported terminal outcome '$outcome'."
    }

    $timing = Get-RequiredReportValue -Object $Report -Path 'Execution.TimingWindow'
    $counts = Get-RequiredReportValue -Object $Report -Path 'Execution.Counts'
    foreach ($path in @('RequestedResourceCount', 'SuccessfulResourceCount', 'ErrorResourceCount')) {
        Assert-NonNegativeNumber -Value (Get-RequiredReportValue -Object $counts -Path $path) -Path "Execution.Counts.$path"
    }

    if ($outcome -eq 'Planned') {
        foreach ($path in @('RequestSubmittedUtc', 'TerminalUtc', 'WallClockMilliseconds')) {
            if ($null -ne $timing.PSObject.Properties[$path].Value) {
                throw "'Execution.TimingWindow.$path' must be null for a planned run."
            }
        }
    }
    else {
        foreach ($path in @('RequestSubmittedUtc', 'TerminalUtc', 'WallClockMilliseconds')) {
            $null = Get-RequiredReportValue -Object $timing -Path $path
        }

        Assert-NonNegativeNumber -Value $timing.WallClockMilliseconds -Path 'Execution.TimingWindow.WallClockMilliseconds'
    }

    if ($counts.RequestedResourceCount -ne $Report.Corpus.TotalResources) {
        throw "'Execution.Counts.RequestedResourceCount' must match 'Corpus.TotalResources'."
    }

    if ($outcome -eq 'Succeeded' -and $counts.ErrorResourceCount -gt 0) {
        throw "A report with import errors cannot be Succeeded."
    }

    if ($outcome -eq 'Succeeded' -and $counts.SuccessfulResourceCount -ne $counts.RequestedResourceCount) {
        throw "A successful report must account for every requested resource."
    }

    if ($counts.SuccessfulResourceCount + $counts.ErrorResourceCount -gt $counts.RequestedResourceCount) {
        throw "Import result counts exceed the requested resource count."
    }

    foreach ($metric in @('Cpu', 'AllocationRateBytesPerSecond', 'PeakWorkingSetBytes')) {
        $metricValue = Get-RequiredReportValue -Object $Report -Path "ServerMetrics.$metric"
        Test-ServerMetric -Metric $metricValue -Path "ServerMetrics.$metric"
    }

    return $true
}

function Assert-MatchingReportValue {
    param(
        [Parameter(Mandatory = $true)] $BaselineReport,
        [Parameter(Mandatory = $true)] $CandidateReport,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $baseline = Get-RequiredReportValue -Object $BaselineReport -Path $Path
    $candidate = Get-RequiredReportValue -Object $CandidateReport -Path $Path
    if ($baseline -ne $candidate) {
        throw "Reports are incomparable because '$Path' differs."
    }
}

function Assert-MatchingReportStructure {
    param(
        [Parameter(Mandatory = $true)] $BaselineReport,
        [Parameter(Mandatory = $true)] $CandidateReport,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $baseline = Get-RequiredReportValue -Object $BaselineReport -Path $Path | ConvertTo-Json -Depth 10 -Compress
    $candidate = Get-RequiredReportValue -Object $CandidateReport -Path $Path | ConvertTo-Json -Depth 10 -Compress
    if ($baseline -cne $candidate) {
        throw "Reports are incomparable because '$Path' differs."
    }
}

function Test-ImportBaselineComparison {
    <#
    .SYNOPSIS
        Rejects reports that differ in anything other than the explicitly selected provider capability.
    #>
    param(
        [Parameter(Mandatory = $true)] $BaselineReport,
        [Parameter(Mandatory = $true)] $CandidateReport,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Import', 'FhirPath')]
        [string] $SelectedCapability
    )

    $null = Test-ImportBaselineReport -Report $BaselineReport
    $null = Test-ImportBaselineReport -Report $CandidateReport

    foreach ($path in @(
        'SchemaVersion',
        'Corpus.Identity',
        'Corpus.Sha256',
        'Corpus.TotalResources',
        'Corpus.InputFileCount',
        'FhirVersion',
        'Binary.Identity',
        'Binary.Kind',
        'Environment.Backend',
        'Environment.Identity',
        'Environment.LoadProfile',
        'Environment.PreparedStoreIdentity',
        'Execution.Repetitions')) {
        Assert-MatchingReportValue -BaselineReport $BaselineReport -CandidateReport $CandidateReport -Path $path
    }
    Assert-MatchingReportStructure -BaselineReport $BaselineReport -CandidateReport $CandidateReport -Path 'Corpus.ResourceTypeCounts'

    $otherCapability = if ($SelectedCapability -eq 'Import') { 'FhirPath' } else { 'Import' }
    $baselineSelectedProvider = Get-RequiredReportValue -Object $BaselineReport -Path "ProviderConfiguration.$SelectedCapability.EffectiveProvider"
    $candidateSelectedProvider = Get-RequiredReportValue -Object $CandidateReport -Path "ProviderConfiguration.$SelectedCapability.EffectiveProvider"
    if ($baselineSelectedProvider -eq $candidateSelectedProvider) {
        throw "Reports are incomparable because the selected provider is unchanged."
    }

    foreach ($path in @(
        "ProviderConfiguration.$otherCapability.EffectiveProvider",
        "ProviderConfiguration.$otherCapability.EvidenceSource")) {
        Assert-MatchingReportValue -BaselineReport $BaselineReport -CandidateReport $CandidateReport -Path $path
    }

    if ($BaselineReport.Execution.TerminalOutcome -ne 'Succeeded' -or $CandidateReport.Execution.TerminalOutcome -ne 'Succeeded') {
        throw 'Only successfully completed reports can be compared.'
    }

    return $true
}

function Resolve-ImportStatusUri {
    <#
    .SYNOPSIS
        Resolves a status or redirect location and rejects cross-origin authorization forwarding.
    #>
    param(
        [Parameter(Mandatory = $true)] [string] $Endpoint,
        [Parameter(Mandatory = $true)] [string] $StatusLocation
    )

    $endpointUri = [Uri]::new($Endpoint.TrimEnd('/') + '/')
    $statusUri = [Uri]::new($endpointUri, $StatusLocation)

    if ($statusUri.Scheme -ne $endpointUri.Scheme -or $statusUri.Host -ne $endpointUri.Host -or $statusUri.Port -ne $endpointUri.Port) {
        throw 'Import status or redirect URL must use the same origin as the configured endpoint.'
    }

    return $statusUri.AbsoluteUri
}

function Get-SafeErrorSummary {
    param([Parameter(Mandatory = $true)] [Exception] $Exception)

    return 'Import request, status polling, or terminal response validation failed.'
}

function Get-ImportPollWaitMilliseconds {
    param(
        [Parameter(Mandatory = $true)] [DateTime] $DeadlineUtc,
        [Parameter(Mandatory = $true)] [int] $PollSeconds
    )

    $remaining = $DeadlineUtc - [DateTime]::UtcNow
    if ($remaining -le [TimeSpan]::Zero) {
        return 0
    }

    return [Math]::Min(
        [long]$PollSeconds * 1000,
        [long][Math]::Ceiling($remaining.TotalMilliseconds))
}

function Wait-ImportPollInterval {
    param(
        [Parameter(Mandatory = $true)] [DateTime] $DeadlineUtc,
        [Parameter(Mandatory = $true)] [int] $PollSeconds,
        [Parameter(Mandatory = $true)] [Threading.CancellationToken] $CancellationToken
    )

    $CancellationToken.ThrowIfCancellationRequested()
    $waitMilliseconds = Get-ImportPollWaitMilliseconds -DeadlineUtc $DeadlineUtc -PollSeconds $PollSeconds
    if ($waitMilliseconds -le 0) {
        throw [OperationCanceledException]::new('Import polling reached the configured timeout.')
    }

    if ($CancellationToken.WaitHandle.WaitOne([int]$waitMilliseconds)) {
        $CancellationToken.ThrowIfCancellationRequested()
    }

    if ([DateTime]::UtcNow -ge $DeadlineUtc) {
        throw [OperationCanceledException]::new('Import polling reached the configured timeout.')
    }
}

function Read-ImportTerminalResponse {
    param(
        [Parameter(Mandatory = $true)] [Net.Http.HttpResponseMessage] $Response,
        [Parameter(Mandatory = $true)] [Threading.CancellationToken] $CancellationToken
    )

    $CancellationToken.ThrowIfCancellationRequested()
    $contentReadTask = $Response.Content.ReadAsStringAsync()
    $cancellationTask = [Threading.Tasks.Task]::Delay([Threading.Timeout]::Infinite, $CancellationToken)
    $completedTask = [Threading.Tasks.Task]::WhenAny([Threading.Tasks.Task[]]@($contentReadTask, $cancellationTask)).GetAwaiter().GetResult()
    if (-not [object]::ReferenceEquals($completedTask, $contentReadTask)) {
        $CancellationToken.ThrowIfCancellationRequested()
    }

    $CancellationToken.ThrowIfCancellationRequested()
    return $contentReadTask.GetAwaiter().GetResult()
}

function Get-ManifestCorpus {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Manifest '$Path' was not found."
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $manifest = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
    $resourceTypes = @($manifest.resourceTypes)
    $inputFileCount = @($resourceTypes | ForEach-Object { @($_.files).Count } | Measure-Object -Sum).Sum
    $counts = [ordered]@{}
    foreach ($resourceType in $resourceTypes) {
        $counts[$resourceType.type] = $resourceType.count
    }

    return [pscustomobject]@{
        Identity = [IO.Path]::GetFileName($resolvedPath)
        Sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        TotalResources = [long]$manifest.totalResources
        InputFileCount = [long]$inputFileCount
        ResourceTypeCounts = $counts
        Manifest = $manifest
    }
}

function Get-ImportStatusLocation {
    param([Parameter(Mandatory = $true)] [Net.Http.HttpResponseMessage] $Response)

    if ($null -ne $Response.Content.Headers.ContentLocation) {
        return $Response.Content.Headers.ContentLocation.OriginalString
    }

    if ($null -ne $Response.Headers.Location) {
        return $Response.Headers.Location.OriginalString
    }

    return $null
}

function New-ImportBaselineReport {
    param(
        [Parameter(Mandatory = $true)] $Corpus,
        [Parameter(Mandatory = $true)] [string] $FhirVersion,
        [Parameter(Mandatory = $true)] [string] $BinaryIdentity,
        [Parameter(Mandatory = $true)] [string] $ImportProvider,
        [Parameter(Mandatory = $true)] [string] $ImportProviderEvidenceSource,
        [Parameter(Mandatory = $true)] [string] $FhirPathProvider,
        [Parameter(Mandatory = $true)] [string] $FhirPathProviderEvidenceSource,
        [Parameter(Mandatory = $true)] [string] $Backend,
        [Parameter(Mandatory = $true)] [string] $EnvironmentIdentity,
        [Parameter(Mandatory = $true)] [string] $LoadProfile,
        [Parameter(Mandatory = $true)] [string] $PreparedStoreIdentity,
        [Parameter(Mandatory = $true)] [int] $Repetitions,
        [Parameter(Mandatory = $true)] [string] $Outcome,
        $TimingWindow,
        $Counts,
        [string] $ErrorSummary
    )

    return [pscustomobject]([ordered]@{
        SchemaVersion = '1.0'
        Corpus = [pscustomobject]@{
            Identity = $Corpus.Identity
            Sha256 = $Corpus.Sha256
            TotalResources = $Corpus.TotalResources
            InputFileCount = $Corpus.InputFileCount
            ResourceTypeCounts = $Corpus.ResourceTypeCounts
        }
        FhirVersion = $FhirVersion
        Binary = [pscustomobject]@{
            Identity = $BinaryIdentity
            Kind = if ($BinaryIdentity.StartsWith('sha256:', [StringComparison]::OrdinalIgnoreCase)) { 'container-image' } else { 'binary' }
        }
        ProviderConfiguration = [pscustomobject]@{
            Import = [pscustomobject]@{
                EffectiveProvider = $ImportProvider
                EvidenceSource = $ImportProviderEvidenceSource
            }
            FhirPath = [pscustomobject]@{
                EffectiveProvider = $FhirPathProvider
                EvidenceSource = $FhirPathProviderEvidenceSource
            }
        }
        Environment = [pscustomobject]@{
            Backend = $Backend
            Identity = $EnvironmentIdentity
            LoadProfile = $LoadProfile
            PreparedStoreIdentity = $PreparedStoreIdentity
        }
        Execution = [pscustomobject]@{
            Repetitions = $Repetitions
            TimingWindow = $TimingWindow
            Counts = $Counts
            TerminalOutcome = $Outcome
            ErrorSummary = $ErrorSummary
        }
        ServerMetrics = [pscustomobject]@{
            Cpu = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
            AllocationRateBytesPerSecond = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
            PeakWorkingSetBytes = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
        }
    })
}

function Write-ImportBaselineReport {
    param(
        [Parameter(Mandatory = $true)] $Report,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $null = Test-ImportBaselineReport -Report $Report
    $Report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-ImportBaseline {
    param(
        [Parameter(Mandatory = $true)] [string] $ManifestPath,
        [Parameter(Mandatory = $true)] [string] $OutputPath,
        [Parameter(Mandatory = $true)] [string] $FhirVersion,
        [Parameter(Mandatory = $true)] [string] $BinaryIdentity,
        [Parameter(Mandatory = $true)] [string] $ImportProvider,
        [Parameter(Mandatory = $true)] [string] $ImportProviderEvidenceSource,
        [Parameter(Mandatory = $true)] [string] $FhirPathProvider,
        [Parameter(Mandatory = $true)] [string] $FhirPathProviderEvidenceSource,
        [Parameter(Mandatory = $true)] [string] $Backend,
        [Parameter(Mandatory = $true)] [string] $EnvironmentIdentity,
        [Parameter(Mandatory = $true)] [string] $LoadProfile,
        [Parameter(Mandatory = $true)] [string] $PreparedStoreIdentity,
        [Parameter(Mandatory = $true)] [int] $Repetitions,
        [switch] $Execute,
        [string] $Endpoint,
        [string] $StorageBaseUri,
        [string] $ErrorContainerName,
        [string] $AccessToken,
        [int] $PollSeconds,
        [int] $TimeoutMinutes
    )

    if ($Repetitions -ne 1) {
        throw 'Each report records one import attempt. Prepare an equivalent fresh store and run the tool again for each repetition.'
    }

    $corpus = Get-ManifestCorpus -Path $ManifestPath
    $plannedCounts = [pscustomobject]@{
        RequestedResourceCount = $corpus.TotalResources
        SuccessfulResourceCount = 0
        ErrorResourceCount = 0
    }
    $plannedTiming = [pscustomobject]@{
        RequestSubmittedUtc = $null
        TerminalUtc = $null
        WallClockMilliseconds = $null
    }

    if (-not $Execute) {
        $report = New-ImportBaselineReport -Corpus $corpus -FhirVersion $FhirVersion -BinaryIdentity $BinaryIdentity `
            -ImportProvider $ImportProvider -ImportProviderEvidenceSource $ImportProviderEvidenceSource `
            -FhirPathProvider $FhirPathProvider -FhirPathProviderEvidenceSource $FhirPathProviderEvidenceSource `
            -Backend $Backend -EnvironmentIdentity $EnvironmentIdentity -LoadProfile $LoadProfile `
            -PreparedStoreIdentity $PreparedStoreIdentity -Repetitions $Repetitions -Outcome 'Planned' `
            -TimingWindow $plannedTiming -Counts $plannedCounts
        Write-ImportBaselineReport -Report $report -Path $OutputPath
        Write-Host 'Wrote offline import baseline plan.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($Endpoint) -or [string]::IsNullOrWhiteSpace($StorageBaseUri) -or [string]::IsNullOrWhiteSpace($ErrorContainerName)) {
        throw '-Execute requires Endpoint, StorageBaseUri, and ErrorContainerName so an already-completed request is not silently reused.'
    }

    $endpointUri = [Uri]::new($Endpoint.TrimEnd('/') + '/')
    $storageUri = [Uri]::new($StorageBaseUri.TrimEnd('/') + '/')
    if ($endpointUri.Scheme -ne 'https' -or $storageUri.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($storageUri.Query)) {
        throw 'Endpoint and StorageBaseUri must be HTTPS origins; StorageBaseUri must not contain a query string.'
    }

    $inputs = @()
    foreach ($resourceType in @($corpus.Manifest.resourceTypes)) {
        foreach ($file in @($resourceType.files)) {
            $inputs += @{
                name = 'input'
                part = @(
                    @{ name = 'type'; valueString = $resourceType.type },
                    @{ name = 'url'; valueUri = ([Uri]::new($storageUri, $file)).AbsoluteUri }
                )
            }
        }
    }

    $parameters = @(
        @{ name = 'inputFormat'; valueString = 'application/fhir+ndjson' },
        @{ name = 'mode'; valueString = 'InitialLoad' },
        @{ name = 'errorContainerName'; valueString = $ErrorContainerName }
    )
    $body = @{ resourceType = 'Parameters'; parameter = $parameters + $inputs } | ConvertTo-Json -Depth 10 -Compress

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $headers = $null
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        $headers = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $AccessToken)
    }
    $requestUri = [Uri]::new($endpointUri, '$import')
    $started = [DateTime]::UtcNow
    $deadline = $started.AddMinutes($TimeoutMinutes)
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $outcome = 'Failed'
    $errorSummary = $null
    $counts = $plannedCounts

    try {
        $cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes($TimeoutMinutes))
        try {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $requestUri)
            $request.Headers.Add('Prefer', 'respond-async')
            if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
                $request.Headers.Authorization = $headers
            }
            $request.Content = [Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/fhir+json')
            $response = $client.SendAsync($request, $cancellation.Token).GetAwaiter().GetResult()
            if ($response.StatusCode -ne [Net.HttpStatusCode]::Accepted) {
                throw "Import submission returned status $([int]$response.StatusCode), not 202 Accepted."
            }

            $statusLocation = Get-ImportStatusLocation -Response $response
            if ([string]::IsNullOrWhiteSpace($statusLocation)) {
                throw 'Import submission did not return Content-Location.'
            }

            $statusUri = Resolve-ImportStatusUri -Endpoint $endpointUri.AbsoluteUri -StatusLocation $statusLocation
            while ($true) {
                Wait-ImportPollInterval -DeadlineUtc $deadline -PollSeconds $PollSeconds -CancellationToken $cancellation.Token

                $pollRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $statusUri)
                if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
                    $pollRequest.Headers.Authorization = $headers
                }

                $pollResponse = $client.SendAsync($pollRequest, $cancellation.Token).GetAwaiter().GetResult()
                if ([int]$pollResponse.StatusCode -ge 300 -and [int]$pollResponse.StatusCode -lt 400) {
                    if ($null -eq $pollResponse.Headers.Location) {
                        throw "Import status returned redirect $([int]$pollResponse.StatusCode) without a location."
                    }

                    $statusUri = Resolve-ImportStatusUri -Endpoint $endpointUri.AbsoluteUri -StatusLocation $pollResponse.Headers.Location.OriginalString
                    continue
                }

                if ($pollResponse.StatusCode -eq [Net.HttpStatusCode]::Accepted) {
                    continue
                }

                if ($pollResponse.StatusCode -ne [Net.HttpStatusCode]::OK) {
                    throw "Import status returned unexpected status $([int]$pollResponse.StatusCode)."
                }

                $result = Read-ImportTerminalResponse -Response $pollResponse -CancellationToken $cancellation.Token | ConvertFrom-Json
                $successful = @($result.output | ForEach-Object { [long]$_.count } | Measure-Object -Sum).Sum
                $errors = @($result.error | ForEach-Object { [long]$_.count } | Measure-Object -Sum).Sum
                $counts = [pscustomobject]@{
                    RequestedResourceCount = $corpus.TotalResources
                    SuccessfulResourceCount = [long]$successful
                    ErrorResourceCount = [long]$errors
                }
                if ($counts.SuccessfulResourceCount + $counts.ErrorResourceCount -ne $counts.RequestedResourceCount) {
                    throw 'Import terminal result counts did not match the requested resource count.'
                }

                if ($counts.ErrorResourceCount -gt 0) {
                    throw 'Import terminal result contained per-resource errors.'
                }

                $outcome = 'Succeeded'
                break
            }
        }
        finally {
            $cancellation.Dispose()
        }
    }
    catch [OperationCanceledException] {
        $outcome = 'TimedOut'
        $errorSummary = 'Import did not reach a terminal status before the configured timeout.'
    }
    catch {
        $outcome = 'Failed'
        $errorSummary = Get-SafeErrorSummary -Exception $_.Exception
    }
    finally {
        $stopwatch.Stop()
        $client.Dispose()
        $handler.Dispose()
    }

    $timing = [pscustomobject]@{
        RequestSubmittedUtc = $started.ToString('O')
        TerminalUtc = [DateTime]::UtcNow.ToString('O')
        WallClockMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
    }
    $report = New-ImportBaselineReport -Corpus $corpus -FhirVersion $FhirVersion -BinaryIdentity $BinaryIdentity `
        -ImportProvider $ImportProvider -ImportProviderEvidenceSource $ImportProviderEvidenceSource `
        -FhirPathProvider $FhirPathProvider -FhirPathProviderEvidenceSource $FhirPathProviderEvidenceSource `
        -Backend $Backend -EnvironmentIdentity $EnvironmentIdentity -LoadProfile $LoadProfile `
        -PreparedStoreIdentity $PreparedStoreIdentity -Repetitions $Repetitions -Outcome $outcome `
        -TimingWindow $timing -Counts $counts -ErrorSummary $errorSummary
    Write-ImportBaselineReport -Report $report -Path $OutputPath

    if ($outcome -ne 'Succeeded') {
        throw "Import baseline execution ended with '$outcome'. See the report for its sanitized error summary."
    }
}

function Compare-ImportBaselineReports {
    param(
        [Parameter(Mandatory = $true)] [string] $BaselineReportPath,
        [Parameter(Mandatory = $true)] [string] $CandidateReportPath,
        [Parameter(Mandatory = $true)] [string] $SelectedCapability
    )

    $baseline = Get-Content -LiteralPath $BaselineReportPath -Raw | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $CandidateReportPath -Raw | ConvertFrom-Json
    $null = Test-ImportBaselineComparison -BaselineReport $baseline -CandidateReport $candidate -SelectedCapability $SelectedCapability
    [pscustomobject]@{
        Comparable = $true
        SelectedCapability = $SelectedCapability
        BaselineWallClockMilliseconds = $baseline.Execution.TimingWindow.WallClockMilliseconds
        CandidateWallClockMilliseconds = $candidate.Execution.TimingWindow.WallClockMilliseconds
        BaselineRequestedResourceCount = $baseline.Execution.Counts.RequestedResourceCount
        CandidateRequestedResourceCount = $candidate.Execution.Counts.RequestedResourceCount
    } | ConvertTo-Json -Depth 5
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($PSCmdlet.ParameterSetName -eq 'Compare') {
        Compare-ImportBaselineReports -BaselineReportPath $BaselineReportPath -CandidateReportPath $CandidateReportPath -SelectedCapability $SelectedCapability
    }
    else {
        foreach ($name in @(
            'ManifestPath', 'OutputPath', 'FhirVersion', 'BinaryIdentity', 'ImportProvider',
            'ImportProviderEvidenceSource', 'FhirPathProvider', 'FhirPathProviderEvidenceSource',
            'Backend', 'EnvironmentIdentity', 'LoadProfile', 'PreparedStoreIdentity')) {
            if ([string]::IsNullOrWhiteSpace($PSBoundParameters[$name])) {
                throw "-$name is required."
            }
        }

        $invokeParameters = @{} + $PSBoundParameters
        $invokeParameters['Repetitions'] = $Repetitions
        $invokeParameters['PollSeconds'] = $PollSeconds
        $invokeParameters['TimeoutMinutes'] = $TimeoutMinutes
        Invoke-ImportBaseline @invokeParameters
    }
}
