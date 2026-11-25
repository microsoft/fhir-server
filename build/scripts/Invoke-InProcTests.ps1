<#
.SYNOPSIS
    Runs an E2E test application against an in-process FHIR server and returns its exit code.

.DESCRIPTION
    The E2E tests target a remote server when TestEnvironmentUrl* is set (pipeline variables in build-variables.yml
    reach every job's environment) and an in-process InProcTestFhirServer otherwise. This script removes those
    variables, and the TestTokenEndpoint and Resource settings that go with them, from its own process, so the test
    host it starts inherits an environment without them. It then checks that the files InProcTestFhirServer reads
    resolve, and runs the test application with a 14-minute Microsoft.Testing.Platform timeout, which fires before the
    16-minute step timeout of the Tier 1 E2E job (capped at 30 minutes, with up to 9 minutes for the SQL Server and
    Azurite containers and about 4 minutes for checkout and artifacts).

    Throws, without starting the test application, when a variable cannot be removed or a file is missing.

.PARAMETER TestDll
    Path of Microsoft.Health.Fhir.<version>.Tests.E2E.dll, extracted somewhere below the source checkout:
    InProcTestFhirServer walks up from the dll's directory to find src/Microsoft.Health.Fhir.<version>.Web.

.PARAMETER Arguments
    Test application arguments, such as filters and reporting options.

.PARAMETER Dotnet
    dotnet host used to run the test application.

.OUTPUTS
    Exits with the test application's exit code.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TestDll,

    [string[]] $Arguments = @(),

    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'

# Names are matched case-insensitively: Linux agents can carry several casings of one name.
$remoteServerVariable = '^(TESTENVIRONMENTURL.*|TESTTOKENENDPOINT|RESOURCE)$'

function Get-RemoteServerVariableNames {
    @([Environment]::GetEnvironmentVariables().Keys | Where-Object { $_ -match $remoteServerVariable } | Sort-Object)
}

$removed = Get-RemoteServerVariableNames
foreach ($name in $removed) {
    # Not [Environment]::SetEnvironmentVariable($name, $null): PowerShell passes $null as '', which keeps the variable.
    Remove-Item -LiteralPath "env:$name"
}

Write-Host "Removed environment variables: $(if ($removed.Count -gt 0) { $removed -join ', ' } else { '(none)' })"
$remaining = Get-RemoteServerVariableNames
if ($remaining.Count -gt 0) {
    throw "Environment variables that select a remote test server are still set: $($remaining -join ', ')"
}

$dll = Get-Item -LiteralPath $TestDll
if (-not ($dll.Name -match '^Microsoft\.Health\.Fhir\.(?<version>[^.]+)\.Tests\.E2E\.dll$')) {
    throw "Expected Microsoft.Health.Fhir.<version>.Tests.E2E.dll, got $($dll.Name)"
}

# Mirrors InProcTestFhirServer.GetProjectPath: the nearest ancestor of the dll's directory with src/<web>/<web>.csproj.
$webProject = "Microsoft.Health.Fhir.$($Matches.version).Web"
$webProjectDirectory = $null
for ($directory = $dll.Directory.Parent; $null -ne $directory; $directory = $directory.Parent) {
    $candidate = Join-Path $directory.FullName 'src' $webProject
    if (Test-Path -LiteralPath (Join-Path $candidate "$webProject.csproj") -PathType Leaf) {
        $webProjectDirectory = $candidate
        break
    }
}

if (-not $webProjectDirectory) {
    throw "launchSettings.json cannot be resolved: no ancestor of $($dll.DirectoryName) contains src/$webProject/$webProject.csproj. The in-process server reads src/$webProject/Properties/launchSettings.json from the source checkout."
}

$requiredFiles = @(
    (Join-Path $webProjectDirectory 'Properties' 'launchSettings.json'),
    (Join-Path $dll.DirectoryName 'testauthenvironment.json'),
    (Join-Path $dll.DirectoryName 'testconfiguration.json'))
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required file not found: $file"
    }
}

$allArguments = @($dll.FullName) + $Arguments + @('--timeout', '14m')
Write-Host "Running $Dotnet $($allArguments -join ' ')"
& $Dotnet @allArguments
exit $LASTEXITCODE
