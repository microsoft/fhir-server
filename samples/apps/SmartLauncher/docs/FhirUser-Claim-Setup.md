# Configuring the `fhirUser` Claim in Microsoft Entra ID

The SMART on FHIR specification defines a `fhirUser` claim that links an authenticated user to
a FHIR resource (e.g. `Patient/123` or `Practitioner/456`). When this claim is present in the
access token, the FHIR server can associate the user with a FHIR compartment and enforce
patient-level access controls.

This guide shows how to add a `fhirUser` claim to access tokens issued by **standard
Microsoft Entra ID** (not External ID) using directory extension attributes and a claims
mapping policy.

---

## Why a Custom Resource Application?

Azure Health Data Services registers a **first-party** application (`Azure Healthcare APIs`)
in your tenant. To use a claims mapping policy you must set `acceptMappedClaims: true` in the
**resource** application's manifest — that is, the application whose audience (`aud`) appears
in the access token.

Because the first-party `Azure Healthcare APIs` app registration is owned by Microsoft, you
**cannot** modify its manifest. The solution is to register your own **third-party resource
application** that represents the FHIR service, configure your FHIR server to accept tokens
issued for that audience, and apply the claims mapping policy there.

---

## Prerequisites

- An Azure subscription with **Microsoft Entra ID P1** or higher (claims mapping policies
  require premium).
- The [Microsoft Graph PowerShell SDK](https://learn.microsoft.com/powershell/microsoftgraph/installation)
  installed:
  ```powershell
  Install-Module Microsoft.Graph -Scope CurrentUser
  ```
- A FHIR server you control, or the ability to configure the audience on Azure Health Data
  Services.

---

## Step 1 — Register a Resource Application for the FHIR Service

This application represents the FHIR service as a resource (audience) in Entra ID.

1. Open **Microsoft Entra admin center** → **Identity** → **Applications** → **App registrations**.
2. Click **New registration**.
   - **Name**: `FHIR Service Resource` (or a name of your choice).
   - **Supported account types**: Accounts in this organizational directory only.
   - Leave **Redirect URI** blank (this app is a resource, not a client).
3. Click **Register**.
4. Note the **Application (client) ID** — this becomes the `aud` claim in tokens.

### Expose an API

1. In the new app registration, go to **Expose an API**.
2. Click **Set** next to **Application ID URI** and accept the default (`api://<app-id>`) or
   set a custom URI (e.g. `https://fhir.contoso.com`).
3. Click **Add a scope**:
   - **Scope name**: `user_impersonation`
   - **Who can consent**: Admins and users
   - **Admin consent display name**: Access FHIR Server
   - **Admin consent description**: Allows the app to access the FHIR server on behalf of the signed-in user.
   - **State**: Enabled
4. Click **Add scope**.

### Enable `acceptMappedClaims`

1. Go to **Manifest** in the app registration.
2. Find `"acceptMappedClaims"` and set it to `true`:
   ```json
   "acceptMappedClaims": true,
   ```
3. Click **Save**.

> **Security note**: Setting `acceptMappedClaims` to `true` allows claims mapping policies to
> modify tokens for this application. For production workloads, consider using a
> [custom signing key](https://learn.microsoft.com/entra/identity-platform/jwt-claims-customization#configure-a-custom-signing-key)
> instead, which avoids the need for this manifest setting.

---

## Step 2 — Create the Directory Extension Attribute

You need an application to "own" the extension attribute. You can use the resource application
from Step 1, or create a dedicated app for extensions. Using a dedicated app keeps extension
management separate from your resource app.

```powershell
# Connect to Microsoft Graph
Connect-MgGraph -Scopes "Application.ReadWrite.All", "User.ReadWrite.All"

# Use the resource application (or a dedicated extensions app)
$app = Get-MgApplication -Filter "displayName eq 'FHIR Service Resource'"

# Create the fhirUser extension attribute
$extension = New-MgApplicationExtensionProperty `
    -ApplicationId $app.Id `
    -Name "fhirUser" `
    -DataType "String" `
    -TargetObjects @("User")

Write-Host "Extension attribute created: $($extension.Name)"
# Output: extension_<appIdNoHyphens>_fhirUser
```

Note the full extension name (e.g. `extension_a1b2c3d4e5f6_fhirUser`). You will need it in
Steps 3 and 4.

---

## Step 3 — Set the `fhirUser` Value on Users

Assign a FHIR resource reference to each user who should have a `fhirUser` claim.

```powershell
# Set fhirUser for a single user
$userId = "<user-object-id>"
$extensionName = "extension_<appIdNoHyphens>_fhirUser"

Update-MgUser -UserId $userId -AdditionalProperties @{
    $extensionName = "Patient/123"
}

Write-Host "Set $extensionName = Patient/123 on user $userId"
```

To set the value for multiple users from a CSV:

```powershell
# CSV format: UserObjectId,FhirUser
# e.g. aabbccdd-1234-..., Patient/123
Import-Csv ".\fhir-users.csv" | ForEach-Object {
    Update-MgUser -UserId $_.UserObjectId -AdditionalProperties @{
        $extensionName = $_.FhirUser
    }
    Write-Host "Set fhirUser=$($_.FhirUser) on $($_.UserObjectId)"
}
```

Verify the value was set:

```powershell
$user = Get-MgUser -UserId $userId -Property "id,displayName,$extensionName"
$user.AdditionalProperties[$extensionName]
# Output: Patient/123
```

---

## Step 4 — Create a Claims Mapping Policy

The claims mapping policy tells Entra ID to read the directory extension attribute and emit it
in the JWT as a claim named `fhirUser`.

```powershell
# Build the policy definition
$policyDefinition = @"
{
    "ClaimsMappingPolicy": {
        "Version": 1,
        "IncludeBasicClaimSet": "true",
        "ClaimsSchema": [
            {
                "Source": "User",
                "ExtensionID": "extension_<appIdNoHyphens>_fhirUser",
                "JWTClaimType": "fhirUser"
            }
        ]
    }
}
"@

# Create the policy
$policy = New-MgPolicyClaimsMappingPolicy `
    -DisplayName "FHIR User Claim Policy" `
    -Definition @($policyDefinition)

Write-Host "Policy created: $($policy.Id)"
```

> **Important**: Replace `extension_<appIdNoHyphens>_fhirUser` with the actual extension name
> from Step 2. The `appIdNoHyphens` is the Application (client) ID of the app that owns the
> extension, with hyphens removed.

---

## Step 5 — Assign the Policy to the Resource Service Principal

The policy must be assigned to the **service principal** (not the app registration) of the
resource application from Step 1.

```powershell
# Find the service principal for the resource app
$sp = Get-MgServicePrincipal -Filter "displayName eq 'FHIR Service Resource'"

# Assign the claims mapping policy
New-MgServicePrincipalClaimMappingPolicyByRef `
    -ServicePrincipalId $sp.Id `
    -BodyParameter @{
        "@odata.id" = "https://graph.microsoft.com/v1.0/policies/claimsMappingPolicies/$($policy.Id)"
    }

Write-Host "Policy assigned to service principal: $($sp.Id)"
```

---

## Step 6 — Grant the Client App Access to the Resource

Your SmartLauncher client app (from the [Entra ID Setup guide](EntraID-Setup.md)) needs
permission to request tokens for the new resource application.

1. Open the **SmartLauncher** app registration.
2. Go to **API permissions** → **Add a permission** → **My APIs**.
3. Select **FHIR Service Resource**.
4. Check **user_impersonation** under Delegated permissions.
5. Click **Add permissions**, then **Grant admin consent**.

Update the SmartLauncher `appsettings.json` scopes to target the new resource:

```json
{
    "Scopes": "api://<resource-app-id>/user_impersonation openid fhirUser launch/patient offline_access"
}
```

---

## Step 7 — Verify the Claim

1. Run the SmartLauncher and complete a login flow.
2. Copy the `access_token` from the Token Response panel.
3. Paste it into [https://jwt.ms](https://jwt.ms) and look for:
   ```json
   {
       "fhirUser": "Patient/123"
   }
   ```

You can also verify with a direct token request:

```powershell
# Quick test using ROPC (for dev/test only, not recommended for production)
$body = @{
    client_id     = "<smart-launcher-client-id>"
    scope         = "api://<resource-app-id>/user_impersonation openid"
    grant_type    = "password"
    username      = "<test-user@domain.com>"
    password      = "<password>"
}

$token = Invoke-RestMethod `
    -Uri "https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token" `
    -Method POST `
    -Body $body

# Decode and inspect
$token.access_token.Split('.')[1] |
    ForEach-Object { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($_.PadRight($_.Length + (4 - $_.Length % 4) % 4, '='))) } |
    ConvertFrom-Json | Select-Object fhirUser
```

---

## Troubleshooting

| Symptom | Likely Cause |
|---|---|
| `fhirUser` claim missing from token | The claims mapping policy is not assigned to the correct service principal, or the user does not have a value set for the extension attribute. |
| `AADSTS50146: acceptMappedClaims is not set` | The resource app manifest does not have `acceptMappedClaims: true`. If using the first-party `Azure Healthcare APIs` app, you cannot modify it — use a custom resource app instead. |
| Extension attribute not found | Ensure the `ExtensionID` in the policy matches the exact name from `New-MgApplicationExtensionProperty`, including casing. |
| Claim appears as `extension_xxx_fhirUser` instead of `fhirUser` | You are using optional claims in the app manifest instead of a claims mapping policy. Only a `ClaimsMappingPolicy` with `JWTClaimType` can rename the claim. |
| Policy not taking effect | Ensure no other claims customization (portal-based or Custom Claims Policy) conflicts. Only one policy type can be active per service principal. |
| `AADSTS700016: Application not found` | The `aud` (audience) in the token request does not match the resource app's Application ID URI. |

---

## Summary

| Component | Purpose |
|---|---|
| **Resource app registration** | Custom app representing the FHIR service, with `acceptMappedClaims: true` |
| **Directory extension attribute** | Stores the `fhirUser` value (e.g. `Patient/123`) on each Entra ID user |
| **Claims mapping policy** | Renames `extension_xxx_fhirUser` → `fhirUser` in the JWT |
| **Service principal assignment** | Links the policy to the resource app so tokens include the claim |

---

## References

- [Directory extension attributes in claims](https://learn.microsoft.com/entra/identity-platform/schema-extensions)
- [Claims mapping policy reference](https://learn.microsoft.com/entra/identity-platform/reference-claims-mapping-policy-type)
- [Customize claims emitted in tokens](https://learn.microsoft.com/entra/identity-platform/saml-claims-customization)
- [SMART App Launch – fhirUser scope](https://build.fhir.org/ig/HL7/smart-app-launch/scopes-and-launch-context.html)
