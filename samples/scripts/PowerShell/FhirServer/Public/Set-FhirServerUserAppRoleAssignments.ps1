function Set-FhirServerUserAppRoleAssignments {
    <#
    .SYNOPSIS
    Set app role assignments for a user
    .DESCRIPTION
    Set AppRoles for a given user. Requires Azure AD admin privileges.
    .EXAMPLE
    Set-FhirServerUserAppRoleAssignments -UserPrincipalName <User Principal Name> -ApiAppId <Resource Api Id> -AppRoles globalReader,globalExporter
    .PARAMETER UserPrincipalName
    The user principal name (e.g. myalias@contoso.com) of the of the user
    .PARAMETER ApiAppId
    The AppId of the API application that has roles that need to be assigned
    .PARAMETER AppRoles
    The array of roles for the client application
    #>
    param(
        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$UserPrincipalName,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true )]
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

    $mgUser = Get-MgUser -Filter "UserPrincipalName eq '$UserPrincipalName'"
    if (!$mgUser)
    {
        throw "User not found"
    }

    $servicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$ApiAppId'"

    # Get the collection of roles for the user
    $existingRoleAssignments = Get-MgUserAppRoleAssignment -UserId $mgUser.Id | Where-Object {$_.ResourceId -eq $servicePrincipal.Id}

    $expectedRoles = New-Object System.Collections.ArrayList
    $rolesToAdd = New-Object System.Collections.ArrayList
    $rolesToRemove = New-Object System.Collections.ArrayList

    foreach ($role in $AppRoles) {
        $expectedRoles += @($servicePrincipal.AppRoles | Where-Object { $_.Value -eq $role })
    }

    # Compare expected roles with existing assignments
    $expectedRoleIds = @($expectedRoles | Select-Object -ExpandProperty Id)
    $existingRoleIds = @($existingRoleAssignments | Select-Object -ExpandProperty AppRoleId)

    foreach ($expectedRoleId in $expectedRoleIds) {
        if ($expectedRoleId -notin $existingRoleIds) {
            $rolesToAdd += $expectedRoleId
        }
    }

    foreach ($existingRoleId in $existingRoleIds) {
        if ($existingRoleId -notin $expectedRoleIds) {
            $rolesToRemove += $existingRoleId
        }
    }

    foreach ($role in $rolesToAdd) {
        New-MgUserAppRoleAssignment -UserId $mgUser.Id -PrincipalId $mgUser.Id -ResourceId $servicePrincipal.Id -AppRoleId $role | Out-Null
    }

    foreach ($role in $rolesToRemove) {
        $roleAssignmentToRemove = $existingRoleAssignments | Where-Object { $_.AppRoleId -eq $role }
        Remove-MgUserAppRoleAssignment -UserId $mgUser.Id -AppRoleAssignmentId $roleAssignmentToRemove.Id | Out-Null
    }
}