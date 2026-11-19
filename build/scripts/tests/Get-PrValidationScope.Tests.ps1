BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'

    # The path rules (the paths policy, Task 3).
    function Get-Scope {
        param(
            [string[]] $ChangedFiles = @(),
            [string[]] $Labels = @(),
            [string] $TargetBranch = 'main',
            [bool] $IsFork = $false,
            [bool] $FileListTruncated = $false,
            [string[]] $ChangedPackages = @())

        & $script:ScriptPath -Policy paths -ChangedFiles $ChangedFiles -Labels $Labels -TargetBranch $TargetBranch `
            -IsFork $IsFork -FileListTruncated $FileListTruncated -ChangedPackages $ChangedPackages
    }

    # A PR run under the default policy (draftFirst). IsDraft $null is a draft state that could not be read.
    function Get-DraftFirstScope {
        param(
            [string[]] $ChangedFiles = @('docs/x.md'),
            [string[]] $Labels = @(),
            [Nullable[bool]] $IsDraft,
            [bool] $IsFork = $false,
            [string] $TargetBranch = 'main',
            [string] $LabelsUnavailableReason = '')

        & $script:ScriptPath -ChangedFiles $ChangedFiles -Labels $Labels -IsDraft $IsDraft -IsFork $IsFork `
            -TargetBranch $TargetBranch -LabelsUnavailableReason $LabelsUnavailableReason
    }

    function Get-ForkWarning([string] $Reasons) {
        "Fork PR: full test matrix not run ($Reasons). A maintainer can push the reviewed SHA to a branch in microsoft/fhir-server to run it."
    }

    # Classifies a change to the root Directory.Packages.props given its diff lines, as classify-pr.yml does.
    function Get-ScopeFromPropsDiff {
        param([string[]] $Diff)

        $content = @(
            '<Project>',
            '  <PropertyGroup>',
            '    <OpenIddictPackageVersion>7.8.0</OpenIddictPackageVersion>',
            '    <Hl7FhirVersion>5.11.5</Hl7FhirVersion>',
            '    <DotNetSdkPackageVersion>11.0.1</DotNetSdkPackageVersion>',
            '    <SomeToolVersion>2.0.0</SomeToolVersion>',
            '    <CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>',
            '  </PropertyGroup>',
            '  <ItemGroup>',
            '    <PackageVersion Include="OpenIddict.AspNetCore" Version="$(OpenIddictPackageVersion)" />',
            '    <PackageVersion Include="Hl7.Fhir.R4" Version="$(Hl7FhirVersion)" />',
            '    <PackageVersion Include="System.Text.Json" Version="$(DotNetSdkPackageVersion)" />',
            '  </ItemGroup>',
            '</Project>')
        $changes = & (Join-Path $PSScriptRoot '..' 'Get-PackagePropsChanges.ps1') -DiffLines $Diff -Content $content
        & $script:ScriptPath -Policy paths -ChangedFiles 'Directory.Packages.props' -TargetBranch 'main' `
            -ChangedPackages $changes.Packages -ChangedPackageProperties $changes.Properties
    }
}

Describe 'Get-PrValidationScope' {
    It 'lean for R4-only change' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.R4.Core/Foo.cs').Scope | Should -Be 'lean'
    }

    It 'lean for docs' {
        (Get-Scope -ChangedFiles 'docs/x.md').Scope | Should -Be 'lean'
    }

    It 'shared for Shared project' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Shared.Core/Features/X.cs').Scope | Should -Be 'shared'
    }

    It 'shared for Shared test project' {
        (Get-Scope -ChangedFiles 'test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/X.cs').Scope | Should -Be 'shared'
    }

    It 'full for Cosmos project' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.CosmosDb/X.cs').Scope | Should -Be 'full'
    }

    It 'full for Cosmos test project' {
        (Get-Scope -ChangedFiles 'test/Microsoft.Health.Fhir.CosmosDb.Tests.Integration/x.cs').Scope | Should -Be 'full'
    }

    It 'full for Stu3 project' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Stu3.Core/X.cs').Scope | Should -Be 'full'
    }

    It 'full for R5 test project' {
        (Get-Scope -ChangedFiles 'test/Microsoft.Health.Fhir.R5.Tests.E2E/X.cs').Scope | Should -Be 'full'
    }

    It 'full for R4B' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.R4B.Api/X.cs').Scope | Should -Be 'full'
    }

    It 'full for build' {
        (Get-Scope -ChangedFiles 'build/pr-pipeline.yml').Scope | Should -Be 'full'
    }

    It 'full for samples templates' {
        (Get-Scope -ChangedFiles 'samples/templates/default-azuredeploy.json').Scope | Should -Be 'full'
    }

    It 'full for OpenIddict' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Api.OpenIddict/X.cs').Scope | Should -Be 'full'
    }

    It 'full for Security folder' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Api/Features/Security/X.cs').Scope | Should -Be 'full'
    }

    It 'full for label' {
        (Get-Scope -ChangedFiles 'docs/x.md' -Labels 'Full-Test-Matrix').Scope | Should -Be 'full'
    }

    It 'the label matches case-insensitively' {
        (Get-Scope -ChangedFiles 'docs/x.md' -Labels 'full-test-matrix').Scope | Should -Be 'full'
        (Get-Scope -ChangedFiles 'docs/x.md' -Labels 'FULL-TEST-MATRIX').Scope | Should -Be 'full'
    }

    It 'full for release target' {
        (Get-Scope -ChangedFiles 'docs/x.md' -TargetBranch 'release/1.2').Scope | Should -Be 'full'
    }

    It 'full for hotfix target' {
        (Get-Scope -ChangedFiles 'docs/x.md' -TargetBranch 'hotfix/x').Scope | Should -Be 'full'
    }

    It 'cosmos project wins over shared' {
        $files = 'src/Microsoft.Health.Fhir.Shared.Core/X.cs', 'src/Microsoft.Health.Fhir.CosmosDb/Y.cs'
        (Get-Scope -ChangedFiles $files).Scope | Should -Be 'full'
    }

    It 'cosmos in file name under Shared project is shared, not full' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Shared.Core/Features/CosmosLikeName.cs').Scope | Should -Be 'shared'
    }

    It 'truncated file list yields full' {
        (Get-Scope -ChangedFiles 'docs/x.md' -FileListTruncated $true).Scope | Should -Be 'full'
    }

    It 'empty file list yields full' {
        $result = Get-Scope -ChangedFiles @()
        $result.Scope | Should -Be 'full'
        ($result.Reasons -join ' ') | Should -Match 'fail-closed'
    }

    # Task 15: a fork PR is lean under either policy; the full matrix can never run for it.
    It 'fork PR with a full path is lean with a warning' {
        $result = Get-Scope -ChangedFiles 'build/x.yml' -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.FullRequiresMaintainer | Should -BeFalse
        $result.Warnings | Should -Be @(Get-ForkWarning 'build: build/x.yml')
    }

    It 'paths are case-insensitive and accept backslashes' {
        (Get-Scope -ChangedFiles 'SRC\Microsoft.Health.Fhir.CosmosDb\X.cs').Scope | Should -Be 'full'
    }

    # A rename lists both paths: classify-pr.yml diffs with --no-renames, so a move out of build/ is a deletion there.
    It 'a rename out of a full path, as a deleted and added pair, is full' {
        (Get-Scope -ChangedFiles 'build/x.yml', 'docs/x.yml').Scope | Should -Be 'full'
    }

    # Ruling R15: root build inputs. R15b: Directory.Packages.props by the changed package names.
    It 'full for Directory.Packages.props' {
        (Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'Microsoft.Data.SqlClient').Scope | Should -Be 'full'
    }

    It 'Cosmos package bump is full' {
        $result = Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'Microsoft.Azure.Cosmos'
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Contain 'package: Microsoft.Azure.Cosmos'
    }

    It 'Azure.Storage.Blobs bump is full' {
        (Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'Azure.Storage.Blobs').Scope | Should -Be 'full'
    }

    It 'OpenIddict bump is full' {
        (Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'OpenIddict.AspNetCore').Scope | Should -Be 'full'
    }

    It 'xunit bump is shared' {
        $result = Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'xunit.v3', 'xunit.runner.visualstudio'
        $result.Scope | Should -Be 'shared'
        $result.Reasons | Should -Contain 'package: xunit.v3'
    }

    It 'a full package wins over shared packages' {
        (Get-Scope -ChangedFiles 'Directory.Packages.props' -ChangedPackages 'xunit.v3', 'Azure.Identity').Scope | Should -Be 'full'
    }

    It 'Directory.Packages.props changed with no parsable package lines is full' {
        $result = Get-Scope -ChangedFiles 'Directory.Packages.props'
        $result.Scope | Should -Be 'full'
        ($result.Reasons -join ' ') | Should -Match 'fail-closed'
    }

    It 'changed packages count only when Directory.Packages.props changed' {
        (Get-Scope -ChangedFiles 'docs/x.md' -ChangedPackages 'Azure.Identity').Scope | Should -Be 'lean'
    }

    It 'a nested Directory.Packages.props follows the project rule' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.R4.Core/Directory.Packages.props').Scope | Should -Be 'lean'
    }

    # Ruling R15c: version properties in Directory.Packages.props, mapped to their packages by Get-PackagePropsChanges.ps1.
    It 'version property change maps to its packages (OpenIddict → full)' {
        $result = Get-ScopeFromPropsDiff @(
            '-    <OpenIddictPackageVersion>7.7.0</OpenIddictPackageVersion>',
            '+    <OpenIddictPackageVersion>7.8.0</OpenIddictPackageVersion>')
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Contain 'package: OpenIddict.AspNetCore'
    }

    It 'property change mapping only to non-matching packages is shared' {
        $result = Get-ScopeFromPropsDiff @('+    <Hl7FhirVersion>5.11.5</Hl7FhirVersion>')
        $result.Scope | Should -Be 'shared'
        $result.Reasons | Should -Contain 'package: Hl7.Fhir.R4'
    }

    It 'unreferenced property change is shared' {
        $result = Get-ScopeFromPropsDiff @('+    <SomeToolVersion>2.0.0</SomeToolVersion>')
        $result.Scope | Should -Be 'shared'
        $result.Reasons | Should -Contain 'package property: SomeToolVersion'
    }

    It 'the .NET SDK release-train property is full' {
        $result = Get-ScopeFromPropsDiff @('+    <DotNetSdkPackageVersion>11.0.1</DotNetSdkPackageVersion>')
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Contain 'build property: DotNetSdkPackageVersion'
    }

    It 'a central package management switch is full' {
        (Get-ScopeFromPropsDiff @('+    <CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>')).Scope |
            Should -Be 'full'
    }

    It 'full for global.json' {
        (Get-Scope -ChangedFiles 'global.json').Scope | Should -Be 'full'
    }

    It 'full for Directory.Build.targets' {
        (Get-Scope -ChangedFiles 'directory.build.TARGETS').Scope | Should -Be 'full'
    }

    It 'full for Directory.Build.props and nuget.config' {
        (Get-Scope -ChangedFiles 'Directory.Build.props').Scope | Should -Be 'full'
        (Get-Scope -ChangedFiles 'NuGet.Config').Scope | Should -Be 'full'
    }

    It 'nested Directory.Build.props under src/<proj> follows the project rule' {
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.R4.Core/Directory.Build.props').Scope | Should -Be 'lean'
        (Get-Scope -ChangedFiles 'src/Microsoft.Health.Fhir.Shared.Core/Directory.Build.props').Scope | Should -Be 'shared'
    }

    # Ruling R16: a label-read failure does not force full.
    It 'labels unavailable classifies by paths and warns' {
        $result = & $script:ScriptPath -Policy paths -ChangedFiles 'src/Microsoft.Health.Fhir.R4.Core/Foo.cs' -TargetBranch 'main' `
            -LabelsUnavailableReason 'HTTP 403 rate limit exceeded'
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(
            'Could not read PR labels (HTTP 403 rate limit exceeded); classifying by paths only. Add the Full-Test-Matrix label and re-run, or queue with tier1Scope=full.')
    }

    It 'labels unavailable still classifies full paths as full' {
        $result = & $script:ScriptPath -Policy paths -ChangedFiles 'build/x.yml' -TargetBranch 'main' -LabelsUnavailableReason 'timeout'
        $result.Scope | Should -Be 'full'
    }

    It 'no warnings when labels were read' {
        (Get-Scope -ChangedFiles 'docs/x.md').Warnings | Should -BeNullOrEmpty
    }

    # Invariants beyond the brief's cases.
    It 'same-repository full scope does not require a maintainer' {
        (Get-Scope -ChangedFiles 'build/x.yml').FullRequiresMaintainer | Should -BeFalse
    }

    It 'fork with lean scope does not require a maintainer' {
        $result = Get-Scope -ChangedFiles 'docs/x.md' -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.FullRequiresMaintainer | Should -BeFalse
    }

    It 'names the triggering path in the reasons' {
        (Get-Scope -ChangedFiles 'docs/x.md', 'build/pr-pipeline.yml').Reasons | Should -Contain 'build: build/pr-pipeline.yml'
    }

    # Task 7: a merge-queue branch (GitHub merge queue, gh-readonly-queue/<base>/pr-<n>-<sha>) always runs the full matrix.
    It 'merge queue branch yields full' {
        $result = & $script:ScriptPath -Policy paths -ChangedFiles 'docs/x.md' -SourceBranch 'refs/heads/gh-readonly-queue/main/pr-123-abc'
        $result.Scope | Should -Be 'full'
        ($result.Reasons -join ' ') | Should -Match 'merge-queue'
    }

    It 'ordinary branch does not force full' {
        (& $script:ScriptPath -Policy paths -ChangedFiles 'docs/x.md' -SourceBranch 'refs/heads/feature/x').Scope | Should -Be 'lean'
    }

    It 'a PR merge ref does not force full' {
        (& $script:ScriptPath -Policy paths -ChangedFiles 'docs/x.md' -SourceBranch 'refs/pull/6061/merge').Scope | Should -Be 'lean'
    }

    It 'a branch merely containing gh-readonly-queue does not force full' {
        (& $script:ScriptPath -Policy paths -ChangedFiles 'docs/x.md' -SourceBranch 'refs/heads/personal/gh-readonly-queue/x').Scope | Should -Be 'lean'
    }
}

# Task 15, iteration 1: draft PRs are lean unless labeled; ready PRs are full; path rules are advisory.
Describe 'Get-PrValidationScope draftFirst policy' {
    It 'is the default policy' {
        (& $script:ScriptPath -ChangedFiles 'build/x.yml' -IsDraft $true).Scope | Should -Be 'lean'
    }

    It 'ready PR is full regardless of paths' {
        $result = Get-DraftFirstScope -ChangedFiles 'docs/x.md' -IsDraft $false
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be @('ready for review')
    }

    It 'draft PR is lean regardless of paths under draftFirst' {
        foreach ($file in 'build/x.yml', 'src/Microsoft.Health.Fhir.Shared.Core/X.cs', 'docs/x.md') {
            $result = Get-DraftFirstScope -ChangedFiles $file -IsDraft $true
            $result.Scope | Should -Be 'lean' -Because $file
            $result.Reasons | Should -Be @('draft PR (draftFirst policy: path rules are advisory)')
        }
    }

    It 'labeled draft PR is full' {
        $result = Get-DraftFirstScope -Labels 'full-test-matrix' -IsDraft $true
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be @('label: Full-Test-Matrix')
    }

    It 'advisory path scope is reported for a draft' {
        (Get-DraftFirstScope -ChangedFiles 'docs/x.md', 'build/pr-pipeline.yml' -IsDraft $true).Advisory |
            Should -Be 'Advisory path scope=full (build: build/pr-pipeline.yml)'
        (Get-DraftFirstScope -ChangedFiles 'docs/x.md' -IsDraft $true).Advisory |
            Should -Be 'Advisory path scope=lean (no full or shared path changed)'
    }

    It 'advisory path scope is reported for a ready PR' {
        (Get-DraftFirstScope -ChangedFiles 'src/Microsoft.Health.Fhir.Shared.Core/X.cs' -IsDraft $false).Advisory |
            Should -Be 'Advisory path scope=shared (shared project: src/Microsoft.Health.Fhir.Shared.Core/X.cs)'
    }

    It 'advisory path scope lists at most twenty reasons' {
        $files = 1..22 | ForEach-Object { "build/$_.yml" }
        $expected = ((1..20 | ForEach-Object { "build: build/$_.yml" }) + '(+2 more)') -join '; '
        (Get-DraftFirstScope -ChangedFiles $files -IsDraft $true).Advisory | Should -Be "Advisory path scope=full ($expected)"
    }

    It 'paths policy keeps Task 3 behavior' {
        $paths = @{ Policy = 'paths'; TargetBranch = 'main' }
        (& $script:ScriptPath @paths -ChangedFiles 'docs/x.md' -IsDraft $false).Scope | Should -Be 'lean'
        (& $script:ScriptPath @paths -ChangedFiles 'docs/x.md').Scope | Should -Be 'lean'
        (& $script:ScriptPath @paths -ChangedFiles 'src/Microsoft.Health.Fhir.Shared.Core/X.cs' -IsDraft $false).Scope | Should -Be 'shared'
        (& $script:ScriptPath @paths -ChangedFiles 'build/x.yml' -IsDraft $true).Scope | Should -Be 'full'
        (& $script:ScriptPath @paths -ChangedFiles 'docs/x.md').Advisory | Should -BeNullOrEmpty
    }

    It 'unknown draft state is full' {
        $result = Get-DraftFirstScope -ChangedFiles 'docs/x.md' -IsDraft $null
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be @('fail-closed: draft state unknown')
    }

    It 'unknown draft state with unreadable labels is full without the label warning' {
        $result = Get-DraftFirstScope -IsDraft $null -LabelsUnavailableReason 'HTTP 403'
        $result.Scope | Should -Be 'full'
        $result.Warnings | Should -BeNullOrEmpty
    }

    It 'draft PR with unreadable labels is lean and says how to request full' {
        $result = Get-DraftFirstScope -IsDraft $true -LabelsUnavailableReason 'HTTP 403'
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(
            'Could not read PR labels (HTTP 403); classifying by draft state only. Add the Full-Test-Matrix label and re-run, or queue with tier1Scope=full.')
    }

    It 'draft PR targeting a release branch is full' {
        $result = Get-DraftFirstScope -IsDraft $true -TargetBranch 'release/1.2'
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be @('target branch: release/1.2')
    }

    It 'fork draft PR is lean' {
        $result = Get-DraftFirstScope -ChangedFiles 'docs/x.md' -IsDraft $true -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.Reasons | Should -Be @('fork PR: Tier 1 only')
        $result.FullRequiresMaintainer | Should -BeFalse
        $result.Warnings | Should -BeNullOrEmpty
        $result.Advisory | Should -Be 'Advisory path scope=lean (no full or shared path changed)'
    }

    It 'fork draft PR with full advisory paths is lean with a warning' {
        $result = Get-DraftFirstScope -ChangedFiles 'build/x.yml' -IsDraft $true -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(Get-ForkWarning 'build: build/x.yml')
    }

    It 'ready fork PR is lean with a warning' {
        $result = Get-DraftFirstScope -ChangedFiles 'docs/x.md' -IsDraft $false -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.FullRequiresMaintainer | Should -BeFalse
        $result.Warnings | Should -Be @(Get-ForkWarning 'ready for review')
    }

    It 'labeled fork PR is lean with a warning' {
        $result = Get-DraftFirstScope -Labels 'Full-Test-Matrix' -IsDraft $true -IsFork $true
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(Get-ForkWarning 'label: Full-Test-Matrix')
    }

    It 'fork PR with an unknown draft state is lean with a warning' {
        $result = Get-DraftFirstScope -IsDraft $null -IsFork $true -LabelsUnavailableReason 'timeout'
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(Get-ForkWarning 'fail-closed: draft state unknown')
    }

    It 'fork warning lists at most five reasons' {
        $files = 1..7 | ForEach-Object { "build/$_.yml" }
        $result = Get-DraftFirstScope -ChangedFiles $files -IsDraft $false -IsFork $true
        $result.Warnings | Should -Be @(Get-ForkWarning (
                'ready for review; build: build/1.yml; build: build/2.yml; build: build/3.yml; build: build/4.yml; (+3 more)'))
    }

    # Non-PR runs: classify-pr.yml makes them full; the merge queue keeps its Task 7 fork gate.
    It 'non-PR runs unchanged' {
        $queue = 'refs/heads/gh-readonly-queue/main/pr-123-abc'
        $result = & $script:ScriptPath -ChangedFiles 'docs/x.md' -SourceBranch $queue
        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be @("merge-queue: $queue")
        $result.FullRequiresMaintainer | Should -BeFalse

        $fork = & $script:ScriptPath -ChangedFiles 'docs/x.md' -SourceBranch $queue -IsFork $true
        $fork.Scope | Should -Be 'full'
        $fork.FullRequiresMaintainer | Should -BeTrue
        $fork.Warnings | Should -BeNullOrEmpty

        $paths = & $script:ScriptPath -Policy paths -ChangedFiles 'docs/x.md' -SourceBranch $queue -IsFork $true
        $paths.Scope | Should -Be 'full'
        $paths.FullRequiresMaintainer | Should -BeTrue
    }

    It 'rejects an unknown policy' {
        { & $script:ScriptPath -Policy 'labels' -ChangedFiles 'docs/x.md' } | Should -Throw
    }
}
