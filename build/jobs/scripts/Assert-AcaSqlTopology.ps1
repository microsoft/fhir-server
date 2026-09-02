function Assert-AcaSqlTopology {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [array] $EnvironmentSettings,

        [Parameter(Mandatory = $true)]
        [string] $ContainerAppName,

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ResourceGroupName = '',

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ExpectedKeyVaultName = '',

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ExpectedSqlServerName = '',

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ExpectedSqlDatabaseName = '',

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ExpectedSqlElasticPoolName = '',

        [Parameter(Mandatory = $false)]
        [scriptblock] $SecretResolver = {
            param($VaultName, $SecretName)
            Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop
        },

        [Parameter(Mandatory = $false)]
        [scriptblock] $DatabaseResolver = {
            param($ResourceGroup, $ServerName, $DatabaseName)
            Get-AzSqlDatabase -ResourceGroupName $ResourceGroup -ServerName $ServerName -DatabaseName $DatabaseName -ErrorAction Stop
        },

        [Parameter(Mandatory = $false)]
        [scriptblock] $AzureEnvironmentResolver = {
            $context = Get-AzContext -ErrorAction Stop
            if ($null -eq $context -or $null -eq $context.Environment) {
                throw 'No active Azure context is available.'
            }

            $environmentName = if ($context.Environment -is [string]) {
                $context.Environment
            }
            else {
                [string]$context.Environment.Name
            }

            if ([string]::IsNullOrWhiteSpace($environmentName)) {
                throw 'The active Azure context does not identify an Azure environment.'
            }

            $environment = Get-AzEnvironment -Name $environmentName -ErrorAction Stop
            [pscustomobject]@{
                KeyVaultDnsSuffix = $environment.AzureKeyVaultDnsSuffix
                SqlDatabaseDnsSuffix = $environment.SqlDatabaseDnsSuffix
            }
        }
    )

    $expectations = @(
        $ExpectedKeyVaultName,
        $ExpectedSqlServerName,
        $ExpectedSqlDatabaseName,
        $ExpectedSqlElasticPoolName
    )
    $suppliedExpectationCount = @($expectations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
    if ($suppliedExpectationCount -eq 0) {
        return
    }

    if ($suppliedExpectationCount -ne $expectations.Count -or [string]::IsNullOrWhiteSpace($ResourceGroupName)) {
        throw 'ACA SQL topology validation requires a resource group and all four expected topology values.'
    }

    $azureEnvironment = & $AzureEnvironmentResolver
    if ($null -eq $azureEnvironment) {
        throw 'The active Azure environment could not be resolved.'
    }

    $keyVaultDnsSuffix = ([string]$azureEnvironment.KeyVaultDnsSuffix).Trim().Trim('.')
    $sqlDatabaseDnsSuffix = ([string]$azureEnvironment.SqlDatabaseDnsSuffix).Trim().Trim('.')
    if ([string]::IsNullOrWhiteSpace($keyVaultDnsSuffix) -or [string]::IsNullOrWhiteSpace($sqlDatabaseDnsSuffix)) {
        throw 'The active Azure environment does not define Key Vault and SQL Database DNS suffixes.'
    }

    $keyVaultSettingName = 'KeyVault__Endpoint'
    $keyVaultSettings = @($EnvironmentSettings | Where-Object { $_.name -eq $keyVaultSettingName })
    if ($keyVaultSettings.Count -ne 1) {
        throw "Container App '$ContainerAppName' must define '$keyVaultSettingName' exactly once."
    }

    $keyVaultEndpoint = [string]$keyVaultSettings[0].value
    $endpointUri = $null
    if (-not [uri]::TryCreate($keyVaultEndpoint, [System.UriKind]::Absolute, [ref]$endpointUri) -or
        $endpointUri.Scheme -ne [System.Uri]::UriSchemeHttps) {
        throw "Container App '$ContainerAppName' has invalid '$keyVaultSettingName' value '$keyVaultEndpoint'."
    }

    $actualKeyVaultHost = $endpointUri.DnsSafeHost.TrimEnd('.')
    $expectedKeyVaultHost = "$ExpectedKeyVaultName.$keyVaultDnsSuffix"
    if ($actualKeyVaultHost -ine $expectedKeyVaultHost) {
        throw "Container App '$ContainerAppName' targets Key Vault host '$actualKeyVaultHost'; expected '$expectedKeyVaultHost'."
    }

    $connectionString = [string](& $SecretResolver $ExpectedKeyVaultName 'SqlServer--ConnectionString')
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "Key Vault '$ExpectedKeyVaultName' secret 'SqlServer--ConnectionString' is empty."
    }

    try {
        $connectionStringBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connectionStringBuilder.set_ConnectionString($connectionString)
    }
    catch {
        throw "Key Vault '$ExpectedKeyVaultName' secret 'SqlServer--ConnectionString' is not a valid connection string."
    }

    $serverValue = $null
    foreach ($serverKey in @('Server', 'Data Source')) {
        if ($connectionStringBuilder.ContainsKey($serverKey)) {
            $serverValue = [string]$connectionStringBuilder[$serverKey]
            break
        }
    }

    $databaseValue = $null
    foreach ($databaseKey in @('Initial Catalog', 'Database')) {
        if ($connectionStringBuilder.ContainsKey($databaseKey)) {
            $databaseValue = [string]$connectionStringBuilder[$databaseKey]
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($serverValue)) {
        throw "Key Vault '$ExpectedKeyVaultName' SQL connection string does not define a server; expected '$ExpectedSqlServerName'."
    }

    $actualServerHost = ($serverValue -replace '^(?i:tcp):', '').Split(',')[0].Trim()
    $expectedServerHost = "$ExpectedSqlServerName.$sqlDatabaseDnsSuffix"
    if ([string]::IsNullOrWhiteSpace($actualServerHost) -or $actualServerHost.TrimEnd('.') -ine $expectedServerHost) {
        throw "Key Vault '$ExpectedKeyVaultName' SQL connection string targets server '$actualServerHost'; expected '$expectedServerHost'."
    }

    if ([string]::IsNullOrWhiteSpace($databaseValue) -or $databaseValue -ine $ExpectedSqlDatabaseName) {
        throw "Key Vault '$ExpectedKeyVaultName' SQL connection string targets database '$databaseValue'; expected '$ExpectedSqlDatabaseName'."
    }

    $database = & $DatabaseResolver $ResourceGroupName $ExpectedSqlServerName $ExpectedSqlDatabaseName
    if ($null -eq $database) {
        throw "SQL database '$ExpectedSqlDatabaseName' was not found on server '$ExpectedSqlServerName'."
    }

    $actualElasticPoolName = [string]$database.ElasticPoolName
    if ($actualElasticPoolName -ine $ExpectedSqlElasticPoolName) {
        throw "SQL database '$ExpectedSqlDatabaseName' belongs to elastic pool '$actualElasticPoolName'; expected '$ExpectedSqlElasticPoolName'."
    }

    Write-Host "Verified Container App '$ContainerAppName' targets Key Vault '$ExpectedKeyVaultName', SQL database '$ExpectedSqlDatabaseName' on server '$ExpectedSqlServerName', and elastic pool '$ExpectedSqlElasticPoolName'."
}
