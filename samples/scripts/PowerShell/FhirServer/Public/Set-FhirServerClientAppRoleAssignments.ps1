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

    # Both the API and client service principals may have just been created earlier in this
    # run, so Microsoft Graph eventual consistency can return nothing for an immediate lookup.
    # Retry the lookup until the service principal propagates before reading its Id; otherwise
    # StrictMode fails the downstream '.Id' access with
    # "The property 'Id' cannot be found on this object".
    function Get-MgServicePrincipalByAppIdWithRetry {
        param(
            [Parameter(Mandatory = $true)]
            [string]$ApplicationId,

            [Parameter(Mandatory = $true)]
            [string]$Description
        )

        for ($attempt = 1; $attempt -le 5; $attempt++) {
            $servicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$ApplicationId'" -ErrorAction Stop
            if ($servicePrincipal) {
                return $servicePrincipal
            }

            if ($attempt -eq 5) {
                throw "The $Description service principal for appId '$ApplicationId' was not found on Microsoft Graph."
            }

            Write-Warning "Waiting for the $Description service principal $ApplicationId to become available before assigning app roles (attempt $attempt of 5)."
            Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
        }
    }

    # Microsoft Graph answers a read of a freshly created service principal's
    # appRoleAssignedTo relationship with HTTP 404 'Request_ResourceNotFound' until that
    # relationship propagates. This classifier recognizes ONLY that specific error so the
    # read can be retried. It trusts the structured Graph error code when the SDK exposes it
    # (an unrelated error that merely shares HTTP 404 carries a different code and must fail
    # fast); otherwise it falls back to the fully-qualified error id, and finally the error
    # text. Every other failure surfaces immediately.
    function Test-MgResourceNotFoundError {
        param(
            [Parameter(Mandatory = $true)]
            $ErrorRecord
        )

        $notFoundCode = 'Request_ResourceNotFound'

        # Prefer the structured error code from the Graph response body
        # ({ "error": { "code": ... } }). When present it is authoritative: a 404 carrying a
        # different code is deliberately NOT retried.
        $structuredCode = $null
        $errorDetails = $ErrorRecord.PSObject.Properties['ErrorDetails']
        if ($errorDetails -and $errorDetails.Value) {
            $detailsMessage = $errorDetails.Value.PSObject.Properties['Message']
            if ($detailsMessage -and $detailsMessage.Value) {
                try {
                    $parsedBody = $detailsMessage.Value | ConvertFrom-Json -ErrorAction Stop
                    $errorNode = $parsedBody.PSObject.Properties['error']
                    if ($errorNode -and $errorNode.Value) {
                        $codeNode = $errorNode.Value.PSObject.Properties['code']
                        if ($codeNode -and $codeNode.Value) {
                            $structuredCode = [string]$codeNode.Value
                        }
                    }
                }
                catch {
                    # Body was not JSON; fall through to the id / text heuristics below.
                }
            }
        }

        if ($structuredCode) {
            return ($structuredCode -eq $notFoundCode)
        }

        # Fall back to the fully-qualified error id, which Graph seeds with the error code
        # (e.g. 'Request_ResourceNotFound,Microsoft.Graph.PowerShell.Cmdlets...').
        $fqeidProperty = $ErrorRecord.PSObject.Properties['FullyQualifiedErrorId']
        if ($fqeidProperty -and $fqeidProperty.Value) {
            $fqeidCode = ("$($fqeidProperty.Value)" -split ',', 2)[0].Trim()
            if ($fqeidCode -eq $notFoundCode) {
                return $true
            }
        }

        # Last resort: match the error-code token in the human-readable message / record text.
        $messageParts = New-Object System.Collections.ArrayList
        $exception = $ErrorRecord.Exception
        if ($exception -and $exception.Message) {
            [void]$messageParts.Add($exception.Message)
        }
        [void]$messageParts.Add("$ErrorRecord")

        return ($messageParts -join ' ') -match [regex]::Escape($notFoundCode)
    }

    # Read the app role assignments for a service principal, retrying only the transient Graph
    # 404 recognized above. This read-only helper wraps the read alone, so retrying it never
    # re-runs role creation or removal (no duplicate assignments). An empty collection is a
    # legitimate "no assignments yet" answer and is returned as-is rather than retried; non-404
    # failures surface immediately; and once the bounded retries are exhausted the original 404
    # is propagated.
    function Get-MgServicePrincipalAppRoleAssignmentWithRetry {
        param(
            [Parameter(Mandatory = $true)]
            [string]$ServicePrincipalId
        )

        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                return @(Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ServicePrincipalId -ErrorAction Stop)
            }
            catch {
                if (-not (Test-MgResourceNotFoundError -ErrorRecord $_)) {
                    throw
                }

                if ($attempt -eq 5) {
                    throw
                }

                Write-Warning "Waiting for the app role assignments of service principal $ServicePrincipalId to become readable on Microsoft Graph (attempt $attempt of 5)."
                Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
            }
        }
    }

    # Get the collection of roles for the user
    $apiApplication = Get-MgServicePrincipalByAppIdWithRetry -ApplicationId $ApiAppId -Description 'API'
    $mgClientServicePrincipal = Get-MgServicePrincipalByAppIdWithRetry -ApplicationId $AppId -Description 'client'
    $ObjectId = $mgClientServicePrincipal.Id

    $existingRoleAssignments = Get-MgServicePrincipalAppRoleAssignmentWithRetry -ServicePrincipalId $ObjectId | Where-Object {$_.ResourceId -eq $apiApplication.Id}

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
            # Read-only verification, so the same transient-404 retry is safe here and never
            # re-runs the role assignment above.
            $roleAssigned = Get-MgServicePrincipalAppRoleAssignmentWithRetry -ServicePrincipalId $apiApplication.Id | Where-Object {$_.PrincipalId -eq $ObjectId -and $_.AppRoleId -eq $role}
            if (!$roleAssigned) {
                throw "Failure adding app role assignment for service principal."
            }
        }
    }

    foreach ($role in $rolesToRemove) {
        $roleAssignmentToRemove = $existingRoleAssignments | Where-Object { $_.AppRoleId -eq $role }
        Remove-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId -AppRoleAssignmentId $roleAssignmentToRemove.Id | Out-Null
    }

    $finalRolesAssignments = Get-MgServicePrincipalAppRoleAssignmentWithRetry -ServicePrincipalId $ObjectId | Where-Object {$_.ResourceId -eq $apiApplication.Id}
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