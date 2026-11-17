BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Test-TestShards.ps1'

    # A stand-in for a Microsoft.Testing.Platform application: prints its tests, honouring --filter-method
    # (OR'd wildcards) and, unless $IgnoreNotMethod, --filter-not-method. Exits 8 when nothing matches.
    function New-FakeTestApp {
        param([switch] $IgnoreNotMethod)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.ps1')
        $notMethod = if ($IgnoreNotMethod) { '$excluded = @()' } else { '$excluded = @(for ($i = 0; $i -lt $args.Count - 1; $i++) { if ($args[$i] -eq "--filter-not-method") { $args[$i + 1] } })' }
        @"
`$tests = 'Ns.Alpha.One', 'Ns.Alpha.Two', 'Ns.Beta.One', 'Ns.Gamma.One', 'Ns.Gamma.Two'
`$included = @(for (`$i = 0; `$i -lt `$args.Count - 1; `$i++) { if (`$args[`$i] -eq '--filter-method') { `$args[`$i + 1] } })
$notMethod
`$selected = @(`$tests | Where-Object {
    `$name = `$_
    (`$included.Count -eq 0 -or @(`$included | Where-Object { `$name -like `$_ }).Count -gt 0) -and
    @(`$excluded | Where-Object { `$name -like `$_ }).Count -eq 0
})
`$selected | ForEach-Object { "  `$(`$_.Substring(0, `$_.LastIndexOf('.')))(Sql, Json)`$(`$_.Substring(`$_.LastIndexOf('.')))" }
"  duration: 1s"
if (`$selected.Count -eq 0) { exit 8 }
exit 0
"@ | Set-Content -Path $path
        return $path
    }

    function New-Manifest {
        param([object[]] $Shards)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        ConvertTo-Json -InputObject @{ suite = 'test-suite'; shards = $Shards } -Depth 6 | Set-Content -Path $path
        return $path
    }
}

Describe 'Test-TestShards' {
    It 'passes when the shards partition the tests' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.Alpha') },
            @{ methods = @('Ns.Gamma.One') },
            @{})

        $result = & $script:ScriptPath -TestExe (New-FakeTestApp) -Manifest $manifest

        $result.Passed | Should -BeTrue
        $result.UnfilteredCount | Should -Be 5
        ($result.Shards | ForEach-Object Count) | Should -Be @(2, 1, 2)
    }

    It 'reports an empty shard without throwing' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.Missing') },
            @{})

        $result = & $script:ScriptPath -TestExe (New-FakeTestApp) -Manifest $manifest

        $result.Passed | Should -BeFalse
        $result.Problems | Should -Contain 'Shard 0 lists no tests.'
    }

    It 'reports overlap and a union mismatch when the catch-all does not exclude listed tests' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.Alpha') },
            @{})

        $result = & $script:ScriptPath -TestExe (New-FakeTestApp -IgnoreNotMethod) -Manifest $manifest

        $result.Passed | Should -BeFalse
        ($result.Problems -join "`n") | Should -Match 'Shards 0 and 1 both list'
        ($result.Problems -join "`n") | Should -Match 'unfiltered run lists 5'
    }

    It 'turns an invalid manifest into a problem' {
        $manifest = New-Manifest @(@{}, @{ classes = @('Ns.Alpha') })

        $result = & $script:ScriptPath -TestExe (New-FakeTestApp) -Manifest $manifest

        $result.Passed | Should -BeFalse
        ($result.Problems -join "`n") | Should -Match 'catch-all'
    }

    It 'emits a warning and ShardLintFailed in pipeline mode' {
        $manifest = New-Manifest @(@{ classes = @('Ns.Missing') }, @{})
        $good = New-Manifest @(@{ classes = @('Ns.Alpha') }, @{})

        $failed = & $script:ScriptPath -TestExe (New-FakeTestApp) -Manifest $manifest -Pipeline 6>&1 | Out-String
        $passed = & $script:ScriptPath -TestExe (New-FakeTestApp) -Manifest $good -Pipeline 6>&1 | Out-String

        $failed | Should -Match '##vso\[task\.logissue type=warning\]Shard lint: Shard 0 lists no tests\.'
        $failed | Should -Match 'variable=ShardLintFailed;isOutput=true\]true'
        $passed | Should -Match 'variable=ShardLintFailed;isOutput=true\]false'
        $passed | Should -Not -Match 'logissue'
    }
}
