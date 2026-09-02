[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stu3', 'R4', 'R4B', 'R5')]
    [string] $Version,

    [Parameter(Mandatory = $false)]
    [string] $SqlDatabaseName = '',

    [Parameter(Mandatory = $false)]
    [ValidateSet('', 'Firely', 'Ignixa')]
    [string] $FhirSdkProviderDefault = '',

    [Parameter(Mandatory = $false)]
    [string] $ConfiguredFhirSdkProviderDefault = ''
)

$resolvedSqlDatabaseName = if ([string]::IsNullOrWhiteSpace($SqlDatabaseName)) {
    "FHIR$Version"
} else {
    $SqlDatabaseName
}

$hasRequestedProvider = -not [string]::IsNullOrWhiteSpace($FhirSdkProviderDefault)
$hasConfiguredProvider = -not [string]::IsNullOrWhiteSpace($ConfiguredFhirSdkProviderDefault)

if ($hasConfiguredProvider -and $ConfiguredFhirSdkProviderDefault -notin @('Firely', 'Ignixa')) {
    throw "Configured FHIR SDK provider '$ConfiguredFhirSdkProviderDefault' is unsupported. Expected 'Firely' or 'Ignixa'."
}

if ($hasRequestedProvider -and $hasConfiguredProvider -and $FhirSdkProviderDefault -ne $ConfiguredFhirSdkProviderDefault) {
    throw "Deployment FHIR SDK provider '$FhirSdkProviderDefault' conflicts with configured provider '$ConfiguredFhirSdkProviderDefault'."
}

$effectiveProvider = if ($hasRequestedProvider) {
    $FhirSdkProviderDefault
} elseif ($hasConfiguredProvider) {
    $ConfiguredFhirSdkProviderDefault
} else {
    'Firely'
}

[pscustomobject]@{
    SqlDatabaseName = $resolvedSqlDatabaseName
    FhirSdkProviderDefault = $effectiveProvider
    EmitFhirSdkProviderEnvironmentVariable = $hasConfiguredProvider -or $effectiveProvider -ne 'Firely'
}
