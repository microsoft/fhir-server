BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Wait-PipelineArtifact.ps1'
}

# GetArtifact scriptblocks are closures over a hashtable: they run in the script's scope, not the test's.
Describe 'Wait-PipelineArtifact' {
    It 'returns when the artifact exists on the first poll' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; $true }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

        $state.Polls | Should -Be 1
    }

    It 'passes the artifact name to GetArtifact' {
        $state = @{ Seen = $null }
        $getArtifact = { param($Name) $state.Seen = $Name; $true }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

        $state.Seen | Should -Be 'tier1-R4-tests'
    }

    It 'returns after the artifact appears on a later poll' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; $state.Polls -ge 3 }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

        $state.Polls | Should -Be 3
    }

    It 'throws with the job name after timeout' {
        $getArtifact = { $false }

        { & $script:ScriptPath -ArtifactName 'tier1-Stu3-tests' -TimeoutMinutes 0.002 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null } } |
            Should -Throw 'Artifact tier1-Stu3-tests was not published within 0.002 minutes; check the Tier1Build_Stu3 job.'
    }

    It 'retries transient errors from GetArtifact until the artifact appears' {
        $state = @{ Polls = 0 }
        $getArtifact = {
            $state.Polls++
            if ($state.Polls -lt 3) { throw 'transient 503' }
            $true
        }.GetNewClosure()

        & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

        $state.Polls | Should -Be 3
    }

    It 'retries transient errors from GetArtifact until timeout' {
        $state = @{ Polls = 0 }
        $getArtifact = { $state.Polls++; throw 'transient 503' }.GetNewClosure()

        { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 0.002 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null } } |
            Should -Throw '*was not published within*'

        $state.Polls | Should -BeGreaterThan 1
    }

    Context 'when the build job fails' {
        It 'throws immediately when the build job failed' {
            $state = @{ Polls = 0 }
            $getArtifact = { $state.Polls++; $false }.GetNewClosure()

            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { 'failed' } } |
                Should -Throw 'Tier1Build_R4 failed*'

            $state.Polls | Should -Be 1
        }

        It 'throws immediately when the build job was canceled' {
            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact { $false } -GetBuildJobResult { 'canceled' } } |
                Should -Throw 'Tier1Build_R4 canceled*'
        }

        It 'keeps waiting while the build job is in progress' {
            $state = @{ Polls = 0 }
            $getArtifact = { $state.Polls++; $state.Polls -ge 3 }.GetNewClosure()

            # An unfinished job has no result.
            & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

            $state.Polls | Should -Be 3
        }

        It 'does not fail on a job that succeeded with issues but has not published yet' {
            $state = @{ Polls = 0 }
            $getArtifact = { $state.Polls++; $state.Polls -ge 2 }.GetNewClosure()

            & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { 'succeededWithIssues' }

            $state.Polls | Should -Be 2
        }

        It 'passes the build job name to GetBuildJobResult' {
            $state = @{ Seen = $null }
            $getJob = { param($Name) $state.Seen = $Name; $null }.GetNewClosure()
            $polls = @{ Count = 0 }
            $getArtifact = { $polls.Count++; $polls.Count -ge 2 }.GetNewClosure()

            & $script:ScriptPath -ArtifactName 'tier1-Stu3-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult $getJob

            $state.Seen | Should -Be 'Tier1Build_Stu3'
        }

        It 'retries when the build job lookup fails transiently' {
            $state = @{ Polls = 0 }
            $getArtifact = { $state.Polls++; $state.Polls -ge 3 }.GetNewClosure()

            & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { throw 'transient 503' }

            $state.Polls | Should -Be 3
        }
    }

    Context 'HTTP errors from the build API' {
        BeforeAll {
            function script:New-HttpError([int] $Status) {
                [System.Net.Http.HttpRequestException]::new("HTTP $Status", $null, [System.Net.HttpStatusCode] $Status)
            }
        }

        It '401 from GetArtifact is fatal and not retried' {
            $state = @{ Polls = 0 }
            $error401 = New-HttpError 401
            $getArtifact = { $state.Polls++; throw $error401 }.GetNewClosure()

            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null } } |
                Should -Throw '*permission*401*'

            $state.Polls | Should -Be 1
        }

        It '403 is fatal' {
            $state = @{ Polls = 0 }
            $error403 = New-HttpError 403
            $getArtifact = { $state.Polls++; throw $error403 }.GetNewClosure()

            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null } } |
                Should -Throw '*permission*403*'

            $state.Polls | Should -Be 1
        }

        It '401 from the build job lookup is fatal' {
            $error401 = New-HttpError 401
            $getJob = { throw $error401 }.GetNewClosure()

            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact { $false } -GetBuildJobResult $getJob } |
                Should -Throw '*permission*401*'
        }

        It '5xx is retried' {
            $state = @{ Polls = 0 }
            $error503 = New-HttpError 503
            $getArtifact = { $state.Polls++; if ($state.Polls -lt 3) { throw $error503 }; $true }.GetNewClosure()

            & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

            $state.Polls | Should -Be 3
        }

        It '429 is retried' {
            $state = @{ Polls = 0 }
            $error429 = New-HttpError 429
            $getArtifact = { $state.Polls++; if ($state.Polls -lt 2) { throw $error429 }; $true }.GetNewClosure()

            & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null }

            $state.Polls | Should -Be 2
        }

        It 'any other 4xx is fatal' {
            $error400 = New-HttpError 400
            $getArtifact = { throw $error400 }.GetNewClosure()
            { & $script:ScriptPath -ArtifactName 'tier1-R4-tests' -TimeoutMinutes 1 -PollSeconds 0 -GetArtifact $getArtifact -GetBuildJobResult { $null } } |
                Should -Throw '*400*'
        }
    }
}
