function Remove-FhirServerApplicationRegistration {
    <#
    .SYNOPSIS
    Remove (delete) an AAD Application registration
    .DESCRIPTION
    Deletes an AAD Application registration with a specific AppId
    .EXAMPLE
    Remove-FhirServerApplicationRegistration -AppId 9125e524-1509-XXXX-XXXX-74137cc75422
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppId
    )

    # Get current AzureAd context
    try {
        $session = Get-AzureADCurrentSessionInfo -ErrorAction Stop
    } 
    catch {
        Write-Host "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
        Break
    }

    $appReg = Get-AzureADApplication -Filter "AppId eq '${AppId}'"

    if (!$appReg) {
        Write-Host "Application with AppId = ${AppId} was not found."
        Break
    }

    Remove-AzureADApplication -ObjectId $appReg.ObjectId
}