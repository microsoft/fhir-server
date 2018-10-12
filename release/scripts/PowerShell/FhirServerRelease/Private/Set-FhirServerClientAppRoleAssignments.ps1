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
        [string]$ObjectId,

        [Parameter(Mandatory = $true )]
        [string]$ApiAppId,

        [Parameter(Mandatory = $true )]
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
    $apiApplication = Get-AzureAdServicePrincipal -Filter "appId eq '$ApiAppId'"

    $existingRoleAssignments = Get-AzureADServiceAppRoleAssignment -ObjectId $apiApplication.ObjectId | Where-Object {$_.PrincipalId -eq $ObjectId} 

    $expectedRoles = $()
    $rolesToAdd = $()
    $roleIdsToRemove = $()

    foreach($role in $Roles)
    {
        $expectedRoles += @($apiApplication.AppRoles | Where-Object { $_.DisplayName -eq $role })
    }

    foreach ($diff in Compare-Object -ReferenceObject @($expectedRoles | Select-Object) -DifferenceObject @($existingRoleAssignments | Select-Object) -Property "Id") 
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
        New-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -PrincipalId $ObjectId -ResourceId $apiApplication.ObjectId -Id $role | Out-Null
    }

    foreach($role in $rolesToRemove)
    {
        Remove-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -AppRoleAssignmentId ($existingRoleAssignments | Where-Object { $_.Id -eq $role }).ObjectId | Out-Null
    }
}