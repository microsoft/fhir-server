<#
.SYNOPSIS
    Runs every unit test module for one target framework, several modules at a time.

.DESCRIPTION
    Each unit test project builds its own module (src/<Project>/bin/<Configuration>/<Framework>/*UnitTests.dll),
    but projects that reference another unit test project (for example Core.UnitTests) also get a copy of that
    project's module in their own bin folder. A single `dotnet test --test-modules "**/bin/.../*UnitTests.dll"`
    glob would therefore run those modules repeatedly, so this script selects each project's own module and runs
    one `dotnet test --test-modules` per module, MaxParallel at a time.

    The TFM directory in the module path provides isolation, so -f is never passed. The script exits non-zero if
    any module fails or if no modules are found.

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

$projects = Get-ChildItem -LiteralPath $SourceRoot -Directory -Filter '*UnitTests'
$projectNames = @($projects.Name)

# A module is a project's own when its name matches the project folder, or when no project folder has that name
# (assembly names such as R4.Api.UnitTests live in the shared Api.UnitTests folder).
$modules = foreach ($project in $projects) {
    $binDirectory = Join-Path $project.FullName "bin/$Configuration/$Framework"
    if (Test-Path -LiteralPath $binDirectory) {
        Get-ChildItem -LiteralPath $binDirectory -Filter '*UnitTests.dll' -File |
            Where-Object { $_.BaseName -eq $project.Name -or $_.BaseName -notin $projectNames } |
            ForEach-Object { [pscustomobject]@{ Name = $_.BaseName; Pattern = "$($project.Name)/bin/$Configuration/$Framework/$($_.Name)" } }
    }
}

if (-not $modules) {
    throw "No unit test modules found under '$SourceRoot' for $Configuration/$Framework."
}

Write-Host "Running $(@($modules).Count) unit test modules for $Framework, $MaxParallel at a time."

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
