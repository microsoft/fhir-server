BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Update-TestShards.ps1'
    $script:Ns = 'Microsoft.Health.Fhir.Tests.E2E.Rest'

    function New-Fixture {
        param([object[]] $Rows)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        ConvertTo-Json -InputObject $Rows | Set-Content -Path $path
        return $path
    }

    function New-Row {
        param([int] $Build, [string] $Test, [int] $Ms)
        [pscustomobject]@{ buildId = $Build; automatedTestName = "$script:Ns.$Test"; durationInMs = $Ms }
    }
}

Describe 'Update-TestShards' {
    BeforeEach {
        $script:out = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '-manifest.json')
        $script:rows = @(
            (New-Row 1 'Search.BigTests.A' 6000), (New-Row 1 'Search.BigTests.B' 4000),
            (New-Row 1 'ReadTests.A' 5000),
            (New-Row 1 'HistoryTests.A' 3000),
            (New-Row 1 'BulkDeleteTests.A' 2000),
            (New-Row 1 'Import.ImportTests.One' 2500), (New-Row 1 'Import.ImportTests.Two' 2500),
            (New-Row 1 'CreateTests.A' 1000)
        )
    }

    It 'packs units into shards and ends with a catch-all' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out | Out-Null

        $manifest = Get-Content $script:out -Raw | ConvertFrom-Json
        @($manifest.shards).Count | Should -Be 3
        @($manifest.shards[-1].PSObject.Properties).Count | Should -Be 0

        $listed = @($manifest.shards[0..1] | ForEach-Object { $_.classes; $_.methods })
        $listed | Should -Contain "$script:Ns.Search.BigTests"
        $listed | Should -Contain "$script:Ns.Import.ImportTests.One"
        $listed | Should -Not -Contain "$script:Ns.Import.ImportTests"
        ($listed | Group-Object | Where-Object Count -gt 1) | Should -BeNullOrEmpty
    }

    It 'leaves HistoryTests in the catch-all and keeps import and bulk-delete tests out of it' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out | Out-Null

        $manifest = Get-Content $script:out -Raw | ConvertFrom-Json
        $listed = @($manifest.shards | ForEach-Object { $_.classes; $_.methods })
        $listed | Should -Not -Contain "$script:Ns.HistoryTests"
        $listed | Should -Contain "$script:Ns.BulkDeleteTests"
        $listed | Should -Contain "$script:Ns.Import.ImportTests.Two"
    }

    It 'uses the median of the builds' {
        $rows = @($script:rows) + @(
            (New-Row 2 'ReadTests.A' 1000), (New-Row 3 'ReadTests.A' 1000), (New-Row 2 'Search.BigTests.A' 100))
        $output = & $script:ScriptPath -ShardCount 2 -FromBuilds 3 -FromFile (New-Fixture $rows) -OutFile $script:out -DryRun 6>&1 | ForEach-Object { "$_" }

        # Medians: BigTests (10000, 100) = 5050 ms, ReadTests (5000, 1000, 1000) = 1000 ms, others as in build 1: 17.05 s in total.
        $loads = $output | Where-Object { $_ -match '^Shard \d' } | ForEach-Object { [double]($_ -replace '^.*?:\s+([\d.]+) s.*$', '$1') }
        ($loads | Measure-Object -Sum).Sum | Should -BeGreaterThan 16
        ($loads | Measure-Object -Sum).Sum | Should -BeLessThan 18
    }

    It 'does not write the manifest on a dry run' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DryRun | Out-Null

        Test-Path $script:out | Should -BeFalse
    }

    It 'output passes the Get-TestShardFilter validation' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out | Out-Null
        $filter = Join-Path $PSScriptRoot '..' 'Get-TestShardFilter.ps1'

        { 0..2 | ForEach-Object { & $filter -Manifest $script:out -ShardIndex $_ } } | Should -Not -Throw
    }
}
