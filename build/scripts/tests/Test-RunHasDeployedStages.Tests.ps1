BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Test-RunHasDeployedStages.ps1'
    $script:Stages = @('testR4Cosmos', 'cleanup_testR4Sql')

    function New-Timeline {
        param([hashtable[]] $Records)
        [pscustomobject]@{ records = @($Records | ForEach-Object { [pscustomobject]$_ }) }
    }

    function New-StageRecord {
        param([string] $Identifier, [string] $Result = 'succeeded', [string] $Type = 'Stage')
        # In a real timeline name is the stage's displayName, so only identifier identifies the stage.
        @{ type = $Type; identifier = $Identifier; name = "Display of $Identifier"; result = $Result }
    }
}

# GetTimeline scriptblocks are closures over a hashtable: they run in the script's scope, not the test's.
Describe 'Test-RunHasDeployedStages' {
    It 'returns false for a lean run without the deploy stages' {
        $timeline = New-Timeline @(
            (New-StageRecord 'Tier1Lean'),
            (New-StageRecord 'Tier1Gate'))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeFalse
    }

    It 'returns true for a legacy run where every listed stage succeeded' {
        $timeline = New-Timeline @(
            (New-StageRecord 'Build'),
            (New-StageRecord 'testR4Cosmos'),
            (New-StageRecord 'cleanup_testR4Sql'),
            (New-StageRecord 'testR5Cosmos'))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeTrue
    }

    It 'returns false when one listed stage is missing' {
        $timeline = New-Timeline @((New-StageRecord 'testR4Cosmos'))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeFalse
    }

    It 'returns false when a listed stage did not succeed' -ForEach @('failed', 'canceled', 'skipped', $null) {
        $timeline = New-Timeline @(
            (New-StageRecord 'testR4Cosmos'),
            (New-StageRecord 'cleanup_testR4Sql' -Result $_))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeFalse
    }

    It 'returns true when a listed stage succeeded with issues' {
        $timeline = New-Timeline @(
            (New-StageRecord 'testR4Cosmos' -Result 'succeededWithIssues'),
            (New-StageRecord 'cleanup_testR4Sql'))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeTrue
    }

    It 'matches the stage identifier, not the display name or a non-stage record' {
        $timeline = New-Timeline @(
            @{ type = 'Stage'; identifier = 'Other'; name = 'testR4Cosmos'; result = 'succeeded' },
            @{ type = 'Stage'; identifier = 'Other2'; name = 'cleanup_testR4Sql'; result = 'succeeded' },
            (New-StageRecord 'testR4Cosmos' -Type 'Job'),
            (New-StageRecord 'cleanup_testR4Sql' -Type 'Job'))

        & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline { $timeline }.GetNewClosure() | Should -BeFalse
    }

    It 'passes the run id to GetTimeline' {
        $state = @{ Seen = $null }
        $getTimeline = { param($Id) $state.Seen = $Id; [pscustomobject]@{ records = @() } }.GetNewClosure()

        & $script:ScriptPath -RunId 53465 -StageNames $script:Stages -GetTimeline $getTimeline | Out-Null

        $state.Seen | Should -Be 53465
    }

    It 'throws when the timeline cannot be read, so nothing is cleaned up' {
        $getTimeline = { throw 'HTTP 503' }

        { & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline $getTimeline } |
            Should -Throw '*Could not read the timeline of run 1*HTTP 503*'
    }

    It 'throws when the timeline has no records property' {
        $getTimeline = { [pscustomobject]@{} }

        { & $script:ScriptPath -RunId 1 -StageNames $script:Stages -GetTimeline $getTimeline } |
            Should -Throw '*Could not read the timeline of run 1*'
    }
}
