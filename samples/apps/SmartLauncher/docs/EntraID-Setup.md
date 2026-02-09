# Entra ID Setup for the SMART on FHIR Sample Launcher

This guide walks through configuring Microsoft Entra ID so that the SmartLauncher sample app
can perform a **SMART on FHIR Standalone Launch** against a FHIR server that uses Entra ID
as its identity provider.

Three client authentication modes are supported. Choose the one that matches your scenario:

| Mode | `ClientType` value | Secret material | Entra ID support |
|---|---|---|---|
| Public | `public` | None (PKCE only) | ✅ |
| Confidential – Symmetric | `confidential-symmetric` | Client secret | ✅ (`client_secret_basic`) |
| Confidential – Asymmetric | `confidential-asymmetric` | Certificate / private key | ✅ (`private_key_jwt`) |

---

## Prerequisites

- An Azure subscription with access to the [Microsoft Entra admin center](https://entra.microsoft.com).
- A FHIR server (e.g. Azure Health Data Services) that publishes a
  `/.well-known/smart-configuration` endpoint and uses Entra ID for authorization.
- .NET 8.0 SDK or later installed locally.

---

## 1. Register the Client Application

1. Open the **Microsoft Entra admin center** → **Identity** → **Applications** → **App registrations**.
2. Select **New registration**.
   - **Name**: `SmartLauncher` (or any name you prefer).
   - **Supported account types**: choose the option that matches your tenant setup.
   - **Redirect URI**: select **Single-page application (SPA)** and enter
     `https://localhost:5001/sampleapp/index.html`
     (adjust the port if you changed `launchSettings.json`).
3. Click **Register**.
4. Note the **Application (client) ID** — this is your `ClientId`.

---

## 2. Configure API Permissions

1. In the app registration, go to **API permissions** → **Add a permission**.
2. Select the API that represents your FHIR server. For Azure Health Data Services this is
   typically **Azure Healthcare APIs** or a custom API registration.
3. Add the **Delegated permissions** that correspond to the SMART scopes you need
   (e.g. `patient.read`, `fhirUser`, `openid`).
4. Click **Grant admin consent** if required by your tenant policy.

---

## 3. Mode-Specific Setup

### 3a. Public Client (no secret)

No additional Entra ID configuration is needed. The app uses PKCE (S256) and sends only the
`client_id` in the token request.

**`appsettings.json`:**

```json
{
    "FhirServerUrl": "https://<your-fhir-server>",
    "ClientId": "<application-client-id>",
    "ClientType": "public",
    "Scopes": "openid fhirUser launch/patient patient/*.rs offline_access"
}
```

> **Entra ID setting**: In the app registration under **Authentication**, make sure
> **Allow public client flows** is set to **Yes**.

---

### 3b. Confidential Client – Symmetric (client secret)

1. In the app registration, go to **Certificates & secrets** → **Client secrets**.
2. Click **New client secret**, give it a description and expiry, then click **Add**.
3. Copy the secret **Value** immediately (it is only shown once).

**`appsettings.json`:**

```json
{
    "FhirServerUrl": "https://<your-fhir-server>",
    "ClientId": "<application-client-id>",
    "ClientType": "confidential-symmetric",
    "ClientSecret": "<client-secret-value>",
    "Scopes": "openid fhirUser launch/patient patient/*.rs offline_access"
}
```

> **How it works**: The browser handles the authorization redirect and receives the
> authorization code. The token exchange is proxied through the SmartLauncher backend
> (`/token-proxy`), which adds an `Authorization: Basic` header containing
> `base64(client_id:client_secret)`. The secret never reaches the browser.

---

### 3c. Confidential Client – Asymmetric (certificate / private key JWT)

#### Step 1 – Generate a certificate

You can use any tool. Below is a PowerShell example that creates a self-signed certificate
in the current user's personal store and exports the public key:

```powershell
# Generate a self-signed certificate (RSA 2048, SHA256, valid 2 years)
$cert = New-SelfSignedCertificate `
    -Subject "CN=SmartLauncherClient" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyLength 2048 `
    -KeyAlgorithm RSA `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(2)

# Display the thumbprint (you will need this for appsettings.json)
Write-Host "Thumbprint: $($cert.Thumbprint)"

# Export the public certificate (.cer) for upload to Entra ID
Export-Certificate -Cert $cert -FilePath ".\SmartLauncherClient.cer"
Write-Host "Public certificate exported to .\SmartLauncherClient.cer"
```

If you prefer a PFX file (e.g. for deployment to a server without the cert store):

```powershell
# Export with private key as PFX
$password = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\SmartLauncherClient.pfx" -Password $password
Write-Host "PFX exported to .\SmartLauncherClient.pfx"
```

#### Step 2 – Upload the public certificate to Entra ID

1. In the app registration, go to **Certificates & secrets** → **Certificates**.
2. Click **Upload certificate**.
3. Upload the `.cer` file created above.
4. Entra ID now trusts the public key and will accept JWTs signed by the corresponding
   private key.

#### Step 3 – Configure the SmartLauncher

**Option A – Certificate in the user's certificate store (recommended for local development):**

```json
{
    "FhirServerUrl": "https://<your-fhir-server>",
    "ClientId": "<application-client-id>",
    "ClientType": "confidential-asymmetric",
    "CertificateThumbprint": "<thumbprint-from-step-1>",
    "Scopes": "openid fhirUser launch/patient patient/*.rs offline_access"
}
```

The app loads the certificate from `Cert:\CurrentUser\My` by thumbprint.

**Option B – Certificate from a PFX file:**

```json
{
    "FhirServerUrl": "https://<your-fhir-server>",
    "ClientId": "<application-client-id>",
    "ClientType": "confidential-asymmetric",
    "CertificatePath": "./SmartLauncherClient.pfx",
    "CertificatePassword": "YourPassword",
    "Scopes": "openid fhirUser launch/patient patient/*.rs offline_access"
}
```

> **How it works**: The browser handles the authorization redirect and receives the
> authorization code. The token exchange is proxied through the SmartLauncher backend
> (`/token-proxy`), which builds a signed JWT client assertion with these claims:
>
> | Claim | Value |
> |---|---|
> | `iss` | Application (client) ID |
> | `sub` | Application (client) ID |
> | `aud` | Token endpoint URL |
> | `jti` | Unique GUID |
> | `exp` | Current time + 5 minutes |
>
> The JWT is signed with **RS256** and includes an `x5t` header containing the certificate
> thumbprint so Entra ID can locate the matching public key. The assertion is sent as:
>
> ```
> client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
> client_assertion=<signed-jwt>
> ```

---

## 4. Run the SmartLauncher

```powershell
cd samples/apps/SmartLauncher
dotnet run
```

The app starts on `https://localhost:5001` by default. Open that URL in a browser to begin
the SMART on FHIR standalone launch flow.

---

## 5. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `AADSTS700016: Application not found` | `ClientId` in `appsettings.json` does not match the Entra ID app registration. |
| `AADSTS7000218: request body must contain client_assertion` | `ClientType` is set to an asymmetric mode but the certificate is not configured or not found. |
| `AADSTS700027: client assertion failed signature validation` | The uploaded certificate in Entra ID does not match the private key the app is using, or the signing algorithm is not RS256. |
| `AADSTS50011: reply URL does not match` | The redirect URI registered in Entra ID does not match the URL the app sends. Ensure it is `https://localhost:5001/sampleapp/index.html`. |
| `CORS error when calling FHIR server` | The FHIR server must allow CORS from `https://localhost:5001`. For Azure Health Data Services, configure CORS in the service settings. |
| Patient data not displayed | Ensure the `launch/patient` scope is requested and a patient is selected during the authorization flow. |

---

## References

- [SMART App Launch Implementation Guide](https://build.fhir.org/ig/HL7/smart-app-launch/app-launch.html)
- [Microsoft identity platform – Certificate credentials](https://learn.microsoft.com/entra/identity-platform/certificate-credentials)
- [Microsoft identity platform – OAuth 2.0 authorization code flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow)
- [Azure Health Data Services – FHIR service](https://learn.microsoft.com/azure/healthcare-apis/fhir/overview)
