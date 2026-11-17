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

    -ModuleShard runs only one share of the modules, so several jobs can split the suite. The shares are balanced by the
    minutes recorded per project in build/test-shards/unit-tests.json (see -DurationsFile): projects are taken longest
    first, each into the lighter share so far, with ties broken by project name (ordinal) and then by share number. A
    project missing from the file counts as -UnknownMinutes, so it lands in the lighter share without being treated as free.

    -ListProjects prints the csproj paths of the selected projects and -SolutionFilterPath writes a solution filter
    (.slnf) of them; neither runs tests. A job that runs one share builds only that filter, not the whole solution.

    A project that does not target the framework is skipped with a message. A project that targets it but has no
    built module fails the script before any test runs. The TFM directory in the module path provides isolation,
    so -f is never passed. The script exits non-zero if any module fails.

.PARAMETER ModuleShard
    '<index>/<count>' (1-based, for example '2/3'): run only that share of the modules.

.PARAMETER DurationsFile
    Minutes per project for -ModuleShard: { "minutes": { "<csproj file name without extension>": <minutes> } }. The
    project file name is the key because it is the same on every OS and framework.

.PARAMETER UnknownMinutes
    Weight of a project that DurationsFile does not list.

.PARAMETER ListProjects
    Print the selected csproj paths and exit.

.PARAMETER SolutionFilterPath
    Write a solution filter of the selected projects here and exit. The solution is -Solution, which defaults to
    Microsoft.Health.Fhir.sln beside SourceRoot.

.PARAMETER TestArguments
    Arguments passed to each test module after `--`.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Framework,
    [Parameter(Mandatory)] [string] $Configuration,
    [Parameter(Mandatory)] [string] $SourceRoot,
    [int] $MaxParallel = 4,
    [ValidatePattern('^[1-9][0-9]*/[1-9][0-9]*$')] [string] $ModuleShard,
    [string] $DurationsFile = (Join-Path $PSScriptRoot '..' 'test-shards' 'unit-tests.json'),
    [double] $UnknownMinutes = 1,
    [switch] $ListProjects,
    [string] $SolutionFilterPath,
    [string] $Solution = (Join-Path $SourceRoot '..' 'Microsoft.Health.Fhir.sln'),
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

$targeted = @()
foreach ($item in $evaluated | Sort-Object { $_.Project.FullName }) {
    if ($Framework -in $item.TargetFrameworks) {
        $targeted += $item
    }
    else {
        Write-Host "Skipping $($item.AssemblyName): does not target $Framework ($($item.TargetFrameworks -join ', '))."
    }
}

# Greedy longest-first packing: each project goes to the share with the least minutes so far. Ordinal name order (not
# the culture-sensitive Sort-Object) and the lowest share number on a tie keep the split identical on every OS.
function Select-ModuleShard {
    param([object[]] $Items, [string] $Shard)

    $index, $count = $Shard -split '/' | ForEach-Object { [int] $_ }
    if ($index -gt $count) {
        throw "Module shard '$Shard' is out of range: the index must be between 1 and $count."
    }

    if (-not (Test-Path -LiteralPath $DurationsFile -PathType Leaf)) {
        throw "Durations file '$DurationsFile' not found."
    }

    $minutes = (Get-Content -Raw -LiteralPath $DurationsFile | ConvertFrom-Json -AsHashtable).minutes
    $ordered = [System.Collections.Generic.List[object]]::new()
    foreach ($item in $Items) {
        $name = $item.Project.BaseName
        $ordered.Add([pscustomobject]@{ Item = $item; Name = $name; Minutes = $minutes.ContainsKey($name) ? [double] $minutes[$name] : $UnknownMinutes })
    }

    $ordered.Sort([Comparison[object]] {
        param($a, $b)
        $byMinutes = $b.Minutes.CompareTo($a.Minutes)
        $byMinutes -ne 0 ? $byMinutes : [string]::CompareOrdinal($a.Name, $b.Name)
    })

    $load = [double[]]::new($count)
    $selected = @()
    foreach ($entry in $ordered) {
        $lightest = 0
        for ($i = 1; $i -lt $count; $i++) {
            if ($load[$i] -lt $load[$lightest]) { $lightest = $i }
        }

        $load[$lightest] += $entry.Minutes
        if ($lightest -eq $index - 1) { $selected += $entry.Item }
    }

    Write-Host "Module shard ${Shard}: $($selected.Count) of $($Items.Count) projects, $($load[$index - 1]) minutes (shares: $($load -join ', '))."
    return $selected
}

if ($ModuleShard) {
    $targeted = @(Select-ModuleShard $targeted $ModuleShard | Sort-Object { $_.Project.FullName })
}

if ($ListProjects) {
    $targeted | ForEach-Object { $_.Project.FullName }
}

if ($SolutionFilterPath) {
    if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) {
        throw "Solution '$Solution' not found."
    }

    $solutionFull = (Resolve-Path -LiteralPath $Solution).Path
    $filterDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($SolutionFilterPath))
    $solutionDirectory = Split-Path -Parent $solutionFull
    # Solution filters use backslash separators on every OS.
    $filter = @{
        solution = @{
            path = [IO.Path]::GetRelativePath($filterDirectory, $solutionFull).Replace('/', '\')
            projects = @($targeted | ForEach-Object { [IO.Path]::GetRelativePath($solutionDirectory, $_.Project.FullName).Replace('/', '\') })
        }
    }
    ConvertTo-Json -InputObject $filter -Depth 4 | Set-Content -LiteralPath $SolutionFilterPath
    Write-Host "Wrote solution filter $SolutionFilterPath with $($targeted.Count) projects."
}

if ($ListProjects -or $SolutionFilterPath) {
    return
}

$modules = @()
$missing = @()
foreach ($item in $targeted) {
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
