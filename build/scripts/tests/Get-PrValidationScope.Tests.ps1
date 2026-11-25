BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'

    function Get-Scope {
        param([hashtable] $Arguments)
        & $script:ScriptPath @Arguments 6>$null
    }
}

Describe 'Get-PrValidationScope' {
    It 'takes the override of a manual run' -ForEach @('lean', 'full') {
        $result = Get-Scope @{ BuildReason = 'Manual'; ManualOverride = $_ }

        $result.Scope | Should -Be $_
        $result.Reasons | Should -Be "override: tier1Scope=$_ (manual run)"
    }

    It 'ignores the override of a PR run' {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; ManualOverride = 'lean'; IsDraft = $false }

        $result.Scope | Should -Be 'full'
    }

    It 'makes a run that is not a pull request full' -ForEach @('Manual', 'IndividualCI', 'Schedule') {
        $result = Get-Scope @{ BuildReason = $_ }

        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be "not a pull request (Build.Reason=$_)"
    }

    It 'makes a fork PR lean with a warning, even when ready or labeled' {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; IsFork = $true; IsDraft = $false; Labels = @('Full-Test-Matrix') }

        $result.Scope | Should -Be 'lean'
        $result.Warning | Should -BeLike 'Fork PR:*'
    }

    It 'fails closed to full when the draft state is unknown' {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; IsDraft = $null; LabelsUnavailableReason = 'HTTP 503' }

        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be 'fail-closed: draft state unknown: HTTP 503'
    }

    It 'makes a ready PR full' {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; IsDraft = $false }

        $result.Scope | Should -Be 'full'
        $result.Reasons | Should -Be 'ready for review'
    }

    It 'makes a draft labeled Full-Test-Matrix full, in any case' -ForEach @('Full-Test-Matrix', 'full-test-matrix') {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; IsDraft = $true; Labels = @('bug', $_) }

        $result.Scope | Should -Be 'full'
    }

    It 'makes any other draft lean without a warning' {
        $result = Get-Scope @{ BuildReason = 'PullRequest'; IsDraft = $true; Labels = @('Full-Test-Matrix-Later') }

        $result.Scope | Should -Be 'lean'
        $result.Reasons | Should -Be 'draft PR'
        $result.Warning | Should -BeNullOrEmpty
    }

    It 'rejects an unknown override' {
        { Get-Scope @{ BuildReason = 'Manual'; ManualOverride = 'shared' } } | Should -Throw
    }
}
