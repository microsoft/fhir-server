function Set-FhirServerClientAppRoleAssignments {
    <#
    .SYNOPSIS
    .DESCRIPTION
    .PARAMETER ObjectId
    .PARAMETER ApiObjectId
    .PARAMETER Roles
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

    $existingRoleAssignments = Get-AzureADServiceAppRoleAssignment -ObjectId $ObjectId | Where-Object {$_.ResourceId -eq $($apiApplication.ObjectId)}

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
        New-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -PrincipalId $ObjectId -ResourceId $($apiApplication.ObjectId) -Id $role | Out-Null
    }

    foreach($role in $rolesToRemove)
    {
        Remove-AzureADServiceAppRoleAssignment -ObjectId $ObjectId -AppRoleAssignmentId ($existingRoleAssignments | Where-Object { $_.Id -eq $role }).ObjectId | Out-Null
    }
}