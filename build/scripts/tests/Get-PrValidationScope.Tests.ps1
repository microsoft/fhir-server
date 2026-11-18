BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'

    function Get-Scope {
        param(
            [string[]] $ChangedFiles = @(),
            [string[]] $Labels = @(),
            [string] $TargetBranch = 'main',
            [bool] $IsFork = $false,
            [bool] $FileListTruncated = $false)

        & $script:ScriptPath -ChangedFiles $ChangedFiles -Labels $Labels -TargetBranch $TargetBranch `
            -IsFork $IsFork -FileListTruncated $FileListTruncated
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
