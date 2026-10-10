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
    $rolesToDisable = @()
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
                    $rolesToDisable += $diff.Id
                    $appRolesToDisable = $true
                }
            }
        }
    }

    if ($appRolesToEnable -or $appRolesToDisable) {
        if ($appRolesToDisable) {
            Write-Host "Disabling old appRoles"
            Update-MgApplication -ApplicationId $mgApplication.Id -AppRoles $mgApplication.AppRoles -ErrorAction Stop | Out-Null

            for ($attempt = 1; $attempt -le 5; $attempt++) {
                $currentRoles = (Get-MgApplication -ApplicationId $mgApplication.Id -ErrorAction Stop).AppRoles
                $enabledOldRoles = @($currentRoles | Where-Object { $_.Id -in $rolesToDisable -and $_.IsEnabled })
                if ($enabledOldRoles.Count -eq 0) {
                    break
                }

                if ($attempt -eq 5) {
                    throw "App roles on application $ApiAppId did not become disabled."
                }

                Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
            }
        }

        Write-Host "Updating appRoles"
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Update-MgApplication -ApplicationId $mgApplication.Id -AppRoles $desiredAppRoles -ErrorAction Stop | Out-Null
                break
            }
            catch {
                if ($attempt -eq 5 -or
                    ($_.FullyQualifiedErrorId -notlike 'CannotDeleteOrUpdateEnabledEntitlement*' -and
                     $_.Exception.Message -notlike '*CannotDeleteOrUpdateEnabledEntitlement*')) {
                    throw
                }

                Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
            }
        }
    }
}
