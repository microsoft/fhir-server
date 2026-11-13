$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot 'RevisionProfiles.psm1'
$catalogPath = Join-Path $PSScriptRoot 'RevisionProfiles.psd1'
$entryPath = Join-Path $PSScriptRoot 'Sync-SqlRevisions.ps1'
foreach ($requiredPath in @($modulePath, $catalogPath, $entryPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required revision profile file '$requiredPath' was not found."
    }
}

Import-Module $modulePath -Force
$module = Get-Module RevisionProfiles
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('revision-profiles-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

$token = ConvertTo-SecureString 'fixture-token' -AsPlainText -Force
$envDomain = 'happy-sky.eastus.azurecontainerapps.io'
$baseImage = 'fxacr.azurecr.io/r4_fhir-server:1234@sha256:' + ('a' * 64)
$longEnvironment = [ordered]@{
    FhirServer__CoreFeatures__UseLongResourceIds = 'true'
    TaskHosting__Enabled = 'false'
    SqlServer__AllowDatabaseCreation = 'false'
    SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'false'
    SqlServer__Initialize = 'true'
    SqlServer__DeleteAllDataOnStartup = 'false'
}
$script:Fake = $null
$script:Catalog = $null

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

$script:Failures = [System.Collections.Generic.List[string]]::new()

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


try {
    $script:Catalog = Get-RevisionCatalog -Path $catalogPath
    $longName = 'fx-sql--long128-b4242a1'
    $longLabel = 'long128-b4242a1'
    $converge = @{ Goal = 'Converge'; BuildId = '4242'; Attempt = '1' }

    Invoke-Case 'shipped catalog contains exactly the Long128 deployment profile' {
        $profiles = @($script:Catalog.Profiles)
        Assert-Equal 1 $profiles.Count 'one shipped profile'
        Assert-Equal 'Standard64' $script:Catalog.Baseline.Name 'baseline name'
        Assert-Equal 'Standard64' $script:Catalog.Baseline.ResourceIdProfile 'baseline resource ID profile'
        Assert-Equal 'Long128' $profiles[0].Name 'profile name'
        Assert-Equal 'long128' $profiles[0].Slug 'profile slug'
        Assert-Equal 'Category=ResourceIdBoundary' $profiles[0].CategoryFilter 'profile category filter'
        Assert-Equal 'Long128' $profiles[0].ResourceIdProfile 'profile resource ID contract'
        Assert-Equal (ConvertTo-CanonicalJson $longEnvironment) (ConvertTo-CanonicalJson $profiles[0].Environment) 'profile environment'
    }

    Invoke-Case 'catalog validation rejects unsafe or ambiguous descriptors' {
        $invalid = [ordered]@{
            'duplicate-name' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha'), (New-ProfileText 'Alpha' 'beta'))
            'duplicate-slug' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha'), (New-ProfileText 'Beta' 'alpha'))
            'reserved-base-slug' = New-CatalogText @((New-ProfileText 'Alpha' 'base'))
            'reserved-baseline-name' = New-CatalogText @((New-ProfileText 'Standard64' 'std'))
            'unsupported-field' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Extra "; Weight = '10'"))
            'missing-field' = "@{ Baseline = @{ Name = 'Standard64'; ResourceIdProfile = 'Standard64' }; Profiles = @(@{ Name = 'Alpha'; Slug = 'alpha'; ResourceIdProfile = 'None'; Environment = @{ A = 'b' } }) }"
            'non-string-value' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Environment 'FhirServer__Features__Alpha = 1'))
            'secret-reference' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Environment "FhirServer__Features__Alpha = @{ secretRef = 'x' }"))
            'empty-environment' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Environment ''))
            'bad-slug' = New-CatalogText @((New-ProfileText 'Alpha' 'Alpha-1'))
            'long-slug' = New-CatalogText @((New-ProfileText 'Alpha' 'abcdefghi'))
            'bad-name' = New-CatalogText @((New-ProfileText 'alpha profile' 'alpha' -Environment "FhirServer__Features__Alpha = 'true'"))
            'bad-resource-id-profile' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -ResourceIdProfile 'Huge'))
            'bad-environment-name' = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Environment "'Bad-Name' = 'x'"))
            'bad-baseline' = "@{ Baseline = @{ Name = 'Other'; ResourceIdProfile = 'Standard64' }; Profiles = @($(New-ProfileText 'Alpha' 'alpha')) }"
        }
        foreach ($key in @('SqlServer__ConnectionString', 'SqlServer__ConnectionStringSuffix', 'FhirServer__Security__Enabled', 'KeyVault__Endpoint', 'AZURE_CLIENT_ID', 'FhirServer__Operations__Export__StorageAccountUri', 'DataStore', 'ASPNETCORE_URLS', 'Telemetry__Provider', 'APPLICATIONINSIGHTS_CONNECTION_STRING')) {
            $invalid["forbidden-$key"] = New-CatalogText @((New-ProfileText 'Alpha' 'alpha' -Environment "$key = 'x'"))
        }

        foreach ($name in $invalid.Keys) {
            $path = New-CatalogFile -Name $name -Content $invalid[$name]
            Assert-Throws { Get-RevisionCatalog -Path $path } 'Revision catalog' "catalog '$name' is rejected"
        }
    }

    Invoke-Case 'single adoption pins the healthy baseline before copying a profile' {
        New-Fake -Mode Single
        $result = Invoke-Goal $converge.Clone()
        $writes = @(Get-FakeWrites)
        Assert-Equal 3 $writes.Count 'atomic adoption, copy and label writes'
        $first = $writes[0]['Body']['properties']['configuration']
        Assert-Equal 'Multiple' $first['activeRevisionsMode'] 'first write switches to Multiple'
        Assert-True $first.Contains('ingress') 'adoption pins traffic in the same mode-change request'
        Assert-True $writes[1]['Body']['properties'].Contains('template') 'second write copies the template'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'baseline keeps default traffic'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'profile label is bound'
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--initial') -Suffix $longLabel -Environment $longEnvironment -MinReplicas 0
        Assert-Equal (ConvertTo-CanonicalJson $expected) (ConvertTo-CanonicalJson $writes[1]['Body']['properties']['template']) 'copy body is the baseline template plus declared changes'
        $binding = @($result.Bindings)[0]
        Assert-Equal 'Long128' $binding.Profile 'binding profile'
        Assert-Equal 'Long128' $binding.ResourceIdProfile 'binding resource ID profile'
        Assert-Equal 'Category=ResourceIdBoundary' $binding.CategoryFilter 'binding category filter'
        Assert-Equal $longName $binding.Revision 'binding revision'
        Assert-Equal $longLabel $binding.Label 'binding label'
        Assert-Equal "https://fx-sql---$longLabel.$envDomain" $binding.Url 'binding label URL'
        Assert-Equal $baseImage $binding.Image 'binding image'
        Assert-Equal 'fx-sql--initial' $binding.BaselineRevision 'binding baseline'
        Assert-True ($binding.Fingerprint -match '^[0-9a-f]{64}$') 'binding fingerprint'
        Assert-Equal 'fx-sql--initial' $result.BaselineRevision 'baseline binding'
        $matrix = ConvertFrom-Json -InputObject $result.Matrix -AsHashtable
        Assert-Equal 1 $matrix.Count 'one matrix row'
        Assert-Equal 'Long128' $matrix['Long128']['SqlRevisionProfile'] 'matrix deployment profile'
        Assert-Equal 'Category=ResourceIdBoundary' $matrix['Long128']['SqlProfileCategoryFilter'] 'matrix category filter'
        Assert-Equal 'Long128' $matrix['Long128']['SqlResourceIdProfile'] 'matrix resource ID profile'
        $matrixBinding = ConvertFrom-Json -InputObject $matrix['Long128']['SqlRevisionBinding'] -AsHashtable
        Assert-Equal $longName $matrixBinding['Revision'] 'matrix carries the captured binding'
        Assert-Equal $binding.Fingerprint $matrixBinding['Fingerprint'] 'matrix carries the captured fingerprint'
    }

    Invoke-Case 'poisoned latest revision never reaches the copied profile template' {
        New-Fake -Mode Multiple
        $poison = New-FixtureTemplate -Suffix 'poison' -Image ('fxacr.azurecr.io/r4_fhir-server:poison@sha256:' + ('b' * 64)) -Environment @{ FhirServer__CoreFeatures__UseLongResourceIds = 'true'; TaskHosting__Enabled = 'false'; KeyVault__Endpoint = 'https://poisonvault.vault.azure.net/'; Poison__Marker = 'poison' } -MinReplicas 5 -MaxReplicas 20 -TerminationGracePeriodSeconds 99
        $poison['containers'][0]['command'] = @('/bin/poison')
        $poison['containers'][0]['resources'] = @{ cpu = 2.0; memory = '4Gi'; ephemeralStorage = '8Gi' }
        $poison['containers'][0]['probes'] = @(@{ type = 'Liveness'; httpGet = @{ path = '/poison'; port = 9999; scheme = 'HTTP' } })
        $poison['initContainers'] = @(@{ name = 'poison-init'; image = 'poison' })
        $poison['volumes'] = @(@{ name = 'poison-vol'; storageType = 'EmptyDir' })
        $poison['scale']['rules'] = @(@{ name = 'poison-rule'; custom = @{ type = 'cpu'; metadata = @{ type = 'Utilization'; value = '5' } } })
        $poisonName = Add-FakeRevision -Suffix 'poison' -Template $poison -Latest
        Invoke-Goal $converge.Clone() | Out-Null
        $copies = @(Get-TemplateWrites)
        Assert-Equal 1 $copies.Count 'one template write'
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--initial') -Suffix $longLabel -Environment $longEnvironment -MinReplicas 0
        Assert-Equal (ConvertTo-CanonicalJson $expected) (ConvertTo-CanonicalJson $copies[0]['Body']['properties']['template']) 'copy ignores the poisoned latest template'
        Assert-True ((ConvertTo-Json -InputObject $copies -Depth 100) -notmatch 'poison') 'no poisoned value is written'
        Assert-True $script:Fake.Revisions[$poisonName].Active 'the poisoned revision is not retired by Converge'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic never moves to latest'
    }

    Invoke-Case 'existing Multiple routing must name its Standard64 revision' {
        New-Fake -Mode Multiple
        Set-FakeTraffic @(@{ latestRevision = $true; weight = 100 })
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'an existing Multiple latest route is not adopted'
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64' } } 'Conflict' 'read-only selection refuses an existing Multiple latest route'

        New-Fake -Mode Multiple
        $script:Fake.Revisions['fx-sql--initial'].Template = New-FixtureTemplate -Suffix 'initial' -Environment $longEnvironment
        Set-FakeTraffic @(@{ latestRevision = $true; weight = 100 })
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'no Standard64 revision'

        New-Fake -Mode Multiple
        Add-FakeRevision -Suffix 'other' -Template (New-FixtureTemplate -Suffix 'other') | Out-Null
        Set-FakeTraffic @(@{ latestRevision = $true; weight = 100 })
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'two Standard64 revisions'

        New-Fake -Mode Multiple
        $other = Add-FakeRevision -Suffix 'other' -Template (New-FixtureTemplate -Suffix 'other')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 50 }, @{ revisionName = $other; weight = 50 })
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'split default traffic'

        New-Fake -Mode Single
        $script:Fake.Revisions['fx-sql--initial'].HealthState = 'Unhealthy'
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'unhealthy Single adoption'
    }

    Invoke-Case 'interrupted adoption resumes from observed state' {
        New-Fake -Mode Single
        $script:Fake.Mode = 'Multiple'
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 })
        Invoke-Goal $converge.Clone() | Out-Null
        $writes = @(Get-FakeWrites)
        Assert-Equal 2 $writes.Count 'copy and label writes after completed adoption'
        Assert-True $writes[0]['Body']['properties'].Contains('template') 'the completed adoption is not repeated'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'baseline is pinned'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'profile label is bound'
    }

    Invoke-Case 'foreign zero-weight labels survive every traffic write' {
        New-Fake -Mode Multiple
        $foreign = Add-FakeRevision -Suffix 'foreign' -Template (New-FixtureTemplate -Suffix 'foreign')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ revisionName = $foreign; weight = 0; label = 'blue' })
        Invoke-Goal $converge.Clone() | Out-Null
        foreach ($write in @(Get-TrafficWrites)) {
            $blue = @($write['Body']['properties']['configuration']['ingress']['traffic'] | Where-Object { $_['label'] -ceq 'blue' })
            Assert-Equal 1 $blue.Count 'every traffic write keeps the foreign label'
            Assert-Equal $foreign $blue[0]['revisionName'] 'foreign label target'
            Assert-Equal 0 ([int]$blue[0]['weight']) 'foreign label weight'
        }

        Assert-Equal $foreign (Get-FakeLabelTarget 'blue') 'foreign label remains bound'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'profile label is bound'
    }

    Invoke-Case 'conflicting labels and owned identities fail before any write' {
        New-Fake -Mode Multiple
        $foreign = Add-FakeRevision -Suffix 'foreign' -Template (New-FixtureTemplate -Suffix 'foreign')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ revisionName = $foreign; weight = 0; label = $longLabel })
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'profile label bound to another revision'

        New-Fake -Mode Multiple
        Add-FakeRevision -Suffix $longLabel -Template (New-FixtureTemplate -Suffix $longLabel -Environment @{ FhirServer__CoreFeatures__UseLongResourceIds = 'false' }) | Out-Null
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'owned revision with a different template'
    }

    Invoke-Case 'created template drift fails and is never rebound' {
        New-Fake -Mode Multiple
        $script:Fake.MutateCreated = { param($Template) $Template['containers'][0]['resources']['cpu'] = 1.0 }
        Assert-Throws { Invoke-Goal $converge.Clone() } 'drift' 'created revision differs from the requested template'
        Assert-Equal $null (Get-FakeLabelTarget $longLabel) 'drifted revision is not labeled'
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'the same attempt does not force-rebind the drifted revision'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic is unchanged'
    }

    Invoke-Case 'a same-attempt provisioning revision resumes without another copy' {
        New-Fake -Mode Multiple
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--initial') -Suffix $longLabel -Environment $longEnvironment -MinReplicas 0
        Add-FakeRevision -Suffix $longLabel -Template $expected -ProvisioningState 'Provisioning' -HealthState 'None' | Out-Null
        $script:Fake.Revisions[$longName].Pending.Add(@{ ProvisioningState = 'Provisioned'; HealthState = 'Healthy' })
        $result = Invoke-Goal $converge.Clone()
        Assert-Equal 0 @(Get-TemplateWrites).Count 'no second template write'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'label is added after readiness'
        Assert-Equal $longName @($result.Bindings)[0].Revision 'binding reuses the owned revision'
        $writes = @(Get-FakeWrites).Count
        $again = Invoke-Goal $converge.Clone()
        Assert-Equal $writes @(Get-FakeWrites).Count 'a converged rerun writes nothing'
        Assert-Equal @($result.Bindings)[0].Fingerprint @($again.Bindings)[0].Fingerprint 'a converged rerun returns the same binding'
    }

    Invoke-Case 'a failed attempt never blocks a rerun attempt' {
        New-Fake -Mode Multiple
        $script:Fake.CreatedPending = @(@{ ProvisioningState = 'Failed'; HealthState = 'Unhealthy' })
        Assert-Throws { Invoke-Goal $converge.Clone() } 'not ready' 'failed revision is reported'
        Assert-Equal $null (Get-FakeLabelTarget $longLabel) 'failed revision is not labeled'
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'not ready' 'the same attempt keeps reporting the failed revision'
        $script:Fake.CreatedPending = @(@{ ProvisioningState = 'Provisioned'; HealthState = 'Healthy' })
        $rerun = $converge.Clone()
        $rerun['Attempt'] = '2'
        $result = Invoke-Goal $rerun
        Assert-Equal 'fx-sql--long128-b4242a2' @($result.Bindings)[0].Revision 'rerun gets a fresh identity'
        Assert-Equal 'fx-sql--long128-b4242a2' (Get-FakeLabelTarget 'long128-b4242a2') 'rerun label'
        Assert-True $script:Fake.Revisions.Contains($longName) 'failed revision is preserved for diagnosis'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic is unchanged'
    }

    Invoke-Case 'an unhealthy revision times out without routing or labeling' {
        New-Fake -Mode Multiple
        $script:Fake.CreatedPending = @(@{ ProvisioningState = 'Provisioning'; HealthState = 'None' }, @{ ProvisioningState = 'Provisioned'; HealthState = 'Unhealthy' })
        $attempts = & $module { $script:PollAttempts }
        & $module { $script:PollAttempts = 3 }
        try {
            Assert-Throws { Invoke-Goal $converge.Clone() } 'not ready' 'unhealthy revision times out'
        }
        finally {
            & $module { param($Value) $script:PollAttempts = $Value } $attempts
        }

        Assert-Equal $null (Get-FakeLabelTarget $longLabel) 'unhealthy revision is not labeled'
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic never moves'
    }

    $longText = New-ProfileText 'Long128' 'long128' -ResourceIdProfile 'Long128' -Environment (($longEnvironment.Keys | ForEach-Object { "$_ = '$($longEnvironment[$_])'" }) -join '; ')

    Invoke-Case 'a synthetic second profile converges beside Long128' {
        $catalog = Get-RevisionCatalog -Path (New-CatalogFile -Name 'two-profiles' -Content (New-CatalogText @($longText, (New-ProfileText 'Alpha' 'alpha'))))
        New-Fake -Mode Multiple
        $arguments = $converge.Clone()
        $arguments['Catalog'] = $catalog
        $result = Invoke-Goal $arguments
        $bindings = @($result.Bindings)
        Assert-Equal 2 $bindings.Count 'two bindings'
        Assert-Equal 'Alpha' $bindings[1].Profile 'synthetic binding profile'
        Assert-Equal 'None' $bindings[1].ResourceIdProfile 'synthetic profile has no resource ID contract'
        Assert-Equal 'fx-sql--alpha-b4242a1' (Get-FakeLabelTarget 'alpha-b4242a1') 'synthetic label'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'Long128 label'
        $alpha = Get-RevisionTemplate 'fx-sql--alpha-b4242a1'
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--initial') -Suffix 'alpha-b4242a1' -Environment @{ FhirServer__Features__Alpha = 'true' } -MinReplicas 0
        Assert-Equal (ConvertTo-CanonicalJson $expected) (ConvertTo-CanonicalJson $alpha) 'synthetic profile copies the baseline, not Long128'
        Assert-Equal $null (Get-TemplateEnvironmentValue $alpha 'FhirServer__CoreFeatures__UseLongResourceIds') 'Long128 settings do not leak'
        $matrix = ConvertFrom-Json -InputObject $result.Matrix -AsHashtable
        Assert-Equal 'Long128,Alpha' ($matrix.Keys -join ',') 'matrix order follows the catalog'
        Assert-Equal 'None' $matrix['Alpha']['SqlResourceIdProfile'] 'synthetic matrix row'

        New-Fake -Mode Multiple
        $foreign = Add-FakeRevision -Suffix 'foreign' -Template (New-FixtureTemplate -Suffix 'foreign')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ revisionName = $foreign; weight = 0; label = 'alpha-b4242a1' })
        Assert-NoWrites { Invoke-Goal $arguments } 'Conflict' 'a later profile conflict stops before the first profile writes'
    }

    Invoke-Case 'fifteen profiles converge without a fixed step cap' {
        $texts = @(foreach ($index in 1..15) { New-ProfileText ('Profile{0:D2}' -f $index) ('p{0:D2}' -f $index) })
        $catalog = Get-RevisionCatalog -Path (New-CatalogFile -Name 'fifteen-profiles' -Content (New-CatalogText $texts))
        New-Fake -Mode Single
        $arguments = $converge.Clone()
        $arguments['Catalog'] = $catalog
        $result = Invoke-Goal $arguments 3>$null
        Assert-Equal 15 @($result.Bindings).Count 'fifteen bindings'
        Assert-Equal 15 @(Get-TemplateWrites).Count 'fifteen template writes'
        foreach ($index in 1..15) {
            $label = 'p{0:D2}-b4242a1' -f $index
            Assert-Equal "fx-sql--$label" (Get-FakeLabelTarget $label) "label $label"
        }

        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic stays on the baseline'
    }

    Invoke-Case 'identities that exceed the DNS budget fail before any write' {
        New-Fake -AppName (('x' * 42) + '-sql') -Mode Multiple
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'DNS budget' 'label host exceeds 63 characters'
        New-Fake -Mode Multiple
        $bad = $converge.Clone()
        $bad['BuildId'] = '42x'
        Assert-NoWrites { Invoke-Goal $bad } 'BuildId' 'build identity must be numeric'
    }

    $poisonTemplate = New-FixtureTemplate -Suffix 'poison' -Image 'fxacr.azurecr.io/poison:1' -Environment @{ FhirServer__CoreFeatures__UseLongResourceIds = 'true'; KeyVault__Endpoint = 'https://poison.vault.azure.net/' }

    Invoke-Case 'resolve reads the routed Standard64 revision without writing or leaking secrets' {
        New-Fake -Mode Multiple
        Add-FakeRevision -Suffix 'poison' -Template $poisonTemplate -Latest | Out-Null
        $resolved = Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64' }
        Assert-Equal 0 @(Get-FakeWrites).Count 'resolve never writes'
        Assert-Equal 'Standard64' $resolved.ResourceIdProfile 'baseline resource ID profile'
        Assert-Equal 'fx-sql--initial' $resolved.Revision 'routed revision, not latest'
        Assert-Equal "https://fx-sql.$envDomain" $resolved.Url 'baseline uses the app URL'
        Assert-Equal $baseImage $resolved.Image 'baseline image'
        Assert-True $resolved.Ready 'baseline readiness'
        Assert-Equal 'https://fxvault.vault.azure.net/' $resolved.Settings['KeyVault__Endpoint'] 'vault from the selected revision'
        Assert-Equal 'https://fx-sql.example.test' $resolved.Settings['FhirServer__Security__Authentication__Audience'] 'audience'
        Assert-Equal 'https://fxstorage.blob.core.windows.net' $resolved.Settings['FhirServer__Operations__Export__StorageAccountUri'] 'export storage'
        Assert-Equal 'https://fxstorage.blob.core.windows.net' $resolved.Settings['FhirServer__Operations__IntegrationDataStore__StorageAccountUri'] 'import storage'
        Assert-True (-not (ConvertTo-Json $resolved -Depth 20).Contains('fixture-secret')) 'resolve output contains no secrets'

        New-Fake -Mode Single
        $single = Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64' }
        Assert-Equal 'fx-sql--initial' $single.Revision 'a healthy Single-mode app resolves to its only revision'
        Assert-Equal 0 @(Get-FakeWrites).Count 'Single-mode resolve never adopts'

        New-Fake -Mode Multiple
        Add-FakeRevision -Suffix 'routed-long' -Template $poisonTemplate | Out-Null
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--routed-long'; weight = 100 })
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64' } } 'Conflict' 'a long-ID default route is not a Standard64 baseline'
    }

    Invoke-Case 'resolve uses the captured profile identity and detects drift' {
        New-Fake -Mode Multiple
        $binding = @((Invoke-Goal $converge.Clone()).Bindings)[0]
        Add-FakeRevision -Suffix 'poison' -Template $poisonTemplate -Latest | Out-Null
        $writes = @(Get-FakeWrites).Count
        $resolved = Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding; Expect = $true }
        Assert-Equal $writes @(Get-FakeWrites).Count 'resolve never writes'
        Assert-Equal $longName $resolved.Revision 'captured revision'
        Assert-Equal "https://fx-sql---$longLabel.$envDomain" $resolved.Url 'captured label URL'
        Assert-Equal 'Long128' $resolved.ResourceIdProfile 'profile resource ID contract'
        Assert-Equal 'true' $resolved.Settings['FhirServer__CoreFeatures__UseLongResourceIds'] 'declared flag from the selected revision'
        Assert-Equal 'false' $resolved.Settings['TaskHosting__Enabled'] 'workers stay off on the profile'
        Assert-Equal 'https://fxvault.vault.azure.net/' $resolved.Settings['KeyVault__Endpoint'] 'vault from the selected revision, not latest'
        Assert-Equal $binding.Fingerprint $resolved.Fingerprint 'fingerprint is stable'

        $other = Add-FakeRevision -Suffix 'other' -Template (New-FixtureTemplate -Suffix 'other' -Environment $longEnvironment)
        $saved = @($script:Fake.Traffic)
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ revisionName = $other; weight = 0; label = $longLabel })
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding } } 'Conflict' 'a moved label never selects another revision'
        Set-FakeTraffic $saved
        @($script:Fake.Revisions[$longName].Template['containers'])[0]['image'] = 'fxacr.azurecr.io/other:1'
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding; Expect = $true } } 'drift' 'a changed selected template is drift'
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Alpha'; Binding = $binding } } 'Revision profile' 'an unknown profile is rejected'
    }

    $newImage = 'fxacr.azurecr.io/r4_fhir-server:5678@sha256:' + ('c' * 64)
    $deploySettings = [ordered]@{
        Telemetry__Provider = 'ApplicationInsights'
        Telemetry__ConnectionString = 'InstrumentationKey=fixture-secret-ai-2'
        APPLICATIONINSIGHTS_CONNECTION_STRING = 'InstrumentationKey=fixture-secret-ai-2'
        FhirServer__Operations__Import__InMemoryTestEnabled = 'true'
        FhirServer__CoreFeatures__UseLongResourceIds = 'false'
        TaskHosting__Enabled = 'true'
    }
    $deploy = @{ Goal = 'Converge'; BuildId = '4242'; Attempt = '1'; Image = $newImage; Environment = $deploySettings; RemoveEnvironment = @('Telemetry__InstrumentationKey'); MinReplicas = 3 }

    Invoke-Case 'baseline deployment copies the routed Standard64 revision after latest became Long128' {
        New-Fake -Mode Multiple
        Invoke-Goal $converge.Clone() | Out-Null
        Assert-Equal $longName $script:Fake.LatestRevisionName 'latest is the Long128 profile'
        $result = Invoke-Goal $deploy.Clone()
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--initial') -Suffix 'base-b4242a1' -Environment $deploySettings -RemoveEnvironment @('Telemetry__InstrumentationKey') -Image $newImage -MinReplicas 3
        Assert-Equal (ConvertTo-CanonicalJson $expected) (ConvertTo-CanonicalJson @(Get-TemplateWrites)[-1]['Body']['properties']['template']) 'deployment copies the routed baseline'
        Assert-Equal 'fx-sql--base-b4242a1' $result.BaselineRevision 'successor identity'
        Assert-Equal 'fx-sql--base-b4242a1' (Get-RoutedRevision) 'verified successor takes default traffic'
        Assert-True ($script:Fake.Probes -contains "https://fx-sql--base-b4242a1.$envDomain") 'successor is probed directly before the pin'
        $created = Get-RevisionTemplate 'fx-sql--base-b4242a1'
        Assert-Equal 'true' (Get-TemplateEnvironmentValue $created 'SqlServer__AllowDatabaseCreation') 'baseline keeps schema ownership'
        Assert-Equal 'true' (Get-TemplateEnvironmentValue $created 'SqlServer__SchemaOptions__AutomaticUpdatesEnabled') 'baseline keeps schema updates'
        Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'the profile label survives the pin'
        Assert-True (-not (ConvertTo-Json $result -Depth 20).Contains('fixture-secret')) 'deploy output contains no secrets'
        $writes = @(Get-FakeWrites).Count
        Invoke-Goal $deploy.Clone() | Out-Null
        Assert-Equal $writes @(Get-FakeWrites).Count 'a repeated deployment writes nothing'

        New-Fake -Mode Multiple
        $script:Fake.ProbeResult = $false
        $attempts = & $module { $script:PollAttempts }
        & $module { $script:PollAttempts = 3 }
        try {
            Assert-Throws { Invoke-Goal $deploy.Clone() } 'not ready' 'an unreachable successor is never promoted'
        }
        finally {
            & $module { param($Value) $script:PollAttempts = $Value } $attempts
        }

        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'default traffic stays on the old baseline'
    }

    Invoke-Case 'settle retires only exact current-build test identities' {
        New-Fake -Mode Multiple
        $foreign = Add-FakeRevision -Suffix 'long128-b9999a1' -Template (New-FixtureTemplate -Suffix 'long128-b9999a1' -Environment $longEnvironment)
        $blue = Add-FakeRevision -Suffix 'blue' -Template (New-FixtureTemplate -Suffix 'blue')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ revisionName = $foreign; weight = 0; label = 'long128-b9999a1' }, @{ revisionName = $blue; weight = 0; label = 'blue' })
        Invoke-Goal $deploy.Clone() | Out-Null
        Invoke-Goal $converge.Clone() | Out-Null
        $rerun = $converge.Clone()
        $rerun['Attempt'] = '2'
        Invoke-Goal $rerun | Out-Null
        Assert-Equal 'fx-sql--long128-b4242a2' $script:Fake.LatestRevisionName 'latest is a Long128 profile'
        $settle = @{ Goal = 'Settle'; BuildId = '4242'; Attempt = '1' }
        $result = Invoke-Goal $settle.Clone()
        Assert-Equal 'fx-sql--base-b4242a1-m0' $result.BaselineRevision 'minimum-zero successor'
        Assert-Equal 'fx-sql--base-b4242a1-m0' (Get-RoutedRevision) 'successor takes default traffic'
        $successor = Get-RevisionTemplate 'fx-sql--base-b4242a1-m0'
        $expected = Get-ExpectedTemplate -Source (Get-RevisionTemplate 'fx-sql--base-b4242a1') -Suffix 'base-b4242a1-m0' -MinReplicas 0
        Assert-Equal (ConvertTo-CanonicalJson $expected) (ConvertTo-CanonicalJson @(Get-TemplateWrites)[-1]['Body']['properties']['template']) 'successor copies the routed Standard64 baseline'
        Assert-Equal $newImage @($successor['containers'])[0]['image'] 'successor keeps the deployed image'
        Assert-Equal $null (Get-FakeLabelTarget $longLabel) 'attempt 1 label removed'
        Assert-Equal $null (Get-FakeLabelTarget 'long128-b4242a2') 'attempt 2 label removed'
        foreach ($name in @($longName, 'fx-sql--long128-b4242a2', 'fx-sql--base-b4242a1')) {
            Assert-True (-not $script:Fake.Revisions[$name].Active) "$name is retired"
        }

        Assert-Equal 'fx-sql--base-b4242a1,fx-sql--long128-b4242a1,fx-sql--long128-b4242a2' ((@($result.Retired) | Sort-Object) -join ',') 'retired list'
        Assert-True $script:Fake.Revisions['fx-sql--initial'].Active 'another build baseline is untouched'
        Assert-Equal $foreign (Get-FakeLabelTarget 'long128-b9999a1') 'another build label is untouched'
        Assert-Equal $blue (Get-FakeLabelTarget 'blue') 'foreign label is untouched'
        $writes = @(Get-FakeWrites).Count
        $again = Invoke-Goal $settle.Clone()
        Assert-Equal $writes @(Get-FakeWrites).Count 'a repeated settle writes nothing'
        Assert-Equal 'fx-sql--base-b4242a1-m0' $again.BaselineRevision 'repeated settle reports the same baseline'
    }

    Invoke-Case 'the operational entry script converges and resolves with JSON bindings' {
        New-Fake -Mode Multiple
        # Dot-source so the fixture's script-scoped fake ARM stays visible to the injected transport.
        $result = . $entryPath -Goal Converge -AppResourceId $script:Fake.AppId -AccessToken $token -BuildId '4242' -Attempt '1'
        $row = (ConvertFrom-Json -InputObject $result.Matrix -AsHashtable)['Long128']
        $resolved = . $entryPath -Goal Resolve -AppResourceId $script:Fake.AppId -AccessToken $token -RevisionProfile 'Long128' -Binding $row['SqlRevisionBinding'] -Expect
        Assert-Equal $longName $resolved.Revision 'entry script resolves the captured binding'
        Assert-True (-not (ConvertTo-Json @($result, $resolved) -Depth 20).Contains('fixture-token')) 'outputs never contain the token'
    }

    Invoke-Case 'a baseline selection captured before the tests is rechecked after them' {
        New-Fake -Mode Multiple
        $before = . $entryPath -Goal Resolve -AppResourceId $script:Fake.AppId -AccessToken $token -RevisionProfile 'Standard64'
        $captured = ConvertTo-Json -Compress -InputObject ([ordered]@{ Profile = $before.Profile; Revision = $before.Revision; Url = $before.Url; Image = $before.Image; Fingerprint = $before.Fingerprint; BaselineRevision = $before.BaselineRevision })
        $after = . $entryPath -Goal Resolve -AppResourceId $script:Fake.AppId -AccessToken $token -RevisionProfile 'Standard64' -Binding $captured -Expect
        Assert-Equal $before.Revision $after.Revision 'the unchanged baseline passes the after-check'
        Invoke-Goal $deploy.Clone() | Out-Null
        Assert-NoWrites { . $entryPath -Goal Resolve -AppResourceId $script:Fake.AppId -AccessToken $token -RevisionProfile 'Standard64' -Binding $captured -Expect } 'Conflict' 'a baseline replaced during the tests fails the after-check'
    }

    $settle = @{ Goal = 'Settle'; BuildId = '4242'; Attempt = '1' }

    Invoke-Case 'a routed identity with another specification is refused' {
        New-Fake -Mode Multiple
        Invoke-Goal $deploy.Clone() | Out-Null
        $image = $deploy.Clone()
        $image['Image'] = 'fxacr.azurecr.io/r4_fhir-server:5678@sha256:' + ('d' * 64)
        Assert-NoWrites { Invoke-Goal $image } 'Conflict' 'a routed identity with another image is refused'
        $scale = $deploy.Clone()
        $scale['MinReplicas'] = 4
        Assert-NoWrites { Invoke-Goal $scale } 'Conflict' 'a routed identity with another scale is refused'
    }

    Invoke-Case 'settle refuses a routed baseline owned by another build' {
        New-Fake -Mode Multiple
        $other = Add-FakeRevision -Suffix 'base-b999a1' -Template (New-FixtureTemplate -Suffix 'base-b999a1')
        Set-FakeTraffic @(@{ revisionName = $other; weight = 100 })
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'another build baseline is never scaled down'
        New-Fake -Mode Multiple
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'an unowned baseline is never scaled down'
        New-Fake -Mode Single
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'settle never adopts a Single-mode app'
    }

    Invoke-Case 'settle refuses current-build labels that are not bound to their owned revision' {
        New-Fake -Mode Multiple
        Invoke-Goal $deploy.Clone() | Out-Null
        Invoke-Goal $converge.Clone() | Out-Null
        $foreign = Add-FakeRevision -Suffix 'foreign' -Template (New-FixtureTemplate -Suffix 'foreign')
        Set-FakeTraffic @(@($script:Fake.Traffic | Where-Object { $_['label'] -cne $longLabel }) + @(@{ revisionName = $foreign; weight = 0; label = $longLabel }))
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'a label bound to a foreign revision is refused'
        Assert-Equal $foreign (Get-FakeLabelTarget $longLabel) 'the foreign binding is kept'

        New-Fake -Mode Multiple
        Invoke-Goal $deploy.Clone() | Out-Null
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--base-b4242a1'; weight = 100; label = $longLabel })
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'a label bound to the baseline is refused'
        Assert-Equal 'fx-sql--base-b4242a1' (Get-FakeLabelTarget $longLabel) 'the baseline label is kept'

        New-Fake -Mode Multiple
        Invoke-Goal $deploy.Clone() | Out-Null
        Add-FakeRevision -Suffix 'long128-b4242a3' -Template (New-FixtureTemplate -Suffix 'long128-b4242a3') | Out-Null
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'an owned identity with another template is refused'
        Assert-True $script:Fake.Revisions['fx-sql--long128-b4242a3'].Active 'the mismatched revision stays active'
    }

    Invoke-Case 'resolve revalidates the captured baseline and endpoint' {
        New-Fake -Mode Multiple
        $binding = @((Invoke-Goal $converge.Clone()).Bindings)[0]
        foreach ($field in @('Url', 'ResourceIdProfile', 'CategoryFilter', 'BaselineRevision')) {
            $tampered = Copy-Deep $binding
            $tampered[$field] = 'tampered'
            Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $tampered } } 'Conflict' "a tampered $field is refused"
            Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $tampered; Expect = $true } } 'Conflict' "a tampered $field is refused after the tests"
        }

        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64'; Binding = @{ Profile = 'Standard64'; Revision = 'fx-sql--initial'; Url = 'https://tampered.example.test' } } } 'Conflict' 'a tampered baseline endpoint is refused'
        Invoke-Goal $deploy.Clone() | Out-Null
        Assert-NoWrites { Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding } } 'Conflict' 'a moved baseline invalidates the captured binding'
    }

    Invoke-Case 'asynchronous writes are awaited without repeating them' {
        New-Fake -Mode Single
        $script:Fake.VisibilityDelay = 4
        Invoke-Goal $deploy.Clone() | Out-Null
        Invoke-Goal $converge.Clone() | Out-Null
        $result = Invoke-Goal $settle.Clone()
        Assert-Equal 'fx-sql--base-b4242a1-m0' $result.BaselineRevision 'settle completes'
        Assert-Equal 0 $script:Fake.Deferred.Count 'every write became visible before success'
        Assert-Equal 'fx-sql--base-b4242a1-m0' (Get-RoutedRevision) 'successor takes default traffic'
        Assert-Equal $null (Get-FakeLabelTarget $longLabel) 'profile label removed'
        Assert-True (-not $script:Fake.Revisions[$longName].Active) 'profile revision retired'
        $keys = @(Get-FakeWrites | ForEach-Object { "$($_['Method']) $($_['Path']) $(ConvertTo-CanonicalJson $_['Body'])" })
        Assert-Equal $keys.Count @($keys | Sort-Object -Unique).Count 'no write is repeated'
    }

    Invoke-Case 'baseline adoption rejects an unhealthy or abnormal source before writing' {
        $variants = @(
            @{ Name = 'unhealthy'; HealthState = 'Unhealthy'; Environment = @{} },
            @{ Name = 'cosmos'; HealthState = 'Healthy'; Environment = @{ DataStore = 'CosmosDb' } },
            @{ Name = 'workers-off'; HealthState = 'Healthy'; Environment = @{ TaskHosting__Enabled = 'false' } },
            @{ Name = 'security-off'; HealthState = 'Healthy'; Environment = @{ FhirServer__Security__Enabled = 'false' } }
        )
        foreach ($variant in $variants) {
            foreach ($mode in @('Multiple', 'Single')) {
                New-Fake -Mode $mode
                $initial = $script:Fake.Revisions['fx-sql--initial']
                $initial.HealthState = $variant.HealthState
                $initial.Template = New-FixtureTemplate -Suffix 'initial' -Environment $variant.Environment
                Assert-NoWrites { Invoke-Goal $deploy.Clone() } 'Conflict' "$($variant.Name) $mode source is not deployed from"
                Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' "$($variant.Name) $mode source is not profiled from"
            }
        }

        New-Fake -Mode Multiple
        $profileLike = Add-FakeRevision -Suffix 'alpha-b77a1' -Template (New-FixtureTemplate -Suffix 'alpha-b77a1')
        Set-FakeTraffic @(@{ revisionName = $profileLike; weight = 100 })
        Assert-NoWrites { Invoke-Goal $deploy.Clone() } 'Conflict' 'a profile identity is never laundered into a baseline'
    }

    Invoke-Case 'pinning preserves foreign labels on the weighted route and unrelated zero-weight entries' {
        New-Fake -Mode Multiple
        $extra = Add-FakeRevision -Suffix 'extra' -Template (New-FixtureTemplate -Suffix 'extra')
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100; label = 'blue' }, @{ revisionName = $extra; weight = 0 })
        Invoke-Goal $deploy.Clone() | Out-Null
        Assert-Equal 'fx-sql--base-b4242a1' (Get-RoutedRevision) 'default traffic moves to the successor'
        Assert-Equal 'fx-sql--initial' (Get-FakeLabelTarget 'blue') 'the foreign label keeps its revision'
        Assert-Equal 1 @($script:Fake.Traffic | Where-Object { $_['revisionName'] -ceq $extra -and -not $_.Contains('label') -and [int]$_['weight'] -eq 0 }).Count 'the unrelated zero-weight entry survives'
        $writes = @(Get-FakeWrites).Count
        Invoke-Goal $deploy.Clone() | Out-Null
        Assert-Equal $writes @(Get-FakeWrites).Count 'a repeated deployment writes nothing'
    }

    Invoke-Case 'accepted goal spellings and resource ID casing never change the operation' {
        New-Fake -Mode Multiple
        $lowerSettle = $settle.Clone()
        $lowerSettle['Goal'] = 'settle'
        $lowerSettle['Image'] = $newImage
        Assert-NoWrites { Invoke-Goal $lowerSettle } 'does not accept -Image' 'lowercase settle is still settle'
        $resolved = Invoke-Goal @{ Goal = 'resolve'; RevisionProfile = 'Standard64' }
        Assert-Equal 0 @(Get-FakeWrites).Count 'lowercase resolve never writes'
        Assert-Equal 'fx-sql--initial' $resolved.Revision 'lowercase resolve selects the baseline'

        New-Fake -Mode Multiple
        $script:Fake.AppId = $script:Fake.AppId -creplace 'Microsoft\.App/containerApps', 'microsoft.app/containerapps'
        $result = Invoke-Goal $converge.Clone()
        Assert-Equal $longName @($result.Bindings)[0].Revision 'lowercase provider casing converges'
    }

    Invoke-Case 'baseline classification never treats a secret reference as an absent default' {
        foreach ($key in @('FhirServer__CoreFeatures__UseLongResourceIds', 'TaskHosting__Enabled')) {
            foreach ($mode in @('Single', 'Multiple')) {
                New-Fake -Mode $mode
                $template = $script:Fake.Revisions['fx-sql--initial'].Template
                $template.containers[0].env = @($template.containers[0].env | Where-Object { $_.name -cne $key }) + @(@{ name = $key; secretRef = 'unresolved-setting' })
                Assert-NoWrites { Invoke-Goal $deploy.Clone() } 'Conflict' "$key secret reference is not an authenticated Standard64 source"
                Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' "$key secret reference is not a profile source"
            }
        }
    }

    Invoke-Case 'the production redeploy map establishes explicit baseline flags' {
        $path = Join-Path $PSScriptRoot '..\redeploy-webapp.yml'
        $source = Get-Content -LiteralPath $path -Raw
        $start = $source.IndexOf('$settings = @{', [StringComparison]::Ordinal)
        $end = $source.IndexOf('}', $start, [StringComparison]::Ordinal)
        $appInsightsConnectionString = 'fixture-connection-string'
        $settings = & ([scriptblock]::Create($source.Substring($start, $end - $start + 1) + "`n`$settings"))
        Assert-Equal 'false' $settings['FhirServer__CoreFeatures__UseLongResourceIds'] 'real caller explicitly disables long IDs on the baseline'
        Assert-Equal 'true' $settings['TaskHosting__Enabled'] 'real caller explicitly enables baseline workers'
    }

    Invoke-Case 'settled baseline sources still require Healthy readiness' {
        foreach ($health in @('None', 'Unknown', 'Degraded', '')) {
            New-Fake -Mode Multiple
            $name = Add-FakeRevision -Suffix 'base-b4242a1-m0' -Template (New-FixtureTemplate -Suffix 'base-b4242a1-m0' -MinReplicas 3)
            $script:Fake.Revisions[$name].HealthState = $health
            Set-FakeTraffic @(@{ revisionName = $name; weight = 100 })
            Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' "an m0 name does not authorize health '$health'"
            Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' "settle does not accept health '$health'"
        }
    }

    Invoke-Case 'a routed m0 name with positive minimum replicas is a specification conflict' {
        New-Fake -Mode Multiple
        $name = Add-FakeRevision -Suffix 'base-b4242a1-m0' -Template (New-FixtureTemplate -Suffix 'base-b4242a1-m0' -MinReplicas 3)
        Set-FakeTraffic @(@{ revisionName = $name; weight = 100 })
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'a wrong-scale routed m0 identity is not reported settled'
    }

    Invoke-Case 'settle preserves full-template conflicts on owned profile identities' {
        foreach ($field in @('image', 'command', 'resources', 'probes', 'volumes', 'environment', 'scale')) {
            New-Fake -Mode Multiple
            Invoke-Goal $deploy.Clone() | Out-Null
            Invoke-Goal $converge.Clone() | Out-Null
            $template = $script:Fake.Revisions[$longName].Template
            switch ($field) {
                'image' { $template.containers[0].image = 'foreign-image' }
                'command' { $template.containers[0].command = @('/bin/foreign') }
                'resources' { $template.containers[0].resources.cpu = 2 }
                'probes' { $template.containers[0].probes[0].httpGet.path = '/foreign' }
                'volumes' { $template.volumes = @(@{ name = 'foreign'; storageType = 'EmptyDir' }) }
                'environment' { $template.containers[0].env += @{ name = 'Foreign__Setting'; value = 'true' } }
                'scale' { $template.scale.maxReplicas = 99 }
            }
            Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' "a profile $field mismatch is preserved before any settle write"
            Assert-Equal $longName (Get-FakeLabelTarget $longLabel) 'diagnostic profile label remains'
            Assert-True $script:Fake.Revisions[$longName].Active 'diagnostic profile stays active'
        }
    }

    Invoke-Case 'settle preserves full-template conflicts on owned baseline predecessors' {
        New-Fake -Mode Multiple
        Invoke-Goal $deploy.Clone() | Out-Null
        $name = Add-FakeRevision -Suffix 'base-b4242a7' -Template (New-FixtureTemplate -Suffix 'base-b4242a7' -Image 'foreign-image')
        Assert-NoWrites { Invoke-Goal $settle.Clone() } 'Conflict' 'a conflicting current-build baseline is not retired by name'
        Assert-True $script:Fake.Revisions[$name].Active 'conflicting baseline stays active'
    }

    Invoke-Case 'floating foreign labels are refused before any revision mutation' {
        New-Fake -Mode Multiple
        Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, @{ latestRevision = $true; label = 'blue'; weight = 0 })
        $state = & $module { param($AppId) Get-ObservedState -AppResourceId $AppId } $script:Fake.AppId
        $plan = Get-RevisionPlan -State $state -Request @{ Kind = 'Profiles'; BuildId = '4242'; Attempt = '1'; Catalog = $script:Catalog }
        Assert-Equal 'Conflict' $plan.Action 'a floating foreign label is rejected by policy before transport'
        Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'copying a revision cannot retarget a foreign latest label'
    }

    Invoke-Case 'catalog names reserve dispatch sentinels and fit native matrix keys' {
        foreach ($name in @('None', 'none', 'Runtime', ('A' * 101))) {
            $path = New-CatalogFile -Name ([guid]::NewGuid().ToString('N')) -Content (New-CatalogText @((New-ProfileText $name 'alpha')))
            Assert-Throws { Get-RevisionCatalog -Path $path } 'Revision catalog' "catalog name '$name' is rejected before deployment"
        }
        $path = New-CatalogFile -Name 'maximum-matrix-name' -Content (New-CatalogText @((New-ProfileText ('A' * 100) 'alpha')))
        $maximumCatalog = Get-RevisionCatalog -Path $path
        Assert-Equal ('A' * 100) $maximumCatalog.Profiles[0].Name 'the native 100-character boundary is accepted'
    }

    Invoke-Case 'all ordinary SQL consumers select and recheck the named baseline without requiring boundary cases' {
        $path = Join-Path $PSScriptRoot '..\..\tasks\e2e-set-variables.yml'
        $source = Get-Content -LiteralPath $path -Raw
        $start = $source.IndexOf('$revisionProfile =', [StringComparison]::Ordinal)
        $end = $source.IndexOf('$armToken = (Get-AzAccessToken', $start, [StringComparison]::Ordinal)
        $dispatch = $source.Substring($start, $end - $start) + "`nthrow 'NAMED_SQL_RESOLVE'`n}"
        $dispatch = $dispatch.Replace('${{ parameters.resourceIdProfile }}', 'None').Replace('${{ parameters.appServiceType }}', 'SqlServer')
        $containerApp = Invoke-FakeArm -Method GET -Path $script:Fake.AppId -Body $null | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        function Get-ContainerEnvValue { param($EnvSettings, $Name) return @($EnvSettings | Where-Object { $_.name -ceq $Name })[0].value }
        Assert-Throws { & ([scriptblock]::Create($dispatch)) } 'NAMED_SQL_RESOLVE' 'SQL None dispatch must resolve the named baseline instead of latest settings'
        $runner = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\e2e-tests.yml') -Raw
        Assert-True $runner.Contains("if or(eq(parameters.appServiceType, 'SqlServer'), ne(parameters.resourceIdProfile, 'None'))") 'SQL None jobs include the selected-binding after-check'
        Assert-True $runner.Contains("if (`$resourceIdProfile -cne 'None')") 'the boundary-result checker remains separate from baseline selection'
    }

    Invoke-Case 'copying reads the exact named source before writing its successor' {
        New-Fake -Mode Multiple
        Invoke-Goal $converge.Clone() | Out-Null
        $requests = @($script:Fake.Requests)
        $read = @($requests | Where-Object { $_.Method -ceq 'GET' -and $_.Path -ceq "$($script:Fake.AppId)/revisions/fx-sql--initial" })
        Assert-True ($read.Count -gt 0) 'the named source is read directly instead of relying on the revision list'
        $copy = @(Get-TemplateWrites)[0]
        Assert-True ([array]::IndexOf($requests, $read[0]) -lt [array]::IndexOf($requests, $copy)) 'the exact source is read before the template PATCH'
    }

    Invoke-Case 'profile resolution reads the exact named selected revision' {
        New-Fake -Mode Multiple
        $binding = @((Invoke-Goal $converge.Clone()).Bindings)[0]
        $before = $script:Fake.Requests.Count
        Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding; Expect = $true } | Out-Null
        $reads = @($script:Fake.Requests | Select-Object -Skip $before | Where-Object { $_.Method -ceq 'GET' -and $_.Path -ceq "$($script:Fake.AppId)/revisions/$longName" })
        Assert-True ($reads.Count -gt 0) 'selected profile settings come from its exact named revision GET'
    }

    Invoke-Case 'admitted case aliases produce one logical deploy and profile override' {
        foreach ($key in @('FhirServer__CoreFeatures__UseLongResourceIds', 'TaskHosting__Enabled')) {
            foreach ($deployFirst in @($false, $true)) {
                New-Fake -Mode Multiple
                $source = $script:Fake.Revisions['fx-sql--initial'].Template
                $value = if ($key -ceq 'TaskHosting__Enabled') { 'true' } else { 'false' }
                $source.containers[0].env = @($source.containers[0].env | Where-Object { $_.name -ine $key }) + @(@{ name = $key.ToLowerInvariant(); value = $value })
                $source.containers[0].env += @{ name = 'UNRELATED__MixedCase'; value = 'preserved' }
                if ($deployFirst) {
                    Invoke-Goal $deploy.Clone() | Out-Null
                    $baseline = Get-RevisionTemplate 'fx-sql--base-b4242a1'
                    Assert-Equal 1 @($baseline.containers[0].env | Where-Object { $_.name -ieq $key }).Count 'baseline has exactly one logical override'
                }
                $binding = @((Invoke-Goal $converge.Clone()).Bindings)[0]
                $resolved = Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Long128'; Binding = $binding; Expect = $true }
                $profile = Get-RevisionTemplate $longName
                Assert-Equal 1 @($profile.containers[0].env | Where-Object { $_.name -ieq $key }).Count 'profile has exactly one logical override'
                Assert-Equal $longEnvironment[$key] $resolved.Settings[$key] 'selected override remains unambiguous'
                Assert-Equal 1 @($profile.containers[0].env | Where-Object { $_.name -ceq 'UNRELATED__MixedCase' -and $_.value -ceq 'preserved' }).Count 'unrelated environment names and values are not normalized'
            }
        }
    }

    Invoke-Case 'unnamed foreign labels are refused with omitted or false latest flags' {
        foreach ($explicitFalse in @($false, $true)) {
            New-Fake -Mode Multiple
            $foreign = @{ label = 'blue'; weight = 0 }
            if ($explicitFalse) { $foreign['latestRevision'] = $false }
            Set-FakeTraffic @(@{ revisionName = 'fx-sql--initial'; weight = 100 }, $foreign)
            $state = & $module { param($AppId) Get-ObservedState -AppResourceId $AppId } $script:Fake.AppId
            $plan = Get-RevisionPlan -State $state -Request @{ Kind = 'Profiles'; BuildId = '4242'; Attempt = '1'; Catalog = $script:Catalog }
            Assert-Equal 'Conflict' $plan.Action 'unnamed-label policy refuses mutation before the transport runs'
            Assert-NoWrites { Invoke-Goal $converge.Clone() } 'Conflict' 'an unnamed foreign label never becomes a synthesized floating target'
        }
    }

    Invoke-Case 'an implicit Single latest route reads the exact named source' {
        New-Fake -Mode Single
        Set-FakeTraffic @(@{ weight = 100 })
        $selected = Invoke-Goal @{ Goal = 'Resolve'; RevisionProfile = 'Standard64' }
        $reads = @($script:Fake.Requests | Where-Object { $_.Method -ceq 'GET' -and $_.Path -ceq "$($script:Fake.AppId)/revisions/fx-sql--initial" })
        Assert-True ($reads.Count -gt 0) 'the accepted implicit-latest source is directly read even before adoption'
        Assert-Equal 'fx-sql--initial' $selected.Revision 'implicit read-only selection resolves the exact baseline'
        Invoke-Goal $converge.Clone() | Out-Null
        Assert-Equal 'fx-sql--initial' (Get-RoutedRevision) 'implicit Single adoption pins the same baseline'
    }

    Invoke-Case 'ordinary SQL None jobs publish their selected endpoint evidence' {
        $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\tasks\e2e-set-variables.yml') -Raw
        Assert-True $source.Contains("if or(eq(parameters.appServiceType, 'SqlServer'), ne(parameters.resourceIdProfile, 'None'))") 'SQL None endpoint evidence is not compiled out'
    }

    Invoke-Case 'an observed intervening baseline promotion cannot be erased by deploy overrides' {
        foreach ($phase in @('copy', 'probe')) {
            New-Fake -Mode Multiple
            $interleave = {
                param($Target)
                $template = Copy-Deep (Get-RevisionTemplate 'fx-sql--base-b4242a1')
                $template.revisionSuffix = 'base-b8888a1'
                $template.containers[0].image = 'fxacr.azurecr.io/r4_fhir-server:8888@sha256:' + ('e' * 64)
                $template.scale.minReplicas = 4
                foreach ($entry in $template.containers[0].env) {
                    if ($entry.name -ceq 'Telemetry__ConnectionString') { $entry.value = 'different-connection' }
                    if ($entry.name -ceq 'FhirServer__Operations__Import__InMemoryTestEnabled') { $entry.value = 'false' }
                }
                $other = Add-FakeRevision -Suffix 'base-b8888a1' -Template $template -Latest
                Set-FakeTraffic @(@{ revisionName = $other; weight = 100 })
                $script:Fake.AfterTemplatePatch = $null
                $script:Fake.AfterProbe = $null
            }
            if ($phase -ceq 'copy') { $script:Fake.AfterTemplatePatch = $interleave } else { $script:Fake.AfterProbe = $interleave }
            Assert-Throws { Invoke-Goal $deploy.Clone() } 'Conflict' "an observed promotion after $phase is refused"
            Assert-Equal 'fx-sql--base-b8888a1' (Get-RoutedRevision) 'the intervening build keeps its default route'
            Assert-Equal 1 @(Get-FakeWrites).Count 'no promotion is written after the intervening route is observed'
            Assert-True $script:Fake.Revisions['fx-sql--base-b4242a1'].Active 'the unpromoted successor remains available for diagnosis'
        }
    }

    if ($script:Failures.Count -gt 0) {
        throw "$($script:Failures.Count) revision profile case(s) failed: $($script:Failures -join '; ')"
    }

    Write-Host 'All revision profile cases passed.'
}
finally {
    Remove-Module RevisionProfiles -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
