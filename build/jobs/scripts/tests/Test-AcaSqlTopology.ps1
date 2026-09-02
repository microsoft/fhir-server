[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$assertionPath = Join-Path $PSScriptRoot '../Assert-AcaSqlTopology.ps1'
. $assertionPath

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)] [scriptblock] $Action,
        [Parameter(Mandatory = $true)] [string] $ExpectedMessage,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Description. Expected error containing '$ExpectedMessage', actual '$($_.Exception.Message)'."
        }

        return
    }

    throw "$Description. Expected an exception."
}

$matchingEnvironment = @(
    [pscustomobject]@{
        name = 'KeyVault__Endpoint'
        value = 'https://expected-vault.vault.azure.net/'
    }
)
$matchingSecretResolver = {
    param($VaultName, $SecretName)
    if ($VaultName -ne 'expected-vault' -or $SecretName -ne 'SqlServer--ConnectionString') {
        throw "Unexpected secret request for '$VaultName/$SecretName'."
    }

    'Server=tcp:shared-sql.database.windows.net,1433;Initial Catalog=FHIRR4VNext;Encrypt=True'
}
$matchingDatabaseResolver = {
    param($ResourceGroup, $ServerName, $DatabaseName)
    if ($ResourceGroup -ne 'expected-rg' -or $ServerName -ne 'shared-sql' -or $DatabaseName -ne 'FHIRR4VNext') {
        throw "Unexpected database request for '$ResourceGroup/$ServerName/$DatabaseName'."
    }

    [pscustomobject]@{ ElasticPoolName = 'vnext-pool' }
}
$matchingArguments = @{
    EnvironmentSettings = $matchingEnvironment
    ContainerAppName = 'expected-app'
    ResourceGroupName = 'expected-rg'
    ExpectedKeyVaultName = 'expected-vault'
    ExpectedSqlServerName = 'shared-sql'
    ExpectedSqlDatabaseName = 'FHIRR4VNext'
    ExpectedSqlElasticPoolName = 'vnext-pool'
    SecretResolver = $matchingSecretResolver
    DatabaseResolver = $matchingDatabaseResolver
}

Assert-AcaSqlTopology @matchingArguments

$wrongKeyVaultEnvironment = @(
    [pscustomobject]@{
        name = 'KeyVault__Endpoint'
        value = 'https://wrong-vault.vault.azure.net/'
    }
)
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -EnvironmentSettings $wrongKeyVaultEnvironment
} -ExpectedMessage "targets Key Vault 'wrong-vault'" -Description 'Wrong Key Vault endpoint was accepted'

$wrongServerResolver = {
    'Server=tcp:wrong-sql.database.windows.net,1433;Initial Catalog=FHIRR4VNext;Encrypt=True'
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -SecretResolver $wrongServerResolver
} -ExpectedMessage "targets server 'wrong-sql.database.windows.net'" -Description 'Wrong SQL server was accepted'

$wrongDatabaseResolver = {
    'Server=tcp:shared-sql.database.windows.net,1433;Initial Catalog=WrongDatabase;Encrypt=True'
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -SecretResolver $wrongDatabaseResolver
} -ExpectedMessage "targets database 'WrongDatabase'" -Description 'Wrong SQL database was accepted'

$wrongPoolResolver = {
    [pscustomobject]@{ ElasticPoolName = 'wrong-pool' }
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -DatabaseResolver $wrongPoolResolver
} -ExpectedMessage "belongs to elastic pool 'wrong-pool'" -Description 'Wrong SQL elastic pool was accepted'

$unexpectedResolver = {
    throw 'A credential-backed resolver was called for a legacy lane.'
}
Assert-AcaSqlTopology `
    -EnvironmentSettings @() `
    -ContainerAppName 'legacy-app' `
    -SecretResolver $unexpectedResolver `
    -DatabaseResolver $unexpectedResolver

Write-Host 'ACA SQL topology behavioral tests passed.'
