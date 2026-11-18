Set-StrictMode -Version Latest

$script:PollAttempts = 60
$script:PollDelaySeconds = 5

. (Join-Path $PSScriptRoot 'RevisionProfiles.Policy.ps1')
. (Join-Path $PSScriptRoot 'RevisionProfiles.Transport.ps1')

function ConvertTo-ObservedRevision {
    param($Resource)

    $properties = Get-Field $Resource 'properties'
    $name = Get-StringField $Resource 'name'
    if ([string]::IsNullOrEmpty($name) -or $properties -isnot [System.Collections.IDictionary]) {
        throw 'ARM returned a revision without a name or properties.'
    }

    return [ordered]@{
        Name = $name
        Active = [bool](Get-Field $properties 'active')
        ProvisioningState = Get-StringField $properties 'provisioningState'
        HealthState = Get-StringField $properties 'healthState'
        Fqdn = Get-StringField $properties 'fqdn'
        Template = Get-WritableTemplate (Get-Field $properties 'template')
    }
}

function Get-ObservedRevision {
    param([string]$AppResourceId, [string]$Name)
    $resource = Invoke-ArmRequest -Method 'GET' -Path "$AppResourceId/revisions/$Name" -Body $null
    if ($null -eq $resource) {
        return $null
    }

    return ConvertTo-ObservedRevision $resource
}

function Test-LatestTrafficEntry {
    param([System.Collections.IDictionary]$Entry)
    return $Entry.LatestRevision -or [string]::IsNullOrEmpty($Entry.RevisionName)
}

function Get-ObservedState {
    param([string]$AppResourceId)

    $app = Invoke-ArmRequest -Method 'GET' -Path $AppResourceId -Body $null
    $properties = Get-Field $app 'properties'
    $ingress = Get-Field (Get-Field $properties 'configuration') 'ingress'
    $fqdn = Get-StringField $ingress 'fqdn'
    if ($null -eq $app -or $null -eq $ingress -or [string]::IsNullOrEmpty($fqdn) -or $fqdn.IndexOf('.') -lt 1) {
        throw "Container app '$AppResourceId' was not found or has no ingress FQDN."
    }

    $traffic = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in (Get-Field $ingress 'traffic')) {
        $weight = Get-Field $entry 'weight'
        $traffic.Add([ordered]@{
            RevisionName = Get-StringField $entry 'revisionName'
            LatestRevision = [bool](Get-Field $entry 'latestRevision')
            Weight = if ($null -eq $weight) { 0 } else { [int]$weight }
            Label = Get-StringField $entry 'label'
        })
    }

    $revisions = [ordered]@{}
    $path = "$AppResourceId/revisions"
    while ($null -ne $path) {
        $page = Invoke-ArmRequest -Method 'GET' -Path $path -Body $null
        foreach ($item in (Get-Field $page 'value')) {
            $revision = ConvertTo-ObservedRevision $item
            $revisions[$revision.Name] = $revision
        }

        $next = Get-StringField $page 'nextLink'
        $path = if ([string]::IsNullOrEmpty($next)) { $null } else { ([uri]$next).PathAndQuery }
    }

    $weighted = @($traffic | Where-Object { $_.Weight -gt 0 })
    if ($weighted.Count -eq 1 -and $weighted[0].Weight -eq 100) {
        $selected = if (Test-LatestTrafficEntry $weighted[0]) { Get-StringField $properties 'latestRevisionName' } else { $weighted[0].RevisionName }
        if (-not [string]::IsNullOrEmpty($selected) -and $revisions.Contains($selected)) {
            $exact = Get-ObservedRevision -AppResourceId $AppResourceId -Name $selected
            if ($null -eq $exact) {
                throw "Routed revision '$selected' disappeared while reading its exact template."
            }

            $revisions[$selected] = $exact
        }
    }

    return [ordered]@{
        AppName = Get-AppName $AppResourceId
        AppId = $AppResourceId
        Fqdn = $fqdn
        EnvDomain = $fqdn.Substring($fqdn.IndexOf('.') + 1)
        Mode = Get-StringField (Get-Field $properties 'configuration') 'activeRevisionsMode'
        LatestRevisionName = Get-StringField $properties 'latestRevisionName'
        Traffic = $traffic.ToArray()
        Revisions = $revisions
    }
}


function Wait-RevisionReady {
    param([string]$AppResourceId, [string]$Name)

    for ($attempt = 1; $attempt -le $script:PollAttempts; $attempt++) {
        $revision = Get-ObservedRevision -AppResourceId $AppResourceId -Name $Name
        if ($null -ne $revision) {
            if (Test-RevisionFailed $revision) {
                throw "Revision '$Name' is $($revision.ProvisioningState) and not ready; it is preserved for diagnosis."
            }

            if (Test-RevisionReady $revision) {
                return $revision
            }
        }

        if ($attempt -lt $script:PollAttempts) {
            Start-Sleep -Seconds $script:PollDelaySeconds
        }
    }

    throw "Revision '$Name' is not ready after $($script:PollAttempts) observations."
}

function Wait-CreatedRevision {
    param([string]$AppResourceId, [string]$Name)

    for ($attempt = 1; $attempt -le $script:PollAttempts; $attempt++) {
        $revision = Get-ObservedRevision -AppResourceId $AppResourceId -Name $Name
        if ($null -ne $revision) {
            return $revision
        }

        if ($attempt -lt $script:PollAttempts) {
            Start-Sleep -Seconds $script:PollDelaySeconds
        }
    }

    throw "Revision '$Name' was not created after $($script:PollAttempts) observations and is not ready."
}


function Set-RevisionTraffic {
    param(
        [string]$AppResourceId,
        [System.Collections.IDictionary]$State,
        [string]$Routed,
        [string[]]$RemoveLabels = @(),
        [object[]]$AddLabels = @())

    $traffic = New-TrafficList -State $State -RoutedRevision $Routed -RemoveLabels $RemoveLabels -AddLabels $AddLabels
    $body = @{ properties = @{ configuration = @{ ingress = @{ traffic = @($traffic) } } } }
    Invoke-ArmRequest -Method 'PATCH' -Path $AppResourceId -Body $body | Out-Null
    return , $traffic
}

# Configuration writes can be accepted before they are readable; wait for the written state and never repeat the write.
function Wait-StepVisible {
    param([string]$AppResourceId, [System.Collections.IDictionary]$Step, [object[]]$Traffic)

    $expected = Get-TrafficSignature $Traffic
    for ($attempt = 0; $attempt -lt $script:PollAttempts; $attempt++) {
        if ($attempt -gt 0) {
            Start-Sleep -Seconds $script:PollDelaySeconds
        }

        $state = Get-ObservedState -AppResourceId $AppResourceId
        $visible = switch -CaseSensitive ($Step.Action) {
            'SetMultiple' { $state.Mode -ieq 'Multiple' -and (Get-TrafficSignature $state.Traffic) -ceq $expected }
            'Deactivate' { -not $state.Revisions.Contains($Step.Target) -or -not $state.Revisions[$Step.Target].Active }
            default { (Get-TrafficSignature $state.Traffic) -ceq $expected }
        }

        if ($visible) {
            return
        }
    }

    throw "Revision step '$($Step.Action)|$($Step.Target)' was accepted but not visible after $($script:PollAttempts) reads; it was not repeated."
}

function Invoke-RevisionStep {
    param([string]$AppResourceId, [System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Step)

    switch -CaseSensitive ($Step.Action) {
        'SetMultiple' {
            $traffic = New-TrafficList -State $State -RoutedRevision $Step.Routed
            $body = @{ properties = @{ configuration = @{ activeRevisionsMode = 'Multiple'; ingress = @{ traffic = @($traffic) } } } }
            Invoke-ArmRequest -Method 'PATCH' -Path $AppResourceId -Body $body | Out-Null
            Wait-StepVisible -AppResourceId $AppResourceId -Step $Step -Traffic $traffic
        }
        'PinTraffic' {
            $traffic = Set-RevisionTraffic -AppResourceId $AppResourceId -State $State -Routed $Step.Target
            Wait-StepVisible -AppResourceId $AppResourceId -Step $Step -Traffic $traffic
        }
        'AddLabel' {
            $traffic = Set-RevisionTraffic -AppResourceId $AppResourceId -State $State -Routed $Step.Routed -AddLabels @($Step.Identity)
            Wait-StepVisible -AppResourceId $AppResourceId -Step $Step -Traffic $traffic
        }
        'RemoveLabels' {
            $traffic = Set-RevisionTraffic -AppResourceId $AppResourceId -State $State -Routed $Step.Routed -RemoveLabels $Step.Labels
            Wait-StepVisible -AppResourceId $AppResourceId -Step $Step -Traffic $traffic
        }
        'CopyTemplate' {
            Invoke-ArmRequest -Method 'PATCH' -Path $AppResourceId -Body @{ properties = @{ template = $Step.Template } } | Out-Null
            $created = Wait-CreatedRevision -AppResourceId $AppResourceId -Name $Step.Target
            $actual = Get-TemplateFingerprint $created.Template
            if ($actual -cne $Step.Fingerprint) {
                throw "Revision '$($Step.Target)' drift: the created template fingerprint $actual does not match the declared $($Step.Fingerprint); the revision is preserved for diagnosis."
            }

            Wait-RevisionReady -AppResourceId $AppResourceId -Name $Step.Target | Out-Null
        }
        'AwaitReady' {
            Wait-RevisionReady -AppResourceId $AppResourceId -Name $Step.Target | Out-Null
        }
        'Probe' {
            $healthy = $false
            for ($attempt = 0; $attempt -lt $script:PollAttempts -and -not $healthy; $attempt++) {
                if ($attempt -gt 0) {
                    Start-Sleep -Seconds $script:PollDelaySeconds
                }

                $healthy = [bool](Invoke-RevisionHealthProbe -Url $Step.Url)
            }

            if (-not $healthy) {
                throw "Revision '$($Step.Target)' is not ready: its health probe did not pass after $($script:PollAttempts) attempts; it is preserved for diagnosis."
            }
        }
        'Deactivate' {
            Invoke-ArmRequest -Method 'POST' -Path "$AppResourceId/revisions/$($Step.Target)/deactivate" -Body $null | Out-Null
            Wait-StepVisible -AppResourceId $AppResourceId -Step $Step -Traffic @()
        }
        default {
            throw "Unknown revision action '$($Step.Action)'."
        }
    }
}

function Invoke-RevisionController {
    param([string]$AppResourceId, [System.Collections.IDictionary]$Request)

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $probed = [System.Collections.Generic.List[string]]::new()
    $retired = [System.Collections.Generic.List[string]]::new()
    $warned = $false
    $expectedBaseline = $null
    while ($true) {
        $state = Get-ObservedState -AppResourceId $AppResourceId
        $routing = Get-RoutingDecision -State $state
        if ($routing.Kind -cne 'Conflict') {
            $observedBaseline = [ordered]@{
                Revision = $routing.Revision
                Fingerprint = Get-TemplateFingerprint $state.Revisions[$routing.Revision].Template
                Mode = $state.Mode
            }
            if ($null -eq $expectedBaseline) {
                $expectedBaseline = $observedBaseline
            }
            elseif ($observedBaseline.Revision -cne $expectedBaseline.Revision -or
                $observedBaseline.Fingerprint -cne $expectedBaseline.Fingerprint -or
                $observedBaseline.Mode -ine $expectedBaseline.Mode) {
                throw "Conflict on '$($state.AppName)': the default binding for '$($expectedBaseline.Revision)' changed during this controller attempt; observed '$($observedBaseline.Revision)' is preserved."
            }
        }

        $active = @($state.Revisions.Values | Where-Object { $_.Active }).Count
        if ($active -gt 10 -and -not $warned) {
            Write-Warning "Container app '$($state.AppName)' has $active active revisions."
            $warned = $true
        }

        $step = Get-RevisionPlan -State $state -Request $Request -Probed $probed.ToArray()
        switch -CaseSensitive ($step.Action) {
            'Done' { return @{ State = $state; Retired = $retired.ToArray() } }
            'Conflict' { throw "Conflict on '$($state.AppName)': $($step.Reason)" }
            'Blocked' { throw $step.Reason }
        }

        $key = '{0}|{1}' -f $step.Action, $step.Target
        if (-not $seen.Add($key)) {
            throw "Revision controller made no progress on '$key' for '$($state.AppName)'."
        }

        Write-Host "Revision step $key"
        Invoke-RevisionStep -AppResourceId $AppResourceId -State $state -Step $step
        if ($step.Action -ceq 'SetMultiple') {
            $expectedBaseline.Mode = 'Multiple'
        }
        elseif ($step.Action -ceq 'PinTraffic') {
            $expectedBaseline.Revision = $step.Target
            $expectedBaseline.Fingerprint = Get-TemplateFingerprint $state.Revisions[$step.Target].Template
        }

        if ($step.Action -ceq 'Probe') {
            $probed.Add($step.Target)
        }
        elseif ($step.Action -ceq 'Deactivate') {
            $retired.Add($step.Target)
        }
    }
}


function Invoke-RevisionGoal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Converge', 'Resolve', 'Settle')][string]$Goal,
        [Parameter(Mandatory)][string]$AppResourceId,
        [Parameter(Mandatory)][securestring]$AccessToken,
        [object]$Catalog,
        [string]$BuildId,
        [string]$Attempt,
        [string]$Image,
        [System.Collections.IDictionary]$Environment,
        [string[]]$RemoveEnvironment = @(),
        [int]$MinReplicas,
        [string]$RevisionProfile,
        [System.Collections.IDictionary]$Binding,
        [switch]$Expect)

    if ($AppResourceId -notmatch '^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.App/containerApps/[^/]+$') {
        throw "AppResourceId '$AppResourceId' is not a container app resource ID."
    }

    $Goal = @('Converge', 'Resolve', 'Settle') | Where-Object { $_ -ieq $Goal } | Select-Object -First 1
    $script:AccessToken = $AccessToken
    if ($null -eq $Catalog) {
        $Catalog = Get-RevisionCatalog
    }

    if ($Goal -ceq 'Resolve') {
        if ([string]::IsNullOrEmpty($RevisionProfile)) {
            throw 'Resolve requires -RevisionProfile.'
        }

        $state = Get-ObservedState -AppResourceId $AppResourceId
        if ($null -ne $Binding -and $RevisionProfile -cne $Catalog.Baseline.Name) {
            $selected = Get-StringField $Binding 'Revision'
            if (-not [string]::IsNullOrEmpty($selected) -and $state.Revisions.Contains($selected)) {
                $state.Revisions[$selected] = Get-ObservedRevision -AppResourceId $AppResourceId -Name $selected
            }
        }

        return Resolve-RevisionSelection -State $state -Catalog $Catalog -RevisionProfile $RevisionProfile -Binding $Binding -Expect:$Expect
    }

    Assert-BuildIdentity -BuildId $BuildId -Attempt $Attempt
    $request = @{
        Kind = 'Profiles'
        Catalog = $Catalog
        BuildId = $BuildId
        Attempt = $Attempt
        Environment = [ordered]@{}
        RemoveEnvironment = $RemoveEnvironment
        MinReplicas = 0
    }

    if ($Goal -ceq 'Settle') {
        if (-not [string]::IsNullOrEmpty($Image)) {
            throw 'Settle keeps the routed image; it does not accept -Image.'
        }

        $request['Kind'] = 'Settle'
        $outcome = Invoke-RevisionController -AppResourceId $AppResourceId -Request $request
        return [ordered]@{
            Goal = 'Settle'
            BaselineRevision = [string](Get-RoutingDecision -State $outcome.State).Revision
            Retired = $outcome.Retired
        }
    }

    if (-not [string]::IsNullOrEmpty($Image)) {
        if (-not $PSBoundParameters.ContainsKey('MinReplicas')) {
            throw 'Converge -Image requires an explicit -MinReplicas.'
        }

        $request['Kind'] = 'Deploy'
        $request['Image'] = $Image
        $request['MinReplicas'] = $MinReplicas
        if ($null -ne $Environment) {
            $request['Environment'] = $Environment
        }
    }
    elseif ($null -ne $Environment -or $RemoveEnvironment.Count -gt 0 -or $PSBoundParameters.ContainsKey('MinReplicas')) {
        throw 'Profile Converge takes its environment and scale from the catalog; pass -Image to deploy the baseline.'
    }

    $outcome = Invoke-RevisionController -AppResourceId $AppResourceId -Request $request
    return Get-ConvergeResult -Request $request -State $outcome.State
}


Export-ModuleMember -Function Get-RevisionCatalog, Get-RevisionPlan, Invoke-RevisionGoal
