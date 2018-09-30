function Remove-FhirServerApplicationRegistration {
    <#
    .SYNOPSIS
    Remove (delete) an AAD Application registration
    .DESCRIPTION
    Deletes an AAD Application registration with a specific AppId
    .EXAMPLE
    Remove-FhirServerApplicationRegistration -AppId 9125e524-1509-XXXX-XXXX-74137cc75422
    #>
    [CmdletBinding(DefaultParameterSetName='ByIdentifierUri')]
    param(
        [Parameter(Mandatory = $true, ParameterSetName = 'ByAppId' )]
        [string]$AppId,

        [Parameter(Mandatory = $true, ParameterSetName = 'ByIdentifierUri' )]
        [string]$IdentifierUri
    )

    # Get current AzureAd context
    try {
        $session = Get-AzureADCurrentSessionInfo -ErrorAction Stop
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
        Break
    }

    $appReg = $null

    if ($AppId) {
        $appReg = Get-AzureADApplication -Filter "AppId eq '${AppId}'"
        if (!$appReg) {
            Write-Host "Application with AppId = ${AppId} was not found."
            Break
        }
    }
    else {
        $appReg = Get-AzureADApplication -Filter "identifierUris/any(uri:uri eq '${IdentifierUri}')"
        if (!$appReg) {
            Write-Host "Application with IdentifierUri = ${IdentifierUri} was not found."
            Break
        }
    }

    Remove-AzureADApplication -ObjectId $appReg.ObjectId
}
