[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stu3', 'R4', 'R4B', 'R5')]
    [string] $Version,

    [Parameter(Mandatory = $false)]
    [string] $SqlDatabaseName = '',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Firely', 'Ignixa')]
    [string] $FhirSdkProviderDefault = 'Firely'
)

$resolvedSqlDatabaseName = if ([string]::IsNullOrWhiteSpace($SqlDatabaseName)) {
    "FHIR$Version"
} else {
    $SqlDatabaseName
}

[pscustomobject]@{
    SqlDatabaseName = $resolvedSqlDatabaseName
    FhirSdkProviderDefault = $FhirSdkProviderDefault
    EmitFhirSdkProviderEnvironmentVariable = $FhirSdkProviderDefault -ne 'Firely'
}
