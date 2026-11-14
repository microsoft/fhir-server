function Copy-Deep {
    param($Value)
    if ($null -eq $Value) {
        return $null
    }

    return , (ConvertTo-Json -InputObject $Value -Depth 100 | ConvertFrom-Json -AsHashtable -NoEnumerate)
}

function ConvertTo-CanonicalValue {
    param($Value, [string]$Key)
    if ($Value -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($name in @($Value.Keys | Sort-Object)) {
            $ordered[$name] = ConvertTo-CanonicalValue -Value $Value[$name] -Key $name
        }

        return $ordered
    }

    if ($Value -is [System.Collections.IList]) {
        $items = @(foreach ($item in $Value) { , (ConvertTo-CanonicalValue -Value $item -Key '') })
        if ($Key -eq 'env') {
            $items = @($items | Sort-Object { $_['name'] })
        }

        return , $items
        return
    }

    return $Value
}

function ConvertTo-CanonicalJson {
    param($Value)
    ConvertTo-Json -InputObject (ConvertTo-CanonicalValue -Value $Value -Key '') -Depth 100 -Compress
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERT FAILED: $Message"
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw "ASSERT FAILED: $Message. Expected '$Expected' but got '$Actual'."
    }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)
    $failure = $null
    try {
        & $Action | Out-Null
    }
    catch {
        $failure = $_.Exception.Message
    }

    if ($null -eq $failure) {
        throw "ASSERT FAILED: $Message. Expected an error matching '$Pattern'."
    }

    if ($failure -notmatch $Pattern) {
        throw "ASSERT FAILED: $Message. Expected an error matching '$Pattern' but got '$failure'."
    }
}

function Invoke-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failures.Add($Name)
        Write-Host "FAIL $Name :: $($_.Exception.Message)"
    }
}

function New-FixtureTemplate {
    param(
        [string]$Suffix,
        [string]$Image = $baseImage,
        [System.Collections.IDictionary]$Environment = @{},
        [int]$MinReplicas = 0,
        [int]$MaxReplicas = 8,
        $TerminationGracePeriodSeconds = $null,
        [string]$AppName = 'fx-sql'
    )

    $variables = [ordered]@{
        APPLICATIONINSIGHTS_CONNECTION_STRING = 'InstrumentationKey=fixture-secret-ai'
        ASPNETCORE_FORWARDEDHEADERS_ENABLED = 'true'
        FhirServer__Operations__ConvertData__ContainerRegistryServers__0 = 'fxacr.azurecr.io'
        FhirServer__Operations__ConvertData__Enabled = 'true'
        FhirServer__Operations__Export__Enabled = 'true'
        FhirServer__Operations__Export__StorageAccountUri = 'https://fxstorage.blob.core.windows.net'
        FhirServer__Operations__Import__Enabled = 'true'
        FhirServer__Operations__IntegrationDataStore__StorageAccountUri = 'https://fxstorage.blob.core.windows.net'
        FhirServer__Operations__Reindex__Enabled = 'true'
        FhirServer__Security__Authentication__Audience = 'https://fx-sql.example.test'
        FhirServer__Security__Authentication__Authority = 'https://login.example.test/tenant'
        FhirServer__Security__EnableAadSmartOnFhirProxy = 'true'
        FhirServer__Security__Enabled = 'true'
        KeyVault__Endpoint = 'https://fxvault.vault.azure.net/'
        TaskHosting__Enabled = 'true'
        TaskHosting__MaxRunningTaskCount = '2'
        Telemetry__ConnectionString = 'InstrumentationKey=fixture-secret-ai'
        Telemetry__Provider = 'ApplicationInsights'
        DataStore = 'SqlServer'
        SqlServer__Initialize = 'true'
        SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'true'
        SqlServer__DeleteAllDataOnStartup = 'false'
        SqlServer__AllowDatabaseCreation = 'true'
    }
    foreach ($name in @($Environment.Keys | Where-Object { $variables.Contains($_) })) {
        $variables[$name] = $Environment[$name]
    }

    foreach ($name in @($Environment.Keys | Where-Object { -not $variables.Contains($_) } | Sort-Object)) {
        $variables[$name] = $Environment[$name]
    }

    $envList = @(foreach ($name in $variables.Keys) {
            if ($null -ne $variables[$name]) {
                @{ name = $name; value = $variables[$name] }
            }
        })
    $probe = { param($Type) @{ type = $Type; httpGet = @{ path = '/health/check'; port = 8080; scheme = 'HTTP' }; initialDelaySeconds = 20; periodSeconds = 10; timeoutSeconds = 5; failureThreshold = 6 } }

    @{
        revisionSuffix = $Suffix
        terminationGracePeriodSeconds = $TerminationGracePeriodSeconds
        initContainers = $null
        containers = @(@{
                name = $AppName
                image = $Image
                env = $envList
                resources = @{ cpu = 0.5; memory = '1Gi'; ephemeralStorage = '2Gi' }
                probes = @((& $probe 'Liveness'), (& $probe 'Readiness'))
            })
        scale = @{
            minReplicas = $MinReplicas
            maxReplicas = $MaxReplicas
            rules = @(
                @{ name = 'http-scaling'; http = @{ metadata = @{ concurrentRequests = '50' } } },
                @{ name = 'cpu-scaling'; custom = @{ type = 'cpu'; metadata = @{ type = 'Utilization'; value = '70' } } })
        }
        volumes = $null
        serviceBinds = $null
    }
}

function Get-TemplateEnvironmentValue {
    param($Template, [string]$Name)
    $container = @($Template['containers'])[0]
    $entries = @($container['env'] | Where-Object { $_['name'] -ceq $Name })
    if ($entries.Count -eq 0) {
        return $null
    }

    return $entries[0]['value']
}

function New-FakeRevision {
    param([string]$Name, $Template, [bool]$Active = $true, [string]$ProvisioningState = 'Provisioned', [string]$HealthState = 'Healthy', [object[]]$Pending = @())
    $queue = [System.Collections.Generic.List[object]]::new()
    foreach ($step in $Pending) {
        $queue.Add($step)
    }

    @{ Name = $Name; Active = $Active; ProvisioningState = $ProvisioningState; HealthState = $HealthState; Template = $Template; Pending = $queue }
}

function New-Fake {
    param([string]$AppName = 'fx-sql', [string]$Mode = 'Single')
    $initialName = "$AppName--initial"
    $template = New-FixtureTemplate -Suffix 'initial' -AppName $AppName
    $fake = @{
        AppName = $AppName
        AppId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/fx-rg/providers/Microsoft.App/containerApps/$AppName"
        Mode = $Mode
        ProvisioningState = 'Succeeded'
        Fqdn = "$AppName.$envDomain"
        Traffic = [System.Collections.Generic.List[object]]::new()
        Revisions = [ordered]@{}
        LatestRevisionName = $initialName
        AppTemplate = Copy-Deep $template
        Requests = [System.Collections.Generic.List[object]]::new()
        Probes = [System.Collections.Generic.List[string]]::new()
        ProbeResult = $true
        CreatedPending = @(@{ ProvisioningState = 'Provisioning'; HealthState = 'None' }, @{ ProvisioningState = 'Provisioned'; HealthState = 'Healthy' })
        MutateCreated = $null
        AfterTemplatePatch = $null
        AfterProbe = $null
        VisibilityDelay = 0
        Deferred = [System.Collections.Generic.List[object]]::new()
    }
    $fake.Revisions[$initialName] = New-FakeRevision -Name $initialName -Template $template
    if ($Mode -eq 'Single') {
        $fake.Traffic.Add(@{ latestRevision = $true; weight = 100 })
    }
    else {
        $fake.Traffic.Add(@{ revisionName = $initialName; weight = 100 })
    }

    $script:Fake = $fake
}

function Add-FakeRevision {
    param([string]$Suffix, $Template, [bool]$Active = $true, [string]$ProvisioningState = 'Provisioned', [string]$HealthState = 'Healthy', [switch]$Latest)
    $name = "$($script:Fake.AppName)--$Suffix"
    $script:Fake.Revisions[$name] = New-FakeRevision -Name $name -Template $Template -Active $Active -ProvisioningState $ProvisioningState -HealthState $HealthState
    if ($Latest) {
        $script:Fake.LatestRevisionName = $name
        $script:Fake.AppTemplate = Copy-Deep $Template
    }

    return $name
}

function Set-FakeTraffic {
    param([object[]]$Entries)
    $script:Fake.Traffic = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $Entries) {
        $script:Fake.Traffic.Add($entry)
    }
}

function ConvertTo-FakeRevisionResource {
    param($Revision)
    if ($Revision.Pending.Count -gt 0) {
        $step = $Revision.Pending[0]
        $Revision.Pending.RemoveAt(0)
        $Revision.ProvisioningState = $step['ProvisioningState']
        $Revision.HealthState = $step['HealthState']
    }

    Copy-Deep @{
        id = "$($script:Fake.AppId)/revisions/$($Revision.Name)"
        name = $Revision.Name
        properties = @{
            active = $Revision.Active
            provisioningState = $Revision.ProvisioningState
            healthState = $Revision.HealthState
            fqdn = "$($Revision.Name).$envDomain"
            template = $Revision.Template
        }
    }
}

function Assert-FakeTraffic {
    param([object[]]$Traffic)
    $weight = 0
    $labels = @{}
    foreach ($entry in $Traffic) {
        if ($entry.Contains('latestRevision') -and $entry['latestRevision']) {
            throw 'Fake ARM rejects default routing to the latest revision after adoption.'
        }

        $revision = $script:Fake.Revisions[$entry['revisionName']]
        if ($null -eq $revision -or -not $revision.Active) {
            throw "Fake ARM traffic names missing or inactive revision '$($entry['revisionName'])'."
        }

        if ($entry.Contains('label')) {
            if ($labels.ContainsKey($entry['label'])) {
                throw "Fake ARM label '$($entry['label'])' is duplicated."
            }

            $labels[$entry['label']] = $true
        }

        $weight += [int]$entry['weight']
    }

    if ($weight -ne 100) {
        throw "Fake ARM traffic weights total $weight."
    }
}

function Invoke-FakeTemplatePatch {
    param($Template)
    $fake = $script:Fake
    $suffix = $Template['revisionSuffix']
    if ([string]::IsNullOrEmpty($suffix)) {
        throw 'Fake ARM requires a revisionSuffix.'
    }

    $name = "$($fake.AppName)--$suffix"
    if ($fake.Revisions.Contains($name)) {
        throw "Fake ARM revision '$name' already exists."
    }

    $stored = Copy-Deep $Template
    $container = @($stored['containers'])[0]
    $environment = @($container['env'])
    [array]::Reverse($environment)
    $container['env'] = $environment
    if ($null -ne $fake.MutateCreated) {
        & $fake.MutateCreated $stored
    }

    $fake.Revisions[$name] = New-FakeRevision -Name $name -Template $stored -ProvisioningState 'Provisioning' -HealthState 'None' -Pending (Copy-Deep $fake.CreatedPending)
    $fake.LatestRevisionName = $name
    $fake.AppTemplate = Copy-Deep $Template
    $fake.ProvisioningState = 'Succeeded'
    if ($fake.Mode -eq 'Single') {
        foreach ($other in @($fake.Revisions.Values | Where-Object { $_.Name -ne $name })) {
            $other.Active = $false
        }
    }

    if ($null -ne $fake.AfterTemplatePatch) {
        & $fake.AfterTemplatePatch $name
    }
}

function Invoke-FakeWrite {
    param([string]$Kind, $Value)
    switch ($Kind) {
        'Mode' { $script:Fake.Mode = $Value }
        'Traffic' { Set-FakeTraffic -Entries $Value }
        'Deactivate' { $script:Fake.Revisions[$Value].Active = $false }
    }
}

function Submit-FakeWrite {
    param([string]$Kind, $Value)
    if ($script:Fake.VisibilityDelay -gt 0) {
        $script:Fake.Deferred.Add(@{ Kind = $Kind; Value = $Value; Remaining = $script:Fake.VisibilityDelay })
    }
    else {
        Invoke-FakeWrite -Kind $Kind -Value $Value
    }
}

function Step-FakeDeferred {
    $deferred = $script:Fake.Deferred
    foreach ($item in $deferred) {
        $item['Remaining'] = $item['Remaining'] - 1
    }

    while ($deferred.Count -gt 0 -and $deferred[0]['Remaining'] -le 0) {
        $item = $deferred[0]
        $deferred.RemoveAt(0)
        Invoke-FakeWrite -Kind $item['Kind'] -Value $item['Value']
    }
}

function Invoke-FakeArm {
    param([string]$Method, [string]$Path, $Body)
    $fake = $script:Fake
    $fake.Requests.Add(@{ Method = $Method; Path = $Path; Body = (Copy-Deep $Body) })
    if ($Method -eq 'GET') {
        Step-FakeDeferred
    }

    $appId = $fake.AppId
    $revisionPrefix = "$appId/revisions/"
    if ($Method -eq 'GET' -and $Path -eq $appId) {
        return Copy-Deep @{
            id = $appId
            name = $fake.AppName
            properties = @{
                provisioningState = $fake.ProvisioningState
                latestRevisionName = $fake.LatestRevisionName
                configuration = @{
                    activeRevisionsMode = $fake.Mode
                    secrets = @(@{ name = 'fixture-secret-name' })
                    ingress = @{ external = $true; targetPort = 8080; fqdn = $fake.Fqdn; traffic = @($fake.Traffic) }
                }
                template = $fake.AppTemplate
            }
        }
    }

    if ($Method -eq 'GET' -and $Path -eq "$appId/revisions") {
        return @{ value = @(foreach ($revision in @($fake.Revisions.Values)) { ConvertTo-FakeRevisionResource $revision }) }
    }

    if ($Method -eq 'GET' -and $Path.StartsWith($revisionPrefix, [StringComparison]::Ordinal)) {
        $revision = $fake.Revisions[$Path.Substring($revisionPrefix.Length)]
        if ($null -eq $revision) {
            return $null
        }

        return ConvertTo-FakeRevisionResource $revision
    }

    if ($Method -eq 'POST' -and $Path.StartsWith($revisionPrefix, [StringComparison]::Ordinal) -and $Path.EndsWith('/deactivate', [StringComparison]::Ordinal)) {
        $name = $Path.Substring($revisionPrefix.Length, $Path.Length - $revisionPrefix.Length - '/deactivate'.Length)
        if (@($fake.Traffic | Where-Object { $_['revisionName'] -eq $name }).Count -gt 0) {
            throw "Fake ARM refuses to deactivate routed or labeled revision '$name'."
        }

        Submit-FakeWrite -Kind 'Deactivate' -Value $name
        return $null
    }

    if ($Method -eq 'PATCH' -and $Path -eq $appId) {
        $properties = $Body['properties']
        if ($Body.Keys.Count -ne 1 -or $properties.Keys.Count -ne 1) {
            throw 'Fake ARM accepts only one properties section per PATCH.'
        }

        if ($properties.Contains('template')) {
            Invoke-FakeTemplatePatch -Template $properties['template']
            return $null
        }

        $configuration = $properties['configuration']
        if ($configuration.Contains('activeRevisionsMode')) {
            Submit-FakeWrite -Kind 'Mode' -Value $configuration['activeRevisionsMode']
        }

        if ($configuration.Contains('ingress')) {
            $traffic = Copy-Deep $configuration['ingress']['traffic']
            if ($fake.Mode -eq 'Multiple') {
                Assert-FakeTraffic -Traffic $traffic
            }

            Submit-FakeWrite -Kind 'Traffic' -Value $traffic
        }

        return $null
    }

    throw "Fake ARM does not support $Method $Path."
}

$fakeArm = { param($Method, $Path, $Body) Invoke-FakeArm -Method $Method -Path $Path -Body $Body }
$fakeProbe = {
    param($Url)
    $script:Fake.Probes.Add($Url)
    if ($null -ne $script:Fake.AfterProbe) {
        & $script:Fake.AfterProbe $Url
    }
    $script:Fake.ProbeResult
}
& $module {
    param($Arm, $Probe)
    $script:FixtureArm = $Arm
    $script:FixtureProbe = $Probe
    $script:PollDelaySeconds = 0
    function script:Invoke-ArmRequest { param($Method, $Path, $Body) & $script:FixtureArm $Method $Path $Body }
    function script:Invoke-RevisionHealthProbe { param($Url) & $script:FixtureProbe $Url }
} $fakeArm $fakeProbe

function Get-FakeWrites {
    @($script:Fake.Requests | Where-Object { $_['Method'] -ne 'GET' })
}

function Get-TemplateWrites {
    @(Get-FakeWrites | Where-Object { $_['Method'] -eq 'PATCH' -and $_['Body']['properties'].Contains('template') })
}

function Get-TrafficWrites {
    @(Get-FakeWrites | Where-Object { $_['Method'] -eq 'PATCH' -and $_['Body']['properties'].Contains('configuration') })
}

function Get-FakeLabelTarget {
    param([string]$Label)
    $entries = @($script:Fake.Traffic | Where-Object { $_['label'] -ceq $Label })
    if ($entries.Count -eq 0) {
        return $null
    }

    return $entries[0]['revisionName']
}

function Get-RoutedRevision {
    $entries = @($script:Fake.Traffic | Where-Object { [int]$_['weight'] -gt 0 })
    Assert-Equal 1 $entries.Count 'exactly one weighted route remains'
    Assert-Equal 100 ([int]$entries[0]['weight']) 'the route carries all default traffic'
    Assert-True (-not ($entries[0].Contains('latestRevision') -and $entries[0]['latestRevision'])) 'default traffic is pinned by revision name'
    return $entries[0]['revisionName']
}

function Invoke-Goal {
    param([hashtable]$Arguments)
    $Arguments['AppResourceId'] = $script:Fake.AppId
    $Arguments['AccessToken'] = $token
    if (-not $Arguments.ContainsKey('Catalog')) {
        $Arguments['Catalog'] = $script:Catalog
    }

    Invoke-RevisionGoal @Arguments
}

function Assert-NoWrites {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)
    $before = @(Get-FakeWrites).Count
    Assert-Throws -Action $Action -Pattern $Pattern -Message $Message
    Assert-Equal $before @(Get-FakeWrites).Count "$Message without any ARM write"
}

function New-CatalogFile {
    param([string]$Name, [string]$Content)
    $path = Join-Path $tempRoot "$Name.psd1"
    Set-Content -LiteralPath $path -Value $Content -Encoding utf8
    return $path
}

function New-ProfileText {
    param([string]$Name, [string]$Slug, [string]$ResourceIdProfile = 'None', [string]$Environment = "FhirServer__Features__$Name = 'true'", [string]$Extra = '')
    "@{ Name = '$Name'; Slug = '$Slug'; CategoryFilter = 'Category=ResourceIdBoundary'; ResourceIdProfile = '$ResourceIdProfile'; Environment = @{ $Environment }$Extra }"
}

function New-CatalogText {
    param([string[]]$Profiles)
    "@{ Baseline = @{ Name = 'Standard64'; ResourceIdProfile = 'Standard64' }; Profiles = @(`n$($Profiles -join ",`n")`n) }"
}

function Get-ExpectedTemplate {
    param($Source, [string]$Suffix, [System.Collections.IDictionary]$Environment = @{}, [string[]]$RemoveEnvironment = @(), [string]$Image, $MinReplicas)
    $expected = Copy-Deep $Source
    $expected['revisionSuffix'] = $Suffix
    $container = @($expected['containers'])[0]
    if ($Image) {
        $container['image'] = $Image
    }

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in @($container['env'])) {
        if ($RemoveEnvironment -notcontains $entry['name'] -and -not $Environment.Contains($entry['name'])) {
            $entries.Add($entry)
        }
    }

    foreach ($name in $Environment.Keys) {
        $entries.Add(@{ name = $name; value = $Environment[$name] })
    }

    $container['env'] = $entries.ToArray()
    if ($null -ne $MinReplicas) {
        $expected['scale']['minReplicas'] = $MinReplicas
    }

    return , $expected
}

function Get-RevisionTemplate {
    param([string]$Name)
    $revision = $script:Fake.Revisions[$Name]
    Assert-True ($null -ne $revision) "revision '$Name' exists"
    return , $revision.Template
}
