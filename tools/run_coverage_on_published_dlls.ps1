<#
.SYNOPSIS
Runs coverage collection against published test DLLs using coverlet.console and aggregates results with reportgenerator.

.DESCRIPTION
This script is intended for CI agents that only have access to published test DLL artifacts (not the test csproj). It installs local dotnet global tools into a .tools folder inside the repository (idempotent), runs coverlet.console for each test DLL found, and aggregates outputs with reportgenerator into a single Cobertura report.

.PARAMETER ArtifactsDir
Path where published test DLLs live. Script searches recursively for '*Tests.dll' by default.
.PARAMETER OutDir
Output directory for coverage artifacts and final aggregated report.
.PARAMETER Pattern
Optional file pattern for test dlls (default '*Tests.dll').

EXAMPLE
PowerShell -File tools\run_coverage_on_published_dlls.ps1 -ArtifactsDir c:\agent\_work\1\a\IntegrationTests -OutDir c:\agent\_work\1\a\coverage
#>
param(
    [Parameter(Mandatory=$true)] [string]$ArtifactsDir,
    [Parameter(Mandatory=$true)] [string]$OutDir,
    [string]$Pattern = '*Tests.dll'
)

Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$toolsPath = Join-Path $repoRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolsPath | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Prepare an expanded artifacts directory to hold any nested zip extractions
$expandedDir = Join-Path $OutDir 'expanded'
New-Item -ItemType Directory -Force -Path $expandedDir | Out-Null

# Recursively extract nested zip archives found under the artifacts dir and any expanded output
Write-Host "Scanning $ArtifactsDir for nested zip archives to extract (recursive)..."

# Track processed archives to avoid re-processing
$processedZips = @{}
$iteration = 0
$maxIterations = 10
while ($true) {
    $iteration++
    if ($iteration -gt $maxIterations) {
        Write-Warning "Reached max extraction iterations ($maxIterations). Stopping further extraction."
        break
    }

    # Find zip files both in the original artifacts dir and in the expanded dir
    $found = @()
    $found += Get-ChildItem -Path $ArtifactsDir -Recurse -Filter '*.zip' -File -ErrorAction SilentlyContinue
    $found += Get-ChildItem -Path $expandedDir -Recurse -Filter '*.zip' -File -ErrorAction SilentlyContinue

    # Filter out already processed zips
    $toProcess = $found | Where-Object { -not $processedZips.ContainsKey($_.FullName) }
    if (-not $toProcess -or $toProcess.Count -eq 0) {
        Write-Host "No new zip archives found in iteration $iteration. Extraction complete."
        break
    }

    foreach ($z in $toProcess) {
        try {
            # Create a unique folder under expanded to hold this archive's contents
            $safeName = ($z.FullName -replace '[:\\/:*?"<>|]', '_')
            $dest = Join-Path $expandedDir ([System.IO.Path]::GetFileNameWithoutExtension($safeName))
            $count = 0
            $finalDest = $dest
            while (Test-Path $finalDest) { $count++; $finalDest = "${dest}_$count" }
            Write-Host "Extracting nested archive: $($z.FullName) -> $finalDest"
            New-Item -ItemType Directory -Force -Path $finalDest | Out-Null
            Expand-Archive -LiteralPath $z.FullName -DestinationPath $finalDest -Force
            $processedZips[$z.FullName] = $true
        }
        catch {
            Write-Warning "Failed to extract $($z.FullName): $_"
            # Mark as processed to avoid infinite loop
            $processedZips[$z.FullName] = $true
        }
    }

    # Loop again to pick up zips extracted in this pass
}

function Find-ToolExecutable($prefix) {
    # find files in tools path whose name starts with the prefix (case-insensitive)
    $files = Get-ChildItem -Path $toolsPath -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "(?i)^$prefix" }
    if ($files -and $files.Count -gt 0) { return $files[0].FullName }
    return $null
}

Write-Host "Using tools path: $toolsPath"

# Install coverlet.console local tool if not present
$coverletExe = Find-ToolExecutable 'coverlet'
if (-not $coverletExe) {
    Write-Host 'Installing coverlet.console local tool to .tools (this may take a moment)...'
    & dotnet tool install --tool-path $toolsPath coverlet.console --version 3.1.2
    $coverletExe = Find-ToolExecutable 'coverlet'
}

if (-not $coverletExe) { throw 'coverlet tool not found or failed to install.' }
Write-Host "coverlet at: $coverletExe"

# Install reportgenerator if not present
$reportGenExe = Find-ToolExecutable 'reportgenerator'
if (-not $reportGenExe) {
    Write-Host 'Installing reportgenerator local tool to .tools...'
    & dotnet tool install --tool-path $toolsPath dotnet-reportgenerator-globaltool --version 5.3.8
    $reportGenExe = Find-ToolExecutable 'reportgenerator'
}

if (-not $reportGenExe) { throw 'reportgenerator tool not found or failed to install.' }
Write-Host "reportgenerator at: $reportGenExe"

$dlls = @()
# Search original artifacts dir
$dlls += Get-ChildItem -Path $ArtifactsDir -Filter $Pattern -Recurse -File -ErrorAction SilentlyContinue
# Search expanded dir (from nested zip extraction)
$dlls += Get-ChildItem -Path $expandedDir -Filter $Pattern -Recurse -File -ErrorAction SilentlyContinue

# De-duplicate by fullname
$dlls = $dlls | Sort-Object -Property FullName -Unique
if (-not $dlls -or $dlls.Count -eq 0) {
    Write-Host "No test DLLs found under $ArtifactsDir with pattern $Pattern. Exiting with success (nothing to do)."
    exit 0
}

Write-Host "Found $($dlls.Count) test DLL(s). Processing..."

$coverageFiles = @()
foreach ($dll in $dlls) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
    $outPrefix = Join-Path $OutDir $base

    Write-Host "Running coverlet for $($dll.FullName) -> output prefix: $outPrefix"

    # coverlet.console accepts --output without extension; use a unique prefix per DLL
    # Build coverlet arguments
    # Use verbose output and request cobertura format
    $vstestLogName = "$($base).trx"
    $vstestLogPath = Join-Path $OutDir $vstestLogName

    $args = @(
        $dll.FullName,
        '--target', 'dotnet',
        '--targetargs', "vstest `"$($dll.FullName)`" --logger:trx;LogFileName=`"$vstestLogPath`"",
        '--format', 'cobertura',
        '--output', $outPrefix,
        '--verbosity', 'detailed'
    )

    # Invoke coverlet with call operator so PowerShell handles quoting correctly
    Write-Host "Invoking: $coverletExe $($args -join ' ')"
    & $coverletExe @args
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        Write-Warning "coverlet returned exit code $exit for $($dll.FullName)"
    }

    # coverlet will write files like <outPrefix>.coverage.cobertura.xml or <outPrefix>.cobertura.xml
    $candidates = Get-ChildItem -Path $OutDir -Filter "$base*" -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'cobertura|coverage' }
    foreach ($c in $candidates) { $coverageFiles += $c.FullName }

    # Collect any generated TRX test logs and copy to OutDir/trx for diagnostics
    $trxFiles = Get-ChildItem -Path $OutDir -Filter "$base*.trx" -File -ErrorAction SilentlyContinue
    if ($trxFiles) {
        $trxDir = Join-Path $OutDir 'trx'
        New-Item -ItemType Directory -Force -Path $trxDir | Out-Null
        foreach ($t in $trxFiles) { Copy-Item -Path $t.FullName -Destination $trxDir -Force }
    }
}

if ($coverageFiles.Count -eq 0) {
    Write-Warning 'No coverage output files were produced by coverlet. Verify the test DLLs are valid and vstest executed them.'
    # Still produce a minimal placeholder so pipeline steps expecting a report can continue
    New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'report') | Out-Null
    # create an empty Cobertura wrapper so PublishCodeCoverageResults doesn't fail with missing file
    $emptyCob = @'
<?xml version="1.0"?>
<!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">
<coverage line-rate="0" branch-rate="0" lines-covered="0" lines-valid="0" timestamp="0" complexity="0"></coverage>
'@
    $emptyPath = Join-Path $OutDir 'report\Cobertura.xml'
    $emptyCob | Out-File -FilePath $emptyPath -Encoding utf8
    Write-Host "Wrote placeholder coverage file to $emptyPath"
    exit 2
}

# Aggregate with reportgenerator
Write-Host "Aggregating coverage files with reportgenerator..."
$reportsArg = ($coverageFiles -join ';')
$reportOut = Join-Path $OutDir 'report'

$rgArgs = @(
    "-reports:$reportsArg",
    "-targetdir:$reportOut",
    '-reporttypes:Cobertura'
)

Write-Host "Invoking: $reportGenExe $($rgArgs -join ' ')"
& $reportGenExe @rgArgs
$rgExit = $LASTEXITCODE
if ($rgExit -ne 0) {
    Write-Warning "reportgenerator returned exit code $rgExit"
    exit 3
}

Write-Host "Aggregated report created at: $reportOut"
# Ensure aggregated Cobertura.xml exists
$finalCob = Join-Path $reportOut 'Cobertura.xml'
if (-not (Test-Path $finalCob)) {
    Write-Warning "Expected aggregated report at $finalCob but it was not found. Available coverage files:`n$($coverageFiles -join "`n")"
    exit 3
}

# touch a sentinel file
New-Item -ItemType File -Force -Path (Join-Path $OutDir 'coverage-aggregated.txt') | Out-Null
Write-Host "Coverage files used: `n$($coverageFiles -join "`n")"
Write-Host 'Done.'
