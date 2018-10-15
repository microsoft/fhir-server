function Set-FhirServerApiUsers {
    <#
    .SYNOPSIS
    Configures (create/update) the needed users for the test environment.
    .DESCRIPTION
    .PARAMETER TenantDomain
    The domain of the AAD tenant. 
    .PARAMETER ServicePrincipalObjectId
    The service principal for the AAD application that contains the roles to be assigned.
    .PARAMETER UserConfiguration
    The collection of users from the testauthenvironment.json.
    .PARAMETER UserNamePrefix
    The prefix to use for the users to stop duplication/collision if multiple environments exist within the same AAD tenant.
    .PARAMETER KeyVaultName
    The name of the key vault to persist the user's passwords to.
    #>
    param(
        [Parameter(Mandatory = $true )]
        [string]$TenantDomain,

        [Parameter(Mandatory = $true )]
        [string]$ServicePrincipalObjectId,

        [Parameter(Mandatory = $true )]
        [object]$UserConfiguration,

        [Parameter(Mandatory = $false )]
        [string]$UserNamePrefix,

        [Parameter(Mandatory = $true)]
        [string]$KeyVaultName
    )

    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    Write-Host "Persisting Users to AAD"
    
    $environmentUsers = @()

    if ($UserConfiguration) {
        $servicePrincipal = Get-AzureADServicePrincipal -ObjectId $ServicePrincipalObjectId

        foreach ($user in $UserConfiguration) {
            $userId = $user.id
            if ($UserNamePrefix) {
                $userId = Get-UserId -EnvironmentName $UserNamePrefix -UserId $user.Id
            }

            $userUpn = Get-UserUpn -UserId $userId -TenantDomain $TenantDomain

            # See if the user exists
            $aadUser = Get-AzureADUser -searchstring $userId

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

            Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name "${userId}-password" -SecretValue $passwordSecureString | Out-Null

            $environmentUsers += @{
                upn           = $userUpn
                environmentId = $userId
                id            = $user.id
            }
            
            # Get the collection of roles for the user
            $existingRoleAssignments = Get-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId | Where-Object {$_.ResourceId -eq $servicePrincipal.ObjectId}

            $expectedRoles = New-Object System.Collections.ArrayList
            $rolesToAdd = New-Object System.Collections.ArrayList
            $rolesToRemove = New-Object System.Collections.ArrayList

            foreach ($role in $user.roles) {
                $expectedRoles += @($servicePrincipal.AppRoles | Where-Object { $_.DisplayName -eq $role })
            }

            foreach ($diff in Compare-Object -ReferenceObject @($expectedRoles | Select-Object) -DifferenceObject @($existingRoleAssignments | Select-Object) -Property "Id") {
                switch ($diff.SideIndicator) {
                    "<=" {
                        $rolesToAdd += $diff.Id
                    }
                    "=>" {
                        $rolesToRemove += $diff.Id
                    }
                }
            }

            foreach ($role in $rolesToAdd) {
                New-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId -PrincipalId $aadUser.ObjectId -ResourceId $servicePrincipal.ObjectId -Id $role | Out-Null
            }

            foreach ($role in $rolesToRemove) {
                Remove-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId -AppRoleAssignmentId ($existingRoleAssignments | Where-Object { $_.Id -eq $role }).ObjectId | Out-Null
            }
        }
    }

    return $environmentUsers
}