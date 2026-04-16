function New-FhirServerSmartClientReplyUrl {
    <#
    .SYNOPSIS
    Adds a SMART on FHIR Proxy Reply URL to a client app
    .DESCRIPTION
    Adds a SMART on FHIR Proxy Reply URL to a client app
    .EXAMPLE
    New-FhirServerSmartClientReplyUrl -AppId 9125e524-1509-XXXX-XXXX-74137cc75422 -FhirServerUrl https://fhir-server -ReplyUrl https://app-server/my-app
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServerUrl,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ReplyUrl
    )

    Set-StrictMode -Version Latest
    
    # Get current Microsoft Graph context
    try {
        $context = Get-MgContext -ErrorAction Stop
        if (-not $context) {
            throw "No context found"
        }
    } 
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph cmdlet before proceeding"
    }


    $appReg = Get-MgApplication -Filter "AppId eq '$AppId'"
    if (!$appReg) {
        Write-Host "Application with AppId = $AppId was not found."
        return
    }

    $origReplyUrls = [System.Collections.Generic.List[string]]$appReg.Web.RedirectUris
    
    # Form new reply URL: https://fhir-server/<base64url encoded reply url>/*
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($ReplyUrl)
    $encodedText =[Convert]::ToBase64String($bytes)
    $encodedText = $encodedText.TrimEnd('=');
    $encodedText = $encodedText.Replace('/','_');
    $encodedText = $encodedText.Replace('+','-');
    
    $newReplyUrl = $FhirServerUrl.TrimEnd('/') + "/AadSmartOnFhirProxy/callback/" + $encodedText

    # Add Reply URL if not already in the list 
    if ($origReplyUrls -NotContains $newReplyUrl) {
        $origReplyUrls.Add($newReplyUrl)
        $webParams = @{
            RedirectUris = $origReplyUrls
        }
        Update-MgApplication -ApplicationId $appReg.Id -Web $webParams
    }
    else
    {
        Write-Host "Skipping Reply URL add. Already added."
    }
}
