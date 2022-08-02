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

    Get-MgApplication -Filter "DisplayName eq '$DisplayName'"
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
    $aadEndpoint = (Get-MgContext).Environment.Endpoints["ActiveDirectory"]
    $aadTenantId = (Get-MgContext).Tenant.Id.ToString()
    "$aadEndpoint$aadTenantId"
}

function Get-AzureADOpenIdConfiguration {
    Invoke-WebRequest "$(Get-AzureADAuthorityUri)/.well-known/openid-configuration" | ConvertFrom-Json
}

function Get-AzureADTokenEndpoint {
    (Get-AzureADOpenIdConfiguration).token_endpoint
}
