$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ResourceIdProfile.ps1"

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected', got '$Actual'." }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $failure = $null
    try { & $Action } catch { $failure = $_ }
    if ($null -eq $failure -or $failure.Exception.Message -notlike "*$Message*") {
        throw "Expected rejection containing '$Message', got '$failure'."
    }
    Write-Host "PASS rejected $Message"
}

foreach ($version in @('Stu3', 'R4', 'R5')) {
    $name = Get-LongResourceIdAppName $version PR '1234567890123456789'
    if ($name.Length -gt 32 -or $name -notlike "*-$($version.ToLowerInvariant())-l128") { throw 'ACA app ownership name is not bounded.' }
    Assert-Equal (Get-LongResourceIdAppName $version CI '1') (Get-LongResourceIdAppName $version CI '2')
}
$groupId = '/subscriptions/test/resourceGroups/msh-fhir-pr-42-123'
$appId = "$groupId/providers/Microsoft.App/containerApps/f123-r4-long128"
Assert-Equal (Get-LongResourceIdRoleName $appId) (Get-LongResourceIdRoleName $appId.ToUpperInvariant())
if ((Get-LongResourceIdRoleName $appId) -eq (Get-LongResourceIdRoleName "$appId-other")) { throw 'Vault roles must be app-owned.' }

$baselineName = 'f123-123-r4-sql'
$vaultId = "$groupId/providers/Microsoft.KeyVault/vaults/f123-r4-sql"
$uamiId = "$groupId/providers/Microsoft.ManagedIdentity/userAssignedIdentities/f123-123-uami"
$imageTag = 'test.azurecr.io/r4_fhir-server:123'
$image = "$imageTag@sha256:$('a' * 64)"
$baseline = @{
    ResourceId = "$groupId/providers/Microsoft.App/containerApps/$baselineName"
    Location = 'westus2'
    Identity = @{ UserAssignedIdentities = @{ $uamiId = @{ clientId = 'sql-client' } } }
    Properties = @{
        managedEnvironmentId = "$groupId/providers/Microsoft.App/managedEnvironments/shared"
        template = @{
            containers = @(@{
                image = $image; name = $baselineName; resources = @{ cpu = 1; memory = '2Gi' }
                probes = @()
                env = @(
                    @{ name = 'DataStore'; value = 'SqlServer' }
                    @{ name = 'FhirServer__Security__Enabled'; value = 'true' }
                    @{ name = 'TaskHosting__Enabled'; value = 'true' }
                    @{ name = 'SqlServer__Initialize'; value = 'true' }
                    @{ name = 'SqlServer__AllowDatabaseCreation'; value = 'true' }
                    @{ name = 'SqlServer__SchemaOptions__AutomaticUpdatesEnabled'; value = 'true' }
                    @{ name = 'KeyVault__Endpoint'; value = 'https://f123-r4-sql.vault.azure.net/' }
                    @{ name = 'FhirServer__Security__Authentication__Audience'; value = 'https://test-audience' }
                    @{ name = 'FhirServer__Operations__Export__StorageAccountUri'; value = 'https://shared.blob.core.windows.net' }
                    @{ name = 'FhirServer__Operations__IntegrationDataStore__StorageAccountUri'; value = 'https://shared.blob.core.windows.net' }
                    @{ name = 'ExistingSecret'; secretRef = 'baseline-secret' }
                )
            })
            scale = @{ maxReplicas = 8; rules = @() }
        }
        configuration = @{
            ingress = @{ external = $true; targetPort = 8080; transport = 'auto'; fqdn = 'baseline.example' }
            registries = @(@{ server = 'test.azurecr.io'; identity = $uamiId })
        }
    }
}
$global:ResourceIdFixture = @{
    Deployed = $null; DbStatus = 'Online'; CurrentVariant = $null
    ConnectionString = 'Server=tcp:f123-123.database.windows.net,1433;Initial Catalog=FHIRR4;Authentication=ActiveDirectoryMSI;User Id=sql-client;'
}

function Get-AzResource {
    [CmdletBinding()] param($ResourceGroupName, $ResourceType, $Name, $ApiVersion, [switch]$ExpandProperties)
    if ($Name -eq $baselineName) { return $baseline }
    if ($Name) { return $global:ResourceIdFixture.CurrentVariant }
    if ($global:ResourceIdFixture.CurrentVariant) { return @($global:ResourceIdFixture.CurrentVariant) }
    return @()
}
function Get-AzSqlDatabase {
    [CmdletBinding()] param($ResourceGroupName, $ServerName, $DatabaseName)
    return @{ Status = $global:ResourceIdFixture.DbStatus; ResourceId = "$groupId/providers/Microsoft.Sql/servers/$ServerName/databases/$DatabaseName" }
}
function Get-AzKeyVault {
    [CmdletBinding()] param($VaultName, $ResourceGroupName)
    return @{ EnableRbacAuthorization = $true; ResourceId = $vaultId }
}
function Get-AzKeyVaultSecret {
    [CmdletBinding()] param($VaultName, $Name, [switch]$AsPlainText)
    if ($Name) { return $global:ResourceIdFixture.ConnectionString }
    return @(@{ Name = 'SqlServer--ConnectionString' })
}
function Get-AzUserAssignedIdentity {
    [CmdletBinding()] param($ResourceGroupName, $Name)
    return @{ Id = $uamiId; ClientId = 'sql-client' }
}
function Get-AzResourceGroup {
    [CmdletBinding()] param($Name)
    return @{ ResourceId = $groupId }
}
function Invoke-AzRestMethod {
    [CmdletBinding()] param($Path, $Method)
    if ($Method -ne 'POST' -or $Path -notlike '*/listSecrets?api-version=2024-03-01') { throw 'Unexpected cloud mutation.' }
    return @{ StatusCode = 200; Content = '{"value":[{"name":"baseline-secret","value":"not-a-real-secret"}]}' }
}
function New-AzResourceGroupDeployment {
    [CmdletBinding()] param($Name, $ResourceGroupName, $TemplateFile, $TemplateParameterObject)
    $global:ResourceIdFixture.Deployed = $TemplateParameterObject
    $global:ResourceIdFixture.CurrentVariant = @{
        Name = $Name
        Tags = $TemplateParameterObject.ownershipTags
        Properties = @{
            template = $TemplateParameterObject.template
            configuration = @{ ingress = @{ fqdn = 'long.example' } }
            managedEnvironmentId = $TemplateParameterObject.managedEnvironmentId
            latestRevisionName = 'revision1'
            latestReadyRevisionName = 'revision1'
        }
    }
}
function Invoke-RestMethod {
    [CmdletBinding()] param($Uri, $TimeoutSec)
    return @{ overallStatus = 'Healthy'; details = @(@{ name = 'StorageInitializedHealthCheck'; status = 'Healthy' }) }
}
$evidence = Join-Path ([IO.Path]::GetTempPath()) "resource-id-$([guid]::NewGuid().ToString('N'))/deployment.json"
$deploymentArgs = @{
    Version = 'R4'; Lifecycle = 'PR'; BuildId = '123'; ResourceGroupName = 'msh-fhir-pr-42-123'
    BaselineAppName = $baselineName; SqlServerName = 'f123-123'; ExpectedImage = $imageTag
    ExpectedAudience = 'https://test-audience'; EvidencePath = $evidence; MinReplicas = 3
}
try {
    Assert-ResourceIdApp $baseline $imageTag Standard64
    Assert-Rejected { Assert-ResourceIdApp $baseline $image Long128 } 'Long128 requires'
    Assert-Rejected { Assert-ResourceIdApp $baseline 'wrong-image' Standard64 } 'expected version/build image'
    $baseline.Properties.template.containers[0].image = $imageTag
    Assert-Rejected { Assert-ResourceIdApp $baseline $imageTag Standard64 } 'expected version/build image'
    $baseline.Properties.template.containers[0].image = $image
    $original = $baseline | ConvertTo-Json -Depth 100 -Compress
    $global:ResourceIdFixture.DbStatus = 'Missing'
    Assert-Rejected { & "$PSScriptRoot/Set-LongResourceIdApp.ps1" @deploymentArgs } 'existing online database'
    if ($global:ResourceIdFixture.Deployed) { throw 'Missing shared prerequisites must not deploy.' }
    $global:ResourceIdFixture.DbStatus = 'Online'
    $connectionString = $global:ResourceIdFixture.ConnectionString
    $global:ResourceIdFixture.ConnectionString = $connectionString.Replace('FHIRR4', 'FHIRR5')
    Assert-Rejected { & "$PSScriptRoot/Set-LongResourceIdApp.ps1" @deploymentArgs } 'does not route to the expected version database'
    if ($global:ResourceIdFixture.Deployed) { throw 'Wrong database routing must not deploy.' }
    $global:ResourceIdFixture.ConnectionString = $connectionString
    & "$PSScriptRoot/Set-LongResourceIdApp.ps1" @deploymentArgs
    Assert-Equal ($baseline | ConvertTo-Json -Depth 100 -Compress) $original
    Assert-ResourceIdApp $global:ResourceIdFixture.CurrentVariant $image Long128
    Assert-Equal $global:ResourceIdFixture.Deployed.template.containers[0].image $image
    Assert-Rejected { Assert-ResourceIdApp $global:ResourceIdFixture.CurrentVariant "$imageTag@sha256:$('b' * 64)" Long128 } 'expected version/build image'
    Assert-Equal $global:ResourceIdFixture.Deployed.containerAppName 'f123-r4-l128'
    Assert-Equal $global:ResourceIdFixture.Deployed.userAssignedIdentities[$uamiId].Count 0
    Assert-Equal $global:ResourceIdFixture.Deployed.configuration.secrets[0].value 'not-a-real-secret'
    Assert-Equal $global:ResourceIdFixture.Deployed.template.containers[0].env.Where({ $_.name -eq 'ExistingSecret' })[0].secretRef 'baseline-secret'
    $manifest = Get-Content $evidence -Raw
    if ($manifest -match 'not-a-real-secret|sql-client|ConnectionString') { throw 'Evidence leaked credentials.' }
    $first = $global:ResourceIdFixture.Deployed | ConvertTo-Json -Depth 100 -Compress
    $global:ResourceIdFixture.CurrentVariant.Tags.buildId = '999'
    Assert-Rejected { & "$PSScriptRoot/Set-LongResourceIdApp.ps1" @deploymentArgs } 'Refusing to overwrite'
    $global:ResourceIdFixture.CurrentVariant.Tags.buildId = '123'
    & "$PSScriptRoot/Set-LongResourceIdApp.ps1" @deploymentArgs
    Assert-Equal ($global:ResourceIdFixture.Deployed | ConvertTo-Json -Depth 100 -Compress) $first
    Write-Host 'PASS app-only deployment is repeatable, preserves the baseline, carries secret references, and publishes only safe evidence.'
} finally {
    Remove-Item (Split-Path $evidence) -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Variable ResourceIdFixture -Scope Global
}
