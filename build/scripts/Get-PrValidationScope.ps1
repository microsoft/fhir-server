<#
.SYNOPSIS
    Classifies a pull request's validation scope (lean, shared or full) from its changed files, labels and target.

.DESCRIPTION
    Implements the path rules of ADR-2611 (docs/arch/adr-2611-tiered-pr-validation.md, "Lean default with
    path-triggered depth"):

      full    any changed path is in a src/ or test/ project whose directory name contains Cosmos, or is a Stu3, R4B
              or R5 project (src/*.Stu3.*, src/*.R4B.*, src/*.R5.*, same under test/), or is under build/ or
              samples/templates/, or is an authentication path (src/**/Security/**,
              src/Microsoft.Health.Fhir.Api.OpenIddict/**); or the PR has the full-matrix label; or it targets
              release/* or hotfix/*. (The ADR's ACA and Key Vault rules, build/**/*keyvault* and build/**/*aca*,
              are inside build/ and samples/templates/, so the rules above already cover them.) Also full: the
              repository-root build inputs Directory.Build.props, Directory.Build.targets, global.json and
              nuget.config, which change every project's build (ruling R15; a nested one follows its project's
              rule).
              The root Directory.Packages.props is classified by its changed packages (rulings R15b, R15c): a
              package whose name matches Cosmos, ^Azure., Identity, OpenIddict, KeyVault or SqlClient is full; any
              other package is shared, since it compiles into every version. A changed version property counts as
              the packages that reference it; a build-wide property (DotNetSdkPackageVersion, the central package
              management switches) is full; any other property is shared. No changed package or property at all is
              full, failing closed.
      shared  not full, and a changed path is in a src/*.Shared.* or test/*.Shared.* project.
      lean    otherwise.

    Fails closed: an empty or truncated file list gives full. Unreadable labels do not: the PR is classified by its
    paths and Warnings says how to request the full matrix (ruling R16).

    Pure: no I/O. Paths are matched case-insensitively after converting backslashes to slashes; a project is the
    second path segment under src/ or test/, so only the project directory name (not the file name) counts for the
    project rules.

.PARAMETER ChangedFiles
    Repository-relative paths of the files the PR changes.

.PARAMETER Labels
    The PR's labels.

.PARAMETER TargetBranch
    The PR's target branch, with or without refs/heads/.

.PARAMETER IsFork
    Whether the PR comes from a fork. A fork whose scope is full needs a maintainer, which routes it away from the
    deployed stages. Routing only, not a security boundary: the approval check on the privileged service connection
    is the gate (ADR-2611).

.PARAMETER FileListTruncated
    Whether ChangedFiles is incomplete.

.PARAMETER LabelsUnavailableReason
    Why the labels could not be read (for example an HTTP error, a timeout or a rate limit); empty when they were
    read. Labels is then ignored.

.PARAMETER ChangedPackages
    Names of the packages that the root Directory.Packages.props diff changes, directly or through a changed version
    property (Get-PackagePropsChanges.ps1). Used only when ChangedFiles includes that file.

.PARAMETER ChangedPackageProperties
    Names of the properties that the root Directory.Packages.props diff changes (Get-PackagePropsChanges.ps1). A
    build-wide property (the .NET SDK release train, the central package management switches) is full; any other is
    shared, and is classified through its packages. Used only when ChangedFiles includes that file.

.OUTPUTS
    [pscustomobject] with Scope ('lean' | 'shared' | 'full'), Reasons (string[]: each rule that matched, with the path
    or value that matched it), FullRequiresMaintainer (true only when Scope is full and IsFork) and Warnings
    (string[], for the caller to log as warnings).
#>
[CmdletBinding()]
param(
    [AllowEmptyCollection()]
    [string[]] $ChangedFiles = @(),

    [AllowEmptyCollection()]
    [string[]] $Labels = @(),

    [string] $TargetBranch = '',

    [bool] $IsFork = $false,

    [bool] $FileListTruncated = $false,

    [string] $LabelsUnavailableReason = '',

    [AllowEmptyCollection()]
    [string[]] $ChangedPackages = @(),

    [AllowEmptyCollection()]
    [string[]] $ChangedPackageProperties = @()
)

$ErrorActionPreference = 'Stop'

# Regexes over the lower-cased project directory name (second segment under src/ or test/).
$fullProjectRules = [ordered]@{
    'cosmos project'          = 'cosmos'
    'Stu3, R4B or R5 project' = '\.(stu3|r4b|r5)\.'
}
$sharedProjectRule = '\.shared\.'

# Regexes over the whole lower-cased path.
$fullPathRules = [ordered]@{
    'build'                       = '^build/'
    'samples templates'           = '^samples/templates/'
    'authentication (Security)'   = '^src/([^/]+/)*security/'
    'authentication (OpenIddict)' = '^src/microsoft\.health\.fhir\.api\.openiddict/'
    'root build input'            = '^(directory\.build\.props|directory\.build\.targets|global\.json|nuget\.config)$'
}

# Package names (root Directory.Packages.props) that need the full matrix: data stores, Azure and authentication.
$fullPackageRule = 'Cosmos|^Azure\.|Identity|OpenIddict|KeyVault|SqlClient'

# Directory.Packages.props properties that change the whole build rather than a few packages: the .NET SDK release
# train and the central package management switches (ruling R15c).
$fullPackagePropertyRule = '^(DotNetSdkPackageVersion|ManagePackageVersionsCentrally|CentralPackageVersionOverrideEnabled|CentralPackageTransitivePinningEnabled)$'

$full = [System.Collections.Generic.List[string]]::new()
$shared = [System.Collections.Generic.List[string]]::new()

if ($FileListTruncated) {
    $full.Add('fail-closed: the changed file list is truncated')
}

$files = @($ChangedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($files.Count -eq 0) {
    $full.Add('fail-closed: no changed files')
}

$warnings = [System.Collections.Generic.List[string]]::new()
if ($LabelsUnavailableReason) {
    $warnings.Add("Could not read PR labels ($LabelsUnavailableReason); classifying by paths only. Add the full-matrix label and re-run, or queue with tier1Scope=full.")
}
elseif (@($Labels | Where-Object { $_ -ieq 'full-matrix' }).Count -gt 0) {
    $full.Add('label: full-matrix')
}

if ($TargetBranch -imatch '^(refs/heads/)?(release|hotfix)/') {
    $full.Add("target branch: $TargetBranch")
}

foreach ($file in $files) {
    $path = $file.Trim().Replace('\', '/').TrimStart('/')
    $lower = $path.ToLowerInvariant()

    foreach ($rule in $fullPathRules.GetEnumerator()) {
        if ($lower -match $rule.Value) {
            $full.Add("$($rule.Key): $path")
        }
    }

    if ($lower -eq 'directory.packages.props') {
        $packages = @($ChangedPackages | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $properties = @($ChangedPackageProperties | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($packages.Count -eq 0 -and $properties.Count -eq 0) {
            $full.Add("fail-closed: ${path} changed but no changed packages or properties were found")
        }

        foreach ($package in $packages) {
            if ($package -imatch $fullPackageRule) { $full.Add("package: $package") } else { $shared.Add("package: $package") }
        }

        foreach ($property in $properties) {
            if ($property -imatch $fullPackagePropertyRule) { $full.Add("build property: $property") } else { $shared.Add("package property: $property") }
        }
    }

    if ($lower -match '^(src|test)/([^/]+)/') {
        $project = $Matches[2]
        $isFull = $false
        foreach ($rule in $fullProjectRules.GetEnumerator()) {
            if ($project -match $rule.Value) {
                $full.Add("$($rule.Key): $path")
                $isFull = $true
            }
        }

        if (-not $isFull -and $project -match $sharedProjectRule) {
            $shared.Add("shared project: $path")
        }
    }
}

if ($full.Count -gt 0) {
    $scope = 'full'
    $reasons = $full.ToArray()
}
elseif ($shared.Count -gt 0) {
    $scope = 'shared'
    $reasons = $shared.ToArray()
}
else {
    $scope = 'lean'
    $reasons = @('no full or shared path changed')
}

[pscustomobject]@{
    Scope                  = $scope
    Reasons                = [string[]] $reasons
    FullRequiresMaintainer = ($scope -eq 'full' -and $IsFork)
    Warnings               = [string[]] $warnings.ToArray()
}
