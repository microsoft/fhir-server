Set-StrictMode -Version Latest

$script:PollAttempts = 60
$script:PollDelaySeconds = 5
$script:AccessToken = $null
$script:ApiVersion = '2024-03-01'
$script:WritableTemplateKeys = @('containers', 'initContainers', 'scale', 'volumes', 'serviceBinds', 'terminationGracePeriodSeconds', 'revisionSuffix')
$script:LongIdKey = 'FhirServer__CoreFeatures__UseLongResourceIds'
$script:ProfileFields = @('Name', 'Slug', 'CategoryFilter', 'ResourceIdProfile', 'Environment')
$script:ResourceIdProfiles = @('None', 'Long128')
$script:ForbiddenEnvironmentPatterns = @(
    '^SqlServer__ConnectionString',
    '^FhirServer__Security__',
    '^KeyVault',
    '^AZURE_',
    '__StorageAccount',
    '^DataStore$',
    '^ASPNETCORE_URLS$',
    '^Telemetry__',
    '^APPLICATIONINSIGHTS_')
$script:SettingNames = @(
    'KeyVault__Endpoint',
    'FhirServer__Security__Authentication__Audience',
    'FhirServer__Security__Authentication__Authority',
    'FhirServer__Operations__Export__StorageAccountUri',
    'FhirServer__Operations__IntegrationDataStore__StorageAccountUri',
    'FhirServer__Operations__ConvertData__ContainerRegistryServers__0',
    'FhirServer__CoreFeatures__UseLongResourceIds',
    'TaskHosting__Enabled')

function Assert-CatalogRule {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "Revision catalog: $Message"
    }
}

function Test-NonEmptyString {
    param($Value)
    return ($Value -is [string]) -and -not [string]::IsNullOrWhiteSpace($Value)
}

function ConvertTo-CatalogProfile {
    param($Entry, [int]$Index)

    Assert-CatalogRule ($Entry -is [System.Collections.IDictionary]) "profile #$Index must be a hashtable."
    foreach ($key in @($Entry.Keys)) {
        Assert-CatalogRule ([string]$key -cin $script:ProfileFields) "profile #$Index has unsupported field '$key'."
    }

    foreach ($field in @('Name', 'Slug', 'CategoryFilter', 'ResourceIdProfile')) {
        Assert-CatalogRule ($Entry.Contains($field) -and (Test-NonEmptyString $Entry[$field])) "profile #$Index must declare string field '$field'."
    }

    $name = [string]$Entry['Name']
    $slug = [string]$Entry['Slug']
    Assert-CatalogRule ($name -cmatch '^[A-Za-z][A-Za-z0-9]*$') "profile name '$name' must be alphanumeric and start with a letter."
    Assert-CatalogRule ($name.Length -le 100) "profile name '$name' exceeds the 100-character matrix-key budget."
    Assert-CatalogRule ($name -inotmatch '^(Standard64|None|Runtime)$') "profile name '$name' is reserved for dispatch."
    Assert-CatalogRule ($slug -cmatch '^[a-z][a-z0-9]{0,7}$') "profile '$name' slug '$slug' must be 1-8 lowercase alphanumerics starting with a letter."
    Assert-CatalogRule ($slug -cne 'base') "profile '$name' slug 'base' is reserved for baseline successors."
    Assert-CatalogRule ([string]$Entry['ResourceIdProfile'] -cin $script:ResourceIdProfiles) "profile '$name' ResourceIdProfile must be one of $($script:ResourceIdProfiles -join ', ')."

    Assert-CatalogRule ($Entry.Contains('Environment') -and $Entry['Environment'] -is [System.Collections.IDictionary]) "profile '$name' must declare an Environment hashtable."
    $source = $Entry['Environment']
    Assert-CatalogRule ($source.Count -gt 0) "profile '$name' Environment must declare at least one setting."

    [string[]]$names = @($source.Keys | ForEach-Object { [string]$_ })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $environment = [ordered]@{}
    foreach ($key in $names) {
        Assert-CatalogRule ($key -cmatch '^[A-Za-z_][A-Za-z0-9_]*$') "profile '$name' environment name '$key' is not a valid variable name."
        foreach ($pattern in $script:ForbiddenEnvironmentPatterns) {
            Assert-CatalogRule ($key -notmatch $pattern) "profile '$name' may not override protected setting '$key'."
        }

        $value = $source[$key]
        Assert-CatalogRule ($value -is [string]) "profile '$name' environment value for '$key' must be a literal string."
        $environment[$key] = $value
    }

    $long = $environment.Contains($script:LongIdKey) -and ([string]$environment[$script:LongIdKey] -ieq 'true')
    if ([string]$Entry['ResourceIdProfile'] -ceq 'Long128') {
        Assert-CatalogRule $long "profile '$name' declares Long128 and must set $($script:LongIdKey) to 'true'."
    }
    else {
        Assert-CatalogRule (-not $long) "profile '$name' sets $($script:LongIdKey) and must declare ResourceIdProfile 'Long128'."
    }

    return [ordered]@{
        Name = $name
        Slug = $slug
        CategoryFilter = [string]$Entry['CategoryFilter']
        ResourceIdProfile = [string]$Entry['ResourceIdProfile']
        Environment = $environment
    }
}

function Get-RevisionCatalog {
    [CmdletBinding()]
    param([string]$Path = (Join-Path $PSScriptRoot 'RevisionProfiles.psd1'))

    $data = Import-PowerShellDataFile -Path $Path
    foreach ($key in @($data.Keys)) {
        Assert-CatalogRule ([string]$key -cin @('Baseline', 'Profiles')) "unsupported top-level key '$key'."
    }

    $baseline = $data['Baseline']
    Assert-CatalogRule ($baseline -is [System.Collections.IDictionary]) 'Baseline must be a hashtable.'
    Assert-CatalogRule ($baseline.Count -eq 2 -and $baseline.Contains('Name') -and $baseline.Contains('ResourceIdProfile')) 'Baseline must declare exactly Name and ResourceIdProfile.'
    Assert-CatalogRule (([string]$baseline['Name'] -ceq 'Standard64') -and ([string]$baseline['ResourceIdProfile'] -ceq 'Standard64')) "Baseline must be Name 'Standard64' with ResourceIdProfile 'Standard64'."

    Assert-CatalogRule ($data.Contains('Profiles')) 'Profiles must be declared.'
    $entries = @($data['Profiles'])
    Assert-CatalogRule ($entries.Count -gt 0) 'Profiles must declare at least one profile.'

    $profiles = [System.Collections.Generic.List[object]]::new()
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $slugs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = ConvertTo-CatalogProfile -Entry $entries[$index] -Index ($index + 1)
        Assert-CatalogRule ($names.Add($entry['Name'])) "duplicate profile name '$($entry['Name'])'."
        Assert-CatalogRule ($slugs.Add($entry['Slug'])) "duplicate profile slug '$($entry['Slug'])'."
        $profiles.Add($entry)
    }

    return [pscustomobject]@{
        Baseline = [ordered]@{ Name = 'Standard64'; ResourceIdProfile = 'Standard64' }
        Profiles = $profiles.ToArray()
    }
}

function Get-Field {
    param($Object, [string]$Name)
    if ($Object -is [System.Collections.IDictionary] -and $Object.Contains($Name)) {
        return , $Object[$Name]
    }

    return $null
}

function Get-StringField {
    param($Object, [string]$Name)
    $value = Get-Field $Object $Name
    if ($null -eq $value) {
        return $null
    }

    return [string]$value
}

function Copy-Data {
    param($Value)
    if ($null -eq $Value) {
        return $null
    }

    return , (ConvertTo-Json -InputObject $Value -Depth 100 -Compress | ConvertFrom-Json -AsHashtable -NoEnumerate)
}

function ConvertTo-CanonicalValue {
    param($Value, [string]$Key)

    if ($Value -is [System.Collections.IDictionary]) {
        [string[]]$keys = @($Value.Keys | ForEach-Object { [string]$_ })
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        $result = [ordered]@{}
        foreach ($name in $keys) {
            $result[$name] = ConvertTo-CanonicalValue -Value $Value[$name] -Key $name
        }

        return $result
    }

    if ($Value -is [System.Collections.IList]) {
        $items = [System.Collections.Generic.List[object]]::new()
        foreach ($item in $Value) {
            $items.Add((ConvertTo-CanonicalValue -Value $item -Key ''))
        }

        if ($Key -ceq 'env') {
            $items.Sort([Comparison[object]] { param($left, $right) [string]::CompareOrdinal([string](Get-Field $left 'name'), [string](Get-Field $right 'name')) })
        }

        return , $items.ToArray()
    }

    return $Value
}

function Get-TemplateFingerprint {
    param([System.Collections.IDictionary]$Template)

    $comparable = [ordered]@{}
    foreach ($key in $Template.Keys) {
        if ([string]$key -cne 'revisionSuffix') {
            $comparable[[string]$key] = $Template[$key]
        }
    }

    $json = ConvertTo-Json -InputObject (ConvertTo-CanonicalValue -Value $comparable -Key '') -Depth 100 -Compress
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($json))
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-WritableTemplate {
    param($Template)

    $projection = [ordered]@{}
    if ($Template -is [System.Collections.IDictionary]) {
        foreach ($key in $script:WritableTemplateKeys) {
            if ($Template.Contains($key)) {
                $projection[$key] = Copy-Data $Template[$key]
            }
        }
    }

    return $projection
}

function Get-PrimaryContainer {
    param([System.Collections.IDictionary]$Template, [string]$Revision)

    $containers = Get-Field $Template 'containers'
    if ($null -eq $containers -or @($containers).Count -ne 1 -or @($containers)[0] -isnot [System.Collections.IDictionary]) {
        throw "Revision '$Revision' template must contain exactly one container."
    }

    return @($containers)[0]
}

function Get-EnvironmentValue {
    param([System.Collections.IDictionary]$Template, [string]$Revision, [string]$Name, [string]$DefaultValue)

    $entries = Get-Field (Get-PrimaryContainer -Template $Template -Revision $Revision) 'env'
    $selected = @($entries | Where-Object { (Get-StringField $_ 'name') -ieq $Name })
    if ($selected.Count -eq 0) {
        return $DefaultValue
    }

    if ($selected.Count -ne 1 -or -not [string]::IsNullOrEmpty((Get-StringField $selected[0] 'secretRef'))) {
        return $null
    }

    return Get-StringField $selected[0] 'value'
}

function Test-Standard64Revision {
    param([System.Collections.IDictionary]$Revision)
    $value = Get-EnvironmentValue -Template $Revision.Template -Revision $Revision.Name -Name $script:LongIdKey -DefaultValue 'false'
    return $value -ieq 'false'
}

function Test-RevisionReady {
    param([System.Collections.IDictionary]$Revision)
    return $Revision.Active -and ($Revision.ProvisioningState -ieq 'Provisioned') -and ($Revision.HealthState -ieq 'Healthy')
}

function Test-RevisionFailed {
    param([System.Collections.IDictionary]$Revision)
    return ($Revision.ProvisioningState -ieq 'Failed') -or ($Revision.ProvisioningState -ieq 'Canceled')
}

function Get-AppName {
    param([string]$AppResourceId)
    if ($AppResourceId -notmatch '^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.App/containerApps/([^/]+)$') {
        throw "AppResourceId '$AppResourceId' must be a Microsoft.App/containerApps resource ID."
    }

    return $Matches[1]
}

function Assert-BuildIdentity {
    param([string]$BuildId, [string]$Attempt)
    if ($BuildId -cnotmatch '^[0-9]+$' -or $Attempt -cnotmatch '^[0-9]+$') {
        throw "BuildId and Attempt must be decimal digits; received BuildId '$BuildId' and Attempt '$Attempt'."
    }
}

function Get-RevisionIdentity {
    param([string]$AppName, [string]$Suffix, [switch]$Labeled)

    $name = "$AppName--$Suffix"
    $fits = ($AppName.Length + 2 + $Suffix.Length -le 63) -and ($name.Length -le 64)
    if ($Labeled) {
        $fits = $fits -and ($AppName.Length + 3 + $Suffix.Length -le 63)
    }

    if (-not $fits) {
        throw "Revision identity '$name' exceeds the DNS budget for app '$AppName'."
    }

    $label = if ($Labeled) { $Suffix } else { $null }
    return [ordered]@{ Suffix = $Suffix; Name = $name; Label = $label }
}

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

function Get-RoutingDecision {
    param([System.Collections.IDictionary]$State)

    $needsMultiple = $State.Mode -ine 'Multiple'
    if ($State.Mode -inotmatch '^(Single|Multiple)$') {
        return @{ Kind = 'Conflict'; Reason = "unsupported revision mode '$($State.Mode)'." }
    }

    if (@($State.Traffic | Where-Object { -not [string]::IsNullOrEmpty($_.Label) -and (Test-LatestTrafficEntry $_) }).Count -ne 0) {
        return @{ Kind = 'Conflict'; Reason = 'floating latest-revision labels cannot be preserved when revisions change.' }
    }
    $weighted = @($State.Traffic | Where-Object { $_.Weight -gt 0 })
    if ($weighted.Count -ne 1 -or $weighted[0].Weight -ne 100) {
        return @{ Kind = 'Conflict'; Reason = 'ingress must route 100 percent of traffic to exactly one revision.' }
    }

    $entry = $weighted[0]
    if (Test-LatestTrafficEntry $entry) {
        if (-not $needsMultiple) {
            return @{ Kind = 'Conflict'; Reason = 'an existing Multiple-mode app must use a named default revision.' }
        }

        $name = $State.LatestRevisionName
        $active = @($State.Revisions.Values | Where-Object { $_.Active })
        if ([string]::IsNullOrEmpty($name) -or -not $State.Revisions.Contains($name) -or $active.Count -ne 1 -or $active[0].Name -cne $name) {
            return @{ Kind = 'Conflict'; Reason = 'latest-routed traffic can be pinned only when the latest revision is the sole active revision.' }
        }

        $revision = $State.Revisions[$name]
        $problem = Get-BaselineProblem $State $revision
        if ($null -ne $problem) {
            return @{ Kind = 'Conflict'; Reason = "latest revision $problem" }
        }

        return @{ Kind = 'Latest'; Revision = $name; NeedsMultiple = $needsMultiple }
    }

    $name = $entry.RevisionName
    if (-not $State.Revisions.Contains($name)) {
        return @{ Kind = 'Conflict'; Reason = "routed revision '$name' is missing." }
    }

    $problem = Get-BaselineProblem $State $State.Revisions[$name]
    if ($null -ne $problem) {
        return @{ Kind = 'Conflict'; Reason = "routed revision $problem" }
    }

    return @{ Kind = 'Pinned'; Revision = $name; NeedsMultiple = $needsMultiple }
}

function Get-BaselineProblem {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Revision)

    $name = $Revision.Name
    if (-not $Revision.Active -or $Revision.ProvisioningState -ine 'Provisioned') {
        return "'$name' must be active and provisioned to serve as the Standard64 baseline."
    }

    if ($Revision.HealthState -ine 'Healthy') {
        return "'$name' is $($Revision.HealthState) and cannot serve as the Standard64 baseline."
    }

    if ($name -cmatch ('^' + [regex]::Escape($State.AppName) + '--(?!base-)[a-z][a-z0-9]{0,7}-b\d+a\d+$')) {
        return "'$name' is named like a revision profile and cannot serve as the Standard64 baseline."
    }

    $read = { param($Key) Get-EnvironmentValue -Template $Revision.Template -Revision $name -Name $Key }
    if (-not (Test-Standard64Revision $Revision) -or (& $read 'DataStore') -ine 'SqlServer' -or
        (& $read 'FhirServer__Security__Enabled') -ine 'true' -or
        (Get-EnvironmentValue -Template $Revision.Template -Revision $name -Name 'TaskHosting__Enabled' -DefaultValue 'true') -ine 'true') {
        return "'$name' is not an authenticated SQL Standard64 revision with background workers enabled."
    }

    return $null
}

function New-TrafficEntry {
    param([string]$RevisionName, [int]$Weight, [string]$Label, [bool]$LatestRevision)
    $entry = [ordered]@{}
    if ($LatestRevision) { $entry['latestRevision'] = $true } else { $entry['revisionName'] = $RevisionName }
    if (-not [string]::IsNullOrEmpty($Label)) { $entry['label'] = $Label }
    $entry['weight'] = $Weight
    return $entry
}

function New-TrafficList {
    param([System.Collections.IDictionary]$State, [string]$RoutedRevision, [string[]]$RemoveLabels = @(), [object[]]$AddLabels = @())

    $entries = [System.Collections.Generic.List[object]]::new()
    $entries.Add((New-TrafficEntry -RevisionName $RoutedRevision -Weight 100 -Label $null -LatestRevision $false))
    $unlabeled = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $State.Traffic) {
        $latest = Test-LatestTrafficEntry $entry
        if (-not [string]::IsNullOrEmpty($entry.Label)) {
            if ($entry.Label -cin $RemoveLabels) {
                continue
            }

            if ($entry.Weight -gt 0) {
                $bound = if ($latest) { $State.LatestRevisionName } else { $entry.RevisionName }
                $entries.Add((New-TrafficEntry -RevisionName $bound -Weight 0 -Label $entry.Label -LatestRevision $false))
            }
            else {
                $entries.Add((New-TrafficEntry -RevisionName $entry.RevisionName -Weight 0 -Label $entry.Label -LatestRevision $latest))
            }
        }
        elseif ($entry.Weight -eq 0 -and -not $latest -and $entry.RevisionName -cne $RoutedRevision -and
            $State.Revisions.Contains($entry.RevisionName) -and $State.Revisions[$entry.RevisionName].Active -and $unlabeled.Add($entry.RevisionName)) {
            $entries.Add((New-TrafficEntry -RevisionName $entry.RevisionName -Weight 0 -Label $null -LatestRevision $false))
        }
    }

    foreach ($identity in $AddLabels) {
        $entries.Add((New-TrafficEntry -RevisionName $identity.Name -Weight 0 -Label $identity.Label -LatestRevision $false))
    }

    return , $entries.ToArray()
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

function New-DesiredTemplate {
    param(
        [System.Collections.IDictionary]$Source,
        [string]$SourceRevision,
        [string]$Suffix,
        [System.Collections.IDictionary]$Environment,
        [string[]]$RemoveEnvironment = @(),
        [string]$Image,
        [int]$MinReplicas)

    $template = Copy-Data $Source
    $template['revisionSuffix'] = $Suffix
    $container = Get-PrimaryContainer -Template $template -Revision $SourceRevision
    $declared = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $Environment.Keys) {
        if (-not $declared.Add([string]$name)) {
            throw "Revision '$SourceRevision' has duplicate logical environment overrides for '$name'."
        }
    }

    $written = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $environmentEntries = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in (Get-Field $container 'env')) {
        $name = Get-StringField $entry 'name'
        if ($name -in $RemoveEnvironment) {
            continue
        }

        $declaredName = $null
        if ($declared.TryGetValue($name, [ref]$declaredName)) {
            if ($written.Add($declaredName)) {
                $environmentEntries.Add([ordered]@{ name = $declaredName; value = [string]$Environment[$declaredName] })
            }
        }
        else {
            $environmentEntries.Add($entry)
        }
    }

    [string[]]$newNames = @($declared | Where-Object { -not $written.Contains($_) })
    [Array]::Sort($newNames, [StringComparer]::Ordinal)
    foreach ($name in $newNames) {
        $environmentEntries.Add([ordered]@{ name = $name; value = [string]$Environment[$name] })
    }

    $container['env'] = $environmentEntries.ToArray()
    if (-not [string]::IsNullOrEmpty($Image)) {
        $container['image'] = $Image
    }

    $scale = Get-Field $template 'scale'
    if ($scale -isnot [System.Collections.IDictionary]) {
        throw "Revision '$SourceRevision' template has no scale section to copy."
    }

    $scale['minReplicas'] = [int]$MinReplicas
    return $template
}

function Get-ProfileIdentities {
    param([System.Collections.IDictionary]$State, $Catalog, [string]$BuildId, [string]$Attempt)

    $identities = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $Catalog.Profiles) {
        $identity = Get-RevisionIdentity -AppName $State.AppName -Suffix "$($entry.Slug)-b$($BuildId)a$($Attempt)" -Labeled
        $identities.Add([ordered]@{ Profile = $entry; Name = $identity.Name; Label = $identity.Label; Suffix = $identity.Suffix })
    }

    return $identities.ToArray()
}

function Get-LabelTarget {
    param([System.Collections.IDictionary]$State, [string]$Label)

    $entries = @($State.Traffic | Where-Object { $_.Label -ceq $Label })
    if ($entries.Count -eq 0) {
        return $null
    }

    if ($entries.Count -gt 1 -or (Test-LatestTrafficEntry $entries[0])) {
        return '<latest or duplicate>'
    }

    return $entries[0].RevisionName
}

function Test-RevisionRouted {
    param([System.Collections.IDictionary]$State, [string]$Name)
    return @($State.Traffic | Where-Object { $_.RevisionName -ceq $Name -and ($_.Weight -gt 0 -or -not [string]::IsNullOrEmpty($_.Label)) }).Count -gt 0
}

function New-ConflictStep {
    param([string]$Reason)
    return @{ Action = 'Conflict'; Target = ''; Reason = $Reason }
}

function New-PlanTarget {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Request, [System.Collections.IDictionary]$Identity, [string]$Routed)

    $parameters = @{
        Source = $State.Revisions[$Routed].Template
        SourceRevision = $Routed
        Suffix = $Identity.Suffix
        Environment = @{}
        MinReplicas = 0
    }

    $probe = $true
    if ($Request.Kind -ceq 'Profiles') {
        $parameters['Environment'] = $Identity.Profile.Environment
        $probe = $false
    }
    elseif ($Request.Kind -ceq 'Deploy') {
        $parameters['Environment'] = $Request.Environment
        $parameters['RemoveEnvironment'] = $Request.RemoveEnvironment
        $parameters['Image'] = $Request.Image
        $parameters['MinReplicas'] = $Request.MinReplicas
    }

    $template = New-DesiredTemplate @parameters
    return [ordered]@{
        Name = $Identity.Name
        Label = $Identity.Label
        Template = $template
        Fingerprint = Get-TemplateFingerprint $template
        Probe = $probe
    }
}

function Get-TargetConflict {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Target)

    if ($Target.Probe -and -not (Test-Standard64Revision ([ordered]@{ Name = $Target.Name; Template = $Target.Template }))) {
        return New-ConflictStep "baseline successor '$($Target.Name)' would route a long resource ID template"
    }

    if (-not [string]::IsNullOrEmpty($Target.Label)) {
        $labelTarget = Get-LabelTarget -State $State -Label $Target.Label
        if ($null -ne $labelTarget -and $labelTarget -cne $Target.Name) {
            return New-ConflictStep "label '$($Target.Label)' routes to '$labelTarget', not '$($Target.Name)'"
        }
    }

    $existing = $State.Revisions[$Target.Name]
    if ($null -eq $existing) {
        return $null
    }

    if (Test-RevisionFailed $existing) {
        return @{ Action = 'Blocked'; Target = $Target.Name; Reason = "Revision '$($Target.Name)' is $($existing.ProvisioningState) and not ready; it is preserved for diagnosis and a new attempt gets a fresh identity." }
    }

    if ((Get-TemplateFingerprint $existing.Template) -cne $Target.Fingerprint) {
        return New-ConflictStep "revision '$($Target.Name)' already exists with a different template"
    }

    return $null
}

function Get-TargetStep {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Target, [string]$Routed, [string[]]$Probed)

    $existing = $State.Revisions[$Target.Name]
    if ($null -eq $existing) {
        return @{ Action = 'CopyTemplate'; Target = $Target.Name; Template = $Target.Template; Fingerprint = $Target.Fingerprint }
    }

    if (-not (Test-RevisionReady $existing)) {
        return @{ Action = 'AwaitReady'; Target = $Target.Name }
    }

    if ($Target.Name -ceq $Routed) {
        return $null
    }

    if (-not [string]::IsNullOrEmpty($Target.Label)) {
        if ((Get-LabelTarget -State $State -Label $Target.Label) -cne $Target.Name) {
            return @{ Action = 'AddLabel'; Target = $Target.Label; Routed = $Routed; Identity = @{ Name = $Target.Name; Label = $Target.Label } }
        }

        return $null
    }

    if (-not $Target.Probe) {
        return $null
    }

    if ($Target.Name -cnotin $Probed) {
        return @{ Action = 'Probe'; Target = $Target.Name; Url = "https://$($existing.Fqdn)" }
    }

    return @{ Action = 'PinTraffic'; Target = $Target.Name }
}

function Get-RetirementStep {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Request, [string]$Routed, [string]$Keep)

    $slugs = @($Request.Catalog.Profiles | ForEach-Object { [regex]::Escape([string]$_.Slug) }) -join '|'
    $build = 'b{0}a\d+' -f $Request.BuildId
    $labelPattern = '^(?<slug>{0})-{1}$' -f $slugs, $build
    $labels = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $State.Traffic) {
        if ([string]::IsNullOrEmpty($entry.Label) -or $entry.Label -cnotmatch $labelPattern) {
            continue
        }

        $owned = "$($State.AppName)--$($entry.Label)"
        $target = Get-LabelTarget -State $State -Label $entry.Label
        if ($target -cne $owned -or $entry.Weight -ne 0 -or $owned -ceq $Routed -or
            -not (Test-ProfileRevision -State $State -Request $Request -Name $owned -Slug $Matches['slug'] -Routed $Routed)) {
            return New-ConflictStep "label '$($entry.Label)' is not a zero-weight label on its owned profile revision '$owned'"
        }

        $labels.Add($entry.Label)
    }

    $profilePattern = '^{0}--(?<slug>{1})-{2}$' -f [regex]::Escape($State.AppName), $slugs, $build
    $basePattern = '^{0}--base-{1}(?:-m0)?$' -f [regex]::Escape($State.AppName), $build
    $baselineTemplate = New-DesiredTemplate -Source $State.Revisions[$Routed].Template -SourceRevision $Routed -Suffix 'baseline-comparison' -Environment @{} -MinReplicas 0
    $baselineFingerprint = Get-TemplateFingerprint $baselineTemplate
    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($revision in $State.Revisions.Values) {
        if (-not $revision.Active -or $revision.Name -ceq $Keep -or (Test-RevisionFailed $revision)) {
            continue
        }

        if ($revision.Name -cmatch $profilePattern) {
            if (-not (Test-ProfileRevision -State $State -Request $Request -Name $revision.Name -Slug $Matches['slug'] -Routed $Routed)) {
                return New-ConflictStep "revision '$($revision.Name)' does not match the full owned profile template"
            }
        }
        else {
            if ($revision.Name -cnotmatch $basePattern) {
                continue
            }

            $candidate = New-DesiredTemplate -Source $revision.Template -SourceRevision $revision.Name -Suffix 'baseline-comparison' -Environment @{} -MinReplicas 0
            if (-not (Test-RevisionReady $revision) -or (Get-TemplateFingerprint $candidate) -cne $baselineFingerprint) {
                return New-ConflictStep "revision '$($revision.Name)' does not match the full owned baseline template"
            }
        }

        $candidates.Add($revision.Name)
    }

    if ($labels.Count -gt 0) {
        return @{ Action = 'RemoveLabels'; Target = ($labels -join ','); Routed = $Routed; Labels = [string[]]$labels.ToArray() }
    }

    foreach ($name in $candidates) {
        if (-not (Test-RevisionRouted -State $State -Name $name)) {
            return @{ Action = 'Deactivate'; Target = $name }
        }
    }

    return $null
}

function Test-ProfileRevision {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Request, [string]$Name, [string]$Slug, [string]$Routed)

    $revision = $State.Revisions[$Name]
    $descriptor = @($Request.Catalog.Profiles | Where-Object { $_.Slug -ceq $Slug })
    if ($null -eq $revision -or $descriptor.Count -ne 1 -or -not (Test-RevisionReady $revision)) {
        return $false
    }

    $expected = New-DesiredTemplate -Source $State.Revisions[$Routed].Template -SourceRevision $Routed -Suffix 'profile-comparison' -Environment $descriptor[0].Environment -MinReplicas 0
    return (Get-TemplateFingerprint $revision.Template) -ceq (Get-TemplateFingerprint $expected)
}

function Get-RevisionPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$State,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Request,
        [string[]]$Probed = @())

    $buildSuffix = 'b{0}a{1}' -f $Request.BuildId, $Request.Attempt
    switch -CaseSensitive ($Request.Kind) {
        'Profiles' { $identities = @(Get-ProfileIdentities -State $State -Catalog $Request.Catalog -BuildId $Request.BuildId -Attempt $Request.Attempt) }
        'Deploy' { $identities = @(Get-RevisionIdentity -AppName $State.AppName -Suffix "base-$buildSuffix") }
        'Settle' { $identities = @(Get-RevisionIdentity -AppName $State.AppName -Suffix "base-$buildSuffix-m0") }
        default { throw "Unknown revision request kind '$($Request.Kind)'." }
    }

    $routing = Get-RoutingDecision -State $State
    if ($routing.Kind -ceq 'Conflict') {
        return New-ConflictStep $routing.Reason
    }

    $routed = [string]$routing.Revision
    $settledPattern = '^{0}--base-b{1}a\d+-m0$' -f [regex]::Escape($State.AppName), $Request.BuildId
    $settled = $Request.Kind -ceq 'Settle' -and $routed -cmatch $settledPattern
    if ($settled -and [int](Get-Field (Get-Field $State.Revisions[$routed].Template 'scale') 'minReplicas') -ne 0) {
        return New-ConflictStep "settled identity '$routed' has a nonzero minimum replica specification"
    }
    $retirement = $null
    if ($Request.Kind -ceq 'Settle') {
        $ownedPattern = '^{0}--base-b{1}a\d+(?:-m0)?$' -f [regex]::Escape($State.AppName), $Request.BuildId
        if ($routing.NeedsMultiple -or $routing.Kind -ceq 'Latest' -or $routed -cnotmatch $ownedPattern) {
            return New-ConflictStep "settle requires build $($Request.BuildId) to own the pinned baseline, but '$routed' is routed"
        }

        $retirement = Get-RetirementStep -State $State -Request $Request -Routed $routed -Keep $routed
        if ($null -ne $retirement -and $retirement.Action -ceq 'Conflict') {
            return $retirement
        }
    }

    $targets = @()
    if (-not $settled) {
        $targets = @($identities | ForEach-Object { New-PlanTarget -State $State -Request $Request -Identity $_ -Routed $routed })
        foreach ($target in $targets) {
            $conflict = Get-TargetConflict -State $State -Target $target
            if ($null -ne $conflict) {
                return $conflict
            }
        }
    }

    if ($routing.NeedsMultiple) {
        return @{ Action = 'SetMultiple'; Target = $State.AppName; Routed = $routed }
    }

    if ($routing.Kind -ceq 'Latest') {
        return @{ Action = 'PinTraffic'; Target = $routed }
    }

    foreach ($target in $targets) {
        $step = Get-TargetStep -State $State -Target $target -Routed $routed -Probed $Probed
        if ($null -ne $step) {
            return $step
        }
    }

    if ($settled -and $null -ne $retirement) {
        return $retirement
    }

    return @{ Action = 'Done'; Target = $routed }
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

function Get-TrafficSignature {
    param([object[]]$Traffic)
    $entries = @($Traffic | ForEach-Object {
            '{0}|{1}|{2}|{3}' -f [bool](Get-Field $_ 'latestRevision'), (Get-StringField $_ 'revisionName'), (Get-StringField $_ 'label'), [int](Get-Field $_ 'weight')
        })
    return (@($entries | Sort-Object -CaseSensitive) -join ';')
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

function Get-RevisionImage {
    param([System.Collections.IDictionary]$Revision)
    return [string](Get-PrimaryContainer -Template $Revision.Template -Revision $Revision.Name).image
}

function New-ProfileBinding {
    param([System.Collections.IDictionary]$State, [System.Collections.IDictionary]$Identity, [string]$BaselineRevision)

    $revision = $State.Revisions[$Identity.Name]
    return [ordered]@{
        Profile = $Identity.Profile.Name
        ResourceIdProfile = $Identity.Profile.ResourceIdProfile
        CategoryFilter = $Identity.Profile.CategoryFilter
        Revision = $Identity.Name
        Label = $Identity.Label
        Url = "https://$($State.AppName)---$($Identity.Label).$($State.EnvDomain)"
        Image = Get-RevisionImage $revision
        Fingerprint = Get-TemplateFingerprint $revision.Template
        BaselineRevision = $BaselineRevision
    }
}

function Get-ConvergeResult {
    param([System.Collections.IDictionary]$Request, [System.Collections.IDictionary]$State)

    $baseline = [string](Get-RoutingDecision -State $State).Revision
    if ($Request.Kind -ceq 'Deploy') {
        $revision = $State.Revisions[$baseline]
        return [ordered]@{
            Goal = 'Converge'
            BaselineRevision = $baseline
            Image = Get-RevisionImage $revision
            Fingerprint = Get-TemplateFingerprint $revision.Template
        }
    }

    $bindings = @(Get-ProfileIdentities -State $State -Catalog $Request.Catalog -BuildId $Request.BuildId -Attempt $Request.Attempt |
        ForEach-Object { New-ProfileBinding -State $State -Identity $_ -BaselineRevision $baseline })
    $matrix = [ordered]@{}
    foreach ($binding in $bindings) {
        $matrix[$binding.Profile] = [ordered]@{
            SqlRevisionProfile = $binding.Profile
            SqlProfileCategoryFilter = $binding.CategoryFilter
            SqlResourceIdProfile = $binding.ResourceIdProfile
            SqlRevisionBinding = ConvertTo-Json -InputObject $binding -Compress -Depth 10
        }
    }

    return [ordered]@{
        Goal = 'Converge'
        BaselineRevision = $baseline
        Bindings = $bindings
        Matrix = ConvertTo-Json -InputObject $matrix -Compress -Depth 10
    }
}

function Resolve-RevisionSelection {
    param(
        [System.Collections.IDictionary]$State,
        [object]$Catalog,
        [string]$RevisionProfile,
        [System.Collections.IDictionary]$Binding,
        [switch]$Expect)

    if ($RevisionProfile -ceq $Catalog.Baseline.Name) {
        $routing = Get-RoutingDecision -State $State
        if ($routing.Kind -ceq 'Conflict') {
            throw "Conflict on '$($State.AppName)': $($routing.Reason)"
        }

        $name = [string]$routing.Revision
        $entry = [ordered]@{ Name = $Catalog.Baseline.Name; ResourceIdProfile = $Catalog.Baseline.ResourceIdProfile; Environment = [ordered]@{} }
        $label = $null
        $url = "https://$($State.Fqdn)"
        $baseline = $name
        if ($null -ne $Binding -and ($Binding.Revision -cne $name -or $Binding.Url -cne $url)) {
            throw "Conflict on '$($State.AppName)': default traffic routes to '$name' at '$url', not the bound '$($Binding.Revision)' at '$($Binding.Url)'."
        }
    }
    else {
        $entries = @($Catalog.Profiles | Where-Object { $_.Name -ceq $RevisionProfile })
        if ($entries.Count -ne 1) {
            throw "Revision profile '$RevisionProfile' is not in the catalog."
        }

        $entry = $entries[0]
        if ($null -eq $Binding) {
            throw "Revision profile '$RevisionProfile' requires the producer's captured -Binding."
        }

        if ($Binding.Profile -cne $RevisionProfile -or $Binding.ResourceIdProfile -cne $entry.ResourceIdProfile -or $Binding.CategoryFilter -cne $entry.CategoryFilter) {
            throw "Conflict on '$($State.AppName)': the binding does not describe catalog profile '$RevisionProfile'."
        }

        $name = [string]$Binding.Revision
        $label = [string]$Binding.Label
        $target = Get-LabelTarget -State $State -Label $label
        if ($name -cne "$($State.AppName)--$label" -or $target -cne $name) {
            throw "Conflict on '$($State.AppName)': label '$label' routes to '$target', not the bound owned revision '$name'."
        }

        $url = "https://$($State.AppName)---$label.$($State.EnvDomain)"
        if ($Binding.Url -cne $url) {
            throw "Conflict on '$($State.AppName)': the bound endpoint '$($Binding.Url)' is not the label endpoint '$url'."
        }

        $routing = Get-RoutingDecision -State $State
        $baseline = [string]$Binding.BaselineRevision
        if ($routing.Kind -cne 'Pinned' -or $routing.Revision -cne $baseline) {
            throw "Conflict on '$($State.AppName)': default traffic no longer routes to the bound baseline '$baseline'."
        }
    }

    $revision = $State.Revisions[$name]
    if ($null -eq $revision) {
        throw "Conflict on '$($State.AppName)': selected revision '$name' does not exist."
    }

    if ($entry.ResourceIdProfile -ceq 'Standard64' -and -not (Test-Standard64Revision $revision)) {
        throw "Conflict on '$($State.AppName)': selected revision '$name' is not a Standard64 revision."
    }

    foreach ($key in $entry.Environment.Keys) {
        $value = Get-EnvironmentValue -Template $revision.Template -Revision $name -Name $key
        if ($value -cne $entry.Environment[$key]) {
            throw "Conflict on '$($State.AppName)': selected revision '$name' does not declare $key for profile '$($entry.Name)'."
        }
    }

    $image = Get-RevisionImage $revision
    $fingerprint = Get-TemplateFingerprint $revision.Template
    if ($Expect -and $null -ne $Binding -and ($fingerprint -cne $Binding.Fingerprint -or $image -cne $Binding.Image)) {
        throw "Revision '$name' drift: its template no longer matches the producer's captured binding."
    }

    $settings = [ordered]@{}
    foreach ($key in @($script:SettingNames) + @($entry.Environment.Keys)) {
        if (-not $settings.Contains($key)) {
            $settings[$key] = Get-EnvironmentValue -Template $revision.Template -Revision $name -Name $key
        }
    }

    $container = Get-PrimaryContainer -Template $revision.Template -Revision $name
    return [ordered]@{
        Profile = $entry.Name
        ResourceIdProfile = $entry.ResourceIdProfile
        Revision = $name
        Label = $label
        Url = $url
        Image = $image
        Ready = Test-RevisionReady $revision
        Fingerprint = $fingerprint
        BaselineRevision = $baseline
        Settings = $settings
        EnvironmentNames = @($container['env'] | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_['name'] })
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

function Invoke-ArmRequest {
    param([Parameter(Mandatory)][string]$Method, [Parameter(Mandatory)][string]$Path, [object]$Body)

    if ($null -eq $script:AccessToken) {
        throw 'The revision transport requires an explicit ARM access token.'
    }

    $query = if ($Path.Contains('?')) { '' } else { "?api-version=$($script:ApiVersion)" }
    $parameters = @{
        Method = $Method
        Uri = "https://management.azure.com$Path$query"
        Authentication = 'Bearer'
        Token = $script:AccessToken
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        $parameters['Body'] = ConvertTo-Json -InputObject $Body -Depth 50 -Compress
        $parameters['ContentType'] = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @parameters
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        $isRevisionRead = $Method -ceq 'GET' -and $Path -cmatch '/revisions/[^/?]+$'
        if ($isRevisionRead -and $_.Exception.Response.StatusCode -eq [System.Net.HttpStatusCode]::NotFound) {
            return $null
        }

        throw "ARM $Method $Path failed with HTTP $([int]$_.Exception.Response.StatusCode)."
    }

    if ([string]::IsNullOrWhiteSpace([string]$response.Content)) {
        return $null
    }

    return ConvertFrom-Json -InputObject ([string]$response.Content) -AsHashtable -Depth 100
}

function Invoke-RevisionHealthProbe {
    param([Parameter(Mandatory)][string]$Url)

    try {
        $response = Invoke-WebRequest -Uri "$Url/health/check" -Method 'GET' -SkipHttpErrorCheck -TimeoutSec 30 -ErrorAction Stop
        return $response.StatusCode -eq 200
    }
    catch [System.Net.Http.HttpRequestException] {
        return $false
    }
    catch [System.Threading.Tasks.TaskCanceledException] {
        return $false
    }
    catch [System.TimeoutException] {
        return $false
    }
}

Export-ModuleMember -Function Get-RevisionCatalog, Get-RevisionPlan, Invoke-RevisionGoal
