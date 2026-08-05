<#
.SYNOPSIS
    Starts a local R4 FHIR server (LocalDB backed) configured for SMART include benchmarking.

.DESCRIPTION
    Enables security plus the in-process development identity provider, and registers one client
    application per benchmark patient. The development identity provider derives the fhirUser claim from
    the client id, so a client application whose id equals a Patient resource id yields a SMART token
    bound to that patient's compartment - which is what makes SMART _include/_revinclude measurable
    without a real identity provider.

.EXAMPLE
    ./Start-LocalFhirServer.ps1 -PatientIds perf-patient-000000,perf-patient-012500
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string[]] $PatientIds = @(),

    [Parameter(Mandatory = $false)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $false)]
    [string] $DatabaseName = 'FhirIncludePerf',

    [Parameter(Mandatory = $false)]
    [string] $SqlConnectionString,

    [Parameter(Mandatory = $false)]
    [int] $Port = 5555,

    [Parameter(Mandatory = $false)]
    [ValidateSet('Stu3', 'R4', 'R4B', 'R5')]
    [string] $FhirVersion = 'R4'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

if ($ManifestPath) {
    $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $PatientIds = @($manifest.heavyPatientIds) + @($manifest.typicalPatientIds)
}

if (-not $PatientIds -or $PatientIds.Count -eq 0) {
    throw "Provide -PatientIds or -ManifestPath so SMART client applications can be registered."
}

if (-not $SqlConnectionString) {
    $SqlConnectionString = "Server=(localdb)\MSSQLLocalDB;Initial Catalog=$DatabaseName;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=60;"
}

$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DataStore = 'SqlServer'
$env:SqlServer__ConnectionString = $SqlConnectionString
$env:SqlServer__AllowDatabaseCreation = 'true'
$env:SqlServer__Initialize = 'true'
$env:SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'true'

$env:FhirServer__Security__Enabled = 'true'
$env:FhirServer__Security__EnableAadSmartOnFhirProxy = 'false'
$env:FhirServer__Security__Authorization__Enabled = 'true'
$env:FhirServer__Security__Authorization__ScopesClaim__0 = 'scope'
$env:FhirServer__Operations__Includes__Enabled = 'true'

$env:DevelopmentIdentityProvider__Enabled = 'true'

# Client application 0 loads data and runs the non-SMART (admin) benchmark cases.
$env:DevelopmentIdentityProvider__ClientApplications__0__Id = 'globalAdminServicePrincipal'
$env:DevelopmentIdentityProvider__ClientApplications__0__Roles__0 = 'globalAdmin'

$index = 1
foreach ($patientId in ($PatientIds | Select-Object -Unique)) {
    Set-Item -Path "env:DevelopmentIdentityProvider__ClientApplications__${index}__Id" -Value $patientId
    Set-Item -Path "env:DevelopmentIdentityProvider__ClientApplications__${index}__Roles__0" -Value 'smartUser'
    Write-Host "  registered SMART client: $patientId"
    $index++
}

$project = Join-Path $repoRoot "src/Microsoft.Health.Fhir.$FhirVersion.Web/Microsoft.Health.Fhir.$FhirVersion.Web.csproj"

Write-Host ""
Write-Host "Starting FHIR server on http://localhost:$Port  (database: $DatabaseName)" -ForegroundColor Cyan
Write-Host ""

dotnet run --project $project --no-build --no-launch-profile -c Debug
