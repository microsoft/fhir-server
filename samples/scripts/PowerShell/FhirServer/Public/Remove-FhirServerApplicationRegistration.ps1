function Remove-FhirServerApplicationRegistration {

param(
    [Parameter(Mandatory = $true)]
    [string]$AppId
)

# Get current AzureAd context
try {
    $session = Get-AzureADCurrentSessionInfo -ErrorAction Stop
} 
catch 
{
    Write-Host "Please log into Azure AD with Connect-AzureAD cmdlet before proceeding"
    Break
}

$appReg = Get-AzureADApplication -Filter "AppId eq '${AppId}'"

if (!$appReg)
{
    Write-Host "Application with AppId = ${AppId} was not found."
    Break
}

Remove-AzureADApplication -ObjectId $appReg.ObjectId

}