function Configure-FhirServerApiUsers {
    <#
    .SYNOPSIS
    .DESCRIPTION
    .PARAMETER UserConfiguration
    Users to be persisted to AAD
    #>
    param(
        [Parameter(Mandatory = $true )]
        [string]$TenantId,

        [Parameter(Mandatory = $true )]
        [string]$ServicePrincipalObjectId,

        [Parameter(Mandatory = $true )]
        [object]$UserConfiguration
    )

    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    Write-Host "Persisting Users to AAD"
    
    if($UserConfiguration)
    {
        $servicePrincipal = Get-AzureADServicePrincipal -ObjectId $ServicePrincipalObjectId

        foreach ($user in $UserConfiguration) 
        {
            # See if the user exists
            $aadUser = Get-AzureADUser -searchstring $user.id

            $PasswordProfile = New-Object -TypeName Microsoft.Open.AzureAD.Model.PasswordProfile
            $PasswordProfile.Password = "!A324sfs34"

            if($aadUser)
            {
                # Update if so
            }
            else
            {
                $aadUser = New-AzureADUser -DisplayName $user.id -PasswordProfile $PasswordProfile -UserPrincipalName ($user.id + "@" + $TenantId) -AccountEnabled $true -MailNickName $user.id
            }

            # Get the collection of roles for the user
            $roleAssignments = Get-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId | Where-Object {$_.ResourceId -eq $servicePrincipal.ObjectId}

            $rolesToAdd = $()
            $rolesToRemove = $()

            $roleIdsToAssign = $()
            foreach($role in $user.roles)
            {
                 $roleIdsToAssign += @($servicePrincipal.AppRoles | Where-Object { $_.DisplayName -eq $role })
            }

            foreach ($diff in Compare-Object -ReferenceObject $roleIdsToAssign -DifferenceObject $roleAssignments -Property "Id") 
            {
                switch ($diff.SideIndicator) {
                    "<=" {
                        $rolesToAdd += $diff.Id
                    }
                    "=>" {
                        $rolesToRemove += $diff.Id
                    }
                }
            }

            foreach($role in $rolesToAdd)
            {
                New-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId -PrincipalId $aadUser.ObjectId -ResourceId $servicePrincipal.ObjectId -Id $role
            }

            foreach($role in $rolesToRemove)
            {
                Remove-AzureADUserAppRoleAssignment -ObjectId $aadUser.ObjectId -AppRoleAssignmentId ($roleAssignments | Where-Object { $_.Id -eq $role }).ObjectId
            }
        }
    }
}
