function Set-FhirServerClientAppRoleAssignments {
    <#
    .SYNOPSIS
    Set app role assignments for the given client application
    .DESCRIPTION
    .PARAMETER ObjectId
    The objectId of the service principal for the client application
    .PARAMETER ApiObjectId
    The objectId of the API application that has roles that need to be assigned
    .PARAMETER Roles
    The collection of roles from the testauthenvironment.json for the client application
    #>
    param(
        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$ObjectId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNull()]
        [object]$Roles
    )

    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    # Get the collection of roles for the user
    $apiApplication = Get-AzureAdServicePrincipalByAppId $ApiAppId

    $existingRoleAssignments = Get-AzureADServiceAppRoleAssignment -ObjectId $apiApplication.ObjectId | Where-Object {$_.PrincipalId -eq $ObjectId} 

    $expectedRoles = $()
    $rolesToAdd = $()
    $roleIdsToRemove = $()

    foreach ($role in $Roles) {
        $expectedRoles += @($apiApplication.AppRoles | Where-Object { $_.DisplayName -eq $role })
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
        # This is know to report failure in certain scenarios, but will actually apply the permissions
        try {
            New-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -PrincipalId $ObjectId -ResourceId $apiApplication.ObjectId -Id $role | Out-Null
        }
    }

    foreach ($role in $rolesToRemove) {
        Remove-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -AppRoleAssignmentId ($existingRoleAssignments | Where-Object { $_.Id -eq $role }).ObjectId | Out-Null
    }

    $finalRolesAssignments = Get-AzureADServiceAppRoleAssignment -ObjectId $apiApplication.ObjectId | Where-Object {$_.PrincipalId -eq $ObjectId} 
    $rolesNotAdded = $()
    $rolesNotRemoved = $()
    foreach ($diff in Compare-Object -ReferenceObject @($expectedRoles | Select-Object) -DifferenceObject @($finalRolesAssignments | Select-Object) -Property "Id") {
        switch ($diff.SideIndicator) {
            "<=" {
                $rolesNotAdded += $diff.Id
            }
            "=>" {
                $rolesNotRemoved += $diff.Id
            }
        }
    }

    if($rolesNotAdded -or $rolesNotRemoved) {
        if($rolesNotAdded) {
            Write-Host "The following roles were not added: $rolesNotAdded"
        }
    
        if($rolesNotRemoved) {
            Write-Host "The following roles were not removed: $rolesNotRemoved"
        }
        throw "There was an issue with adding or removing app role assignments"
    }
}