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

    $actualKeyVaultName = $endpointUri.DnsSafeHost.Split('.')[0]
    if ($actualKeyVaultName -ine $ExpectedKeyVaultName) {
        throw "Container App '$ContainerAppName' targets Key Vault '$actualKeyVaultName'; expected '$ExpectedKeyVaultName'."
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
    $expectedServerHost = ($ExpectedSqlServerName -replace '^(?i:tcp):', '').Split(',')[0].Trim()
    $actualServerName = $actualServerHost.Split('.')[0]
    $expectedServerName = $expectedServerHost.Split('.')[0]
    if ([string]::IsNullOrWhiteSpace($actualServerName) -or $actualServerName -ine $expectedServerName) {
        throw "Key Vault '$ExpectedKeyVaultName' SQL connection string targets server '$actualServerHost'; expected '$ExpectedSqlServerName'."
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
