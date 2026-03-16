<#
.SYNOPSIS
    Generates a runsettings file for running E2E tests against a remote FHIR server environment.

.DESCRIPTION
    This script discovers Key Vault, App Services, and auth settings from an Azure resource group,
    fetches secrets, and generates a Visual Studio runsettings file for E2E tests.

.PARAMETER ResourceGroupName
    The name of the Azure resource group containing the FHIR server (e.g., "msh-fhir-pr-5348-47287").

.PARAMETER OutputPath
    The path where the runsettings file will be written. Defaults to "test/remote.runsettings".

.PARAMETER FhirVersion
    The FHIR version to configure (R4, R4B, R5, Stu3). Defaults to R4.

.EXAMPLE
    .\Generate-RemoteRunSettings.ps1 -ResourceGroupName "msh-fhir-pr-5348-47287"

.EXAMPLE
    .\Generate-RemoteRunSettings.ps1 -ResourceGroupName "msh-fhir-pr-5348-47287" -FhirVersion "Stu3"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "$PSScriptRoot/remote.runsettings",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Stu3", "R4", "R4B", "R5")]
    [string]$FhirVersion = "R4"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FHIR E2E Test RunSettings Generator" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check Azure login
Write-Host "Checking Azure login status..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged into Azure. Please run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "  Account: $($account.user.name)" -ForegroundColor Green
Write-Host "  Subscription: $($account.name)" -ForegroundColor Green
Write-Host ""

# Discover resources from the resource group
Write-Host "Discovering resources in '$ResourceGroupName'..." -ForegroundColor Yellow

# Key Vault (prefer name ending in -ts)
$KeyVaultName = az keyvault list -g $ResourceGroupName --query "[?ends_with(name, '-ts')].name | [0]" -o tsv 2>$null
if (-not $KeyVaultName) {
    $KeyVaultName = az keyvault list -g $ResourceGroupName --query "[0].name" -o tsv 2>$null
}
if (-not $KeyVaultName) {
    Write-Host "  Error: No Key Vault found in resource group." -ForegroundColor Red
    exit 1
}
Write-Host "  Key Vault: $KeyVaultName" -ForegroundColor Green

# App Service prefix (find app matching *-{version}, exclude *-sql)
$versionSuffix = $FhirVersion.ToLower()
$webApps = az webapp list -g $ResourceGroupName --query "[].name" -o tsv 2>$null
if (-not $webApps) {
    Write-Host "  Error: No web apps found in resource group." -ForegroundColor Red
    exit 1
}

$matchSuffix = "-$versionSuffix"
$matchedApp = $webApps | Where-Object { $_ -like "*$matchSuffix" -and $_ -notlike "*-sql" } | Select-Object -First 1
if (-not $matchedApp) {
    Write-Host "  Error: No web app matching '*$matchSuffix' found. Available:" -ForegroundColor Red
    $webApps | ForEach-Object { Write-Host "    - $_" -ForegroundColor White }
    exit 1
}

$AppServicePrefix = $matchedApp.Substring(0, $matchedApp.Length - $matchSuffix.Length)
$cosmosAppName = $matchedApp
$sqlAppName = "$AppServicePrefix-$versionSuffix-sql"
if ($versionSuffix -eq "stu3") {
    $cosmosAppName = $AppServicePrefix
    $sqlAppName = "$AppServicePrefix-sql"
}

$cosmosUrl = "https://$cosmosAppName.azurewebsites.net/"
$sqlUrl = "https://$sqlAppName.azurewebsites.net/"

Write-Host "  Cosmos App: $cosmosAppName" -ForegroundColor Green
Write-Host "  SQL App: $sqlAppName" -ForegroundColor Green
Write-Host ""

# Fetch auth settings from app service
Write-Host "Fetching auth settings from '$cosmosAppName'..." -ForegroundColor Yellow
$appSettings = az webapp config appsettings list -g $ResourceGroupName -n $cosmosAppName 2>$null | ConvertFrom-Json
if (-not $appSettings) {
    Write-Host "  Error: Could not fetch app settings." -ForegroundColor Red
    exit 1
}

$resourceUri = ($appSettings | Where-Object { $_.name -eq "FhirServer__Security__Authentication__Audience" }).value
if (-not $resourceUri) {
    Write-Host "  Error: Audience setting not found on app service." -ForegroundColor Red
    exit 1
}
Write-Host "  Resource: $resourceUri" -ForegroundColor Green

$authority = ($appSettings | Where-Object { $_.name -eq "FhirServer__Security__Authentication__Authority" }).value
if (-not $authority) {
    Write-Host "  Error: Authority setting not found on app service." -ForegroundColor Red
    exit 1
}
$tenantId = $authority.TrimEnd('/').Split('/')[-1]
Write-Host "  Tenant ID: $tenantId" -ForegroundColor Green
Write-Host ""

# Fetch secrets from Key Vault
Write-Host "Fetching secrets from Key Vault '$KeyVaultName'..." -ForegroundColor Yellow

$secretMappings = @(
    @{ SecretName = "app--globalAdminServicePrincipal--id"; EnvVarName = "app_globalAdminServicePrincipal_id" },
    @{ SecretName = "app--globalAdminServicePrincipal--secret"; EnvVarName = "app_globalAdminServicePrincipal_secret" },
    @{ SecretName = "app--globalAdminUserApp--id"; EnvVarName = "app_globalAdminUserApp_id" },
    @{ SecretName = "app--globalAdminUserApp--secret"; EnvVarName = "app_globalAdminUserApp_secret" },
    @{ SecretName = "app--globalConverterUserApp--id"; EnvVarName = "app_globalConverterUserApp_id" },
    @{ SecretName = "app--globalConverterUserApp--secret"; EnvVarName = "app_globalConverterUserApp_secret" },
    @{ SecretName = "app--globalExporterUserApp--id"; EnvVarName = "app_globalExporterUserApp_id" },
    @{ SecretName = "app--globalExporterUserApp--secret"; EnvVarName = "app_globalExporterUserApp_secret" },
    @{ SecretName = "app--globalImporterUserApp--id"; EnvVarName = "app_globalImporterUserApp_id" },
    @{ SecretName = "app--globalImporterUserApp--secret"; EnvVarName = "app_globalImporterUserApp_secret" },
    @{ SecretName = "app--globalReaderUserApp--id"; EnvVarName = "app_globalReaderUserApp_id" },
    @{ SecretName = "app--globalReaderUserApp--secret"; EnvVarName = "app_globalReaderUserApp_secret" },
    @{ SecretName = "app--globalWriterUserApp--id"; EnvVarName = "app_globalWriterUserApp_id" },
    @{ SecretName = "app--globalWriterUserApp--secret"; EnvVarName = "app_globalWriterUserApp_secret" },
    @{ SecretName = "app--nativeClient--id"; EnvVarName = "app_nativeClient_id" },
    @{ SecretName = "app--nativeClient--secret"; EnvVarName = "app_nativeClient_secret" },
    @{ SecretName = "app--wrongAudienceClient--id"; EnvVarName = "app_wrongAudienceClient_id" },
    @{ SecretName = "app--wrongAudienceClient--secret"; EnvVarName = "app_wrongAudienceClient_secret" },
    @{ SecretName = "app--smartUserClient--id"; EnvVarName = "app_smartUserClient_id" },
    @{ SecretName = "app--smartUserClient--secret"; EnvVarName = "app_smartUserClient_secret" },
    @{ SecretName = "app--smart-patient-A--id"; EnvVarName = "app_smart-patient-A_id" },
    @{ SecretName = "app--smart-patient-A--secret"; EnvVarName = "app_smart-patient-A_secret" },
    @{ SecretName = "app--smart-patient-B--id"; EnvVarName = "app_smart-patient-B_id" },
    @{ SecretName = "app--smart-patient-B--secret"; EnvVarName = "app_smart-patient-B_secret" },
    @{ SecretName = "app--smart-patient-C--id"; EnvVarName = "app_smart-patient-C_id" },
    @{ SecretName = "app--smart-patient-C--secret"; EnvVarName = "app_smart-patient-C_secret" },
    @{ SecretName = "app--smart-practitioner-A--id"; EnvVarName = "app_smart-practitioner-A_id" },
    @{ SecretName = "app--smart-practitioner-A--secret"; EnvVarName = "app_smart-practitioner-A_secret" },
    @{ SecretName = "app--smart-practitioner-B--id"; EnvVarName = "app_smart-practitioner-B_id" },
    @{ SecretName = "app--smart-practitioner-B--secret"; EnvVarName = "app_smart-practitioner-B_secret" }
)

$secrets = @{}
$failedSecrets = @()

foreach ($mapping in $secretMappings) {
    $kvSecretName = $mapping.SecretName
    $envVarName = $mapping.EnvVarName

    Write-Host "  Fetching: $kvSecretName" -ForegroundColor Gray -NoNewline

    try {
        $secretValue = & az keyvault secret show --vault-name "$KeyVaultName" --name "$kvSecretName" --query "value" -o tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and $secretValue) {
            $secrets[$envVarName] = $secretValue
            Write-Host " OK" -ForegroundColor Green
        } else {
            $failedSecrets += $kvSecretName
            Write-Host " NOT FOUND" -ForegroundColor Yellow
        }
    }
    catch {
        $failedSecrets += $kvSecretName
        Write-Host " FAILED: $_" -ForegroundColor Red
    }
}

Write-Host ""

if ($failedSecrets.Count -gt 0) {
    Write-Host "Warning: Could not fetch $($failedSecrets.Count) secrets:" -ForegroundColor Yellow
    foreach ($secret in $failedSecrets) {
        Write-Host "  - $secret" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Generate the runsettings XML
Write-Host "Generating runsettings file..." -ForegroundColor Yellow

$versionSuffixUpper = "_$($FhirVersion.ToUpper())"

$xmlContent = @"
<?xml version="1.0" encoding="utf-8"?>
<!--
  Auto-generated runsettings for E2E tests against remote FHIR server.
  Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
  Resource Group: $ResourceGroupName
-->
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <!-- Server URLs -->
      <TestEnvironmentUrl$versionSuffixUpper>$cosmosUrl</TestEnvironmentUrl$versionSuffixUpper>
      <TestEnvironmentUrl$($versionSuffixUpper)_Sql>$sqlUrl</TestEnvironmentUrl$($versionSuffixUpper)_Sql>

      <!-- Azure AD Configuration -->
      <tenant-id>$tenantId</tenant-id>
      <Resource>$resourceUri</Resource>

      <!-- Service Principal Credentials -->

"@

foreach ($envVar in $secrets.Keys | Sort-Object) {
    $value = $secrets[$envVar]
    $escapedValue = [System.Security.SecurityElement]::Escape($value)
    $xmlContent += "      <$envVar>$escapedValue</$envVar>`n"
}

$xmlContent += @"
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
"@

$xmlContent | Out-File -FilePath $OutputPath -Encoding utf8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "RunSettings file generated: $OutputPath" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Usage:" -ForegroundColor Yellow
Write-Host "  Visual Studio: Test > Configure Run Settings > Select Solution Wide runsettings File" -ForegroundColor White
Write-Host "  CLI: dotnet test --settings `"$OutputPath`"" -ForegroundColor White
Write-Host ""
Write-Host "SECURITY: Do NOT commit this file to source control." -ForegroundColor Red
