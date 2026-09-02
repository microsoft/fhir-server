[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$assertionPath = Join-Path $PSScriptRoot '../Assert-AcaSqlTopology.ps1'
. $assertionPath

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)] [scriptblock] $Action,
        [Parameter(Mandatory = $true)] [string] $ExpectedMessage,
        [Parameter(Mandatory = $true)] [string] $Description,
        [Parameter(Mandatory = $false)] [string] $ExpectedInnerMessage
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Description. Expected error containing '$ExpectedMessage', actual '$($_.Exception.Message)'."
        }

        if (-not [string]::IsNullOrEmpty($ExpectedInnerMessage) -and
            ($null -eq $_.Exception.InnerException -or $_.Exception.InnerException.Message -notlike "*$ExpectedInnerMessage*")) {
            throw "$Description. Expected inner error containing '$ExpectedInnerMessage'."
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
$matchingAzureEnvironmentResolver = {
    [pscustomobject]@{
        KeyVaultDnsSuffix = 'vault.azure.net'
        SqlDatabaseDnsSuffix = '.database.windows.net'
    }
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
    AzureEnvironmentResolver = $matchingAzureEnvironmentResolver
}

Assert-AcaSqlTopology @matchingArguments

$nullAzureEnvironmentResolver = {
    $null
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -AzureEnvironmentResolver $nullAzureEnvironmentResolver
} -ExpectedMessage 'SQL vNext topology validation could not resolve the active Azure environment' -Description 'Null Azure environment metadata was accepted'

foreach ($azureEnvironmentMetadata in @(
    [pscustomobject]@{ SqlDatabaseDnsSuffix = '.database.windows.net' },
    [pscustomobject]@{ KeyVaultDnsSuffix = '   '; SqlDatabaseDnsSuffix = '.database.windows.net' }
)) {
    $missingKeyVaultDnsSuffixResolver = {
        $azureEnvironmentMetadata
    }.GetNewClosure()
    Assert-Throws -Action {
        Assert-AcaSqlTopology @matchingArguments -AzureEnvironmentResolver $missingKeyVaultDnsSuffixResolver
    } -ExpectedMessage 'define a Key Vault DNS suffix' -Description 'Missing or blank Key Vault DNS suffix was accepted'
}

foreach ($azureEnvironmentMetadata in @(
    [pscustomobject]@{ KeyVaultDnsSuffix = 'vault.azure.net' },
    [pscustomobject]@{ KeyVaultDnsSuffix = 'vault.azure.net'; SqlDatabaseDnsSuffix = '   ' }
)) {
    $missingSqlDnsSuffixResolver = {
        $azureEnvironmentMetadata
    }.GetNewClosure()
    Assert-Throws -Action {
        Assert-AcaSqlTopology @matchingArguments -AzureEnvironmentResolver $missingSqlDnsSuffixResolver
    } -ExpectedMessage 'define a SQL Database DNS suffix' -Description 'Missing or blank SQL DNS suffix was accepted'
}

$throwingAzureEnvironmentResolver = {
    throw 'Simulated Azure context failure.'
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -AzureEnvironmentResolver $throwingAzureEnvironmentResolver
} `
    -ExpectedMessage 'SQL vNext topology validation could not resolve the active Azure environment. Verify that the Azure service connection is authenticated and configured for the target cloud.' `
    -ExpectedInnerMessage 'Simulated Azure context failure.' `
    -Description 'Azure environment resolver failure was not wrapped'

$wrongKeyVaultEnvironment = @(
    [pscustomobject]@{
        name = 'KeyVault__Endpoint'
        value = 'https://wrong-vault.vault.azure.net/'
    }
)
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -EnvironmentSettings $wrongKeyVaultEnvironment
} -ExpectedMessage "targets Key Vault host 'wrong-vault.vault.azure.net'" -Description 'Wrong Key Vault endpoint was accepted'

$lookalikeKeyVaultEnvironment = @(
    [pscustomobject]@{
        name = 'KeyVault__Endpoint'
        value = 'https://expected-vault.example.invalid/'
    }
)
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -EnvironmentSettings $lookalikeKeyVaultEnvironment
} -ExpectedMessage "expected-vault.vault.azure.net" -Description 'Lookalike Key Vault DNS suffix was accepted'

$wrongServerResolver = {
    'Server=tcp:wrong-sql.database.windows.net,1433;Initial Catalog=FHIRR4VNext;Encrypt=True'
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -SecretResolver $wrongServerResolver
} -ExpectedMessage "targets server 'wrong-sql.database.windows.net'" -Description 'Wrong SQL server was accepted'

$lookalikeServerResolver = {
    'Server=tcp:shared-sql.example.invalid,1433;Initial Catalog=FHIRR4VNext;Encrypt=True'
}
Assert-Throws -Action {
    Assert-AcaSqlTopology @matchingArguments -SecretResolver $lookalikeServerResolver
} -ExpectedMessage "expected 'shared-sql.database.windows.net'" -Description 'Lookalike SQL DNS suffix was accepted'

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
    -DatabaseResolver $unexpectedResolver `
    -AzureEnvironmentResolver $unexpectedResolver

$sovereignEnvironment = @(
    [pscustomobject]@{
        name = 'KeyVault__Endpoint'
        value = 'https://expected-vault.vault.azure.cn/'
    }
)
$sovereignSecretResolver = {
    'Server=tcp:shared-sql.database.chinacloudapi.cn,1433;Initial Catalog=FHIRR4VNext;Encrypt=True'
}
$sovereignAzureEnvironmentResolver = {
    [pscustomobject]@{
        KeyVaultDnsSuffix = '.vault.azure.cn.'
        SqlDatabaseDnsSuffix = 'database.chinacloudapi.cn'
    }
}
Assert-AcaSqlTopology `
    @matchingArguments `
    -EnvironmentSettings $sovereignEnvironment `
    -SecretResolver $sovereignSecretResolver `
    -AzureEnvironmentResolver $sovereignAzureEnvironmentResolver

Write-Host 'ACA SQL topology behavioral tests passed.'
