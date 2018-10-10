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
    .PARAMETER Roles
    List of Roles
    .PARAMETER UserRoleAssignment
    List of Users and their Role Assignments
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName,

        [Parameter(Mandatory = $false)]
        [string]$ReplyUrl = "https://www.getpostman.com/oauth2/callback",

        [Parameter(Mandatory = $false)]
        [string]$IdentifierUri = "https://${DisplayName}",
        
        [Parameter(Mandatory = $false)]
        [PSCustomObject]$Roles = $null,
        
        [Parameter(Mandatory = $false)]
        [PSCustomObject]$UserRoleAssignment = $null
    )

    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    $apiAppReg = Get-AzureADApplication -Filter "AppId eq '${ApiAppId}'"

    # Some GUID values for Azure Active Directory
    # https://blogs.msdn.microsoft.com/aaddevsup/2018/06/06/guid-table-for-windows-azure-active-directory-permissions/
    # Windows AAD Resource ID:
    $windowsAadResourceId = "00000002-0000-0000-c000-000000000000"
    # 'Sign in and read user profile' permission (scope)
    $signInScope = "311a71cc-e848-46a1-bdf8-97ff7156d8e6"

    # Required App permission for Azure AD sign-in
    $reqAad = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
    $reqAad.ResourceAppId = $windowsAadResourceId
    $reqAad.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList $signInScope, "Scope"

    # Required App Permission for the API application registration. 
    $reqApi = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
    $reqApi.ResourceAppId = $apiAppReg.AppId #From API App registration above

    # Just add the first scope (user impersonation)
    $reqApi.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList $apiAppReg.Oauth2Permissions[0].id, "Scope"
    
    $clientAppReg = New-AzureADApplication -DisplayName $DisplayName -IdentifierUris $IdentifierUri -RequiredResourceAccess $reqAad, $reqApi -ReplyUrls $ReplyUrl

    # Create a client secret
    $clientAppPassword = New-AzureADApplicationPasswordCredential -ObjectId $clientAppReg.ObjectId

    # Create Service Principal
    $appServicePrincipal = New-AzureAdServicePrincipal -AppId $clientAppReg.AppId | Out-Null
    
    # Create Roles and Users
    CreateUsersandRoles $Roles $UserRoleAssignment $appServicePrincipal $clientAppReg
    
    $securityAuthenticationAudience = $apiAppReg.IdentifierUris[0]
    $aadEndpoint = (Get-AzureADCurrentSessionInfo).Environment.Endpoints["ActiveDirectory"]
    $aadTenantId = (Get-AzureADCurrentSessionInfo).Tenant.Id.ToString()
    $securityAuthenticationAuthority = "${aadEndpoint}${aadTenantId}"

    @{
        AppId     = $clientAppReg.AppId;
        AppSecret = $clientAppPassword.Value;
        ReplyUrl  = $clientAppReg.ReplyUrls[0]
        AuthUrl   = "${securityAuthenticationAuthority}/oauth2/authorize?resource=${securityAuthenticationAudience}"
        TokenUrl  = "${securityAuthenticationAuthority}/oauth2/token"
    }

}

function CreateUsersandRoles($Roles, $UserRoleAssignment, $AppServicePrincipal, $AppADApplication)
{
    Write-Host "Creating Roles"
    $appRoles = New-Object System.Collections.Generic.List[Microsoft.Open.AzureAD.Model.AppRole]
    $roleNameHash = @{}
    if ($Roles) 
    {
        foreach ($role in $Roles) 
        {
            $appRole = New-Object Microsoft.Open.AzureAD.Model.AppRole
            $appRole.AllowedMemberTypes = New-Object System.Collections.Generic.List[string]
            $appRole.AllowedMemberTypes.Add("User");
            $appRole.DisplayName = $role.name
            $appRole.Id = New-Guid
            $appRole.IsEnabled = $true
            $appRole.Description = $role.name
            $appRole.Value = $role.name
            $appRoles.Add($appRole)
            $roleNameHash.Add($role.name, $appRole.Id)
        }
        Set-AzureADApplication -ObjectId $AppADApplication.objectId -appRoles $appRoles
    }
    if ($UserRoleAssignment)
    {
        foreach ($userRole in $UserRoleAssignment)
        {
            $user = $userRole.userName
            Write-Host "Get or create user for user $user"
            $userObjectId = GetUserId $user
            foreach($roleName in $userRole.roles)
            {
                if ($roleNameHash.ContainsKey($roleName))
                {
                        Write-Host "Creating RoleAssignment for role $roleName and user $user"
                        New-AzureADServiceAppRoleAssignment -Id $roleNameHash.$roleName -ObjectId $userObjectId.ObjectId -PrincipalId $userObjectId.ObjectId -ResourceId $AppServicePrincipal.ObjectId
                }
            }
        }
    }
}
        
function GetUserId($Name)
{
    $user = Get-AzureRmADUser -UserPrincipalName $Name
    if (!$user) {
        # Create the user
        Write-Host "Creating User $Name"
        $PasswordProfile = New-Object -TypeName Microsoft.Open.AzureAD.Model.PasswordProfile
        $PasswordProfile.Password = Hash($Name)
        $user = New-AzureADUser -DisplayName $Name -PasswordProfile $PasswordProfile -UserPrincipalName $Name -AccountEnabled $true
    }
    return $user.ObjectId
}

function Hash($secret)
{
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = [System.Text.Encoding]::UTF8.GetBytes($secret)
    return [Convert]::ToBase64String($hasher.ComputeHash($hashBytes))
}
