BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'

    function Get-Scope {
        param(
            [string[]] $ChangedFiles = @(),
            [string[]] $Labels = @(),
            [string] $TargetBranch = 'main',
            [bool] $IsFork = $false,
            [bool] $FileListTruncated = $false,
            [string[]] $ChangedPackages = @())

        & $script:ScriptPath -ChangedFiles $ChangedFiles -Labels $Labels -TargetBranch $TargetBranch `
            -IsFork $IsFork -FileListTruncated $FileListTruncated -ChangedPackages $ChangedPackages
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
        & $script:ScriptPath -ChangedFiles 'Directory.Packages.props' -TargetBranch 'main' `
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
        (Get-Scope -ChangedFiles 'docs/x.md' -Labels 'full-matrix').Scope | Should -Be 'full'
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

    It 'fork with full scope returns fullRequiresMaintainer' {
        $result = Get-Scope -ChangedFiles 'build/x.yml' -IsFork $true
        $result.Scope | Should -Be 'full'
        $result.FullRequiresMaintainer | Should -BeTrue
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
        $result = & $script:ScriptPath -ChangedFiles 'src/Microsoft.Health.Fhir.R4.Core/Foo.cs' -TargetBranch 'main' `
            -LabelsUnavailableReason 'HTTP 403 rate limit exceeded'
        $result.Scope | Should -Be 'lean'
        $result.Warnings | Should -Be @(
            'Could not read PR labels (HTTP 403 rate limit exceeded); classifying by paths only. Add the full-matrix label and re-run, or queue with tier1Scope=full.')
    }

    It 'labels unavailable still classifies full paths as full' {
        $result = & $script:ScriptPath -ChangedFiles 'build/x.yml' -TargetBranch 'main' -LabelsUnavailableReason 'timeout'
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
}
