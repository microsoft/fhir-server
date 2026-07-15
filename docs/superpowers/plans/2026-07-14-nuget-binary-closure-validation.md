# NuGet Binary Closure Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a blocking, reproducible binary-closure check for every non-symbol NuGet package produced by the shared OSS PR and CI packaging flow.

**Architecture:** A cross-platform PowerShell validator discovers package metadata, creates one multi-target temporary consumer project per package, restores the exact locally built package, publishes each target-framework closure, and compares `checkbinarycompat` output with checked-in baselines. A reusable Azure Pipelines template installs the pinned checker, runs the validator after `dotnet pack`, and publishes diagnostic reports even when validation fails.

**Tech Stack:** PowerShell 7, .NET SDK 9/8, NuGet package archives, `checkbinarycompat` 1.0.45, Azure Pipelines YAML.

---

## File Structure

- Create `build/scripts/NuGetBinaryClosure.psm1`: pure package-discovery, baseline-inventory, generated-project, and NuGet-config helpers.
- Create `build/scripts/Validate-NuGetBinaryClosure.ps1`: orchestration and native process failure aggregation.
- Create `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`: dependency-free PowerShell test runner for deterministic helper behavior.
- Create `build/steps/validate-nuget-binary-closure.yml`: pinned tool installation, validation invocation, and report publication.
- Modify `build/jobs/package.yml`: invoke validation after `dotnet pack` and before any package artifact publication.
- Create `build/binarycompat/*.txt`: 90 generated baselines, one for each of 45 packages and two target frameworks.

### Baseline inventory

Each package below requires both `.net8.0.txt` and `.net9.0.txt` files under `build/binarycompat/`:

```text
Fhir.BlobRewriter
Fhir.Importer
Fhir.IndexRebuilder
Fhir.RegisterAndMonitorImport
Microsoft.Health.Extensions.Xunit
Microsoft.Health.Fhir.Api
Microsoft.Health.Fhir.Api.OpenIddict
Microsoft.Health.Fhir.Azure
Microsoft.Health.Fhir.Core
Microsoft.Health.Fhir.CosmosDb
Microsoft.Health.Fhir.CosmosDb.Core
Microsoft.Health.Fhir.CosmosDb.Initialization
Microsoft.Health.Fhir.R4.Api
Microsoft.Health.Fhir.R4.Client
Microsoft.Health.Fhir.R4.Core
Microsoft.Health.Fhir.R4.ResourceParser
Microsoft.Health.Fhir.R4.Tests.E2E
Microsoft.Health.Fhir.R4.Web
Microsoft.Health.Fhir.R4B.Api
Microsoft.Health.Fhir.R4B.Client
Microsoft.Health.Fhir.R4B.Core
Microsoft.Health.Fhir.R4B.Tests.E2E
Microsoft.Health.Fhir.R4B.Web
Microsoft.Health.Fhir.R5.Api
Microsoft.Health.Fhir.R5.Client
Microsoft.Health.Fhir.R5.Core
Microsoft.Health.Fhir.R5.Tests.E2E
Microsoft.Health.Fhir.R5.Web
Microsoft.Health.Fhir.SchemaManager
Microsoft.Health.Fhir.SchemaManager.Console
Microsoft.Health.Fhir.SqlServer
Microsoft.Health.Fhir.Store.Utils
Microsoft.Health.Fhir.Stu3.Api
Microsoft.Health.Fhir.Stu3.Client
Microsoft.Health.Fhir.Stu3.Core
Microsoft.Health.Fhir.Stu3.Tests.E2E
Microsoft.Health.Fhir.Stu3.Web
Microsoft.Health.Fhir.Tests.Common
Microsoft.Health.Fhir.ValueSets
Microsoft.Health.Internal.Fhir.EventsReader
Microsoft.Health.Internal.Fhir.Exporter
Microsoft.Health.Internal.Fhir.SqlScriptRunner
Microsoft.Health.Internal.SmartLauncher
Microsoft.Health.TaskManagement
ResourceParser
```

## Task 1: Package and Baseline Helpers

**Files:**
- Create: `build/scripts/NuGetBinaryClosure.psm1`
- Create: `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`

- [ ] **Step 1: Write the failing helper tests**

Create `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`:

```powershell
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot '..\NuGetBinaryClosure.psm1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "fhir-binary-closure-$([guid]::NewGuid())"
$script:Failures = 0

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-SequenceEqual {
    param(
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][object[]]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    Assert-Equal ($Expected -join '|') ($Actual -join '|') $Message
}

function Invoke-TestCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Test
    )

    try {
        & $Test
        Write-Host "PASS: $Name"
    }
    catch {
        $script:Failures++
        Write-Error "FAIL: $Name`n$($_.Exception.Message)" -ErrorAction Continue
    }
}

function New-TestPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string[]]$Frameworks
    )

    $archive = [System.IO.Compression.ZipFile]::Open(
        $Path,
        [System.IO.Compression.ZipArchiveMode]::Create)

    try {
        $nuspec = $archive.CreateEntry("$Id.nuspec")
        $writer = [System.IO.StreamWriter]::new($nuspec.Open())
        try {
            $writer.Write(
                "<?xml version=`"1.0`"?><package xmlns=`"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd`"><metadata><id>$Id</id><version>$Version</version></metadata></package>")
        }
        finally {
            $writer.Dispose()
        }

        foreach ($framework in $Frameworks) {
            $entry = $archive.CreateEntry("lib/$framework/$Id.dll")
            $stream = $entry.Open()
            try {
                $stream.WriteByte(0)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    Invoke-TestCase 'module exists and imports' {
        Import-Module $modulePath -Force
    }

    Invoke-TestCase 'discovers package identity and sorted supported frameworks' {
        $packagePath = Join-Path $testRoot 'sample.nupkg'
        New-TestPackage $packagePath 'Microsoft.Health.Sample' '1.2.3-preview.1' @('net9.0', 'net8.0', 'net7.0')

        $metadata = Get-NuGetPackageMetadata `
            -PackagePath $packagePath `
            -SupportedFrameworks @('net8.0', 'net9.0')

        Assert-Equal 'Microsoft.Health.Sample' $metadata.Id 'Package ID'
        Assert-Equal '1.2.3-preview.1' $metadata.Version 'Package version'
        Assert-SequenceEqual @('net8.0', 'net9.0') $metadata.Frameworks 'Frameworks'
    }

    Invoke-TestCase 'rejects packages without a supported managed framework' {
        $packagePath = Join-Path $testRoot 'unsupported.nupkg'
        New-TestPackage $packagePath 'Microsoft.Health.Unsupported' '1.0.0' @('net7.0')

        try {
            Get-NuGetPackageMetadata `
                -PackagePath $packagePath `
                -SupportedFrameworks @('net8.0', 'net9.0')
            throw 'Expected unsupported package metadata to fail.'
        }
        catch {
            if (-not $_.Exception.Message.Contains('no supported managed target framework')) {
                throw
            }
        }
    }

    Invoke-TestCase 'returns non-symbol packages in ordinal file-name order' {
        New-Item -ItemType File -Path (Join-Path $testRoot 'b.nupkg') -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $testRoot 'a.nupkg') -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $testRoot 'a.snupkg') -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $testRoot 'a.symbols.nupkg') -Force | Out-Null

        $names = @(Get-NuGetPackageFiles -PackageDirectory $testRoot | ForEach-Object Name)

        Assert-SequenceEqual @('a.nupkg', 'b.nupkg', 'sample.nupkg', 'unsupported.nupkg') $names 'Package files'
    }

    Invoke-TestCase 'reports missing and orphaned baselines deterministically' {
        $baselineDirectory = Join-Path $testRoot 'baselines'
        New-Item -ItemType Directory -Path $baselineDirectory | Out-Null
        New-Item -ItemType File -Path (Join-Path $baselineDirectory 'Package.A.net8.0.txt') | Out-Null
        New-Item -ItemType File -Path (Join-Path $baselineDirectory 'Orphan.net9.0.txt') | Out-Null

        $closures = @(
            [pscustomobject]@{ SafePackageId = 'Package.A'; Framework = 'net8.0' }
            [pscustomobject]@{ SafePackageId = 'Package.B'; Framework = 'net9.0' }
        )
        $inventory = Compare-BinaryClosureBaselineInventory `
            -Closures $closures `
            -BaselineDirectory $baselineDirectory

        Assert-SequenceEqual @('Package.B.net9.0.txt') $inventory.Missing 'Missing baselines'
        Assert-SequenceEqual @('Orphan.net9.0.txt') $inventory.Orphaned 'Orphaned baselines'
    }

    Invoke-TestCase 'writes exact package reference and all target frameworks' {
        $consumerDirectory = Join-Path $testRoot 'consumer'
        New-BinaryClosureConsumerProject `
            -Directory $consumerDirectory `
            -PackageId 'Microsoft.Health.Sample' `
            -PackageVersion '1.2.3-preview.1' `
            -Frameworks @('net8.0', 'net9.0')

        [xml]$project = Get-Content (Join-Path $consumerDirectory 'Consumer.csproj')
        Assert-Equal 'net8.0;net9.0' $project.Project.PropertyGroup.TargetFrameworks 'Target frameworks'
        Assert-Equal 'Microsoft.Health.Sample' $project.Project.ItemGroup.PackageReference.Include 'Package reference'
        Assert-Equal '[1.2.3-preview.1]' $project.Project.ItemGroup.PackageReference.Version 'Exact package version'
    }

    Invoke-TestCase 'adds the local package source and mapping before repository sources' {
        $sourceConfig = Join-Path $testRoot 'source.config'
        $targetConfig = Join-Path $testRoot 'target.config'
        $packageDirectory = Join-Path $testRoot 'packages'
        New-Item -ItemType Directory -Path $packageDirectory | Out-Null
        @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@ | Set-Content -Path $sourceConfig

        New-BinaryClosureRestoreConfig `
            -SourceConfigPath $sourceConfig `
            -DestinationPath $targetConfig `
            -PackageDirectory $packageDirectory

        [xml]$config = Get-Content $targetConfig
        $sources = @($config.configuration.packageSources.add)
        Assert-Equal 'binary-closure-local' $sources[0].key 'First package source'
        Assert-Equal 'binary-closure-local' $config.configuration.packageSourceMapping.packageSource[0].key 'First mapping'
        Assert-Equal '*' $config.configuration.packageSourceMapping.packageSource[0].package.pattern 'Local mapping pattern'
    }
}
finally {
    Remove-Item -Path $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) {
    throw "$script:Failures test case(s) failed."
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: FAIL because `build/scripts/NuGetBinaryClosure.psm1` does not exist.

- [ ] **Step 3: Implement the helper module**

Create `build/scripts/NuGetBinaryClosure.psm1`:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BinaryClosureSafePathSegment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Value
    )

    return [regex]::Replace($Value, '[^A-Za-z0-9._-]', '_')
}

function Get-NuGetPackageFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageDirectory
    )

    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Package directory not found: $PackageDirectory"
    }

    return @(
        Get-ChildItem -LiteralPath $PackageDirectory -File |
            Where-Object {
                $_.Name.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.Name.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.Name.EndsWith('.symbols.nupkg', [StringComparison]::OrdinalIgnoreCase)
            } |
            Sort-Object -Property Name
    )
}

function Get-NuGetPackageMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string[]]$SupportedFrameworks
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntries = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) -and
                    -not $_.FullName.Contains('/')
                }
        )

        if ($nuspecEntries.Count -ne 1) {
            throw "Expected one root .nuspec in '$PackagePath', found $($nuspecEntries.Count)."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $namespace = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespace.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
        $id = $nuspec.SelectSingleNode('/n:package/n:metadata/n:id', $namespace).InnerText
        $version = $nuspec.SelectSingleNode('/n:package/n:metadata/n:version', $namespace).InnerText

        $supported = [System.Collections.Generic.HashSet[string]]::new(
            $SupportedFrameworks,
            [StringComparer]::OrdinalIgnoreCase)
        $frameworks = @(
            $archive.Entries |
                ForEach-Object {
                    if ($_.FullName -match '^(?:lib|ref)/([^/]+)/[^/]+\.dll$' -and $supported.Contains($Matches[1])) {
                        $Matches[1]
                    }
                } |
                Sort-Object -Unique
        )

        if ($frameworks.Count -eq 0) {
            throw "Package '$id' has no supported managed target framework."
        }

        return [pscustomobject]@{
            Id = $id
            SafePackageId = ConvertTo-BinaryClosureSafePathSegment $id
            Version = $version
            Frameworks = $frameworks
            Path = (Resolve-Path -LiteralPath $PackagePath).Path
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Compare-BinaryClosureBaselineInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Closures,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$BaselineDirectory
    )

    $expected = @(
        $Closures |
            ForEach-Object { "$($_.SafePackageId).$($_.Framework).txt" } |
            Sort-Object -Unique
    )
    $actual = @()
    if (Test-Path -LiteralPath $BaselineDirectory -PathType Container) {
        $actual = @(
            Get-ChildItem -LiteralPath $BaselineDirectory -File -Filter '*.txt' |
                ForEach-Object Name |
                Sort-Object -Unique
        )
    }

    return [pscustomobject]@{
        Missing = @($expected | Where-Object { $_ -notin $actual })
        Orphaned = @($actual | Where-Object { $_ -notin $expected })
    }
}

function New-BinaryClosureConsumerProject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][string[]]$Frameworks
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $escapedId = [System.Security.SecurityElement]::Escape($PackageId)
    $escapedVersion = [System.Security.SecurityElement]::Escape($PackageVersion)
    $frameworkList = [System.Security.SecurityElement]::Escape(($Frameworks -join ';'))

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>$frameworkList</TargetFrameworks>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$escapedId" Version="[$escapedVersion]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $Directory 'Consumer.csproj')

    'return 0;' | Set-Content -LiteralPath (Join-Path $Directory 'Program.cs')
}

function New-BinaryClosureRestoreConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourceConfigPath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$PackageDirectory
    )

    [xml]$config = Get-Content -LiteralPath $SourceConfigPath -Raw
    $sources = $config.configuration.packageSources
    $source = $config.CreateElement('add')
    $source.SetAttribute('key', 'binary-closure-local')
    $source.SetAttribute('value', (Resolve-Path -LiteralPath $PackageDirectory).Path)
    $firstSource = @($sources.add)[0]
    if ($null -eq $firstSource) {
        $sources.AppendChild($source) | Out-Null
    }
    else {
        $sources.InsertBefore($source, $firstSource) | Out-Null
    }

    $mapping = $config.configuration.packageSourceMapping
    if ($null -eq $mapping) {
        $mapping = $config.CreateElement('packageSourceMapping')
        $config.configuration.AppendChild($mapping) | Out-Null
    }

    $localMapping = $config.CreateElement('packageSource')
    $localMapping.SetAttribute('key', 'binary-closure-local')
    $pattern = $config.CreateElement('package')
    $pattern.SetAttribute('pattern', '*')
    $localMapping.AppendChild($pattern) | Out-Null
    $firstMapping = @($mapping.packageSource)[0]
    if ($null -eq $firstMapping) {
        $mapping.AppendChild($localMapping) | Out-Null
    }
    else {
        $mapping.InsertBefore($localMapping, $firstMapping) | Out-Null
    }

    $destinationDirectory = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $config.Save($DestinationPath)
}

Export-ModuleMember -Function @(
    'Compare-BinaryClosureBaselineInventory',
    'ConvertTo-BinaryClosureSafePathSegment',
    'Get-NuGetPackageFiles',
    'Get-NuGetPackageMetadata',
    'New-BinaryClosureConsumerProject',
    'New-BinaryClosureRestoreConfig'
)
```

- [ ] **Step 4: Run the helper tests**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: seven `PASS:` lines and exit code 0.

- [ ] **Step 5: Commit the helper layer**

```powershell
git add build/scripts/NuGetBinaryClosure.psm1 build/scripts/tests/NuGetBinaryClosure.Tests.ps1
git commit -m "Add NuGet closure validation helpers" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

## Task 2: Closure Validation Orchestrator

**Files:**
- Create: `build/scripts/Validate-NuGetBinaryClosure.ps1`
- Modify: `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`

- [ ] **Step 1: Add failing process and inventory tests**

Append these cases before the `finally` block in `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`:

```powershell
    Invoke-TestCase 'safe package IDs replace path separators and spaces' {
        Assert-Equal 'Package_Name_Test' `
            (ConvertTo-BinaryClosureSafePathSegment 'Package Name/Test') `
            'Safe package ID'
    }

    Invoke-TestCase 'duplicate package identities are detectable by ID and version' {
        $packages = @(
            [pscustomobject]@{ Id = 'Package.A'; Version = '1.0.0' }
            [pscustomobject]@{ Id = 'Package.A'; Version = '1.0.0' }
        )
        $duplicates = @(
            $packages |
                Group-Object { "$($_.Id)/$($_.Version)" } |
                Where-Object Count -gt 1
        )

        Assert-Equal 1 $duplicates.Count 'Duplicate identity count'
    }
```

- [ ] **Step 2: Run the tests to verify the new expectations**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: PASS. These tests lock down invariants consumed by the orchestration script before implementation.

- [ ] **Step 3: Implement the validator**

Create `build/scripts/Validate-NuGetBinaryClosure.ps1`:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaselineDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReportDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WorkDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$NuGetConfigPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CheckBinaryCompatPath,

    [string[]]$SupportedFrameworks = @('net8.0', 'net9.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'NuGetBinaryClosure.psm1') -Force

$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$BaselineDirectory = [System.IO.Path]::GetFullPath($BaselineDirectory)
$ReportDirectory = [System.IO.Path]::GetFullPath($ReportDirectory)
$WorkDirectory = [System.IO.Path]::GetFullPath($WorkDirectory)
$NuGetConfigPath = (Resolve-Path -LiteralPath $NuGetConfigPath).Path
$CheckBinaryCompatPath = (Get-Command $CheckBinaryCompatPath -ErrorAction Stop).Source

$failures = [System.Collections.Generic.List[string]]::new()
$emptyBaselineDirectory = Join-Path $WorkDirectory 'empty-baselines'
$consumerRoot = Join-Path $WorkDirectory 'consumers'
$packageCache = Join-Path $WorkDirectory 'package-cache'
$restoreConfig = Join-Path $WorkDirectory 'NuGet.config'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Invoke-BinaryClosureValidation {
    New-Item -ItemType Directory -Path @(
        $BaselineDirectory,
        $ReportDirectory,
        $WorkDirectory,
        $emptyBaselineDirectory,
        $consumerRoot,
        $packageCache
    ) -Force | Out-Null

    $packageFiles = @(Get-NuGetPackageFiles -PackageDirectory $PackageDirectory)
    if ($packageFiles.Count -eq 0) {
        throw "No non-symbol NuGet packages found in '$PackageDirectory'."
    }

    $packages = @(
        foreach ($packageFile in $packageFiles) {
            Get-NuGetPackageMetadata `
                -PackagePath $packageFile.FullName `
                -SupportedFrameworks $SupportedFrameworks
        }
    )

    $duplicates = @(
        $packages |
            Group-Object { "$($_.Id)/$($_.Version)" } |
            Where-Object Count -gt 1
    )
    if ($duplicates.Count -gt 0) {
        throw "Duplicate package identities: $($duplicates.Name -join ', ')"
    }

    $closures = @(
        foreach ($package in $packages) {
            foreach ($framework in $package.Frameworks) {
                [pscustomobject]@{
                    SafePackageId = $package.SafePackageId
                    Framework = $framework
                }
            }
        }
    )
    $inventory = Compare-BinaryClosureBaselineInventory `
        -Closures $closures `
        -BaselineDirectory $BaselineDirectory

    foreach ($missing in $inventory.Missing) {
        $failures.Add("Missing baseline: $missing")
    }
    foreach ($orphaned in $inventory.Orphaned) {
        $failures.Add("Orphaned baseline: $orphaned")
    }

    New-BinaryClosureRestoreConfig `
        -SourceConfigPath $NuGetConfigPath `
        -DestinationPath $restoreConfig `
        -PackageDirectory $PackageDirectory

    foreach ($package in $packages) {
        $consumerDirectory = Join-Path $consumerRoot $package.SafePackageId
        try {
            New-BinaryClosureConsumerProject `
                -Directory $consumerDirectory `
                -PackageId $package.Id `
                -PackageVersion $package.Version `
                -Frameworks $package.Frameworks

            $projectPath = Join-Path $consumerDirectory 'Consumer.csproj'
            Invoke-DotNet `
                -Arguments @(
                    'restore',
                    $projectPath,
                    '--configfile', $restoreConfig,
                    '--packages', $packageCache,
                    '--no-cache',
                    '--force',
                    '--verbosity', 'minimal'
                ) `
                -FailureMessage "Restore failed for $($package.Id)"

            $assets = Get-Content (Join-Path $consumerDirectory 'obj\project.assets.json') -Raw | ConvertFrom-Json
            $restoredIdentity = @(
                $assets.libraries.PSObject.Properties.Name |
                    Where-Object {
                        $_.StartsWith("$($package.Id)/", [StringComparison]::OrdinalIgnoreCase)
                    }
            )
            if ($restoredIdentity.Count -ne 1) {
                throw "Expected one restored identity for '$($package.Id)', found: $($restoredIdentity -join ', ')"
            }
            $restoredVersion = $restoredIdentity[0].Substring($package.Id.Length + 1)
            if (-not $restoredVersion.Equals($package.Version, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Expected $($package.Id) $($package.Version), restored $restoredVersion."
            }

            foreach ($framework in $package.Frameworks) {
                $reportPath = Join-Path $ReportDirectory "$($package.SafePackageId)\$framework"
                $publishPath = Join-Path $consumerDirectory "publish\$framework"
                New-Item -ItemType Directory -Path $reportPath -Force | Out-Null

                try {
                    Invoke-DotNet `
                        -Arguments @(
                            'publish',
                            $projectPath,
                            '--configuration', 'Release',
                            '--framework', $framework,
                            '--no-restore',
                            '--output', $publishPath
                        ) `
                        -FailureMessage "Publish failed for $($package.Id) $framework"

                    $baselineName = "$($package.SafePackageId).$framework.txt"
                    $baselinePath = Join-Path $BaselineDirectory $baselineName
                    if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
                        $baselinePath = Join-Path $emptyBaselineDirectory $baselineName
                        New-Item -ItemType File -Path $baselinePath -Force | Out-Null
                    }

                    $actualReport = Join-Path $reportPath 'BinaryCompatReport.txt'
                    $comparisonOutput = Join-Path $reportPath 'Comparison.txt'
                    Push-Location $reportPath
                    try {
                        $output = & $CheckBinaryCompatPath `
                            $publishPath `
                            '-s' `
                            '-l' `
                            '-ignoreFrameworkAssemblies' `
                            "-baseline:$baselinePath" `
                            "-out:$actualReport" `
                            '-outputNewWarnings' `
                            '-outputSummary' 2>&1
                        $checkerExitCode = $LASTEXITCODE
                        $output | Tee-Object -FilePath $comparisonOutput | Write-Host
                    }
                    finally {
                        Pop-Location
                    }

                    if ($checkerExitCode -ne 0) {
                        $failures.Add(
                            "Binary closure differs for $($package.Id) $framework. " +
                            "See '$actualReport'.")
                    }
                }
                catch {
                    $failures.Add("$($package.Id) $framework`: $($_.Exception.Message)")
                }
            }
        }
        catch {
            $failures.Add("$($package.Id): $($_.Exception.Message)")
        }
    }

    if ($failures.Count -gt 0) {
        $failures | Sort-Object -Unique | ForEach-Object {
            Write-Error $_ -ErrorAction Continue
        }
        exit 1
    }

    Write-Host "Validated $($closures.Count) binary closures across $($packages.Count) NuGet packages."
}

try {
    Invoke-BinaryClosureValidation
}
finally {
    Remove-Item -LiteralPath $consumerRoot, $packageCache -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 4: Run static PowerShell validation**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command @'
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path 'build/scripts/Validate-NuGetBinaryClosure.ps1'),
    [ref]$null,
    [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw ($errors | Out-String) }
'@
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: parser exits 0 and all helper tests pass.

- [ ] **Step 5: Commit the validator**

```powershell
git add build/scripts/Validate-NuGetBinaryClosure.ps1 build/scripts/tests/NuGetBinaryClosure.Tests.ps1
git commit -m "Validate restored NuGet binary closures" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

## Task 3: Azure Pipelines Integration

**Files:**
- Create: `build/steps/validate-nuget-binary-closure.yml`
- Modify: `build/jobs/package.yml:20-25`

- [ ] **Step 1: Add the reusable validation template**

Create `build/steps/validate-nuget-binary-closure.yml`:

```yaml
parameters:
  packageDirectory: ''
  baselineDirectory: ''
  reportDirectory: ''
  workDirectory: ''
  nuGetConfigPath: ''
  toolVersion: '1.0.45'

steps:
- task: PowerShell@2
  displayName: 'Validate NuGet binary closures'
  inputs:
    pwsh: true
    targetType: inline
    script: |
      $ErrorActionPreference = 'Stop'
      $toolDirectory = Join-Path '${{ parameters.workDirectory }}' 'tools'
      $toolConfig = Join-Path '${{ parameters.workDirectory }}' 'NuGet.Tools.config'
      New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null

      @'
      <?xml version="1.0" encoding="utf-8"?>
      <configuration>
        <packageSources>
          <clear />
          <add key="NuGetOrg" value="https://api.nuget.org/v3/index.json" />
        </packageSources>
      </configuration>
      '@ | Set-Content -LiteralPath $toolConfig

      & dotnet tool update checkbinarycompat `
          --tool-path $toolDirectory `
          --version '${{ parameters.toolVersion }}' `
          --configfile $toolConfig
      if ($LASTEXITCODE -ne 0) {
          throw "checkbinarycompat installation failed with exit code $LASTEXITCODE."
      }

      $toolExecutable = Join-Path $toolDirectory 'checkbinarycompat'
      & '$(Build.SourcesDirectory)/build/scripts/Validate-NuGetBinaryClosure.ps1' `
          -PackageDirectory '${{ parameters.packageDirectory }}' `
          -BaselineDirectory '${{ parameters.baselineDirectory }}' `
          -ReportDirectory '${{ parameters.reportDirectory }}' `
          -WorkDirectory '${{ parameters.workDirectory }}/validation' `
          -NuGetConfigPath '${{ parameters.nuGetConfigPath }}' `
          -CheckBinaryCompatPath $toolExecutable
      exit $LASTEXITCODE

- task: PublishBuildArtifacts@1
  displayName: 'Publish binary closure reports'
  condition: succeededOrFailed()
  inputs:
    pathToPublish: '${{ parameters.reportDirectory }}'
    artifactName: 'binarycompat'
    artifactType: 'container'
```

- [ ] **Step 2: Invoke validation immediately after packing**

Modify `build/jobs/package.yml` after the `PackNugets` step:

```yaml
  - template: ../steps/validate-nuget-binary-closure.yml
    parameters:
      packageDirectory: '$(Build.ArtifactStagingDirectory)/nupkgs'
      baselineDirectory: '$(Build.SourcesDirectory)/build/binarycompat'
      reportDirectory: '$(Build.ArtifactStagingDirectory)/binarycompat'
      workDirectory: '$(Agent.TempDirectory)/binarycompat'
      nuGetConfigPath: '$(Build.SourcesDirectory)/nuget.config'
```

Keep the existing artifact publication steps after this template. Their default `succeeded()` condition ensures the NuGet artifact is not published after validation fails, while the report task still runs.

- [ ] **Step 3: Parse the changed YAML**

Run:

```powershell
python -c "import pathlib,yaml; [yaml.safe_load(pathlib.Path(p).read_text(encoding='utf-8')) for p in ['build/steps/validate-nuget-binary-closure.yml','build/jobs/package.yml']]; print('YAML parsed')"
```

Expected: `YAML parsed`.

- [ ] **Step 4: Verify both build paths reach the validator once**

Run:

```powershell
$packageTemplateReferences = rg --count 'template: package.yml' build/jobs/build.yml
$prPackagingReferences = rg --count 'packageArtifacts: true' build/pr-pipeline.yml
$ciPackagingReferences = rg --count 'packageArtifacts: true' build/ci-pipeline.yml
$validatorReferences = rg --count 'validate-nuget-binary-closure.yml' build/jobs/package.yml

if ($packageTemplateReferences -ne '1' -or
    $prPackagingReferences -ne '1' -or
    $ciPackagingReferences -ne '1' -or
    $validatorReferences -ne '1') {
    throw 'PR/CI packaging call-chain validation failed.'
}
```

Expected: exit code 0.

- [ ] **Step 5: Commit pipeline integration**

```powershell
git add build/steps/validate-nuget-binary-closure.yml build/jobs/package.yml
git commit -m "Run binary closure checks during packaging" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

## Task 4: Bootstrap Current Baselines

**Files:**
- Create: `build/binarycompat/Fhir.BlobRewriter.net8.0.txt`
- Create: `build/binarycompat/Fhir.BlobRewriter.net9.0.txt`
- Create: `build/binarycompat/Fhir.Importer.net8.0.txt`
- Create: `build/binarycompat/Fhir.Importer.net9.0.txt`
- Create: `build/binarycompat/Fhir.IndexRebuilder.net8.0.txt`
- Create: `build/binarycompat/Fhir.IndexRebuilder.net9.0.txt`
- Create: `build/binarycompat/Fhir.RegisterAndMonitorImport.net8.0.txt`
- Create: `build/binarycompat/Fhir.RegisterAndMonitorImport.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Extensions.Xunit.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Extensions.Xunit.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Api.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Api.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Api.OpenIddict.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Api.OpenIddict.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Azure.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Azure.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.Initialization.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.CosmosDb.Initialization.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Api.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Api.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Client.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Client.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.ResourceParser.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.ResourceParser.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Tests.E2E.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Tests.E2E.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Web.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4.Web.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Api.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Api.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Client.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Client.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Tests.E2E.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Tests.E2E.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Web.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R4B.Web.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Api.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Api.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Client.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Client.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Tests.E2E.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Tests.E2E.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Web.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.R5.Web.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SchemaManager.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SchemaManager.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SchemaManager.Console.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SchemaManager.Console.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SqlServer.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.SqlServer.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Store.Utils.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Store.Utils.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Api.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Api.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Client.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Client.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Core.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Core.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Tests.E2E.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Tests.E2E.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Web.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Stu3.Web.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Tests.Common.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.Tests.Common.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.ValueSets.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Fhir.ValueSets.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.EventsReader.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.EventsReader.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.Exporter.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.Exporter.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.SqlScriptRunner.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.Fhir.SqlScriptRunner.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.SmartLauncher.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.Internal.SmartLauncher.net9.0.txt`
- Create: `build/binarycompat/Microsoft.Health.TaskManagement.net8.0.txt`
- Create: `build/binarycompat/Microsoft.Health.TaskManagement.net9.0.txt`
- Create: `build/binarycompat/ResourceParser.net8.0.txt`
- Create: `build/binarycompat/ResourceParser.net9.0.txt`

- [ ] **Step 1: Build and pack with a unique local version**

Run:

```powershell
Remove-Item artifacts/binarycompat-bootstrap -Recurse -Force -ErrorAction SilentlyContinue
dotnet build Microsoft.Health.Fhir.sln `
    --configuration Release `
    -p:ContinuousIntegrationBuild=true `
    -p:Version=0.0.0-binaryclosure `
    -warnaserror
dotnet pack Microsoft.Health.Fhir.sln `
    --configuration Release `
    --no-build `
    --output artifacts/binarycompat-bootstrap/nupkgs `
    -p:PackageVersion=0.0.0-binaryclosure
```

Expected: build and pack succeed and exactly 45 `.nupkg` files are present.

- [ ] **Step 2: Install the pinned checker locally**

Run:

```powershell
New-Item artifacts/binarycompat-bootstrap -ItemType Directory -Force | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="NuGetOrg" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@ | Set-Content artifacts/binarycompat-bootstrap/NuGet.Tools.config

dotnet tool update checkbinarycompat `
    --tool-path artifacts/binarycompat-bootstrap/tools `
    --version 1.0.45 `
    --configfile artifacts/binarycompat-bootstrap/NuGet.Tools.config
```

Expected: `Tool 'checkbinarycompat' was successfully installed` or updated at version 1.0.45.

- [ ] **Step 3: Run once with no baselines to generate actual reports**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/Validate-NuGetBinaryClosure.ps1 `
    -PackageDirectory artifacts/binarycompat-bootstrap/nupkgs `
    -BaselineDirectory build/binarycompat `
    -ReportDirectory artifacts/binarycompat-bootstrap/reports `
    -WorkDirectory artifacts/binarycompat-bootstrap/work `
    -NuGetConfigPath nuget.config `
    -CheckBinaryCompatPath artifacts/binarycompat-bootstrap/tools/checkbinarycompat
```

Expected: exit code 1 with 90 missing-baseline messages and 90 actual `BinaryCompatReport.txt` files.

- [ ] **Step 4: Copy actual reports into the exact baseline inventory**

Run:

```powershell
$reportRoot = (Resolve-Path artifacts/binarycompat-bootstrap/reports).Path
New-Item build/binarycompat -ItemType Directory -Force | Out-Null

Get-ChildItem $reportRoot -Filter BinaryCompatReport.txt -Recurse |
    ForEach-Object {
        $relativeDirectory = [IO.Path]::GetRelativePath($reportRoot, $_.Directory.FullName)
        $parts = $relativeDirectory -split '[\\/]'
        if ($parts.Count -ne 2) {
            throw "Unexpected report path: $relativeDirectory"
        }

        Copy-Item `
            -LiteralPath $_.FullName `
            -Destination (Join-Path 'build/binarycompat' "$($parts[0]).$($parts[1]).txt")
    }

$baselines = @(Get-ChildItem build/binarycompat -Filter '*.txt')
if ($baselines.Count -ne 90) {
    throw "Expected 90 baselines, found $($baselines.Count)."
}
```

Expected: exactly 90 baseline files.

- [ ] **Step 5: Rerun validation against the checked-in baseline candidates**

Run the Step 3 command again after deleting only the report and work directories:

```powershell
Remove-Item artifacts/binarycompat-bootstrap/reports -Recurse -Force
Remove-Item artifacts/binarycompat-bootstrap/work -Recurse -Force
pwsh -NoLogo -NoProfile -File build/scripts/Validate-NuGetBinaryClosure.ps1 `
    -PackageDirectory artifacts/binarycompat-bootstrap/nupkgs `
    -BaselineDirectory build/binarycompat `
    -ReportDirectory artifacts/binarycompat-bootstrap/reports `
    -WorkDirectory artifacts/binarycompat-bootstrap/work `
    -NuGetConfigPath nuget.config `
    -CheckBinaryCompatPath artifacts/binarycompat-bootstrap/tools/checkbinarycompat
```

Expected: `Validated 90 binary closures across 45 NuGet packages.` and exit code 0.

- [ ] **Step 6: Commit the reviewed baseline**

Inspect `git diff -- build/binarycompat` for unexpected missing assemblies or version mismatches before committing.

```powershell
git add build/binarycompat
git commit -m "Baseline NuGet binary closures" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

## Task 5: Failure-Mode and End-to-End Verification

**Files:**
- Modify only if verification exposes a defect:
  - `build/scripts/NuGetBinaryClosure.psm1`
  - `build/scripts/Validate-NuGetBinaryClosure.ps1`
  - `build/steps/validate-nuget-binary-closure.yml`
  - `build/jobs/package.yml`

- [ ] **Step 1: Verify baseline drift fails and preserves the actual report**

Run:

```powershell
$baseline = 'build/binarycompat/Microsoft.Health.Fhir.Core.net8.0.txt'
$backup = "$baseline.backup"
Copy-Item $baseline $backup
try {
    Add-Content $baseline 'intentional drift'
    pwsh -NoLogo -NoProfile -File build/scripts/Validate-NuGetBinaryClosure.ps1 `
        -PackageDirectory artifacts/binarycompat-bootstrap/nupkgs `
        -BaselineDirectory build/binarycompat `
        -ReportDirectory artifacts/binarycompat-bootstrap/drift-reports `
        -WorkDirectory artifacts/binarycompat-bootstrap/drift-work `
        -NuGetConfigPath nuget.config `
        -CheckBinaryCompatPath artifacts/binarycompat-bootstrap/tools/checkbinarycompat

    if ($LASTEXITCODE -eq 0) {
        throw 'Expected baseline drift to fail.'
    }
    if (-not (Test-Path artifacts/binarycompat-bootstrap/drift-reports/Microsoft.Health.Fhir.Core/net8.0/BinaryCompatReport.txt)) {
        throw 'Actual report was not preserved.'
    }
}
finally {
    Move-Item $backup $baseline -Force
}
```

Expected: validator fails for `Microsoft.Health.Fhir.Core net8.0`, preserves the report, and restores the repository baseline in `finally`.

- [ ] **Step 2: Verify missing and orphaned baseline failures**

Run:

```powershell
$baseline = 'build/binarycompat/Microsoft.Health.Fhir.Core.net8.0.txt'
$backup = "$baseline.backup"
Move-Item $baseline $backup
New-Item build/binarycompat/Orphan.net9.0.txt -ItemType File | Out-Null
try {
    pwsh -NoLogo -NoProfile -File build/scripts/Validate-NuGetBinaryClosure.ps1 `
        -PackageDirectory artifacts/binarycompat-bootstrap/nupkgs `
        -BaselineDirectory build/binarycompat `
        -ReportDirectory artifacts/binarycompat-bootstrap/inventory-reports `
        -WorkDirectory artifacts/binarycompat-bootstrap/inventory-work `
        -NuGetConfigPath nuget.config `
        -CheckBinaryCompatPath artifacts/binarycompat-bootstrap/tools/checkbinarycompat

    if ($LASTEXITCODE -eq 0) {
        throw 'Expected baseline inventory errors to fail.'
    }
}
finally {
    Remove-Item build/binarycompat/Orphan.net9.0.txt -Force
    Move-Item $backup $baseline -Force
}
```

Expected: output includes both missing and orphaned baseline errors.

- [ ] **Step 3: Run all local validation**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
python -c "import pathlib,yaml; [yaml.safe_load(pathlib.Path(p).read_text(encoding='utf-8')) for p in ['build/steps/validate-nuget-binary-closure.yml','build/jobs/package.yml']]; print('YAML parsed')"
pwsh -NoLogo -NoProfile -File build/scripts/Validate-NuGetBinaryClosure.ps1 `
    -PackageDirectory artifacts/binarycompat-bootstrap/nupkgs `
    -BaselineDirectory build/binarycompat `
    -ReportDirectory artifacts/binarycompat-bootstrap/final-reports `
    -WorkDirectory artifacts/binarycompat-bootstrap/final-work `
    -NuGetConfigPath nuget.config `
    -CheckBinaryCompatPath artifacts/binarycompat-bootstrap/tools/checkbinarycompat
git diff --check
```

Expected: helper tests pass, YAML parses, 90 closures validate, and `git diff --check` emits no output.

- [ ] **Step 4: Review the final change set**

Run:

```powershell
git --no-pager status --short
git --no-pager diff main...HEAD --stat
git --no-pager diff main...HEAD -- build/jobs/package.yml build/steps/validate-nuget-binary-closure.yml
```

Expected: only the design/plan, helper scripts/tests, validation template, packaging integration, and 90 baselines are present.

- [ ] **Step 5: Commit any verification fixes**

If verification required code changes:

```powershell
git add build/scripts build/steps/validate-nuget-binary-closure.yml build/jobs/package.yml build/binarycompat
git commit -m "Harden NuGet binary closure validation" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

If no files changed, do not create an empty commit.
