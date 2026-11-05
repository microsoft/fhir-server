param(
    [Parameter(Mandatory = $true)] [ValidateSet('Stu3', 'R4', 'R5')] [string] $Version,
    [Parameter(Mandatory = $true)] [ValidateSet('PR', 'CI')] [string] $Lifecycle,
    [Parameter(Mandatory = $true)] [ValidatePattern('^[0-9]+$')] [string] $BuildId,
    [Parameter(Mandatory = $true)] [string] $ResourceGroupName,
    [Parameter(Mandatory = $true)] [string] $BaselineAppName,
    [Parameter(Mandatory = $true)] [string] $SqlServerName,
    [Parameter(Mandatory = $true)] [string] $ExpectedImage,
    [Parameter(Mandatory = $true)] [string] $ExpectedAudience,
    [Parameter(Mandatory = $true)] [string] $EvidencePath,
    [Parameter(Mandatory = $true)] [int] $MinReplicas
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ResourceIdProfile.ps1"

function Read-App([string] $Name) {
    $resource = Get-AzResource -ResourceGroupName $ResourceGroupName -ResourceType 'Microsoft.App/containerApps' -Name $Name -ApiVersion '2024-03-01' -ExpandProperties -ErrorAction Stop
    return $resource | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable
}

$baseline = Read-App $BaselineAppName
Assert-ResourceIdApp $baseline $ExpectedImage Standard64
$image = $baseline.Properties.template.containers[0].image
$database = Get-AzSqlDatabase -ResourceGroupName $ResourceGroupName -ServerName $SqlServerName -DatabaseName "FHIR$Version" -ErrorAction Stop
if ($null -eq $database -or $database.Status -ne 'Online') { throw 'The baseline must own an existing online database.' }
$audience = Get-ResourceIdEnv $baseline 'FhirServer__Security__Authentication__Audience'
if ($audience -cne $ExpectedAudience) { throw 'The baseline authentication audience differs from the pipeline audience.' }
$vaultEndpoint = [uri](Get-ResourceIdEnv $baseline 'KeyVault__Endpoint')
$vaultName = $vaultEndpoint.Host.Split('.')[0]
if ([string]::IsNullOrWhiteSpace($vaultName)) { throw 'The baseline must have a Key Vault endpoint.' }
$vault = Get-AzKeyVault -VaultName $vaultName -ResourceGroupName $ResourceGroupName -ErrorAction Stop
if (-not $vault.EnableRbacAuthorization) { throw 'The shared vault must already use RBAC.' }

$fixed = @{
    FhirServer__CoreFeatures__UseLongResourceIds = 'true'
    TaskHosting__Enabled = 'false'
    SqlServer__AllowDatabaseCreation = 'false'
    SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'false'
    SqlServer__Initialize = 'true'
    SqlServer__DeleteAllDataOnStartup = 'false'
}
$vaultSecrets = @(Get-AzKeyVaultSecret -VaultName $vaultName -ErrorAction Stop)
foreach ($key in $fixed.Keys) {
    if ($vaultSecrets.Name -contains $key.Replace('__', '--')) { throw "Shared vault configuration overrides the fixed setting '$key'." }
}
if (@($baseline.Properties.template.containers[0].env | Where-Object name -EQ 'SqlServer__ConnectionString').Count -ne 0) { throw 'SQL routing must come from the baseline shared vault, not an app override.' }
$connection = Get-AzKeyVaultSecret -VaultName $vaultName -Name 'SqlServer--ConnectionString' -AsPlainText -ErrorAction Stop
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($connection)
if ($builder['Initial Catalog'] -cne "FHIR$Version" -or $builder['Server'] -ine "tcp:$SqlServerName.database.windows.net,1433" -or $builder['Authentication'] -ine 'ActiveDirectoryMSI') {
    throw 'The existing shared SQL secret does not route to the expected version database and managed identity.'
}
$sqlIdentity = Get-AzUserAssignedIdentity -ResourceGroupName $ResourceGroupName -Name "$SqlServerName-uami" -ErrorAction Stop
if ($builder['User Id'] -ine $sqlIdentity.ClientId) { throw 'The shared SQL secret does not use the baseline SQL UAMI.' }
$connection = $null
$builder.Clear()
$identities = @{}
foreach ($id in $baseline.Identity.UserAssignedIdentities.Keys) { $identities[$id] = @{} }
if (-not $identities.ContainsKey($sqlIdentity.Id)) { throw 'The baseline is missing its SQL UAMI.' }
foreach ($uriName in @('FhirServer__Operations__Export__StorageAccountUri', 'FhirServer__Operations__IntegrationDataStore__StorageAccountUri')) {
    $uriValue = Get-ResourceIdEnv $baseline $uriName
    if ([string]::IsNullOrWhiteSpace($uriValue) -or $uriValue -eq 'null') { throw "Baseline prerequisite '$uriName' is missing." }
}
if ($baseline.Properties.template.volumes -or $baseline.Properties.template.initContainers) { throw 'This app-only profile does not support baseline volumes or init containers.' }
$ingress = $baseline.Properties.configuration.ingress
if (-not $ingress.external -or $ingress.targetPort -ne 8080 -or $ingress.customDomains) { throw 'The baseline must use its normal external FHIR ingress without custom domains.' }
$appName = Get-LongResourceIdAppName $Version $Lifecycle $BuildId
$group = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction Stop
$appId = "$($group.ResourceId)/providers/Microsoft.App/containerApps/$appName"
$roleName = Get-LongResourceIdRoleName $appId
$existing = @(Get-AzResource -ResourceGroupName $ResourceGroupName -ResourceType 'Microsoft.App/containerApps' -ErrorAction Stop | Where-Object Name -EQ $appName)
if ($existing.Count -gt 0 -and ($existing[0].Tags['resourceIdProfile'] -ne 'Long128' -or $existing[0].Tags['baselineApp'] -ne $BaselineAppName -or ($Lifecycle -eq 'PR' -and $existing[0].Tags['buildId'] -ne $BuildId))) {
    throw 'Refusing to overwrite an app without matching profile ownership.'
}

$secretResponse = Invoke-AzRestMethod -Path "$($baseline.ResourceId)/listSecrets?api-version=2024-03-01" -Method POST -ErrorAction Stop
if ($secretResponse.StatusCode -ne 200) { throw 'Could not read the baseline ACA secret references.' }
$secrets = @()
foreach ($secret in ($secretResponse.Content | ConvertFrom-Json).value) {
    $entry = @{ name = $secret.name }
    if ($secret.keyVaultUrl) {
        if (-not $identities.ContainsKey([string]$secret.identity)) { throw 'A baseline secret reference requires its system identity. It cannot be reused by this profile.' }
        $entry.keyVaultUrl = $secret.keyVaultUrl
        $entry.identity = $secret.identity
    } else {
        $entry.value = $secret.value
    }
    $secrets += $entry
}
$container = $baseline.Properties.template.containers[0]
$env = @($container.env | Where-Object { -not $fixed.ContainsKey([string]$_.name) } | ForEach-Object {
    if ($_.secretRef) { @{ name = $_.name; secretRef = $_.secretRef } }
    else { @{ name = $_.name; value = $_.value } }
})
foreach ($key in $fixed.Keys) { $env += @{ name = $key; value = $fixed[$key] } }
$registries = @($baseline.Properties.configuration.registries | ForEach-Object {
    $registry = @{ server = $_.server }
    if ($_.identity) {
        if (-not $identities.ContainsKey([string]$_.identity)) { throw 'The baseline registry identity is not a reusable UAMI.' }
        $registry.identity = $_.identity
    } else {
        $registry.username = $_.username
        $registry.passwordSecretRef = $_.passwordSecretRef
    }
    $registry
})
$parameters = @{
    containerAppName = $appName
    location = $baseline.Location
    managedEnvironmentId = $baseline.Properties.managedEnvironmentId
    userAssignedIdentities = $identities
    keyVaultName = $vaultName
    keyVaultRoleName = $roleName
    ownershipTags = @{ resourceIdProfile = 'Long128'; baselineApp = $BaselineAppName; buildId = $BuildId; fhirVersion = $Version }
    configuration = @{
        activeRevisionsMode = 'Single'
        ingress = @{ external = $true; targetPort = 8080; allowInsecure = $false; transport = $ingress.transport }
        registries = $registries
        secrets = $secrets
    }
    template = @{
        containers = @(@{
            name = $appName
            image = $image
            resources = @{ cpu = $container.resources.cpu; memory = $container.resources.memory }
            probes = $container.probes
            env = $env
        })
        scale = @{ minReplicas = $MinReplicas; maxReplicas = $baseline.Properties.template.scale.maxReplicas; rules = $baseline.Properties.template.scale.rules }
    }
}
[void](New-AzResourceGroupDeployment -Name $appName -ResourceGroupName $ResourceGroupName -TemplateFile "$PSScriptRoot/../../../samples/templates/aca/fhir-sql-long-resource-ids.bicep" -TemplateParameterObject $parameters -ErrorAction Stop)

$deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
$healthy = $false
do {
    $app = Read-App $appName
    Assert-ResourceIdApp $app $image Long128
    $url = "https://$($app.Properties.configuration.ingress.fqdn)"
    if ($app.Properties.latestReadyRevisionName -eq $app.Properties.latestRevisionName -and $app.Properties.latestReadyRevisionName) {
        try {
            $health = Invoke-RestMethod "$url/health/check" -TimeoutSec 30
            $storage = @($health.details | Where-Object { $_.name -eq 'StorageInitializedHealthCheck' -and $_.status -eq 'Healthy' })
            $healthy = $health.overallStatus -eq 'Healthy' -and $storage.Count -eq 1
        } catch {
            Write-Warning "Long128 health request not ready: $($_.Exception.Message)"
        }
    }
    if (-not $healthy) { Start-Sleep -Seconds 10 }
} while (-not $healthy -and [DateTimeOffset]::UtcNow -lt $deadline)
if (-not $healthy) { throw 'Long128 never reached a ready revision with healthy initialized storage.' }
if ((Get-ResourceIdEnv $app 'FhirServer__Security__Authentication__Audience') -cne $ExpectedAudience -or $app.Properties.managedEnvironmentId -ine $baseline.Properties.managedEnvironmentId) {
    throw 'The variant changed its shared audience or environment.'
}
$evidence = @{
    profile = 'Long128'
    version = $Version
    image = $image
    baselineApp = $BaselineAppName
    containerAppName = $appName
    containerAppId = $appId
    keyVaultRoleId = "$($vault.ResourceId)/providers/Microsoft.Authorization/roleAssignments/$roleName"
    databaseId = $database.ResourceId
    managedEnvironmentId = $app.Properties.managedEnvironmentId
    url = $url
    readyRevision = $app.Properties.latestReadyRevisionName
    storageStatus = 'Healthy'
    settings = $fixed
}
[void](New-Item (Split-Path $EvidencePath) -ItemType Directory -Force)
[IO.File]::WriteAllText($EvidencePath, ($evidence | ConvertTo-Json -Depth 10))
Write-Host "Verified Long128 $Version at $url against existing FHIR$Version."
Write-Host "##vso[task.setvariable variable=LongResourceIdAppName]$appName"
