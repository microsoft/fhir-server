function Get-ApplicationDisplayName { 
    param (
        [Parameter(Mandatory = $false)]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppId
    )

    if (!$EnvironmentName) {
        return $AppId
    }
    else {
        return "$EnvironmentName-$AppId"
    }
}

function Get-AzureAdApplicationByDisplayName {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$DisplayName
    )
    return Get-MgApplication -Filter "DisplayName eq '$DisplayName'"
}

function Get-AzureAdApplicationByIdentifierUri {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceAudience
    )
    return Get-MgApplication -Filter "identifierUris/any(uri:uri eq '$FhirServiceAudience')"
}

function Get-AzureAdServicePrincipalByAppId {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppId
    )

    return Get-MgServicePrincipal -Filter "appId eq '$AppId'"
}

function Get-ServiceAudience {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TenantId
    )
 
     Write-Host "ServiceName $ServiceName"
     Write-Host "TenantId $TenantId"

    # AppId Uri in single tenant applications will require use of default scheme or verified domains
    # It needs to be in one of the many formats mentioned in https://docs.microsoft.com/en-us/azure/active-directory/develop/reference-breaking-changes
    # We use the format api://<tenantId>/<string>
    return "https://$ServiceName.$TenantId"
}

function Get-UserId { 
    param (
        [Parameter(Mandatory = $false)]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$UserId
    )

    if (!$EnvironmentName) {
        return $UserId
    }
    else {
        return "$EnvironmentName-$UserId"
    }
}

function Get-UserUpn {
    param (
        [Parameter(Mandatory = $false)]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$UserId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TenantDomain
    )

    return "$(Get-UserId -EnvironmentName $EnvironmentName -UserId $UserId)@$TenantDomain"
}

function Get-AzureADAuthorityUri {
    $context = Get-MgContext
    if (-not $context) {
        throw "No Microsoft Graph session found. Please connect using Connect-MgGraph."
    }
    
    # For Microsoft Graph, use the standard authority endpoint
    $tenantId = $context.TenantId
    "https://login.microsoftonline.com/$tenantId"
}

function Get-AzureADOpenIdConfiguration {
    $authorityUri = Get-AzureADAuthorityUri
    Invoke-WebRequest "$authorityUri/.well-known/openid-configuration" | ConvertFrom-Json
}

function Get-AzureADTokenEndpoint {
    (Get-AzureADOpenIdConfiguration).token_endpoint
}
