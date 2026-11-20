BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-TestShardFilter.ps1'

    function New-Manifest {
        param([object[]] $Shards)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        ConvertTo-Json -InputObject @{ suite = 'test-suite'; shards = $Shards } -Depth 6 | Set-Content -Path $path
        return $path
    }

    # Returns the value that follows each occurrence of $Flag in an MTP argument list.
    function Get-FlagValues {
        param([string[]] $Arguments, [string] $Flag)
        for ($i = 0; $i -lt $Arguments.Count - 1; $i++) {
            if ($Arguments[$i] -eq $Flag) { $Arguments[$i + 1] }
        }
    }
}

Describe 'Get-TestShardFilter' {
    It 'emits --filter-method wildcard per listed class' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.AlphaTests', 'Ns.BetaTests') },
            @{})

        $arguments = & $script:ScriptPath -Manifest $manifest -ShardIndex 0

        $arguments | Should -Be @('--filter-method', 'Ns.AlphaTests.*', '--filter-method', 'Ns.BetaTests.*')
    }

    It 'emits exact --filter-method per listed method' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.AlphaTests'); methods = @('Ns.ImportTests.GivenA', 'Ns.ImportTests.GivenB') },
            @{ methods = @('Ns.ImportTests.GivenC') },
            @{})

        $first = & $script:ScriptPath -Manifest $manifest -ShardIndex 0
        $second = & $script:ScriptPath -Manifest $manifest -ShardIndex 1

        Get-FlagValues $first '--filter-method' | Should -Be @('Ns.AlphaTests.*', 'Ns.ImportTests.GivenA', 'Ns.ImportTests.GivenB')
        Get-FlagValues $second '--filter-method' | Should -Be @('Ns.ImportTests.GivenC')
    }

    It 'catch-all excludes every listed class and method' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.AlphaTests'); methods = @('Ns.ImportTests.GivenA') },
            @{ classes = @('Ns.BetaTests'); methods = @('Ns.ImportTests.GivenB') },
            @{})

        $arguments = & $script:ScriptPath -Manifest $manifest -ShardIndex 2

        $arguments | Should -Not -Contain '--filter-method'
        Get-FlagValues $arguments '--filter-not-method' | Should -Be @(
            'Ns.AlphaTests.*', 'Ns.BetaTests.*', 'Ns.ImportTests.GivenA', 'Ns.ImportTests.GivenB')
    }

    It 'throws on duplicate class across shards' {
        $manifest = New-Manifest @(
            @{ classes = @('Ns.AlphaTests') },
            @{ classes = @('Ns.AlphaTests') },
            @{})

        { & $script:ScriptPath -Manifest $manifest -ShardIndex 0 } | Should -Throw '*Duplicate*Ns.AlphaTests*'
    }

    It 'throws when catch-all is not last' {
        $manifest = New-Manifest @(
            @{},
            @{ classes = @('Ns.AlphaTests') })

        { & $script:ScriptPath -Manifest $manifest -ShardIndex 1 } | Should -Throw '*catch-all*'
    }

    It 'throws on duplicate method and on a method whose class is also listed' {
        $duplicateMethod = New-Manifest @(
            @{ methods = @('Ns.ImportTests.GivenA') },
            @{ methods = @('Ns.ImportTests.GivenA') },
            @{})
        $methodOfListedClass = New-Manifest @(
            @{ classes = @('Ns.ImportTests') },
            @{ methods = @('Ns.ImportTests.GivenA') },
            @{})

        { & $script:ScriptPath -Manifest $duplicateMethod -ShardIndex 0 } | Should -Throw '*Duplicate*'
        { & $script:ScriptPath -Manifest $methodOfListedClass -ShardIndex 0 } | Should -Throw '*Duplicate*'
    }

    It 'throws on an out-of-range shard index' {
        $manifest = New-Manifest @(@{ classes = @('Ns.AlphaTests') }, @{})

        { & $script:ScriptPath -Manifest $manifest -ShardIndex 2 } | Should -Throw '*out of range*'
    }
}
