function Set-FhirServerClientAppRoleAssignments {
    <#
    .SYNOPSIS
    Set app role assignments for the given client application
    .DESCRIPTION
    Set AppRoles for a given client application. Requires Azure AD admin privileges.
    .EXAMPLE
    Set-FhirServerClientAppRoleAssignments -AppId <Client App Id> -ApiAppId <Resource Api Id> -AppRoles globalReader,globalExporter
    .PARAMETER AppId
    The AppId of the of the client application
    .PARAMETER ApiAppId
    The objectId of the API application that has roles that need to be assigned
    .PARAMETER AppRoles
    The collection of roles from the testauthenvironment.json for the client application
    #>
    param(
        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$AppId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true )]
        [AllowEmptyCollection()]
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

    # Get the collection of roles for the user
    $apiApplication = Get-MgServicePrincipal -Filter "appId eq '$ApiAppId'"
    $mgClientServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$AppId'"
    $ObjectId = $mgClientServicePrincipal.Id

    $existingRoleAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId | Where-Object {$_.ResourceId -eq $apiApplication.Id} 

    $expectedRoles = New-Object System.Collections.ArrayList
    $rolesToAdd = New-Object System.Collections.ArrayList
    $rolesToRemove = New-Object System.Collections.ArrayList

    foreach ($role in $AppRoles) {
        $expectedRoles += @($apiApplication.AppRoles | Where-Object { $_.Value -eq $role })
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
        # This is known to report failure in certain scenarios, but will actually apply the permissions
        try {
            New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId -PrincipalId $ObjectId -ResourceId $apiApplication.Id -AppRoleId $role | Out-Null
        }
        catch {
            #The role may have been assigned. Check:
            $roleAssigned = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $apiApplication.Id | Where-Object {$_.PrincipalId -eq $ObjectId -and $_.AppRoleId -eq $role}
            if (!$roleAssigned) {
                throw "Failure adding app role assignment for service principal."
            }
        }
    }

    foreach ($role in $rolesToRemove) {
        $roleAssignmentToRemove = $existingRoleAssignments | Where-Object { $_.AppRoleId -eq $role }
        Remove-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId -AppRoleAssignmentId $roleAssignmentToRemove.Id | Out-Null
    }

    $finalRolesAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId | Where-Object {$_.ResourceId -eq $apiApplication.Id} 
    $rolesNotAdded = @()
    $rolesNotRemoved = @()
    $finalRoleIds = @($finalRolesAssignments | Select-Object -ExpandProperty AppRoleId)
    
    foreach ($expectedRoleId in $expectedRoleIds) {
        if ($expectedRoleId -notin $finalRoleIds) {
            $rolesNotAdded += $expectedRoleId
        }
    }

    foreach ($finalRoleId in $finalRoleIds) {
        if ($finalRoleId -notin $expectedRoleIds) {
            $rolesNotRemoved += $finalRoleId
        }
    }

    if($rolesNotAdded -or $rolesNotRemoved) {
        if($rolesNotAdded) {
            Write-Host "The following roles were not added: $rolesNotAdded"
        }
    
        if($rolesNotRemoved) {
            Write-Host "The following roles were not removed: $rolesNotRemoved"
        }
    }
}