<#
.SYNOPSIS
    Sets up Microsoft Entra ID app registrations for use with the SMART on FHIR v2 sample launcher.

.DESCRIPTION
    This script automates the Entra ID configuration needed for the SmartLauncher sample app.
    It creates/updates TWO app registrations:

      1. Resource App — represents the FHIR server. Exposes SMART v2 scopes as
         oauth2PermissionScopes (delegated permissions) so that client apps can request them.
      2. Client App — represents the SmartLauncher. Configured with redirect URI,
         credentials, and API permissions referencing the resource app's scopes.

    Supports both standard Entra ID (workforce) and Entra External ID (CIAM) tenants.

    SMART v2 scopes use slash notation (patient/Patient.rs) but Entra ID scope values
    only allow alphanumeric, dots, underscores, and hyphens. This script registers scopes
    using dot notation (patient.Patient.rs) and the FHIR server maps between the two formats.

.PARAMETER ResourceAppName
    Display name for the FHIR server resource app registration.
    Default: "FHIR Server SMART Scopes"

.PARAMETER ExistingResourceAppId
    If specified, updates an existing resource app registration instead of creating a new one.

.PARAMETER AppName
    Display name for the client app registration. Default: "SMART on FHIR Sample App"

.PARAMETER ExistingClientId
    If specified, updates an existing client app registration instead of creating a new one.

.PARAMETER RedirectUri
    The redirect URI for the sample app. Default: "https://localhost:5001/sampleapp/index.html"

.PARAMETER FhirServerUrl
    The FHIR server URL. Used for display in the output configuration.
    Default: "https://localhost:44348"

.PARAMETER CreateSecret
    If specified, creates a client secret for symmetric auth on the client app.

.PARAMETER CreateCertificate
    If specified, generates a self-signed certificate, uploads it to the client app registration,
    and exports the private key as a JWK.

.PARAMETER CertificateSubject
    Subject name for the generated self-signed certificate. Default: "CN=SmartOnFhirSampleApp"

.PARAMETER CertificateValidityDays
    Number of days the generated certificate is valid. Default: 365

.PARAMETER ExternalId
    If specified, configures the apps for a Microsoft Entra External ID (CIAM) tenant.

.PARAMETER TenantId
    If specified, targets a specific Entra ID tenant by its tenant (directory) ID.

.PARAMETER TenantName
    The tenant name (e.g. "contoso" from contoso.ciamlogin.com). Required when -ExternalId
    is specified.

.EXAMPLE
    # Create both resource and client apps with a client secret
    .\Setup-SmartOnFhirEntraClient.ps1 -CreateSecret

.EXAMPLE
    # Update existing apps (provide both app IDs)
    .\Setup-SmartOnFhirEntraClient.ps1 -ExistingResourceAppId "11111111-..." -ExistingClientId "22222222-..." -CreateSecret

.EXAMPLE
    # Create new resource app, update existing client app
    .\Setup-SmartOnFhirEntraClient.ps1 -ExistingClientId "22222222-..." -CreateCertificate

.EXAMPLE
    # Entra External ID tenant
    .\Setup-SmartOnFhirEntraClient.ps1 -ExternalId -TenantName "contoso" -CreateSecret

.NOTES
    Prerequisites:
      - PowerShell 7+ recommended
      - Microsoft Graph PowerShell SDK: Install-Module Microsoft.Graph -Scope CurrentUser
      - Sufficient Entra ID permissions (Application Developer role or Application.ReadWrite.All)
#>

[CmdletBinding()]
param(
    [string]$ResourceAppName = "FHIR Server SMART Scopes",
    [string]$ExistingResourceAppId,
    [string]$AppName = "SMART on FHIR Sample App",
    [string]$ExistingClientId,
    [string]$RedirectUri = "https://localhost:5001/sampleapp/index.html",
    [string]$FhirServerUrl = "https://localhost:44348",
    [switch]$CreateSecret,
    [switch]$CreateCertificate,
    [string]$CertificateSubject = "CN=SmartOnFhirSampleApp",
    [int]$CertificateValidityDays = 365,
    [switch]$ExternalId,
    [string]$TenantName,
    [string]$TenantId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Validate External ID parameters ──
if ($ExternalId -and -not $TenantName) {
    Write-Error "The -TenantName parameter is required when using -ExternalId. Example: -TenantName 'contoso' (from contoso.ciamlogin.com)"
    return
}

# ── Helper: Convert byte array to Base64url ──
function ConvertTo-Base64Url {
    param([byte[]]$Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# ── Helper: Export RSA private key as JWK ──
function Export-RsaPrivateKeyAsJwk {
    param(
        [System.Security.Cryptography.RSA]$Rsa,
        [string]$Kid
    )
    $params = $Rsa.ExportParameters($true)
    return [ordered]@{
        kty = "RSA"
        kid = $Kid
        alg = "RS384"
        n   = ConvertTo-Base64Url -Bytes $params.Modulus
        e   = ConvertTo-Base64Url -Bytes $params.Exponent
        d   = ConvertTo-Base64Url -Bytes $params.D
        p   = ConvertTo-Base64Url -Bytes $params.P
        q   = ConvertTo-Base64Url -Bytes $params.Q
        dp  = ConvertTo-Base64Url -Bytes $params.DP
        dq  = ConvertTo-Base64Url -Bytes $params.DQ
        qi  = ConvertTo-Base64Url -Bytes $params.InverseQ
    }
}

# ── Helper: Export RSA public key as JWK ──
function Export-RsaPublicKeyAsJwk {
    param(
        [System.Security.Cryptography.RSA]$Rsa,
        [string]$Kid
    )
    $params = $Rsa.ExportParameters($false)
    return [ordered]@{
        kty = "RSA"
        kid = $Kid
        alg = "RS384"
        n   = ConvertTo-Base64Url -Bytes $params.Modulus
        e   = ConvertTo-Base64Url -Bytes $params.Exponent
    }
}

# ── Define SMART v2 scopes ──
# Entra ID scope values support ? = | characters, but NOT / in scope values.
# Encoding scheme:
#   - Resource-level / becomes .  (patient/Patient.rs -> patient.Patient.rs)
#   - ? = | in fine-grained query params are kept as-is
#   - / within URLs in query params (e.g. http://...) becomes %2f
#
# The FHIR server decodes scope strings using standard query string decoding,
# so %2f is decoded back to / when processing claims from the access token.
$smartScopes = @(
    # ── Identity & context ──
    @{ Value = "fhirUser";                       Desc = "Access the logged-in user's FHIR identity (fhirUser claim)" }
    @{ Value = "launch.patient";                 Desc = "Standalone launch context - prompt for patient selection" }

    # ── Patient-level base scopes ──
    @{ Value = "patient.Patient.rs";             Desc = "Read and search Patient resources" }
    @{ Value = "patient.Patient.r";              Desc = "Read Patient resources" }
    @{ Value = "patient.Observation.rs";         Desc = "Read and search all Observation resources" }
    @{ Value = "patient.Observation.r";          Desc = "Read Observation resources" }
    @{ Value = "patient.Condition.rs";           Desc = "Read and search all Condition resources" }
    @{ Value = "patient.Condition.r";            Desc = "Read Condition resources" }
    @{ Value = "patient.MedicationRequest.rs";   Desc = "Read and search MedicationRequest resources" }
    @{ Value = "patient.MedicationRequest.r";    Desc = "Read MedicationRequest resources" }
    @{ Value = "patient.AllergyIntolerance.rs";  Desc = "Read and search AllergyIntolerance resources" }
    @{ Value = "patient.AllergyIntolerance.r";   Desc = "Read AllergyIntolerance resources" }
    @{ Value = "patient.Procedure.rs";           Desc = "Read and search Procedure resources" }
    @{ Value = "patient.Immunization.rs";        Desc = "Read and search Immunization resources" }
    @{ Value = "patient.DiagnosticReport.rs";    Desc = "Read and search DiagnosticReport resources" }
    @{ Value = "patient.Encounter.rs";           Desc = "Read and search Encounter resources" }
    @{ Value = "patient.CarePlan.rs";            Desc = "Read and search CarePlan resources" }
    @{ Value = "patient.CareTeam.rs";            Desc = "Read and search CareTeam resources" }
    @{ Value = "patient.DocumentReference.rs";   Desc = "Read and search DocumentReference resources" }

    # ── Patient-level fine-grained query scopes (SMART v2) ──
    # These narrow access beyond the base scope. Each must be registered individually
    # so it appears in the token's scp claim for the FHIR server to enforce.
    # The / in URLs is encoded as %2f; the FHIR server decodes via query string decoding.
    @{ Value = "patient.Observation.rs?code=loinc.org|85354-9";  Desc = "Observations: Blood pressure (LOINC 85354-9)" }
    @{ Value = "patient.Observation.rs?code=loinc.org|8867-4";   Desc = "Observations: Heart rate (LOINC 8867-4)" }
    @{ Value = "patient.Observation.rs?code=loinc.org|8310-5";   Desc = "Observations: Body temperature (LOINC 8310-5)" }
    @{ Value = "patient.Observation.rs?code=loinc.org|29463-7";  Desc = "Observations: Body weight (LOINC 29463-7)" }
    @{ Value = "patient.Observation.rs?code=loinc.org|2708-6";   Desc = "Observations: SpO2 (LOINC 2708-6)" }
    # ── User-level scopes ──
    @{ Value = "user.Patient.rs";                Desc = "User-level read and search Patient resources" }
    @{ Value = "user.Observation.rs";            Desc = "User-level read and search Observation resources" }
    @{ Value = "user.Condition.rs";              Desc = "User-level read and search Condition resources" }
)

# ── Ensure Microsoft.Graph module is available ──
$tenantType = if ($ExternalId) { "Entra External ID" } else { "Entra ID" }
Write-Host "`n=== SMART on FHIR $tenantType Setup ===" -ForegroundColor Cyan
Write-Host ""

if ($ExternalId) {
    Write-Host "Tenant type: Entra External ID (CIAM)" -ForegroundColor Magenta
    Write-Host "Tenant name: $TenantName ($TenantName.ciamlogin.com)" -ForegroundColor Magenta
    Write-Host ""
}

$graphModule = Get-Module -ListAvailable -Name Microsoft.Graph.Applications
if (-not $graphModule) {
    Write-Host "Microsoft.Graph PowerShell SDK not found." -ForegroundColor Yellow
    Write-Host "Install it with: Install-Module Microsoft.Graph -Scope CurrentUser" -ForegroundColor Yellow
    Write-Host ""
    $install = Read-Host "Install now? (y/N)"
    if ($install -eq 'y') {
        Install-Module Microsoft.Graph -Scope CurrentUser -Force
    }
    else {
        Write-Error "Microsoft.Graph module is required. Exiting."
        return
    }
}

# ── Connect to Microsoft Graph ──
Write-Host "Connecting to Microsoft Graph..." -ForegroundColor Yellow
Write-Host "(A browser window will open for authentication)" -ForegroundColor Gray
$connectParams = @{
    Scopes    = @("Application.ReadWrite.All")
    NoWelcome = $true
}
if ($TenantId) {
    $connectParams.TenantId = $TenantId
    Write-Host "Targeting tenant: $TenantId" -ForegroundColor Gray
}
Connect-MgGraph @connectParams

$context = Get-MgContext
if (-not $context) {
    Write-Error "Failed to connect to Microsoft Graph."
    return
}
Write-Host "Connected as: $($context.Account)" -ForegroundColor Green

$tenantId = $context.TenantId

# ══════════════════════════════════════════════════════════════════════
# STEP 1: Resource App Registration (FHIR Server)
# ══════════════════════════════════════════════════════════════════════
Write-Host "`n--- Step 1: Resource App (FHIR Server) ---" -ForegroundColor Cyan

$resourceApp = $null
if ($ExistingResourceAppId) {
    Write-Host "Looking up existing resource app: $ExistingResourceAppId" -ForegroundColor Yellow
    $resourceApp = Get-MgApplication -Filter "appId eq '$ExistingResourceAppId'"
    if (-not $resourceApp) {
        Write-Error "Resource app with Client ID '$ExistingResourceAppId' not found."
        return
    }
    Write-Host "Found: $($resourceApp.DisplayName) ($($resourceApp.AppId))" -ForegroundColor Green
}
else {
    Write-Host "Creating resource app registration: $ResourceAppName" -ForegroundColor Yellow
    $resourceApp = New-MgApplication -DisplayName $ResourceAppName -SignInAudience "AzureADMyOrg"
    Write-Host "Created: $($resourceApp.DisplayName)" -ForegroundColor Green
    Write-Host "  App (client) ID: $($resourceApp.AppId)" -ForegroundColor Green
    Write-Host "  Object ID:       $($resourceApp.Id)" -ForegroundColor Green
}

$resourceAppId = $resourceApp.AppId

# ── Set Application ID URI if not already set ──
$appIdUri = "api://$resourceAppId"
$existingUris = $resourceApp.IdentifierUris
if ($existingUris -notcontains $appIdUri) {
    Write-Host "Setting Application ID URI: $appIdUri" -ForegroundColor Yellow
    Update-MgApplication -ApplicationId $resourceApp.Id -IdentifierUris @($appIdUri)
    Write-Host "  Application ID URI set." -ForegroundColor Green
}

# ── Build oauth2PermissionScopes ──
# Preserve any existing scopes not in our list, and merge ours in
$existingScopes = @()
$resourceAppFull = Get-MgApplication -ApplicationId $resourceApp.Id
if ($resourceAppFull.Api.Oauth2PermissionScopes) {
    $existingScopes = @($resourceAppFull.Api.Oauth2PermissionScopes)
}

# Build a lookup of existing scopes by value
$existingScopeMap = @{}
foreach ($s in $existingScopes) {
    $existingScopeMap[$s.Value] = $s
}

$allScopes = [System.Collections.ArrayList]::new()

# Add/update our SMART scopes
foreach ($scope in $smartScopes) {
    if ($existingScopeMap.ContainsKey($scope.Value)) {
        # Keep existing scope (preserve its ID)
        $existing = $existingScopeMap[$scope.Value]
        [void]$allScopes.Add(@{
            Id                    = $existing.Id
            Value                 = $scope.Value
            Type                  = "User"
            AdminConsentDisplayName = "SMART: $($scope.Desc)"
            AdminConsentDescription = "SMART v2 scope: $($scope.Value)"
            UserConsentDisplayName  = "SMART: $($scope.Desc)"
            UserConsentDescription  = "SMART v2 scope: $($scope.Value)"
            IsEnabled             = $true
        })
        $existingScopeMap.Remove($scope.Value)
    }
    else {
        # Create new scope with a new GUID
        [void]$allScopes.Add(@{
            Id                    = [Guid]::NewGuid().ToString()
            Value                 = $scope.Value
            Type                  = "User"
            AdminConsentDisplayName = "SMART: $($scope.Desc)"
            AdminConsentDescription = "SMART v2 scope: $($scope.Value)"
            UserConsentDisplayName  = "SMART: $($scope.Desc)"
            UserConsentDescription  = "SMART v2 scope: $($scope.Value)"
            IsEnabled             = $true
        })
    }
}

# Keep any other existing scopes that aren't in our SMART list
foreach ($remaining in $existingScopeMap.Values) {
    [void]$allScopes.Add(@{
        Id                    = $remaining.Id
        Value                 = $remaining.Value
        Type                  = $remaining.Type
        AdminConsentDisplayName = $remaining.AdminConsentDisplayName
        AdminConsentDescription = $remaining.AdminConsentDescription
        UserConsentDisplayName  = $remaining.UserConsentDisplayName
        UserConsentDescription  = $remaining.UserConsentDescription
        IsEnabled             = $remaining.IsEnabled
    })
}

Write-Host "Configuring $($smartScopes.Count) SMART v2 scopes on resource app..." -ForegroundColor Yellow
Update-MgApplication -ApplicationId $resourceApp.Id -Api @{ Oauth2PermissionScopes = $allScopes }
Write-Host "  Scopes configured." -ForegroundColor Green

# ── Ensure resource app has a service principal ──
$resourceSp = Get-MgServicePrincipal -Filter "appId eq '$resourceAppId'" -ErrorAction SilentlyContinue
if (-not $resourceSp) {
    $resourceSp = New-MgServicePrincipal -AppId $resourceAppId
    Write-Host "  Service principal created for resource app." -ForegroundColor Green
}

# Use the scope data we already built (avoids Graph API propagation delays on re-read)
$finalScopes = $allScopes

Write-Host "  Registered scopes:" -ForegroundColor Gray
foreach ($s in $finalScopes | Sort-Object { $_.Value }) {
    Write-Host "    $($s.Value)" -ForegroundColor Gray
}

# ══════════════════════════════════════════════════════════════════════
# STEP 2: Client App Registration (SmartLauncher)
# ══════════════════════════════════════════════════════════════════════
Write-Host "`n--- Step 2: Client App (SmartLauncher) ---" -ForegroundColor Cyan

$app = $null
if ($ExistingClientId) {
    Write-Host "Looking up existing client app: $ExistingClientId" -ForegroundColor Yellow
    $app = Get-MgApplication -Filter "appId eq '$ExistingClientId'"
    if (-not $app) {
        Write-Error "Client app with Client ID '$ExistingClientId' not found."
        return
    }
    Write-Host "Found: $($app.DisplayName) ($($app.AppId))" -ForegroundColor Green
}
else {
    Write-Host "Creating client app registration: $AppName" -ForegroundColor Yellow
    $webApp = @{
        RedirectUris = @($RedirectUri)
    }
    # External ID tenants only support single-tenant (AzureADMyOrg)
    $app = New-MgApplication -DisplayName $AppName -Web $webApp -SignInAudience "AzureADMyOrg"
    Write-Host "Created: $($app.DisplayName)" -ForegroundColor Green
    Write-Host "  App (client) ID: $($app.AppId)" -ForegroundColor Green
    Write-Host "  Object ID:       $($app.Id)" -ForegroundColor Green
}

$clientId = $app.AppId

# ── Ensure redirect URI is configured ──
$existingUris = $app.Web.RedirectUris
if ($existingUris -notcontains $RedirectUri) {
    Write-Host "Adding redirect URI: $RedirectUri" -ForegroundColor Yellow
    $updatedUris = @($existingUris) + @($RedirectUri)
    Update-MgApplication -ApplicationId $app.Id -Web @{ RedirectUris = $updatedUris }
    Write-Host "  Redirect URI added." -ForegroundColor Green
}

# ── Add API permissions (requiredResourceAccess) for the FHIR resource app ──
Write-Host "Configuring API permissions on client app..." -ForegroundColor Yellow

# Build resource access entries for all SMART scopes
$resourceAccessList = @()
foreach ($s in $finalScopes) {
    $scopeId = if ($s -is [hashtable]) { $s.Id } else { $s.Id }
    $resourceAccessList += @{
        Id   = $scopeId
        Type = "Scope"
    }
}

if ($resourceAccessList.Count -eq 0) {
    Write-Error "No SMART scopes were registered on the resource app. Cannot configure API permissions."
    return
}

Write-Host "  Adding $($resourceAccessList.Count) FHIR scope permissions..." -ForegroundColor Gray

# Also request openid and profile from Microsoft Graph (built-in)
$msgraphAppId = "00000003-0000-0000-c000-000000000000"
$msgraphSp = Get-MgServicePrincipal -Filter "appId eq '$msgraphAppId'"
$openidScope = $msgraphSp.Oauth2PermissionScopes | Where-Object { $_.Value -eq "openid" }
$profileScope = $msgraphSp.Oauth2PermissionScopes | Where-Object { $_.Value -eq "profile" }

$requiredResourceAccess = @(
    @{
        ResourceAppId  = $resourceAppId
        ResourceAccess = $resourceAccessList
    }
    @{
        ResourceAppId  = $msgraphAppId
        ResourceAccess = @(
            @{ Id = $openidScope.Id; Type = "Scope" }
            @{ Id = $profileScope.Id; Type = "Scope" }
        )
    }
)

Update-MgApplication -ApplicationId $app.Id -RequiredResourceAccess $requiredResourceAccess
Write-Host "  API permissions configured ($($resourceAccessList.Count) FHIR scopes + openid + profile)." -ForegroundColor Green

# ── Create client secret (symmetric auth) ──
$clientSecret = ""
if ($CreateSecret) {
    Write-Host "`nCreating client secret..." -ForegroundColor Yellow
    $secretParams = @{
        DisplayName = "SmartLauncher-$(Get-Date -Format 'yyyyMMdd')"
        EndDateTime = (Get-Date).AddDays(180)
    }
    $secret = Add-MgApplicationPassword -ApplicationId $app.Id -PasswordCredential $secretParams
    $clientSecret = $secret.SecretText
    Write-Host "Client secret created (expires: $($secret.EndDateTime))" -ForegroundColor Green
    Write-Host "  Secret: $clientSecret" -ForegroundColor Yellow
    Write-Host "  ** Save this now - it cannot be retrieved later **" -ForegroundColor Red
}

# ── Create certificate and upload (asymmetric auth) ──
$privateJwkJson = ""
$publicJwksJson = ""
if ($CreateCertificate) {
    Write-Host "`nGenerating self-signed certificate..." -ForegroundColor Yellow
    Write-Host "  Subject: $CertificateSubject" -ForegroundColor Gray
    Write-Host "  Validity: $CertificateValidityDays days" -ForegroundColor Gray

    # Generate self-signed cert with RSA 4096-bit key
    $cert = New-SelfSignedCertificate `
        -Subject $CertificateSubject `
        -KeyAlgorithm RSA `
        -KeyLength 4096 `
        -HashAlgorithm SHA384 `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddDays($CertificateValidityDays) `
        -KeyExportPolicy Exportable `
        -KeySpec Signature

    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

    # Upload certificate to app registration
    Write-Host "Uploading certificate to client app registration..." -ForegroundColor Yellow
    $certBytes = $cert.GetRawCertData()

    $keyCredential = @{
        Type        = "AsymmetricX509Cert"
        Usage       = "Verify"
        Key         = $certBytes
        DisplayName = "SmartOnFhir-$($cert.Thumbprint.Substring(0,8))"
    }
    Update-MgApplication -ApplicationId $app.Id -KeyCredentials @($keyCredential)
    Write-Host "Certificate uploaded to client app registration." -ForegroundColor Green

    # Export private key as JWK
    Write-Host "Exporting private key as JWK..." -ForegroundColor Yellow
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
    $kid = "smart-entra-$($cert.Thumbprint.Substring(0,8).ToLower())"

    $privateJwk = Export-RsaPrivateKeyAsJwk -Rsa $rsa -Kid $kid
    $publicJwk = Export-RsaPublicKeyAsJwk -Rsa $rsa -Kid $kid

    $privateJwkJson = $privateJwk | ConvertTo-Json -Compress
    $publicJwksJson = @{ keys = @($publicJwk) } | ConvertTo-Json -Depth 3

    # Save JWK files
    $privateJwkPath = Join-Path $PSScriptRoot "smart-private-key.jwk.json"
    $publicJwksPath = Join-Path $PSScriptRoot "smart-public-jwks.json"
    $privateJwk | ConvertTo-Json | Set-Content -Path $privateJwkPath -Encoding UTF8
    @{ keys = @($publicJwk) } | ConvertTo-Json -Depth 3 | Set-Content -Path $publicJwksPath -Encoding UTF8

    Write-Host "  Private JWK saved to: $privateJwkPath" -ForegroundColor Green
    Write-Host "  Public JWKS saved to: $publicJwksPath" -ForegroundColor Green
    Write-Host "  ** Keep the private key file secure - do not commit it to source control **" -ForegroundColor Red

    Write-Host "  Certificate remains in Cert:\CurrentUser\My\$($cert.Thumbprint)" -ForegroundColor Gray
}

# ══════════════════════════════════════════════════════════════════════
# STEP 3: Grant Admin Consent (External ID or optionally for all)
# ══════════════════════════════════════════════════════════════════════
if ($ExternalId) {
    Write-Host "`n--- Step 3: Admin Consent (External ID) ---" -ForegroundColor Magenta
    Write-Host "External ID tenants require admin consent for all API permissions." -ForegroundColor Yellow
    Write-Host "Granting admin consent..." -ForegroundColor Yellow

    # Ensure a service principal exists for the client app
    $clientSp = Get-MgServicePrincipal -Filter "appId eq '$clientId'" -ErrorAction SilentlyContinue
    if (-not $clientSp) {
        $clientSp = New-MgServicePrincipal -AppId $clientId
        Write-Host "  Client service principal created." -ForegroundColor Green
    }

    # Grant consent for FHIR resource scopes
    $fhirScopeValues = ($finalScopes | ForEach-Object { $_.Value }) -join " "
    try {
        New-MgOauth2PermissionGrant -ClientId $clientSp.Id -ConsentType "AllPrincipals" `
            -ResourceId $resourceSp.Id -Scope $fhirScopeValues | Out-Null
        Write-Host "  Admin consent granted for FHIR scopes." -ForegroundColor Green
    }
    catch {
        Write-Host "  Note: Could not auto-grant consent for FHIR scopes: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  You may need to grant admin consent manually in the Entra admin center." -ForegroundColor Yellow
    }

    # Grant consent for Microsoft Graph (openid, profile)
    try {
        New-MgOauth2PermissionGrant -ClientId $clientSp.Id -ConsentType "AllPrincipals" `
            -ResourceId $msgraphSp.Id -Scope "openid profile" | Out-Null
        Write-Host "  Admin consent granted for Microsoft Graph (openid, profile)." -ForegroundColor Green
    }
    catch {
        Write-Host "  Note: Could not auto-grant consent for MS Graph: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ══════════════════════════════════════════════════════════════════════
# Output
# ══════════════════════════════════════════════════════════════════════

# ── Compute authority URLs ──
if ($ExternalId) {
    $authority = "https://$TenantName.ciamlogin.com/$tenantId/v2.0"
    $oidcDiscovery = "https://$TenantName.ciamlogin.com/$tenantId/v2.0/.well-known/openid-configuration"
    $authorizeEndpoint = "https://$TenantName.ciamlogin.com/$tenantId/oauth2/v2.0/authorize"
    $tokenEndpoint = "https://$TenantName.ciamlogin.com/$tenantId/oauth2/v2.0/token"
}
else {
    $authority = "https://login.microsoftonline.com/$tenantId/v2.0"
    $oidcDiscovery = "https://login.microsoftonline.com/$tenantId/v2.0/.well-known/openid-configuration"
    $authorizeEndpoint = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/authorize"
    $tokenEndpoint = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
}

# ── Output configuration ──
Write-Host "`n=== Configuration ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Tenant ID:           $tenantId" -ForegroundColor White
Write-Host "Resource App ID:     $resourceAppId" -ForegroundColor White
Write-Host "Resource App ID URI: $appIdUri" -ForegroundColor White
Write-Host "Client App ID:       $clientId" -ForegroundColor White
Write-Host ""

Write-Host "Authority URLs ($tenantType):" -ForegroundColor Yellow
Write-Host "  Authority:          $authority" -ForegroundColor White
Write-Host "  OIDC Discovery:     $oidcDiscovery" -ForegroundColor White
Write-Host "  Authorize Endpoint: $authorizeEndpoint" -ForegroundColor White
Write-Host "  Token Endpoint:     $tokenEndpoint" -ForegroundColor White
Write-Host ""

Write-Host "Scope format mapping (SMART v2 -> Entra):" -ForegroundColor Yellow
Write-Host "  Encoding: resource-level / -> .  |  ? = | kept as-is  |  full CodeSystem URL shortened to code system name" -ForegroundColor Gray
Write-Host ""
Write-Host "  openid                         -> openid (Microsoft Graph built-in)" -ForegroundColor Gray
Write-Host "  fhirUser                       -> $appIdUri/fhirUser" -ForegroundColor Gray
Write-Host "  launch/patient                 -> $appIdUri/launch.patient" -ForegroundColor Gray
Write-Host "  patient/Patient.rs             -> $appIdUri/patient.Patient.rs" -ForegroundColor Gray
Write-Host "  patient/Observation.rs         -> $appIdUri/patient.Observation.rs" -ForegroundColor Gray
Write-Host "  patient/Observation.rs?code... -> $appIdUri/patient.Observation.rs?code=loinc.org|85354-9" -ForegroundColor Gray
Write-Host ""
Write-Host "  The FHIR server expands the shortened system name back to the full URL" -ForegroundColor Gray
Write-Host "  (e.g. loinc.org -> http://loinc.org)" -ForegroundColor Gray
Write-Host ""

$certPath = ""
$certThumbprintValue = ""
$clientTypeValue = "public"
if ($CreateCertificate -and $cert) {
    $certPath = (Join-Path $PSScriptRoot "smart-private-key.pfx")
    $certThumbprintValue = $cert.Thumbprint
    $clientTypeValue = "confidential-asymmetric"
    # Export PFX for the token proxy to load
    $pfxBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, "")
    [System.IO.File]::WriteAllBytes($certPath, $pfxBytes)
    Write-Host "  PFX exported to: $certPath" -ForegroundColor Green
} elseif ($CreateSecret) {
    $clientTypeValue = "confidential-symmetric"
}

Write-Host "Update your appsettings.json with these values:" -ForegroundColor Yellow
Write-Host ""

$config = [ordered]@{
    FhirServerUrl         = $FhirServerUrl
    ClientId              = $clientId
    DefaultSmartAppUrl    = "/sampleapp/launch.html"
    ClientType            = $clientTypeValue
    Scopes                = ""
    ClientSecret          = $clientSecret
    CertificatePath       = $certPath
    CertificatePassword   = ""
    CertificateThumbprint = $certThumbprintValue
}

$configJson = $config | ConvertTo-Json
Write-Host $configJson -ForegroundColor White

# Save appsettings if desired
$appSettingsPath = Join-Path $PSScriptRoot "appsettings.json"
$saveSettings = Read-Host "`nSave to $($appSettingsPath)? (y/N)"
if ($saveSettings -eq 'y') {
    $configJson | Set-Content -Path $appSettingsPath -Encoding UTF8
    Write-Host "Saved." -ForegroundColor Green
}

if ($privateJwkJson) {
    Write-Host "`nCertificate uploaded to the Entra ID client app registration." -ForegroundColor Green
    Write-Host ""
    Write-Host "For asymmetric auth with Entra ID, use the 'Token Proxy' OAuth flow in the launcher." -ForegroundColor Yellow
    Write-Host "The token proxy signs with RS256 server-side using the certificate configured above." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The private JWK (for use with fhirclient's Standard flow against non-Entra servers):" -ForegroundColor Gray
    Write-Host $privateJwkJson -ForegroundColor White
    Write-Host ""
    Write-Host "NOTE: fhirclient uses RS384/ES384 which Entra ID does not support." -ForegroundColor Yellow
    Write-Host "For Entra ID, always use the Token Proxy flow or client secret (symmetric) mode." -ForegroundColor Yellow
}

# ── External ID post-setup guidance ──
if ($ExternalId) {
    Write-Host "`n--- Entra External ID: Additional Setup Required ---" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "The following steps must be completed manually in the Entra admin center:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Configure the fhirUser claim:" -ForegroundColor White
    Write-Host "   a. Go to External Identities > Custom user attributes" -ForegroundColor Gray
    Write-Host "   b. Create a custom attribute named 'fhirUser' (String type)" -ForegroundColor Gray
    Write-Host "   c. Create or update a User Flow that includes the fhirUser attribute" -ForegroundColor Gray
    Write-Host "   d. In the app's Enterprise Application > Single sign-on (Preview)," -ForegroundColor Gray
    Write-Host "      add a 'fhirUser' claim sourced from the directory extension" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Link users to FHIR resources via Microsoft Graph:" -ForegroundColor White
    Write-Host "   For each user, PATCH the user object with:" -ForegroundColor Gray
    Write-Host "   extension_{B2C_EXTENSION_APP_ID}_fhirUser = 'https://{fhir-server}/Patient/{id}'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Configure your FHIR server to trust this External ID tenant:" -ForegroundColor White
    Write-Host "   Set the SMART authority URL to:" -ForegroundColor Gray
    Write-Host "   $authority" -ForegroundColor White
    Write-Host ""
    Write-Host "4. The FHIR scopes are already registered on the resource app above." -ForegroundColor White
    Write-Host "   The FHIR server must map SMART slash notation to Entra dot notation." -ForegroundColor Gray
    Write-Host ""
    Write-Host "See: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/azure-entra-external-id-setup" -ForegroundColor Cyan
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "App Registrations:" -ForegroundColor White
Write-Host "  Resource (FHIR Server): $resourceAppId  ($ResourceAppName)" -ForegroundColor Gray
Write-Host "  Client (SmartLauncher): $clientId  ($AppName)" -ForegroundColor Gray
Write-Host ""
