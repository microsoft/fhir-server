param(
    [Parameter(Mandatory = $true)]
    [string] $RedoxSource,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$redoxRevision = '47a9b74442e8fc3456163fd8a02db9245a111bed'
$actualRevision = & git -C $RedoxSource rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $redoxRevision) {
    throw "REDox source must be checked out at $redoxRevision."
}

$sourceChanges = & git -C $RedoxSource status --porcelain
if ($LASTEXITCODE -ne 0 -or $sourceChanges) {
    throw 'REDox source must be unmodified.'
}

if (Test-Path $OutputDirectory) {
    throw "Refusing to overwrite an existing output directory: $OutputDirectory"
}

$null = New-Item -ItemType Directory -Path $OutputDirectory
$output = (Resolve-Path $OutputDirectory).Path
$redoxAssembly = Join-Path $RedoxSource 'src\REDox\bin\Release\net10.0\REDox.dll'
if (!(Test-Path $redoxAssembly)) {
    throw 'Build the pinned REDox source in Release before running the experiment.'
}

$project = Join-Path $PSScriptRoot 'RedoxExperiment.csproj'
& $dotnet build $project -c Release --no-restore --nologo -v:q "-p:RedoxAssembly=$redoxAssembly"
if ($LASTEXITCODE -ne 0) { throw 'Experiment build failed.' }

$assembly = Join-Path $PSScriptRoot 'bin\Release\net10.0\RedoxExperiment.dll'
$corpus = Join-Path $repo 'src\Microsoft.Health.Fhir.Azure\IntegrationDataStore\TestData\representative-import-1000.ndjson.gz'
$previousProcessorCount = $env:DOTNET_PROCESSOR_COUNT
$env:DOTNET_PROCESSOR_COUNT = '2'

try {
    $verification = & $dotnet $assembly verify $corpus
    $verificationExit = $LASTEXITCODE
    $verification | Set-Content (Join-Path $output 'verification.json') -Encoding utf8
    if ($verificationExit -notin 0, 3) {
        throw "Corpus verification failed with exit code $verificationExit."
    }

    if ($verificationExit -eq 3) {
        Write-Warning 'Input compatibility gate failed. Measurements are valid-corpus component diagnostics, not adoption evidence.'
    }

    $workloads = @(
        @{ Name = 'parse'; Passes = 20 },
        @{ Name = 'roundtrip'; Passes = 20 },
        @{ Name = 'metadata'; Passes = 1000 }
    )
    $random = [Random]::new(239717)
    $schedule = @()
    foreach ($workload in $workloads) {
        foreach ($pair in 1..2) {
            foreach ($label in @('A', 'A2')) {
                $schedule += [pscustomobject]@{
                    Phase = 'AA'; Pair = $pair; Label = $label
                    Variant = 'baseline'; Workload = $workload.Name; Passes = $workload.Passes
                }
            }
        }

        $orders = @($true, $false, $true, $false, ($random.Next(2) -eq 0))
        for ($i = $orders.Count - 1; $i -gt 0; $i--) {
            $j = $random.Next($i + 1)
            $orders[$i], $orders[$j] = $orders[$j], $orders[$i]
        }

        foreach ($pair in 1..5) {
            $variants = if ($orders[$pair - 1]) { @('baseline', 'redox') } else { @('redox', 'baseline') }
            foreach ($variant in $variants) {
                $schedule += [pscustomobject]@{
                    Phase = 'AB'; Pair = $pair; Label = $variant
                    Variant = $variant; Workload = $workload.Name; Passes = $workload.Passes
                }
            }
        }
    }

    $manifest = [ordered]@{
        utc = [DateTime]::UtcNow.ToString('o')
        baselineCommit = '87c599ab28f6146a5544146cca2c3543b37e2a3e'
        branchCommit = (& git -C $repo rev-parse HEAD)
        redoxRevision = $redoxRevision
        redoxAssemblySha256 = (Get-FileHash $redoxAssembly -Algorithm SHA256).Hash
        experimentAssemblySha256 = (Get-FileHash $assembly -Algorithm SHA256).Hash
        compressedCorpusSha256 = (Get-FileHash $corpus -Algorithm SHA256).Hash
        processorCount = 2
        warmupSeconds = 3
        integrationReady = ($verificationExit -eq 0)
        scope = 'In-process components only. No SQL, HTTP, search indexing, blob I/O, or import job execution.'
        sourceHashes = @(Get-ChildItem $PSScriptRoot -File | Where-Object {
            $_.Extension -in '.cs', '.csproj', '.props', '.ps1' -or $_.Name -eq 'packages.lock.json'
        } | ForEach-Object {
            @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
        })
        schedule = $schedule
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'manifest.json') -Encoding utf8

    $index = 0
    foreach ($run in $schedule) {
        $index++
        $raw = & $dotnet $assembly benchmark $corpus $run.Variant $run.Workload $run.Passes
        if ($LASTEXITCODE -ne 0) {
            $raw | Set-Content (Join-Path $output "failed-$index.txt") -Encoding utf8
            throw "Measurement $index failed. Prior results remain in $output."
        }

        $result = $raw | ConvertFrom-Json
        $result | Add-Member -NotePropertyName phase -NotePropertyValue $run.Phase
        $result | Add-Member -NotePropertyName pair -NotePropertyValue $run.Pair
        $result | Add-Member -NotePropertyName label -NotePropertyValue $run.Label
        $result | Add-Member -NotePropertyName sequence -NotePropertyValue $index
        $result | ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $output 'measurements.jsonl') -Encoding utf8
        Write-Host "$index/$($schedule.Count) $($run.Phase) $($run.Workload) $($run.Variant): $([math]::Round($result.cpuMs)) ms CPU"
    }
}
finally {
    $env:DOTNET_PROCESSOR_COUNT = $previousProcessorCount
}
