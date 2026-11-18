BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Wait-PipelineArtifact.ps1'
}

# GetArtifact scriptblocks are closures over a hashtable: they run in the script's scope, not the test's.
Describe 'Wait-PipelineArtifact' {
    It 'returns when the artifact exists on the first poll' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; $true }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact

        $state.Polls | Should -Be 1
    }

    It 'passes the artifact name to GetArtifact' {
        $state = @{ Seen = $null }
        $getArtifact = { param($Name) $state.Seen = $Name; $true }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact

        $state.Seen | Should -Be 'tier1-R4-tests'
    }

    It 'returns after the artifact appears on a later poll' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; $state.Polls -ge 3 }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact

        $state.Polls | Should -Be 3
    }

    It 'throws with the job name after timeout' {
        $getArtifact = { $false }

        { & $script:ScriptPath -ArtifactName 'tier1-Stu3-tests' -TimeoutMinutes 0.002 -PollSeconds 0 -GetArtifact $getArtifact } |
            Should -Throw 'Artifact tier1-Stu3-tests was not published within 0.002 minutes; check the Tier1Build_Stu3 job.'
    }

    It 'retries transient errors from GetArtifact until the artifact appears' {
        $state = @{ Polls = 0 }
        $getArtifact = {
            $state.Polls++
            if ($state.Polls -lt 3) { throw 'transient 503' }
            $true
        }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact

        $state.Polls | Should -Be 3
    }

    It 'retries transient errors from GetArtifact until timeout' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; throw 'transient 503' }.GetNewClosure()

        { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 0.002 -PollSeconds 0 -GetArtifact $getArtifact } |
            Should -Throw '*was not published within*'

        $state.Polls | Should -BeGreaterThan 1
    }
}
