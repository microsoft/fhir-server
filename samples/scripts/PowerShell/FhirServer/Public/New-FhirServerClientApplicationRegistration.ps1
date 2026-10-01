function New-FhirServerClientApplicationRegistration {
    <#
    .SYNOPSIS
    Create an AAD Application registration for a client application.
    .DESCRIPTION
    Create a new AAD Application registration for a client application that consumes an API.
    .EXAMPLE
    New-FhirServerClientApplicationRegistration -DisplayName "clientapplication" -ApiAppId 9125e524-1509-XXXX-XXXX-74137cc75422
    .PARAMETER ApiAppId
    API AAD Application registration Id
    .PARAMETER DisplayName
    Display name for the client AAD Application registration
    .PARAMETER ReplyUrl
    Reply URL for the client AAD Application registration
    .PARAMETER IdentifierUri
    Identifier URI for the client AAD Application registration
    .PARAMETER PublicClient
    Switch to indicate if the client application should be a public client (desktop/mobile applications)
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$DisplayName,

        [Parameter(Mandatory = $false)]
        [string]$ReplyUrl = "https://www.getpostman.com/oauth2/callback",

        [Parameter(Mandatory = $false)]
        [string]$IdentifierUri = "api://$DisplayName",

        [Parameter(Mandatory = $false)]
        [switch]$PublicClient
    )

    Set-StrictMode -Version Latest
    
    # Get current Microsoft Graph context
    try {
        $context = Get-MgContext -ErrorAction Stop
        if (-not $context) {
            throw "No Microsoft Graph session found"
        }
    } 
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph cmdlet before proceeding"
    }

    # The API application may have just been created, so it can take a moment to become
    # queryable on Microsoft Graph. Retry the lookup until it propagates before using it.
    $apiAppReg = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $apiAppReg = Get-MgApplication -Filter "AppId eq '$ApiAppId'" -ErrorAction Stop
        if ($apiAppReg) {
            break
        }

        if ($attempt -eq 5) {
            throw "API application '$ApiAppId' was not found on Microsoft Graph."
        }

        $delaySeconds = 5 * [math]::Pow(2, $attempt - 1)
        Write-Warning "Waiting for API application $ApiAppId to become available before registering the client application (attempt $attempt of 5)."
        Start-Sleep -Seconds $delaySeconds
    }

    # Some GUID values for Azure Active Directory
    # Windows AAD Resource ID:
    $windowsAadResourceId = "00000002-0000-0000-c000-000000000000"
    # 'Sign in and read user profile' permission (scope)
    $signInScope = "311a71cc-e848-46a1-bdf8-97ff7156d8e6"

    # Required App permission for Azure AD sign-in
    $reqAad = @{
        ResourceAppId  = $windowsAadResourceId
        ResourceAccess = @(@{
            Id   = $signInScope
            Type = "Scope"
        })
    }

    # Required App Permission for the API application registration
    $reqApi = @{
        ResourceAppId  = $apiAppReg.AppId
        ResourceAccess = @()
    }
    
    # Only add OAuth2 permission scope if it exists
    if ($apiAppReg.Api -and $apiAppReg.Api.Oauth2PermissionScopes -and $apiAppReg.Api.Oauth2PermissionScopes.Count -gt 0) {
        $reqApi.ResourceAccess = @(@{
            Id   = $apiAppReg.Api.Oauth2PermissionScopes[0].Id
            Type = "Scope"
        })
    }

    $appParams = @{
        DisplayName            = $DisplayName
        RequiredResourceAccess = @($reqAad, $reqApi)
        Web                    = @{
            RedirectUris = @($ReplyUrl)
        }
    }

    if ($PublicClient) {
        $appParams.PublicClient = @{
            RedirectUris = @($ReplyUrl)
        }
        $appParams.IsFallbackPublicClient = $true
    } else {
        $appParams.IdentifierUris = @($IdentifierUri)
    }

    $clientAppReg = New-MgApplication @appParams

    # Create a client secret. A newly created application object can take a moment to
    # propagate across Microsoft Graph, so retry only the specific, known transient
    # read-after-write 404s before failing. Two distinct propagation errors have been
    # observed immediately after New-MgApplication for this secret creation call:
    #   * Request_ResourceNotFound  - the application object is not yet queryable.
    #   * Directory_ObjectNotFound  - "Unable to read the company information from the
    #                                 directory" (HTTP 404), seen in ADO build 51862.
    # Matching on these exact Graph error codes (rather than a broad "404"/not-found text
    # match) ensures permanent errors are surfaced immediately and never retried.
    $transientSecretPropagationCodes = @('Request_ResourceNotFound', 'Directory_ObjectNotFound')

    # Classify a caught error as a known transient propagation 404. The Graph error code
    # can surface in several places depending on the SDK/transport, so prefer the most
    # authoritative source and fall back progressively:
    #   1. The structured error body (ErrorDetails.Message JSON -> error.code). This is
    #      authoritative: when present, an unrelated code fails fast and we do NOT fall
    #      through to looser matching that could mask a permanent error.
    #   2. FullyQualifiedErrorId, which the Graph cmdlets prefix with the error code.
    #   3. Free-text fallback over the exception message / raw error-details text, for
    #      transports that only expose the code in the message string.
    $isTransientSecretPropagationError = {
        param($ErrorRecord)

        # 1. Authoritative: the Graph error code from the structured response body.
        $errorDetailsMessage = $null
        if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
            $errorDetailsMessage = [string]$ErrorRecord.ErrorDetails.Message
            $structuredCode = $null
            try {
                $parsedBody = $errorDetailsMessage | ConvertFrom-Json -ErrorAction Stop
                if ($parsedBody -and
                    ($parsedBody.PSObject.Properties.Name -contains 'error') -and
                    $parsedBody.error -and
                    ($parsedBody.error.PSObject.Properties.Name -contains 'code')) {
                    $structuredCode = [string]$parsedBody.error.code
                }
            }
            catch {
                # Body was not parseable JSON; treat the structured code as unavailable
                # and continue to the FullyQualifiedErrorId / text fallbacks below.
                $structuredCode = $null
            }

            if (-not [string]::IsNullOrWhiteSpace($structuredCode)) {
                # Trust the structured code exclusively: unrelated structured errors must
                # fail fast rather than being retried via a looser text match.
                return ($transientSecretPropagationCodes -contains $structuredCode)
            }
        }

        # 2. FullyQualifiedErrorId often carries the Graph code when the body is absent.
        $fullyQualifiedErrorId = [string]$ErrorRecord.FullyQualifiedErrorId
        foreach ($code in $transientSecretPropagationCodes) {
            if ($fullyQualifiedErrorId -like "*$code*") {
                return $true
            }
        }

        # 3. Free-text fallback over the exception message and raw error-details text.
        $candidateMessages = @([string]$ErrorRecord.Exception.Message, $errorDetailsMessage)
        foreach ($candidateMessage in $candidateMessages) {
            if ([string]::IsNullOrEmpty($candidateMessage)) {
                continue
            }
            foreach ($code in $transientSecretPropagationCodes) {
                if ($candidateMessage -like "*$code*") {
                    return $true
                }
            }
        }

        return $false
    }

    $passwordCredential = @{
        displayName = "Generated by New-FhirServerClientApplicationRegistration"
    }

    $clientAppPassword = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            $clientAppPassword = Add-MgApplicationPassword -ApplicationId $clientAppReg.Id -PasswordCredential $passwordCredential -ErrorAction Stop
            break
        }
        catch {
            if ($attempt -eq 5 -or -not (& $isTransientSecretPropagationError $_)) {
                throw
            }

            $delaySeconds = 5 * [math]::Pow(2, $attempt - 1)
            Write-Warning "Waiting for application $($clientAppReg.Id) to propagate before creating its client secret (attempt $attempt of 5)."
            Start-Sleep -Seconds $delaySeconds
        }
    }

    # Create Service Principal
    New-FhirServerServicePrincipal -AppId $clientAppReg.AppId

    $securityAuthenticationAudience = $apiAppReg.IdentifierUris[0]
    $tenantId = $context.TenantId
    $securityAuthenticationAuthority = "https://login.microsoftonline.com/$tenantId"

    @{
        AppId     = $clientAppReg.AppId;
        AppSecret = $clientAppPassword.SecretText;
        ReplyUrl  = $clientAppReg.Web.RedirectUris[0]
        AuthUrl   = "$securityAuthenticationAuthority/oauth2/authorize?resource=$securityAuthenticationAudience"
        TokenUrl  = "$securityAuthenticationAuthority/oauth2/token"
    }
}
