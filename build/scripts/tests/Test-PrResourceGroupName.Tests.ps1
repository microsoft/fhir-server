BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Test-PrResourceGroupName.ps1'

    function Test-Name([string] $Name, [string] $PrNumber = '6') {
        & $script:ScriptPath -Name $Name -ResourceGroupRoot 'msh-fhir-pr' -PrNumber $PrNumber
    }
}

Describe 'Test-PrResourceGroupName' {
    It 'matches the PR''s per-build resource group' {
        Test-Name 'msh-fhir-pr-6-53474' | Should -BeTrue
    }

    It 'matches the PR''s resource group without a build suffix' {
        Test-Name 'msh-fhir-pr-6' | Should -BeTrue
    }

    It 'does not match another PR whose number starts with the same digits' {
        Test-Name 'msh-fhir-pr-60-53474' | Should -BeFalse
        Test-Name 'msh-fhir-pr-612' | Should -BeFalse
        Test-Name 'msh-fhir-pr-6061-53479' -PrNumber '606' | Should -BeFalse
    }

    It 'does not match another root' {
        Test-Name 'msh-fhir-ci' | Should -BeFalse
        Test-Name 'msh-fhir-pr-mq-6-1' | Should -BeFalse
    }

    It 'is case-insensitive, as resource group names are' {
        Test-Name 'MSH-FHIR-PR-6-53474' | Should -BeTrue
    }

    It 'rejects a PR number that is not a number' {
        { Test-Name 'msh-fhir-pr-6-1' -PrNumber '6*' } | Should -Throw '*not a number*'
        { Test-Name 'msh-fhir-pr-mq6-1' -PrNumber 'mq6' } | Should -Throw '*not a number*'
    }

    # The previous prefix pattern, <root>-<pr>*, selected these same names for the PR's own groups; it differs only in
    # also selecting the groups of PRs whose number extends this one.
    It 'agrees with the previous prefix pattern except for longer PR numbers' {
        $names = 'msh-fhir-pr-6', 'msh-fhir-pr-6-1', 'msh-fhir-pr-6-53474', 'msh-fhir-pr-60', 'msh-fhir-pr-60-1',
            'msh-fhir-pr-6061-53479', 'msh-fhir-pr-7-1', 'msh-fhir-ci'
        foreach ($name in $names) {
            $previous = $name -like 'msh-fhir-pr-6*'
            $extendsNumber = $name -match '^msh-fhir-pr-6\d'
            Test-Name $name | Should -Be ($previous -and -not $extendsNumber) -Because $name
        }
    }
}
