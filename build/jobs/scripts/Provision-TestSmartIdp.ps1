<#
.SYNOPSIS
    Provisions the static OIDC discovery and JWKS endpoints used by remote SMART E2E tests.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ResourceGroup,
    [Parameter(Mandatory = $true)] [string] $KeyVaultName,
    [Parameter(Mandatory = $true)] [string] $Location
)

$ErrorActionPreference = 'Stop'

function Get-StorageAccountName {
    param([string] $ResourceGroupName)

    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($ResourceGroupName.ToLowerInvariant()))
    $suffix = ([System.BitConverter]::ToString($hash) -replace '-', '').Substring(0, 15).ToLowerInvariant()
    return "fhirsmart$suffix"
}

function ConvertTo-Base64Url {
    param([byte[]] $Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-Jwks {
    param([System.Security.Cryptography.RSA] $Rsa)

    $parameters = $Rsa.ExportParameters($false)
    $modulus = ConvertTo-Base64Url $parameters.Modulus
    $exponent = ConvertTo-Base64Url $parameters.Exponent
    $thumbprintJson = '{"e":"' + $exponent + '","kty":"RSA","n":"' + $modulus + '"}'
    $kid = ConvertTo-Base64Url ([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($thumbprintJson)))

    return @{
        keys = @(
            @{
                alg = 'RS256'
                e = $exponent
                kid = $kid
                kty = 'RSA'
                n = $modulus
                use = 'sig'
            }
        )
    }
}

$storageAccountName = Get-StorageAccountName -ResourceGroupName $ResourceGroup
$storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroup -Name $storageAccountName -ErrorAction SilentlyContinue
if ($null -eq $storageAccount) {
    Write-Host "Creating SMART test OIDC storage account '$storageAccountName'."
    try {
        $storageAccount = New-AzStorageAccount `
            -ResourceGroupName $ResourceGroup `
            -Name $storageAccountName `
            -Location $Location `
            -SkuName Standard_LRS `
            -Kind StorageV2 `
            -AllowBlobPublicAccess $true `
            -ErrorAction Stop
    }
    catch {
        $storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroup -Name $storageAccountName -ErrorAction SilentlyContinue
        if ($null -eq $storageAccount) {
            throw
        }
    }
}

$issuer = ([string]$storageAccount.PrimaryEndpoints.Web).TrimEnd('/')
$privateKeySecret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name 'TestSmartTokenPrivateKey' -ErrorAction SilentlyContinue

if ($null -eq $privateKeySecret) {
    Write-Host 'Creating the SMART test token signing key.'
    $newRsa = [System.Security.Cryptography.RSA]::Create(2048)
    $privateKey = $newRsa.ExportRSAPrivateKeyPem()
    $newRsa.Dispose()
    $securePrivateKey = ConvertTo-SecureString -String $privateKey -AsPlainText -Force
    Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name 'TestSmartTokenPrivateKey' -SecretValue $securePrivateKey | Out-Null
}
else {
    $privateKey = [System.Net.NetworkCredential]::new('', $privateKeySecret.SecretValue).Password
}

if ([string]::IsNullOrWhiteSpace($privateKey)) {
    $privateKeySecret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name 'TestSmartTokenPrivateKey' -ErrorAction Stop
    $privateKey = [System.Net.NetworkCredential]::new('', $privateKeySecret.SecretValue).Password
}

$secureIssuer = ConvertTo-SecureString -String $issuer -AsPlainText -Force
Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name 'TestSmartTokenIssuer' -SecretValue $secureIssuer | Out-Null

$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($privateKey)
$jwks = Get-Jwks -Rsa $rsa | ConvertTo-Json -Depth 4 -Compress
$rsa.Dispose()
$oidcConfiguration = @{
    authorization_endpoint = "$issuer/authorize"
    issuer = $issuer
    response_types_supported = @('token', 'id_token')
    subject_types_supported = @('public')
    token_endpoint = "$issuer/token"
    jwks_uri = "$issuer/jwks.json"
    id_token_signing_alg_values_supported = @('RS256')
} | ConvertTo-Json -Depth 4 -Compress

$storageKey = (Get-AzStorageAccountKey -ResourceGroupName $ResourceGroup -Name $storageAccountName -ErrorAction Stop | Select-Object -First 1).Value
$storageContext = New-AzStorageContext -StorageAccountName $storageAccountName -StorageAccountKey $storageKey
Enable-AzStorageStaticWebsite -Context $storageContext -IndexDocument 'index.html' | Out-Null

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "fhir-smart-idp-$storageAccountName"
New-Item -ItemType Directory -Path (Join-Path $temporaryDirectory '.well-known') -Force | Out-Null
Set-Content -Path (Join-Path $temporaryDirectory 'jwks.json') -Value $jwks -NoNewline
Set-Content -Path (Join-Path $temporaryDirectory '.well-known/openid-configuration') -Value $oidcConfiguration -NoNewline
Set-Content -Path (Join-Path $temporaryDirectory 'index.html') -Value '<html><body>SMART E2E test identity provider</body></html>' -NoNewline

try {
    Set-AzStorageBlobContent -Context $storageContext -Container '$web' -File (Join-Path $temporaryDirectory 'jwks.json') -Blob 'jwks.json' -Force | Out-Null
    Set-AzStorageBlobContent -Context $storageContext -Container '$web' -File (Join-Path $temporaryDirectory '.well-known/openid-configuration') -Blob '.well-known/openid-configuration' -Force | Out-Null
    Set-AzStorageBlobContent -Context $storageContext -Container '$web' -File (Join-Path $temporaryDirectory 'index.html') -Blob 'index.html' -Force | Out-Null
}
finally {
    Remove-Item -Path $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output $issuer
