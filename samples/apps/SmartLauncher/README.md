# SMART on FHIR v2 Sample Launcher

A sample web app that demonstrates SMART on FHIR v2 standalone launch with OAuth2/PKCE authentication against a FHIR server. It supports multiple client types and two OAuth flow patterns.

## Quick Start

1. Configure `appsettings.json` with your FHIR server URL and client ID.
2. Run the app:
   ```bash
   dotnet run --project samples/apps/SmartLauncher
   ```
3. Open `https://localhost:<port>` in a browser.
4. Select your auth mode, scopes, and click **Launch App**.

## Configuration

All settings are in `appsettings.json`:

| Key | Required | Description |
|-----|----------|-------------|
| `FhirServerUrl` | Yes | Base URL of the FHIR server |
| `ClientId` | Yes | OAuth2 client/application ID |
| `ClientType` | No | `public` (default), `confidential-symmetric`, or `confidential-asymmetric` |
| `Scopes` | No | Default scopes (space-separated). Defaults to common SMART v2 scopes if empty |
| `DefaultSmartAppUrl` | No | Launch target path. Defaults to `/sampleapp/launch.html` |
| `ClientSecret` | No | Client secret (for `confidential-symmetric` only) |
| `CertificatePath` | No | Path to .pfx certificate file (for `confidential-asymmetric` token proxy) |
| `CertificatePassword` | No | Password for the certificate file |
| `CertificateThumbprint` | No | Alternative to `CertificatePath`: thumbprint to load from the Windows cert store |

## Client Types

### Public

No credentials required. The browser exchanges the authorization code directly with the token endpoint using PKCE. Suitable for SPAs and environments where secrets cannot be stored securely.

### Confidential-Symmetric (Client Secret)

Uses a shared secret (`ClientSecret`) for authentication. In the **token proxy** flow, the server adds an HTTP Basic `Authorization` header when exchanging the code. In the **fhirclient** flow, the secret is passed to the fhirclient library in the browser (less secure).

### Confidential-Asymmetric (Private Key JWT)

Uses a signed JWT assertion (`private_key_jwt`) to prove client identity. No secret is transmitted.

- **Token proxy flow (recommended for Entra ID):** The server signs the JWT using an X.509 certificate configured via `CertificatePath` or `CertificateThumbprint`. Uses RS256, which is compatible with Entra ID.
- **fhirclient flow:** The browser signs the JWT using a private JWK (RS384 or ES384 per SMART v2 spec). The launcher UI includes tools to generate key pairs and export certificates.

> **Entra ID note:** Entra ID requires RS256 for client assertions, not the RS384/ES384 algorithms in the SMART v2 spec. Use the token proxy flow for Entra ID compatibility.

## OAuth Flows

### Standard Flow (fhirclient library)

Uses the [fhirclient.js](https://github.com/smart-on-fhir/client-js) library to handle the full OAuth2 flow in the browser.

1. `launch.html` loads fhirclient.js and calls `FHIR.oauth2.authorize()`.
2. The user authenticates and grants scopes at the authorization server.
3. The browser is redirected back to `index.html` with an authorization code.
4. fhirclient.js exchanges the code for tokens (adding credentials for confidential clients).
5. The sample app displays the token response and fetches the patient resource.

**When to use:** Standard SMART v2 servers, public clients, or when you don't need Entra ID asymmetric auth.

### Token Proxy Flow

Uses a server-side proxy (`/token-proxy`) to exchange the authorization code. Secrets and certificates never leave the server.

1. `launch.html` manually generates a PKCE pair and redirects to the authorization endpoint.
2. The user authenticates and grants scopes.
3. The browser is redirected back to `index.html` with an authorization code.
4. The browser sends the code to the `/token-proxy` server endpoint.
5. The server discovers the token endpoint from the FHIR server's `/.well-known/smart-configuration`, attaches client credentials, and forwards the token request.
6. The token response is returned to the browser.

**When to use:** Confidential clients (especially asymmetric with Entra ID), or any scenario where credentials should not be exposed to the browser.

## Launcher UI

The launcher page (`/`) provides:

- **Auth mode selection** — public, symmetric, or asymmetric, with conditional credential fields.
- **OAuth flow selection** — standard (fhirclient) or token proxy.
- **Scope picker** — checkboxes for common SMART v2 scopes with an editable text field.
- **Key generation tools** (asymmetric only) — generate ES384/RS384 key pairs in the browser, export public JWKS for app registration, and export X.509 certificates for Entra ID.

## Server Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/config` | GET | Returns public configuration (FHIR server URL, client ID, client type, scopes). No secrets. |
| `/token-proxy` | POST | Proxies the authorization code token exchange. Discovers the token endpoint server-side to prevent SSRF. |

## Automated Setup

`Setup-SmartOnFhirEntraClient.ps1` is a PowerShell script that automates Entra ID app registration, certificate generation, and configuration.
