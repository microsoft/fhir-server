function Set-FhirServerApiApplicationRoles {
    <#
    .SYNOPSIS
    Configures (create/update) the roles on the API application for the test environment.
    .DESCRIPTION
    .PARAMETER ObjectId
    ObjectId for the API application
    .PARAMETER RoleConfiguration
    Role configuration to be persisted to AAD from the testauthenvironment.json
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ObjectId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
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

    foreach ($role in $RoleConfiguration) {
        $id = ($azureAdApplication.AppRoles | Where-Object Value -eq $role.name).Id

        if (!$id) {
            $id = New-Guid
        }

        $desiredAppRoles += @{
            AllowedMemberTypes = @("User", "Application")
            Description        = $role.name
            DisplayName        = $role.name
            Id                 = $id
            IsEnabled          = "true"
            Value              = $role.name
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
            Set-AzureADApplication -ObjectId $azureAdApplication.objectId -appRoles $azureAdApplication.AppRoles | Out-Null
        }

        # Update app roles 
        Write-Host "Updating appRoles"
        Set-AzureADApplication -ObjectId $azureAdApplication.objectId -appRoles $desiredAppRoles | Out-Null
    }
}
