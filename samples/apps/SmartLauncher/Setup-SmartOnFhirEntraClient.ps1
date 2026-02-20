<#
.SYNOPSIS
    Sets up a Microsoft Entra ID app registration for use with the SMART on FHIR v2 sample launcher.

.DESCRIPTION
    This script automates the Entra ID configuration needed for the SmartLauncher sample app.
    It can create a new app registration or update an existing one, and supports both
    symmetric (client secret) and asymmetric (certificate) confidential client authentication.

    Supports both standard Entra ID (workforce) and Entra External ID (CIAM) tenants.

    What this script does:
      1. Creates a new Entra ID app registration (or uses an existing one)
      2. Configures a redirect URI for the sample app
      3. Optionally creates a client secret (for symmetric auth)
      4. Optionally generates a self-signed certificate, uploads it to the app registration,
         and exports the private key as a JWK for use with the fhirclient library (for asymmetric auth)
      5. For External ID tenants: grants admin consent for configured API permissions
      6. Outputs the configuration values needed by the SmartLauncher, including
         the correct authority/OIDC discovery URLs for your tenant type

.PARAMETER AppName
    Display name for the app registration. Default: "SMART on FHIR Sample App"

.PARAMETER ExistingClientId
    If specified, updates an existing app registration instead of creating a new one.

.PARAMETER RedirectUri
    The redirect URI for the sample app. Default: "https://localhost:5001/sampleapp/index.html"

.PARAMETER FhirServerUrl
    The FHIR server URL. Used only for display in the output configuration.
    Default: "https://localhost:44348"

.PARAMETER CreateSecret
    If specified, creates a client secret for symmetric auth.

.PARAMETER CreateCertificate
    If specified, generates a self-signed certificate, uploads it to the app registration,
    and exports the private key as a JWK.

.PARAMETER CertificateSubject
    Subject name for the generated self-signed certificate. Default: "CN=SmartOnFhirSampleApp"

.PARAMETER CertificateValidityDays
    Number of days the generated certificate is valid. Default: 365

.PARAMETER ExternalId
    If specified, configures the app for a Microsoft Entra External ID (CIAM) tenant.
    This changes the authority URLs to use the ciamlogin.com domain and grants admin
    consent for API permissions (required because External ID does not support user consent).

.PARAMETER TenantId
    If specified, targets a specific Entra ID tenant by its tenant (directory) ID.
    Useful when your account has access to multiple tenants. If omitted, Connect-MgGraph
    uses your home tenant by default.

.PARAMETER TenantName
    The tenant name (e.g. "contoso" from contoso.ciamlogin.com). Required when -ExternalId
    is specified. Used to construct the correct authority and OIDC discovery URLs.

.EXAMPLE
    # Create a new app with a client secret (standard Entra ID)
    .\Setup-SmartOnFhirEntraClient.ps1 -CreateSecret

.EXAMPLE
    # Create a new app with a certificate (standard Entra ID)
    .\Setup-SmartOnFhirEntraClient.ps1 -CreateCertificate

.EXAMPLE
    # Create a new app with both secret and certificate
    .\Setup-SmartOnFhirEntraClient.ps1 -CreateSecret -CreateCertificate

.EXAMPLE
    # Update an existing app registration
    .\Setup-SmartOnFhirEntraClient.ps1 -ExistingClientId "00000000-0000-0000-0000-000000000000" -CreateSecret

.EXAMPLE
    # Create a new app for an Entra External ID tenant
    .\Setup-SmartOnFhirEntraClient.ps1 -ExternalId -TenantName "contoso" -CreateSecret

.EXAMPLE
    # Target a specific tenant by ID
    .\Setup-SmartOnFhirEntraClient.ps1 -TenantId "00000000-0000-0000-0000-000000000000" -CreateSecret

.NOTES
    Prerequisites:
      - PowerShell 7+ recommended
      - Microsoft Graph PowerShell SDK: Install-Module Microsoft.Graph -Scope CurrentUser
      - Sufficient Entra ID permissions (Application Developer role or Application.ReadWrite.All)

    Entra External ID notes:
      - External ID tenants use ciamlogin.com endpoints instead of login.microsoftonline.com
      - Only admin consent is supported (no user consent) — this script grants consent automatically
      - The fhirUser claim requires additional manual setup: create a custom user attribute,
        configure a user flow, and link users via Microsoft Graph
      - See: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/azure-entra-external-id-setup
#>

[CmdletBinding()]
param(
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

# ── Create or get app registration ──
$app = $null
if ($ExistingClientId) {
    Write-Host "`nLooking up existing app registration: $ExistingClientId" -ForegroundColor Yellow
    $app = Get-MgApplication -Filter "appId eq '$ExistingClientId'"
    if (-not $app) {
        Write-Error "App registration with Client ID '$ExistingClientId' not found."
        return
    }
    Write-Host "Found: $($app.DisplayName) ($($app.AppId))" -ForegroundColor Green
}
else {
    Write-Host "`nCreating app registration: $AppName" -ForegroundColor Yellow
    $webApp = @{
        RedirectUris = @($RedirectUri)
    }
    # External ID tenants only support single-tenant (AzureADMyOrg)
    $app = New-MgApplication -DisplayName $AppName -Web $webApp -SignInAudience "AzureADMyOrg"
    Write-Host "Created: $($app.DisplayName)" -ForegroundColor Green
    Write-Host "  Client ID: $($app.AppId)" -ForegroundColor Green
    Write-Host "  Object ID: $($app.Id)" -ForegroundColor Green
}

$clientId = $app.AppId

# ── Ensure redirect URI is configured ──
$existingUris = $app.Web.RedirectUris
if ($existingUris -notcontains $RedirectUri) {
    Write-Host "`nAdding redirect URI: $RedirectUri" -ForegroundColor Yellow
    $updatedUris = @($existingUris) + @($RedirectUri)
    Update-MgApplication -ApplicationId $app.Id -Web @{ RedirectUris = $updatedUris }
    Write-Host "Redirect URI added." -ForegroundColor Green
}

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
    Write-Host "Uploading certificate to app registration..." -ForegroundColor Yellow
    $certBytes = $cert.GetRawCertData()

    $keyCredential = @{
        Type        = "AsymmetricX509Cert"
        Usage       = "Verify"
        Key         = $certBytes
        DisplayName = "SmartOnFhir-$($cert.Thumbprint.Substring(0,8))"
    }
    Update-MgApplication -ApplicationId $app.Id -KeyCredentials @($keyCredential)
    Write-Host "Certificate uploaded to app registration." -ForegroundColor Green

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

# ── External ID: Grant admin consent ──
if ($ExternalId) {
    Write-Host "`n--- Entra External ID: Admin Consent ---" -ForegroundColor Magenta
    Write-Host "External ID tenants require admin consent for all API permissions." -ForegroundColor Yellow
    Write-Host "Granting admin consent for configured permissions..." -ForegroundColor Yellow

    # Ensure a service principal exists for the app
    $sp = Get-MgServicePrincipal -Filter "appId eq '$clientId'" -ErrorAction SilentlyContinue
    if (-not $sp) {
        $sp = New-MgServicePrincipal -AppId $clientId
        Write-Host "  Service principal created." -ForegroundColor Green
    }

    # Grant admin consent for any configured delegated permissions
    # For each requiredResourceAccess entry, find the resource SP and grant consent
    $appFull = Get-MgApplication -ApplicationId $app.Id
    foreach ($resourceAccess in $appFull.RequiredResourceAccess) {
        $resourceSpId = $resourceAccess.ResourceAppId
        $resourceSp = Get-MgServicePrincipal -Filter "appId eq '$resourceSpId'" -ErrorAction SilentlyContinue
        if (-not $resourceSp) { continue }

        $delegatedScopes = $resourceAccess.ResourceAccess | Where-Object { $_.Type -eq "Scope" }
        if ($delegatedScopes) {
            $scopeIds = ($delegatedScopes | ForEach-Object { $_.Id }) -join " "
            try {
                New-MgOauth2PermissionGrant -ClientId $sp.Id -ConsentType "AllPrincipals" `
                    -ResourceId $resourceSp.Id -Scope $scopeIds | Out-Null
                Write-Host "  Admin consent granted for resource: $($resourceSp.DisplayName)" -ForegroundColor Green
            }
            catch {
                Write-Host "  Note: Could not auto-grant consent for $($resourceSp.DisplayName): $($_.Exception.Message)" -ForegroundColor Yellow
                Write-Host "  You may need to grant admin consent manually in the Entra admin center." -ForegroundColor Yellow
            }
        }
    }
}

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

Write-Host "Tenant ID:  $tenantId" -ForegroundColor White
Write-Host "Client ID:  $clientId" -ForegroundColor White
Write-Host ""

Write-Host "Authority URLs ($tenantType):" -ForegroundColor Yellow
Write-Host "  Authority:          $authority" -ForegroundColor White
Write-Host "  OIDC Discovery:     $oidcDiscovery" -ForegroundColor White
Write-Host "  Authorize Endpoint: $authorizeEndpoint" -ForegroundColor White
Write-Host "  Token Endpoint:     $tokenEndpoint" -ForegroundColor White
Write-Host ""

Write-Host "Update your appsettings.json with these values:" -ForegroundColor Yellow
Write-Host ""

$config = [ordered]@{
    FhirServerUrl      = $FhirServerUrl
    ClientId           = $clientId
    ClientSecret       = $clientSecret
    DefaultSmartAppUrl = "/sampleapp/launch.html"
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
    Write-Host "`nFor asymmetric auth, paste this private JWK into the launcher:" -ForegroundColor Yellow
    Write-Host $privateJwkJson -ForegroundColor White
    Write-Host ""
    Write-Host "The certificate has been uploaded to the Entra ID app registration." -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: The SMART on FHIR v2 fhirclient library uses RS384/ES384," -ForegroundColor Yellow
    Write-Host "while Entra ID documents RS256/PS256 for client assertions." -ForegroundColor Yellow
    Write-Host "If asymmetric auth fails, use the client secret (symmetric) mode instead." -ForegroundColor Yellow
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
    Write-Host "4. Register FHIR scopes (use dot notation instead of slash):" -ForegroundColor White
    Write-Host "   SMART scope 'patient/Patient.rs' -> register as 'patient.Patient.rs'" -ForegroundColor Gray
    Write-Host "   All scopes must have admin consent granted (no user consent in External ID)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "See: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/azure-entra-external-id-setup" -ForegroundColor Cyan
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
