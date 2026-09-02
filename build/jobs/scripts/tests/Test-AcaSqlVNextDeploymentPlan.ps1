[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path "$PSScriptRoot/../../../..").Path
$resolver = Join-Path $repositoryRoot 'build/jobs/scripts/Resolve-AcaSqlDeploymentPlan.ps1'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if ($Expected -ne $Actual) {
        throw "$Description. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)] [string] $Text,
        [Parameter(Mandatory = $true)] [string] $Expected,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if (-not $Text.Contains($Expected, [System.StringComparison]::Ordinal)) {
        throw "$Description. Missing '$Expected'."
    }
}

$legacyPlan = & $resolver -Version Stu3
Assert-Equal -Expected 'FHIRStu3' -Actual $legacyPlan.SqlDatabaseName -Description 'Legacy database default changed'
Assert-Equal -Expected 'Firely' -Actual $legacyPlan.FhirSdkProviderDefault -Description 'Legacy SDK provider default changed'
Assert-Equal -Expected $false -Actual $legacyPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'Legacy deployment would emit a new provider setting'

$stu3VNextPlan = & $resolver -Version Stu3 -SqlDatabaseName FHIRStu3VNext -FhirSdkProviderDefault Ignixa
$r4VNextPlan = & $resolver -Version R4 -SqlDatabaseName FHIRR4VNext -FhirSdkProviderDefault Ignixa
Assert-Equal -Expected 'FHIRStu3VNext' -Actual $stu3VNextPlan.SqlDatabaseName -Description 'STU3 vNext database is not isolated'
Assert-Equal -Expected 'FHIRR4VNext' -Actual $r4VNextPlan.SqlDatabaseName -Description 'R4 vNext database is not isolated'
Assert-Equal -Expected $true -Actual $stu3VNextPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'STU3 vNext provider setting would not be emitted'
Assert-Equal -Expected $true -Actual $r4VNextPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'R4 vNext provider setting would not be emitted'

$variables = Get-Content -Raw (Join-Path $repositoryRoot 'build/build-variables.yml')
Assert-Contains -Text $variables -Expected 'DeploymentEnvironmentNameSqlVNext: ''$(DeploymentEnvironmentName)-svn''' -Description 'STU3 vNext app name is not distinct'
Assert-Contains -Text $variables -Expected 'DeploymentEnvironmentNameR4SqlVNext: ''$(DeploymentEnvironmentName)-r4vn''' -Description 'R4 vNext app name is not distinct'
Assert-Contains -Text $variables -Expected 'KeyVaultNameSqlVNext: ''$(KeyVaultBaseName)-sql-vn''' -Description 'STU3 vNext Key Vault is not distinct'
Assert-Contains -Text $variables -Expected 'KeyVaultNameR4SqlVNext: ''$(KeyVaultBaseName)-r4-vn''' -Description 'R4 vNext Key Vault is not distinct'

$bicep = Get-Content -Raw (Join-Path $repositoryRoot 'samples/templates/aca/fhir-sql.bicep')
Assert-Contains -Text $bicep -Expected "fhirSdkProviderDefault == 'Firely' ? []" -Description 'Legacy Bicep deployment does not preserve its environment'
Assert-Contains -Text $bicep -Expected 'FhirServer__CoreFeatures__FhirSdkProvider__Default' -Description 'vNext provider environment variable is not emitted'

$e2eVariables = Get-Content -Raw (Join-Path $repositoryRoot 'build/tasks/e2e-set-variables.yml')
Assert-Contains -Text $e2eVariables -Expected "actualFhirSdkProviderDefault -ne `$expectedFhirSdkProviderDefault" -Description 'Expected-provider assertion is missing'

Write-Host 'SQL vNext deployment plan validation passed.'
