<#
.SYNOPSIS
    Runs every unit test module for one target framework, several modules at a time.

.DESCRIPTION
    Each unit test project builds its own module (<AssemblyName>.dll in src/<Project>/bin/<Configuration>/<Framework>/),
    but projects that reference another unit test project (for example Core.UnitTests) also get a copy of that
    project's module in their own bin folder. A single `dotnet test --test-modules "**/bin/.../*UnitTests.dll"`
    glob would therefore run those modules repeatedly, so this script derives the expected module of every
    src/*UnitTests/*.csproj (AssemblyName and TargetFrameworks are evaluated, not built) and runs one
    `dotnet test --test-modules` per module, MaxParallel at a time.

    A project that does not target the framework is skipped with a message. A project that targets it but has no
    built module fails the script before any test runs. The TFM directory in the module path provides isolation,
    so -f is never passed. The script exits non-zero if any module fails.

.PARAMETER TestArguments
    Arguments passed to each test module after `--`.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Framework,
    [Parameter(Mandatory)] [string] $Configuration,
    [Parameter(Mandatory)] [string] $SourceRoot,
    [int] $MaxParallel = 4,
    [string[]] $TestArguments = @()
)

$ErrorActionPreference = 'Stop'

$projectFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Directory -Filter '*UnitTests' |
    Get-ChildItem -File -Filter '*.csproj')

if (-not $projectFiles) {
    throw "No unit test projects found under '$SourceRoot'."
}

# Evaluate each project (no build) for its assembly name and target frameworks. The module is exactly
# <AssemblyName>.dll in the project's own output folder, so copies of other projects' modules that land in
# the same folder are never selected.
$evaluated = $projectFiles | ForEach-Object -ThrottleLimit 8 -Parallel {
    $json = & dotnet msbuild $_.FullName -nologo "-p:Configuration=$using:Configuration" -getProperty:AssemblyName -getProperty:TargetFramework -getProperty:TargetFrameworks
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to evaluate '$($_.FullName)': $json"
    }

    $properties = ($json | Out-String | ConvertFrom-Json).Properties
    [pscustomobject]@{
        Project = $_
        AssemblyName = $properties.AssemblyName
        TargetFrameworks = @(($properties.TargetFrameworks, $properties.TargetFramework) -join ';' -split ';' | Where-Object { $_ })
    }
}

$modules = @()
$missing = @()
foreach ($item in $evaluated | Sort-Object { $_.Project.FullName }) {
    if ($Framework -notin $item.TargetFrameworks) {
        Write-Host "Skipping $($item.AssemblyName): does not target $Framework ($($item.TargetFrameworks -join ', '))."
        continue
    }

    $projectDirectory = $item.Project.Directory
    $relativePath = "$($projectDirectory.Name)/bin/$Configuration/$Framework/$($item.AssemblyName).dll"
    if (Test-Path -LiteralPath (Join-Path $SourceRoot $relativePath) -PathType Leaf) {
        $modules += [pscustomobject]@{ Name = $item.AssemblyName; Pattern = $relativePath }
    }
    else {
        $missing += $relativePath
    }
}

if ($missing) {
    throw "Unit test projects that target $Framework have no built module (was the solution built for $Configuration/${Framework}?):`n$($missing -join "`n")"
}

if (-not $modules) {
    throw "No unit test modules found under '$SourceRoot' for $Configuration/$Framework."
}

Write-Host "Running $($modules.Count) unit test modules for $Framework, $MaxParallel at a time."

$results = $modules | ForEach-Object -ThrottleLimit $MaxParallel -Parallel {
    $module = $_
    $output = & dotnet test --test-modules $module.Pattern --root-directory $using:SourceRoot -- @using:TestArguments 2>&1 | Out-String
    [pscustomobject]@{ Name = $module.Name; ExitCode = $LASTEXITCODE; Output = $output }
} | ForEach-Object {
    # Emit each module's output as one block as soon as it completes.
    Write-Host "##[group]$($_.Name) (exit code $($_.ExitCode))"
    Write-Host $_.Output
    Write-Host '##[endgroup]'
    $_
}

$failed = @($results | Where-Object ExitCode -ne 0)
foreach ($result in $failed) {
    Write-Host "##[error]Unit test module $($result.Name) failed with exit code $($result.ExitCode)."
}

exit ($failed.Count -gt 0 ? 1 : 0)
