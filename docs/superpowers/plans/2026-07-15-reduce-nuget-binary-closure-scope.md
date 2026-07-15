# Reduce NuGet Binary Closure Scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce OSS binary-closure validation from every generated package to the four supported FHIR Web deployment roots across `net8.0` and `net9.0`, while preserving the downstream fhir-paas final-publish gate as a separate repository change.

**Architecture:** Keep the validator generic by adding an optional required-package-ID filter. The Azure Pipelines template owns the FHIR Server policy and supplies the four Web package IDs; all locally generated package IDs remain mapped to the local NuGet source so transitive OSS dependencies resolve from the same build. Baseline inventory, validation counts, and reports are computed only from the selected roots.

**Tech Stack:** PowerShell 7, .NET SDK 8/9, NuGet package archives, `checkbinarycompat` 1.0.45, Azure Pipelines YAML.

---

## Scope Boundary

This plan changes only `microsoft/fhir-server`. The authoritative fhir-paas check must be planned and implemented in the fhir-paas repository because it runs after that repository applies package pins, runtime identifiers, and publish settings.

The OSS deployment roots are:

```text
Microsoft.Health.Fhir.Stu3.Web
Microsoft.Health.Fhir.R4.Web
Microsoft.Health.Fhir.R4B.Web
Microsoft.Health.Fhir.R5.Web
```

Each root is validated for `net8.0` and `net9.0`, yielding eight baselines:

```text
Microsoft.Health.Fhir.Stu3.Web.net8.0.txt
Microsoft.Health.Fhir.Stu3.Web.net9.0.txt
Microsoft.Health.Fhir.R4.Web.net8.0.txt
Microsoft.Health.Fhir.R4.Web.net9.0.txt
Microsoft.Health.Fhir.R4B.Web.net8.0.txt
Microsoft.Health.Fhir.R4B.Web.net9.0.txt
Microsoft.Health.Fhir.R5.Web.net8.0.txt
Microsoft.Health.Fhir.R5.Web.net9.0.txt
```

## File Structure

- Modify `build/scripts/NuGetBinaryClosure.psm1`: add deterministic required-package selection.
- Modify `build/scripts/Validate-NuGetBinaryClosure.ps1`: validate inventory and process closures only for selected roots while retaining all local package-source mappings.
- Modify `build/scripts/tests/NuGetBinaryClosure.Tests.ps1`: cover selection, missing roots, ignored utilities, and a real renamed-member fixture.
- Modify `build/steps/validate-nuget-binary-closure.yml`: supply the four Web root IDs.
- Delete 82 obsolete `build/binarycompat/*.txt` files; retain the eight Web baselines.
- Modify `docs/superpowers/plans/2026-07-14-nuget-binary-closure-validation.md`: mark the original every-package plan as superseded by this scope amendment.

### Task 1: Required Package Selection Helper

**Files:**
- Modify: `build/scripts/NuGetBinaryClosure.psm1:156-209,370-377`
- Test: `build/scripts/tests/NuGetBinaryClosure.Tests.ps1:441-458,993-1036`

- [ ] **Step 1: Add failing selector tests**

Add these test cases after `module import`:

```powershell
Invoke-TestCase 'package selection uses required ids and reports missing roots' {
    Import-TestModule

    $packages = @(
        [pscustomobject]@{ Id = 'Utility.Package'; Version = '1.0.0'; Path = 'utility.nupkg' }
        [pscustomobject]@{ Id = 'Microsoft.Health.Fhir.R4.Web'; Version = '1.0.0'; Path = 'r4.nupkg' }
        [pscustomobject]@{ Id = 'Microsoft.Health.Fhir.R5.Web'; Version = '1.0.0'; Path = 'r5.nupkg' }
    )

    $selection = Select-BinaryClosurePackages -Packages $packages -RequiredPackageIds @(
        'microsoft.health.fhir.r5.web'
        'Microsoft.Health.Fhir.R4.Web'
        'Microsoft.Health.Fhir.Stu3.Web'
        'Microsoft.Health.Fhir.R4.Web'
    )

    Assert-SequenceEqual @(
        'Microsoft.Health.Fhir.R4.Web'
        'Microsoft.Health.Fhir.R5.Web'
    ) @($selection.Selected | Select-Object -ExpandProperty Id) 'Selected deployment roots mismatch'
    Assert-SequenceEqual @(
        'Microsoft.Health.Fhir.Stu3.Web'
    ) $selection.Missing 'Missing deployment roots mismatch'
}

Invoke-TestCase 'empty required package ids select all packages' {
    Import-TestModule

    $packages = @(
        [pscustomobject]@{ Id = 'Z.Package'; Version = '1.0.0'; Path = 'z.nupkg' }
        [pscustomobject]@{ Id = 'A.Package'; Version = '1.0.0'; Path = 'a.nupkg' }
    )

    $selection = Select-BinaryClosurePackages -Packages $packages -RequiredPackageIds @()

    Assert-SequenceEqual @('A.Package', 'Z.Package') @($selection.Selected | Select-Object -ExpandProperty Id) 'Unfiltered package order mismatch'
    Assert-SequenceEqual @() $selection.Missing 'Unfiltered selection should not report missing packages'
}
```

Update the module export assertion to include `Select-BinaryClosurePackages`.

- [ ] **Step 2: Run the tests and verify the selector is missing**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: both new cases fail because `Select-BinaryClosurePackages` is not exported.

- [ ] **Step 3: Implement deterministic selection**

Add this function before `Compare-BinaryClosureBaselineInventory`:

```powershell
function Select-BinaryClosurePackages {
    [CmdletBinding()]
    param(
        [object[]]$Packages,

        [string[]]$RequiredPackageIds
    )

    $packagesById = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    foreach ($package in @($Packages)) {
        if ($null -eq $package -or [string]::IsNullOrWhiteSpace([string]$package.Id)) {
            continue
        }

        if (-not $packagesById.ContainsKey([string]$package.Id)) {
            $packagesById[[string]$package.Id] = [System.Collections.Generic.List[object]]::new()
        }

        $packagesById[[string]$package.Id].Add($package) | Out-Null
    }

    $requiredIds = [System.Collections.Generic.SortedDictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($requiredPackageId in @($RequiredPackageIds)) {
        if (-not [string]::IsNullOrWhiteSpace($requiredPackageId)) {
            $requiredIds[$requiredPackageId] = $requiredPackageId
        }
    }

    $selected = [System.Collections.Generic.List[object]]::new()
    $missing = [System.Collections.Generic.List[string]]::new()

    if ($requiredIds.Count -eq 0) {
        foreach ($package in @($Packages | Sort-Object Id, Version, Path)) {
            if ($null -ne $package) {
                $selected.Add($package) | Out-Null
            }
        }
    }
    else {
        foreach ($requiredPackageId in $requiredIds.Values) {
            if (-not $packagesById.ContainsKey($requiredPackageId)) {
                $missing.Add($requiredPackageId) | Out-Null
                continue
            }

            foreach ($package in @($packagesById[$requiredPackageId] | Sort-Object Id, Version, Path)) {
                $selected.Add($package) | Out-Null
            }
        }
    }

    return [pscustomobject]@{
        Selected = @($selected)
        Missing = @($missing)
    }
}
```

Add `'Select-BinaryClosurePackages'` to `Export-ModuleMember`.

- [ ] **Step 4: Run the helper tests**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: all existing tests and both selector tests pass.

- [ ] **Step 5: Commit the selector**

```powershell
git add build/scripts/NuGetBinaryClosure.psm1 build/scripts/tests/NuGetBinaryClosure.Tests.ps1
git commit -m "Select binary closure deployment roots" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

### Task 2: Validator and Pipeline Root Policy

**Files:**
- Modify: `build/scripts/Validate-NuGetBinaryClosure.ps1:1-28,407-547,693`
- Modify: `build/steps/validate-nuget-binary-closure.yml:80-95`
- Test: `build/scripts/tests/NuGetBinaryClosure.Tests.ps1:125-173,589-615`

- [ ] **Step 1: Extend the validator test launcher**

Add this parameter to `Invoke-ValidatorScript`:

```powershell
[string[]]$RequiredPackageIds = @()
```

After constructing `$arguments`, append:

```powershell
if ($RequiredPackageIds.Count -gt 0) {
    $arguments += '-RequiredPackageIds'
    $arguments += $RequiredPackageIds
}
```

- [ ] **Step 2: Add failing validator selection tests**

Add these cases after `validator restores publishes and preserves reports on success`:

```powershell
Invoke-TestCase 'validator processes only required deployment roots' {
    $rootDirectory = Join-Path $script:TempRoot 'validator-required-roots'
    $packageDirectory = Join-Path $rootDirectory 'packages'
    $baselineDirectory = Join-Path $rootDirectory 'baselines'
    $reportDirectory = Join-Path $rootDirectory 'reports'
    $workDirectory = Join-Path $rootDirectory 'work'
    $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'

    New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
    New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'selected') -PackageDirectory $packageDirectory -PackageId 'Selected.Web' -Version '1.0.0' -Framework 'net8.0' | Out-Null
    New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'utility') -PackageDirectory $packageDirectory -PackageId 'Ignored.Utility' -Version '1.0.0' -Framework 'net8.0' | Out-Null
    New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory 'Selected.Web.net8.0.txt'), [byte[]]@())

    $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0') -RequiredPackageIds @('Selected.Web')

    Assert-Equal 0 $result.ExitCode 'Selected deployment root should validate successfully'
    Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Selected closure summary mismatch'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $reportDirectory 'Selected.Web/net8.0/BinaryCompatReport.txt')) 'Selected root report missing'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $reportDirectory 'Ignored.Utility')) 'Ignored utility should not produce reports'
}

Invoke-TestCase 'validator fails when a required deployment root is missing' {
    $rootDirectory = Join-Path $script:TempRoot 'validator-missing-root'
    $packageDirectory = Join-Path $rootDirectory 'packages'
    $baselineDirectory = Join-Path $rootDirectory 'baselines'
    $reportDirectory = Join-Path $rootDirectory 'reports'
    $workDirectory = Join-Path $rootDirectory 'work'

    New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
    New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'utility') -PackageDirectory $packageDirectory -PackageId 'Available.Utility' -Version '1.0.0' -Framework 'net8.0' | Out-Null

    $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0') -RequiredPackageIds @('Missing.Web')

    Assert-Equal 1 $result.ExitCode 'Missing deployment root should fail validation'
    Assert-Contains "Required package 'Missing.Web' was not found." $result.Output 'Missing root error mismatch'
}
```

- [ ] **Step 3: Run the tests and verify selection is not implemented**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
```

Expected: the selected-root case reports two closures or an orphaned baseline, and the missing-root case lacks the required-package error.

- [ ] **Step 4: Add the required package parameter and selection**

Add to the validator parameter block:

```powershell
[string[]]$RequiredPackageIds = @(),
```

Place it before `SupportedFrameworks`.

After package identity validation and before baseline inventory comparison, add:

```powershell
$selection = Select-BinaryClosurePackages -Packages @($packages) -RequiredPackageIds $RequiredPackageIds
foreach ($missingPackageId in $selection.Missing) {
    Add-ValidationError -Errors $errors -Message "Required package '$missingPackageId' was not found."
}

$selectedPackages = @($selection.Selected)
$packageCount = $selectedPackages.Count
$closureCount = ($selectedPackages | ForEach-Object { @($_.Frameworks).Count } | Measure-Object -Sum).Sum
```

Remove the earlier `$packageCount` and `$closureCount` calculation based on `$packages`.

Change baseline inventory to:

```powershell
$inventory = Compare-BinaryClosureBaselineInventory -Closures $selectedPackages -BaselineDirectory $resolvedBaselineDirectory
```

Change packages-to-process to:

```powershell
$packagesToProcess = @(
    $selectedPackages |
        Where-Object { -not $invalidPackagePaths.Contains([string]$_.Path) }
)
```

Keep `$localPackageIds` sourced from all `$packages`. This is required so a selected Web root restores every locally built transitive `Microsoft.Health.*` dependency from the same build rather than from a feed.

- [ ] **Step 5: Supply the four Web roots from Azure Pipelines**

Before invoking the validator in `build/steps/validate-nuget-binary-closure.yml`, define:

```powershell
$requiredPackageIds = @(
  'Microsoft.Health.Fhir.Stu3.Web'
  'Microsoft.Health.Fhir.R4.Web'
  'Microsoft.Health.Fhir.R4B.Web'
  'Microsoft.Health.Fhir.R5.Web'
)
```

Add the argument:

```powershell
-RequiredPackageIds $requiredPackageIds `
```

immediately before `-CheckBinaryCompatPath`.

- [ ] **Step 6: Run tests and parse configuration**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
python -c "import pathlib,yaml; [yaml.safe_load(pathlib.Path(p).read_text(encoding='utf-8')) for p in ['build/steps/validate-nuget-binary-closure.yml','build/jobs/package.yml']]; print('YAML parsed')"
```

Expected: all PowerShell tests pass and output includes `YAML parsed`.

- [ ] **Step 7: Commit validator selection**

```powershell
git add build/scripts/Validate-NuGetBinaryClosure.ps1 build/scripts/tests/NuGetBinaryClosure.Tests.ps1 build/steps/validate-nuget-binary-closure.yml
git commit -m "Validate Web package deployment closures" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

### Task 3: Renamed-Member Regression Fixture

**Files:**
- Modify: `build/scripts/tests/NuGetBinaryClosure.Tests.ps1:1,206-379,930`

- [ ] **Step 1: Add an optional real-checker parameter**

Add at the top of the test script:

```powershell
[CmdletBinding()]
param(
    [string]$RealCheckBinaryCompatPath
)
```

- [ ] **Step 2: Add a dependency-version rewrite helper**

Add after `Update-TestPackageNuspecVersion`:

```powershell
function Update-TestPackageDependencyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$DependencyId,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $extractDirectory = "$PackagePath.expanded"
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }

    Expand-Archive -LiteralPath $PackagePath -DestinationPath $extractDirectory
    try {
        $nuspecPath = (Get-ChildItem -LiteralPath $extractDirectory -Filter *.nuspec | Select-Object -First 1).FullName
        [xml]$nuspecXml = Get-Content -LiteralPath $nuspecPath -Raw
        $dependencyNode = $nuspecXml.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='dependencies']//*[local-name()='dependency' and @id='$DependencyId']")
        if ($null -eq $dependencyNode) {
            throw "Dependency '$DependencyId' was not found in '$PackagePath'."
        }

        $dependencyNode.SetAttribute('version', "[$Version]")
        $nuspecXml.Save($nuspecPath)
        Remove-Item -LiteralPath $PackagePath -Force
        Compress-Archive -Path (Join-Path $extractDirectory '*') -DestinationPath $PackagePath
    }
    finally {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

- [ ] **Step 3: Add a real renamed-member test**

Add this test only when `RealCheckBinaryCompatPath` is supplied:

```powershell
if (-not [string]::IsNullOrWhiteSpace($RealCheckBinaryCompatPath)) {
    Invoke-TestCase 'real checker rejects a dependency with a renamed referenced member' {
        $root = Join-Path $script:TempRoot 'renamed-member'
        $packages = Join-Path $root 'packages'
        $baselines = Join-Path $root 'baselines'
        $reports = Join-Path $root 'reports'
        $work = Join-Path $root 'work'
        $config = Join-Path $root 'NuGet.config'

        New-Item -ItemType Directory -Path $packages -Force | Out-Null
        New-Item -ItemType Directory -Path $baselines -Force | Out-Null
        Set-Content -LiteralPath $config -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="fixture" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

        foreach ($version in @('1.0.0', '2.0.0')) {
            $dependencyRoot = Join-Path $root "dependency-$version"
            New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $dependencyRoot 'Breaking.Dependency.csproj') -Encoding utf8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>Breaking.Dependency</PackageId>
    <Version>$version</Version>
  </PropertyGroup>
</Project>
"@
            $member = if ($version -eq '1.0.0') { 'public void OldMethod() { }' } else { 'public void RenamedMethod() { }' }
            Set-Content -LiteralPath (Join-Path $dependencyRoot 'Api.cs') -Encoding utf8 -Value "namespace Breaking.Dependency; public sealed class Api { $member }"
            $pack = Invoke-TestNativeCommand -FilePath 'dotnet' -ArgumentList @('pack', (Join-Path $dependencyRoot 'Breaking.Dependency.csproj'), '--output', $packages, '--configuration', 'Release', '-v', 'minimal') -WorkingDirectory $dependencyRoot
            Assert-Equal 0 $pack.ExitCode "Dependency $version pack failed"
        }

        $webRoot = Join-Path $root 'web'
        New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $webRoot 'Broken.Web.csproj') -Encoding utf8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>Broken.Web</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Breaking.Dependency" Version="1.0.0" />
  </ItemGroup>
</Project>
"@
        Set-Content -LiteralPath (Join-Path $webRoot 'Caller.cs') -Encoding utf8 -Value 'namespace Broken.Web; public static class Caller { public static void Invoke() => new Breaking.Dependency.Api().OldMethod(); }'
        $pack = Invoke-TestNativeCommand -FilePath 'dotnet' -ArgumentList @('pack', (Join-Path $webRoot 'Broken.Web.csproj'), '--configfile', $config, '--output', $packages, '--configuration', 'Release', '-v', 'minimal') -WorkingDirectory $webRoot
        Assert-Equal 0 $pack.ExitCode 'Broken Web package pack failed'

        $webPackage = Join-Path $packages 'Broken.Web.1.0.0.nupkg'
        Update-TestPackageDependencyVersion -PackagePath $webPackage -DependencyId 'Breaking.Dependency' -Version '2.0.0'
        [System.IO.File]::WriteAllBytes((Join-Path $baselines 'Broken.Web.net8.0.txt'), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packages -BaselineDirectory $baselines -ReportDirectory $reports -WorkDirectory $work -NuGetConfigPath $config -CheckBinaryCompatPath $RealCheckBinaryCompatPath -SupportedFrameworks @('net8.0') -RequiredPackageIds @('Broken.Web')
        $actualReport = Get-Content -LiteralPath (Join-Path $reports 'Broken.Web/net8.0/BinaryCompatReport.txt') -Raw

        Assert-Equal 1 $result.ExitCode 'Renamed dependency member should fail closure validation'
        Assert-Contains 'Binary closure baseline drift detected' $result.Output 'Renamed member should produce baseline drift'
        Assert-Contains 'Failed to resolve member reference' $actualReport 'Actual report should identify the unresolved member'
    }
}
```

- [ ] **Step 4: Install the pinned checker and run the regression**

Run:

```powershell
$tools = 'artifacts/binarycompat-member-test/tools'
$toolConfig = 'artifacts/binarycompat-member-test/NuGet.Tools.config'
New-Item (Split-Path $toolConfig -Parent) -ItemType Directory -Force | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath $toolConfig -Encoding utf8
dotnet tool update checkbinarycompat --tool-path $tools --version 1.0.45 --configfile $toolConfig
$checker = Join-Path $tools $(if ($IsWindows) { 'checkbinarycompat.exe' } else { 'checkbinarycompat' })
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1 -RealCheckBinaryCompatPath $checker
```

Expected: every test passes, including `real checker rejects a dependency with a renamed referenced member`.

- [ ] **Step 5: Commit the regression fixture**

```powershell
git add build/scripts/tests/NuGetBinaryClosure.Tests.ps1
git commit -m "Test renamed dependency member failures" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

### Task 4: Prune Baselines and Verify Eight Closures

**Files:**
- Delete: 82 non-Web files under `build/binarycompat/`
- Retain: 8 Web files under `build/binarycompat/`
- Modify: `docs/superpowers/plans/2026-07-14-nuget-binary-closure-validation.md:1-9`

- [ ] **Step 1: Mark the original plan as superseded**

Add below the original plan title:

```markdown
> **Scope amendment:** The every-package validation matrix in this historical plan is superseded by `docs/superpowers/plans/2026-07-15-reduce-nuget-binary-closure-scope.md`. The final OSS implementation validates four Web deployment roots across two target frameworks.
```

- [ ] **Step 2: Remove every non-Web baseline**

Run:

```powershell
$requiredBaselines = @(
    'Microsoft.Health.Fhir.Stu3.Web.net8.0.txt'
    'Microsoft.Health.Fhir.Stu3.Web.net9.0.txt'
    'Microsoft.Health.Fhir.R4.Web.net8.0.txt'
    'Microsoft.Health.Fhir.R4.Web.net9.0.txt'
    'Microsoft.Health.Fhir.R4B.Web.net8.0.txt'
    'Microsoft.Health.Fhir.R4B.Web.net9.0.txt'
    'Microsoft.Health.Fhir.R5.Web.net8.0.txt'
    'Microsoft.Health.Fhir.R5.Web.net9.0.txt'
)

Get-ChildItem build/binarycompat -File -Filter *.txt |
    Where-Object Name -NotIn $requiredBaselines |
    Remove-Item -Force

$actual = @(Get-ChildItem build/binarycompat -File -Filter *.txt | Select-Object -ExpandProperty Name | Sort-Object)
$expected = @($requiredBaselines | Sort-Object)
if (Compare-Object $expected $actual) {
    throw 'Binary closure baseline inventory does not match the eight deployment roots.'
}
```

Expected: exactly eight files remain.

- [ ] **Step 3: Run the full Linux closure validation**

Build the package set:

```powershell
Remove-Item artifacts/binarycompat-scope-linux -Recurse -Force -ErrorAction SilentlyContinue
dotnet build Microsoft.Health.Fhir.sln `
    --configuration Release `
    -p:ContinuousIntegrationBuild=true `
    -p:Version=0.0.0-binaryclosure `
    -warnaserror
dotnet pack Microsoft.Health.Fhir.sln `
    --configuration Release `
    --no-build `
    --output artifacts/binarycompat-scope-linux/nupkgs `
    -p:PackageVersion=0.0.0-binaryclosure

$packageCount = @(Get-ChildItem artifacts/binarycompat-scope-linux/nupkgs -File -Filter *.nupkg |
    Where-Object Name -NotLike '*.symbols.nupkg').Count
if ($packageCount -ne 45) {
    throw "Expected 45 non-symbol packages, found $packageCount."
}
```

Create a NuGet.org-only tool configuration and a Linux runner:

```powershell
$root = 'artifacts/binarycompat-scope-linux'
$toolConfig = Join-Path $root 'NuGet.Tools.config'
$runner = Join-Path $root 'run.sh'

@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath $toolConfig -Encoding utf8

$runnerContent = @'
set -euo pipefail
rm -rf /tools /tmp/fhir-binarycompat-scope /repo/artifacts/binarycompat-scope-linux/reports
dotnet tool install PowerShell --version 7.5.3 --tool-path /tools --configfile /repo/artifacts/binarycompat-scope-linux/NuGet.Tools.config
dotnet tool install checkbinarycompat --version 1.0.45 --tool-path /tools --configfile /repo/artifacts/binarycompat-scope-linux/NuGet.Tools.config
export PATH="/tools:$PATH"
/tools/pwsh -NoLogo -NoProfile -File /repo/build/scripts/tests/NuGetBinaryClosure.Tests.ps1 -RealCheckBinaryCompatPath /tools/checkbinarycompat
/tools/pwsh -NoLogo -NoProfile -File /repo/build/scripts/Validate-NuGetBinaryClosure.ps1 \
  -PackageDirectory /repo/artifacts/binarycompat-scope-linux/nupkgs \
  -BaselineDirectory /repo/build/binarycompat \
  -ReportDirectory /repo/artifacts/binarycompat-scope-linux/reports \
  -WorkDirectory /tmp/fhir-binarycompat-scope \
  -NuGetConfigPath /repo/nuget.config \
  -CheckBinaryCompatPath /tools/checkbinarycompat \
  -RequiredPackageIds \
    Microsoft.Health.Fhir.Stu3.Web \
    Microsoft.Health.Fhir.R4.Web \
    Microsoft.Health.Fhir.R4B.Web \
    Microsoft.Health.Fhir.R5.Web
'@
[System.IO.File]::WriteAllText(
    (Join-Path $PWD $runner),
    ($runnerContent -replace "`r`n?", "`n"),
    [System.Text.UTF8Encoding]::new($false))
```

Run the same Linux SDK image and tool versions used for baseline generation:

```powershell
docker run --rm `
  -v "${PWD}:/repo" `
  -w /repo `
  mcr.microsoft.com/dotnet/sdk:9.0.315 `
  bash /repo/artifacts/binarycompat-scope-linux/run.sh
```

Expected final line:

```text
Validated 8 binary closures across 4 NuGet packages.
```

Expected retained artifacts: 8 `BinaryCompatReport.txt`, 8 `BinaryCompatReport.Assemblies.txt`, and 8 `Comparison.txt`.

- [ ] **Step 4: Run all final checks**

Run:

```powershell
pwsh -NoLogo -NoProfile -File build/scripts/tests/NuGetBinaryClosure.Tests.ps1
python -c "import pathlib,yaml; [yaml.safe_load(pathlib.Path(p).read_text(encoding='utf-8')) for p in ['build/steps/validate-nuget-binary-closure.yml','build/jobs/package.yml','build/pr-pipeline.yml','build/ci-pipeline.yml']]; print('YAML parsed: 4 files')"
git --no-pager diff --check
git --no-pager status --short
```

Expected: tests pass, four YAML files parse, `git diff --check` emits no output, and only the planned scripts, YAML, documentation, and baseline deletions are present.

- [ ] **Step 5: Commit the reduced baseline inventory**

```powershell
git add build/binarycompat docs/superpowers/plans/2026-07-14-nuget-binary-closure-validation.md
git commit -m "Reduce binary closure baseline inventory" `
  -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: ef0bf75e-afad-46b4-8331-a436acedbe9a"
```

### Task 5: Final Review and Draft PR Update

**Files:**
- Review all changes from `origin/main...HEAD`.

- [ ] **Step 1: Confirm PR and CI coverage**

Run:

```powershell
git --no-pager grep -n "packageArtifacts: true" -- build/pr-pipeline.yml build/ci-pipeline.yml
git --no-pager grep -n "validate-nuget-binary-closure.yml" -- build/jobs/package.yml
```

Expected: each pipeline reaches the shared package job once and `package.yml` invokes the validation template once after `dotnet pack`.

- [ ] **Step 2: Request whole-feature review**

Review `origin/main...HEAD` against:

```text
docs/superpowers/specs/2026-07-14-nuget-binary-closure-validation-design.md
docs/superpowers/plans/2026-07-15-reduce-nuget-binary-closure-scope.md
```

Require no Critical or Important findings before updating the draft PR.

- [ ] **Step 3: Push and update the draft PR**

Run:

```powershell
git push
```

Update the PR summary and validation section to say:

```markdown
- validate four supported FHIR Web package roots across net8.0 and net9.0
- preserve all locally built package mappings so the selected roots restore the complete OSS dependency graph
- retain eight Linux-generated baselines and add a renamed-member regression fixture
```

Do not claim that this OSS PR protects against dependency versions selected by fhir-paas. Link a separate fhir-paas work item or PR for the authoritative final-publish gate.
