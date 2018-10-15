function Get-AzureAdApplicationByDisplayName {
    param (
        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    Get-AzureAdApplication -Filter "DisplayName eq '${DisplayName}'"
}

function Get-AzureAdApplicationByIdentifierUri {
    param (
        [Parameter(Mandatory = $true)]
        [string]$FhirServiceAudience
    )

    return Get-AzureAdApplication -Filter "identifierUris/any(uri:uri eq '${FhirServiceAudience}')"
}

function Get-AzureAdServicePrincipalByAppId {
    param (
        [Parameter(Mandatory = $true)]
        [string]$AppId
    )

    return Get-AzureAdServicePrincipal -Filter "appId eq '${AppId}'"
}

function Get-ServiceAudience {
    param (
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )
 
    return "https://${EnvironmentName}.azurewebsites.net"
}

function Get-UserUpn {
    param (
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $true)]
        [string]$UserId,

        [Parameter(Mandatory = $true)]
        [string]$TenantDomain
    )

    return "${EnvironmentName}-${UserId}@${TenantDomain}"
}