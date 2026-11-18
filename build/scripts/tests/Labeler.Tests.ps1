BeforeAll {
    $script:ClassifierPath = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'
    $script:LabelerPath = Join-Path $PSScriptRoot '..' '..' '..' '.github' 'labeler.yml'

    # The any-glob-to-any-file globs of the Full-Test-Matrix label: the quoted list items between "Full-Test-Matrix:"
    # and the next top-level key. Line-based, because the file's shape is fixed by actions/labeler and no YAML module
    # is assumed.
    function Get-FullMatrixGlobs {
        $globs = @()
        $inBlock = $false
        foreach ($line in Get-Content -LiteralPath $script:LabelerPath) {
            if ($line -match '^Full-Test-Matrix:') { $inBlock = $true; continue }
            if ($inBlock -and $line -match '^[^\s#-]') { break }
            if ($inBlock -and $line -match "^\s+-\s+'([^']+)'\s*$") { $globs += $Matches[1] }
        }

        $globs
    }

    # minimatch (actions/labeler) to regex, for the subset the label uses: ** spans directories, * and ? stay within one
    # path segment. Case-sensitive, as minimatch is by default.
    function ConvertTo-GlobRegex([string] $Glob) {
        $regex = [regex]::Escape($Glob)
        $regex = $regex.Replace('/\*\*/', '/(?:.*/)?').Replace('/\*\*', '/.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
        "^$regex$"
    }

    function Test-Labeled([string] $Path) {
        foreach ($glob in Get-FullMatrixGlobs) {
            if ($Path -cmatch (ConvertTo-GlobRegex $glob)) { return $true }
        }

        $false
    }

    # The single-path samples of Get-PrValidationScope.Tests.ps1 (Task 3), with their real casing, plus Key Vault, ACA and
    # nested-folder paths. Excluded, because only the classifier can decide them: Directory.Packages.props (full or shared
    # by its changed packages, rulings R15b/R15c) and case or backslash variants (the classifier normalizes paths; GitHub
    # reports the repository's own casing).
    $script:Samples = @(
        'src/Microsoft.Health.Fhir.R4.Core/Foo.cs'
        'docs/x.md'
        'src/Microsoft.Health.Fhir.Shared.Core/Features/X.cs'
        'test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/X.cs'
        'src/Microsoft.Health.Fhir.CosmosDb/X.cs'
        'test/Microsoft.Health.Fhir.CosmosDb.Tests.Integration/x.cs'
        'src/Microsoft.Health.Fhir.Stu3.Core/X.cs'
        'test/Microsoft.Health.Fhir.R5.Tests.E2E/X.cs'
        'src/Microsoft.Health.Fhir.R4B.Api/X.cs'
        'build/pr-pipeline.yml'
        'samples/templates/default-azuredeploy.json'
        'src/Microsoft.Health.Fhir.Api.OpenIddict/X.cs'
        'src/Microsoft.Health.Fhir.Api/Features/Security/X.cs'
        'src/Microsoft.Health.Fhir.Shared.Core/Features/CosmosLikeName.cs'
        'build/x.yml'
        'src/Microsoft.Health.Fhir.R4.Core/Directory.Packages.props'
        'global.json'
        'Directory.Build.props'
        'Directory.Build.targets'
        'nuget.config'
        'src/Microsoft.Health.Fhir.R4.Core/Directory.Build.props'
        'src/Microsoft.Health.Fhir.Shared.Core/Directory.Build.props'
        'build/templates/steps/delete-keyvaults.yml'
        'samples/templates/aca/aca-environment.bicep'
        'src/Microsoft.Health.Fhir.Core/Features/Security/Authorization/X.cs'
        'src/Microsoft.Health.Fhir.Stu3.Core/Directory.Build.props'
        'test/Configuration/testauthenvironment.json'
        'samples/apps/x.cs'
        'README.md'
    )
}

Describe 'Full-Test-Matrix label (.github/labeler.yml)' {
    It 'has globs' {
        @(Get-FullMatrixGlobs).Count | Should -BeGreaterThan 0
    }

    It 'agrees with the classifier on full versus not full for every sample path' {
        foreach ($path in $script:Samples) {
            $classifiedFull = (& $script:ClassifierPath -ChangedFiles $path -TargetBranch 'main').Scope -eq 'full'
            Test-Labeled $path | Should -Be $classifiedFull -Because $path
        }
    }

    It 'does not label Directory.Packages.props, which the classifier decides by its changed packages' {
        Test-Labeled 'Directory.Packages.props' | Should -BeFalse
    }

    It 'converts ** to zero or more directories' {
        'src/Security/X.cs' | Should -Match (ConvertTo-GlobRegex 'src/**/Security/**')
        'src/A/B/Security/C/X.cs' | Should -Match (ConvertTo-GlobRegex 'src/**/Security/**')
        'src/Microsoft.Health.Fhir.Stu3.Core/X.cs' | Should -Not -Match (ConvertTo-GlobRegex 'src/*Cosmos*/**')
    }
}
