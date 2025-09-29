function Set-FhirServerApiApplicationRoles {
    <#
    .SYNOPSIS
    Configures (create/update) the roles on the API application.
    .DESCRIPTION
    Configures (create/update) the roles of the API Application registration, specifically, it populates the AppRoles field of the application manifest.
    .EXAMPLE
    Set-FhirServerApiApplicationRoles -AppId <ID of API App> -AppRoles globalReader,globalExporter
    .PARAMETER ApiAppId
    ApiId for the API application
    .PARAMETER AppRoles
    List of roles to be defined on the API App
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [string[]]$AppRoles
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

    Write-Host "Persisting Roles to Microsoft Graph application"

    $mgApplication = Get-MgApplication -Filter "AppId eq '$ApiAppId'"

    $appRolesToDisable = $false
    $appRolesToEnable = $false
    $desiredAppRoles = @()

    foreach ($role in $AppRoles) {
        $existingAppRole = $mgApplication.AppRoles | Where-Object Value -eq $role
        
        if($existingAppRole) {
            $id = $existingAppRole.Id
        }
        else {
            $id = New-Guid
        }

        $desiredAppRoles += @{
            AllowedMemberTypes = @("User", "Application")
            Description        = $role
            DisplayName        = $role
            Id                 = $id
            IsEnabled          = "true"
            Value              = $role
        }
    }

    if (!($mgApplication.PsObject.Properties.Name -eq "AppRoles")) {
        $appRolesToEnable = $true
    }
    else {
        foreach ($diff in Compare-Object -ReferenceObject $desiredAppRoles -DifferenceObject $mgApplication.AppRoles -Property "Id") {
            switch ($diff.SideIndicator) {
                "<=" {
                    $appRolesToEnable = $true
                }
                "=>" {
                    ($mgApplication.AppRoles | Where-Object Id -eq $diff.Id).IsEnabled = $false
                    $appRolesToDisable = $true
                }
            }
        }
    }

    if ($appRolesToEnable -or $appRolesToDisable) {
        if ($appRolesToDisable) {
            Write-Host "Disabling old appRoles"
            Update-MgApplication -ApplicationId $mgApplication.Id -AppRoles $mgApplication.AppRoles | Out-Null
        }

        # Update app roles 
        Write-Host "Updating appRoles"
        Update-MgApplication -ApplicationId $mgApplication.Id -AppRoles $desiredAppRoles | Out-Null
    }
}
