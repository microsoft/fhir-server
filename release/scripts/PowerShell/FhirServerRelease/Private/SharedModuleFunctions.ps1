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
        return "${EnvironmentName}-${AppId}"
    }
}

function Get-AzureAdApplicationByDisplayName {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$DisplayName
    )

    Get-AzureAdApplication -Filter "DisplayName eq '${DisplayName}'"
}

function Get-AzureAdApplicationByIdentifierUri {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceAudience
    )

    return Get-AzureAdApplication -Filter "identifierUris/any(uri:uri eq '${FhirServiceAudience}')"
}

function Get-AzureAdServicePrincipalByAppId {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppId
    )

    return Get-AzureAdServicePrincipal -Filter "appId eq '${AppId}'"
}

function Get-ServiceAudience {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentName
    )
 
    return "https://${EnvironmentName}.azurewebsites.net/"
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
        return "${EnvironmentName}-${UserId}"
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

    return "$(Get-UserId -EnvironmentName $EnvironmentName -UserId $UserId)@${TenantDomain}"
}