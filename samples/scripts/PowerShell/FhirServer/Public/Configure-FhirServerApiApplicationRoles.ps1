function Configure-FhirServerApiApplicationRoles {
    <#
    .SYNOPSIS
    .DESCRIPTION
    .PARAMETER ObjectId
    ObjectId for the application
    .PARAMETER RoleConfiguration
    Role configuration to be persisted to AAD
    #>
    [CmdletBinding(DefaultParameterSetName='ByObjectId')]
    param(
        [Parameter(Mandatory = $true, ParameterSetName = 'ByObjectId' )]
        [string]$ObjectId,

        [Parameter(Mandatory = $true, ParameterSetName = 'ByObjectId' )]
        [object]$RoleConfiguration
    )

    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    Write-Host "Persisting Roles to AAD application"
    
    $azureAdApplication = Get-AzureADApplication -ObjectId $ObjectId

    $appRolesToDisable = $false
    $appRolesToEnable = $false
    $desiredAppRoles = @()

    if($RoleConfiguration)
    {
        foreach ($role in $RoleConfiguration) 
        {
            $desiredAppRoles += @{
                AllowedMemberTypes = @("User", "Application")
                Description = $role.name
                DisplayName = $role.name
                Id = New-Guid
                IsEnabled = "true"
                Value = $role.name
            }
        }

        if (!($azureAdApplication.PsObject.Properties.Name -eq "AppRoles")) {
            $appRolesToEnable = $true
        }
        else {
            foreach ($diff in Compare-Object -ReferenceObject $desiredAppRoles -DifferenceObject $azureAdApplication.AppRoles -Property "Id") {
                switch ($diff.SideIndicator) {
                    "<=" {
                        $appRolesToEnable = $true
                    }
                    "=>" {
                        ($azureAdApplication.AppRoles | Where-Object Id -eq $diff.Id).IsEnabled = $false
                        $appRolesToDisable = $true
                    }
                }
            }
        }

        if ($appRolesToEnable -or $appRolesToDisable) {
            if ($appRolesToDisable) {
                Write-Host "Disabling old appRoles"
                Set-AzureADApplication -ObjectId $azureAdApplication.objectId -appRoles $azureAdApplication.AppRoles
            }

            # Update app roles 
            Write-Host "Updating appRoles"
            Write-Host ($desiredAppRoles | Format-Table | Out-String)
            Set-AzureADApplication -ObjectId $azureAdApplication.objectId -appRoles $desiredAppRoles
        }
    }
}
