function Set-FhirServerApiUsers {
    <#
    .SYNOPSIS
    Configures (create/update) the needed users for the test environment.
    .DESCRIPTION
    .PARAMETER TenantDomain
    The domain of the AAD tenant. 
    .PARAMETER ApiAppId
    The AppId for the AAD application that contains the roles to be assigned.
    .PARAMETER UserConfiguration
    The collection of users from the testauthenvironment.json.
    .PARAMETER UserNamePrefix
    The prefix to use for the users to stop duplication/collision if multiple environments exist within the same AAD tenant.
    .PARAMETER KeyVaultName
    The name of the key vault to persist the user's passwords to.
    #>
    param(
        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$TenantDomain,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNull()]
        [object]$UserConfiguration,

        [Parameter(Mandatory = $false )]
        [string]$UserNamePrefix,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$KeyVaultName
    )

    Set-StrictMode -Version Latest
    
    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    Write-Host "Persisting Users to AAD"
    
    $environmentUsers = @()

    foreach ($user in $UserConfiguration) {
        $userId = $user.id
        if ($UserNamePrefix) {
            $userId = Get-UserId -EnvironmentName $UserNamePrefix -UserId $user.Id
        }

        $userUpn = Get-UserUpn -UserId $userId -TenantDomain $TenantDomain

        # See if the user exists
        $aadUser = Get-AzureADUser -Filter "userPrincipalName eq '$userUpn'"

        Add-Type -AssemblyName System.Web
        $password = [System.Web.Security.Membership]::GeneratePassword(16, 5)
        $passwordSecureString = ConvertTo-SecureString $password -AsPlainText -Force

        if ($aadUser) {
            Set-AzureADUserPassword -ObjectId $aadUser.ObjectId -Password $passwordSecureString -EnforceChangePasswordPolicy $false -ForceChangePasswordNextLogin $false
        }
        else {
            $PasswordProfile = New-Object -TypeName Microsoft.Open.AzureAD.Model.PasswordProfile
            $PasswordProfile.Password = $password
            $PasswordProfile.EnforceChangePasswordPolicy = $false
            $PasswordProfile.ForceChangePasswordNextLogin = $false

            $aadUser = New-AzureADUser -DisplayName $userId -PasswordProfile $PasswordProfile -UserPrincipalName $userUpn -AccountEnabled $true -MailNickName $userId
        }

        $upnSecureString = ConvertTo-SecureString -string $userUpn -AsPlainText -Force
        Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name "user--$($user.id)--id" -SecretValue $upnSecureString | Out-Null
        Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name "user--$($user.id)--secret" -SecretValue $passwordSecureString | Out-Null

        $environmentUsers += @{
            upn           = $userUpn
            environmentId = $userId
            id            = $user.id
        }
            
        Set-FhirServerUserAppRoleAssignments -ApiAppId $ApiAppId -UserPrincipalName $userUpn -AppRoles $user.roles
    }

    return $environmentUsers
}
