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

    # After an ambiguous Microsoft Graph write failure - a create that reported 404
    # 'Request_ResourceNotFound' but may actually have applied, or a reissue that reported the
    # assignment already exists - a single read can be STALE: Graph can answer with an empty,
    # not-yet-visible result even though the assignment was persisted. Reconcile the SPECIFIC
    # assignment with a bounded, read-only, capped backoff before concluding it is absent, so a
    # delayed-visibility apply is not mistaken for a missing one (which would otherwise drive a
    # duplicate reissue). This reuses the read-only 404 read-retry helper and never writes, so it
    # cannot create a duplicate. A read error propagates to the caller, which preserves the
    # original write error. Returns $true once the assignment is visible, $false if it never
    # appears within the capped attempts.
    function Confirm-MgClientAppRoleAssignmentWithRetry {
        param(
                [Parameter(Mandatory = $true)]
                [string]$ServicePrincipalId,

                [Parameter(Mandatory = $true)]
                [string]$ResourceId,

                [Parameter(Mandatory = $true)]
                [string]$AppRoleId,

                [int]$MaxAttempts = 3
        )

        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
                $assignment = Get-MgServicePrincipalAppRoleAssignmentWithRetry -ServicePrincipalId $ServicePrincipalId |
                    Where-Object { $_.PrincipalId -eq $ServicePrincipalId -and $_.ResourceId -eq $ResourceId -and $_.AppRoleId -eq $AppRoleId }

                if ($assignment) {
                    return $true
                }

                if ($attempt -eq $MaxAttempts) {
                    return $false
                }

                Write-Warning "Reconciling whether app role '$AppRoleId' on service principal $ServicePrincipalId has become visible on Microsoft Graph before reissuing the assignment (attempt $attempt of $MaxAttempts)."
                Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
        }
    }

    # Microsoft Graph answers an attempt to create an appRoleAssignment that already exists with
    # HTTP 400 and the specific message "Permission being assigned already exists on the object".
    # There is no distinct, stable error code for this, so this classifier matches ONLY that
    # specific phrase. A reissue that trips this is confirmation the assignment is present (a
    # delayed-visibility apply that the prior read had not yet surfaced), not a real failure - so
    # the caller verifies the assignment rather than failing. Any other error does not match.
    function Test-MgAssignmentAlreadyExistsError {
        param(
                [Parameter(Mandatory = $true)]
                $ErrorRecord
        )

        $messageParts = New-Object System.Collections.ArrayList

        $errorDetails = $ErrorRecord.PSObject.Properties['ErrorDetails']
        if ($errorDetails -and $errorDetails.Value) {
                $detailsMessage = $errorDetails.Value.PSObject.Properties['Message']
                if ($detailsMessage -and $detailsMessage.Value) {
                    [void]$messageParts.Add([string]$detailsMessage.Value)
                }
        }

        $exception = $ErrorRecord.Exception
        if ($exception -and $exception.Message) {
                [void]$messageParts.Add($exception.Message)
        }
        [void]$messageParts.Add("$ErrorRecord")

        return ($messageParts -join ' ') -match 'already exists'
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

    # Adding an app role assignment can fail transiently in several Graph-realistic ways, each of
    # which resolves WITHOUT creating a duplicate assignment:
    #   * The create reports a failure even though Microsoft Graph applied the assignment (a
    #     known behaviour). Verifying the CLIENT service principal's own appRoleAssignments
    #     surfaces the applied assignment and the reported failure is swallowed.
    #   * The resource (API) app role has not yet propagated to the write endpoint, so the
    #     write itself answers HTTP 404 'Request_ResourceNotFound'. The write may not have
    #     persisted - or it may have applied and not yet be visible. Because a single read can be
    #     stale, a bounded delayed-visibility RECONCILIATION (read-only, capped backoff) runs
    #     before any reissue; the write is reissued ONLY when the assignment is still absent after
    #     that reconciliation, so a lagging apply is never written a second time.
    #   * A reissue can itself race a delayed-visibility apply and report the assignment already
    #     exists; that is confirmation, not a failure, so the assignment is verified instead.
    # Every re-read is read-only and never reissues the create. Any non-404 failure preserves the
    # existing verify-then-rethrow semantics, and a 404 that never clears surfaces the ORIGINAL
    # Graph error (type, code and message) after the bounded attempts.
    foreach ($role in $rolesToAdd) {
        for ($writeAttempt = 1; $writeAttempt -le 5; $writeAttempt++) {
            $writeError = $null
            try {
                # -ErrorAction Stop is REQUIRED: the Microsoft Graph SDK commonly emits a Graph
                # failure (including the transient write-endpoint 404) as a NON-terminating error,
                # which would otherwise skip the catch and leave $writeError $null - falsely
                # signalling success and defeating the retry/verification below. Stop promotes it
                # to a terminating error so every Graph failure is observed here.
                New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ObjectId -PrincipalId $ObjectId -ResourceId $apiApplication.Id -AppRoleId $role -ErrorAction Stop | Out-Null
            }
            catch {
                $writeError = $_
            }

            if (-not $writeError) {
                break
            }

            if (Test-MgAssignmentAlreadyExistsError -ErrorRecord $writeError) {
                # Graph reported the assignment already exists. This happens when a reissue (or a
                # create) races a delayed-visibility apply: the role IS assigned, so this is
                # confirmation rather than a real failure. Reconcile against the CLIENT principal
                # and, when the assignment is confirmed, treat the create as successful instead of
                # falsely failing. If it genuinely cannot be confirmed, the ORIGINAL Graph error
                # surfaces. The reconciliation is read-only and never reissues the create, so it
                # cannot create a duplicate.
                $confirmedExisting = $false
                try {
                    $confirmedExisting = Confirm-MgClientAppRoleAssignmentWithRetry -ServicePrincipalId $ObjectId -ResourceId $apiApplication.Id -AppRoleId $role
                }
                catch {
                    # The reconciliation read itself failed; it is only a probe, so surface it as a
                    # warning and rethrow the preserved original write error rather than letting
                    # the probe failure mask it.
                    Write-Warning "Could not verify whether app role '$role' already exists on service principal $ObjectId after Microsoft Graph reported a duplicate assignment: $($_.Exception.Message)"
                    throw $writeError
                }

                if ($confirmedExisting) {
                    break
                }

                throw $writeError
            }

            if (Test-MgResourceNotFoundError -ErrorRecord $writeError) {
                # Transient write-endpoint 404: the resource role is still propagating. The write
                # may not have persisted - or it may have applied and not yet be visible. Reconcile
                # the specific assignment with a bounded, read-only, capped backoff BEFORE
                # reissuing, so a delayed-visibility apply is confirmed (and the write is not sent a
                # second time, which would risk a duplicate/conflict). That reconciliation backoff
                # also serves as the wait between reissues. The write is reissued ONLY when the
                # assignment is still absent after reconciliation; when the bounded write attempts
                # are exhausted the ORIGINAL 404 is rethrown. A read failure during reconciliation
                # is surfaced as a warning and the original write error is preserved.
                $alreadyAssigned = $false
                try {
                    $alreadyAssigned = Confirm-MgClientAppRoleAssignmentWithRetry -ServicePrincipalId $ObjectId -ResourceId $apiApplication.Id -AppRoleId $role
                }
                catch {
                    Write-Warning "Could not verify whether app role '$role' was already applied to service principal $ObjectId after Microsoft Graph returned Request_ResourceNotFound on the write: $($_.Exception.Message)"
                    throw $writeError
                }

                if ($alreadyAssigned) {
                    break
                }

                if ($writeAttempt -eq 5) {
                    throw $writeError
                }

                Write-Warning "Microsoft Graph returned Request_ResourceNotFound writing app role '$role' to service principal $ObjectId; the assignment is still not visible after reconciliation, so the resource role may still be propagating to the write endpoint. Reissuing the assignment (attempt $writeAttempt of 5)."
                continue
            }

            # Any non-404 failure: the create is known to report a failure in some environments
            # even though Microsoft Graph did apply the assignment. Verify it against the CLIENT
            # service principal's own appRoleAssignments collection - that is the relationship
            # this assignment is written to ('/servicePrincipals/{client}/appRoleAssignments').
            # Reading the API (resource) service principal here, as a previous version did, would
            # never surface the assignment and so masked a genuine success as a failure.
            #
            # Graph write propagation can lag the create call, so re-read with a bounded,
            # targeted retry until the specific assignment becomes visible. This read is
            # read-only and never re-issues the create above, so it cannot create a duplicate
            # assignment. If the assignment still cannot be confirmed, the ORIGINAL Graph error
            # is rethrown (preserving its type, code and message) instead of being masked by a
            # generic message - so a real permission or other failure stays visible. A non-404
            # error is NOT reissued.
            $roleAssigned = $null
            for ($verifyAttempt = 1; $verifyAttempt -le 5; $verifyAttempt++) {
                try {
                    $roleAssigned = Get-MgServicePrincipalAppRoleAssignmentWithRetry -ServicePrincipalId $ObjectId |
                        Where-Object { $_.PrincipalId -eq $ObjectId -and $_.ResourceId -eq $apiApplication.Id -and $_.AppRoleId -eq $role }
                }
                catch {
                    # The verification read itself failed (e.g. a 403, or the transient 404 that
                    # never cleared). This is only a diagnostic probe, so it must NOT replace the
                    # original write failure: surface it via Write-Warning and fall through to
                    # rethrow the preserved original Graph error below.
                    Write-Warning "Could not verify whether app role '$role' was applied to service principal $ObjectId after the create reported a failure: $($_.Exception.Message)"
                    $roleAssigned = $null
                    break
                }

                if ($roleAssigned) {
                    break
                }

                if ($verifyAttempt -eq 5) {
                    break
                }

                Write-Warning "Verifying whether app role '$role' was applied to service principal $ObjectId despite the reported failure (attempt $verifyAttempt of 5)."
                Start-Sleep -Seconds (5 * [math]::Pow(2, $verifyAttempt - 1))
            }

            if (-not $roleAssigned) {
                throw $writeError
            }

            break
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