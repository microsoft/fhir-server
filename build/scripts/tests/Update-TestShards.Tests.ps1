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
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DedicatedShardCount 0 | Out-Null

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
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DedicatedShardCount 0 | Out-Null

        $manifest = Get-Content $script:out -Raw | ConvertFrom-Json
        $listed = @($manifest.shards | ForEach-Object { $_.classes; $_.methods })
        $listed | Should -Not -Contain "$script:Ns.HistoryTests"
        $listed | Should -Contain "$script:Ns.BulkDeleteTests"
        $listed | Should -Contain "$script:Ns.Import.ImportTests.Two"
    }

    It 'uses the median of the builds' {
        $rows = @($script:rows) + @(
            (New-Row 2 'ReadTests.A' 1000), (New-Row 3 'ReadTests.A' 1000), (New-Row 2 'Search.BigTests.A' 100))
        $output = & $script:ScriptPath -ShardCount 2 -FromBuilds 3 -FromFile (New-Fixture $rows) -OutFile $script:out -DedicatedShardCount 0 -DryRun 6>&1 | ForEach-Object { "$_" }

        # Medians: BigTests (10000, 100) = 5050 ms, ReadTests (5000, 1000, 1000) = 1000 ms, others as in build 1: 17.05 s in total.
        $loads = $output | Where-Object { $_ -match '^Shard \d' } | ForEach-Object { [double]($_ -replace '^.*?:\s+([\d.]+) s.*$', '$1') }
        ($loads | Measure-Object -Sum).Sum | Should -BeGreaterThan 16
        ($loads | Measure-Object -Sum).Sum | Should -BeLessThan 18
    }

    It 'does not write the manifest on a dry run' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DedicatedShardCount 0 -DryRun | Out-Null

        Test-Path $script:out | Should -BeFalse
    }

    It 'output passes the Get-TestShardFilter validation' {
        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DedicatedShardCount 0 | Out-Null
        $filter = Join-Path $PSScriptRoot '..' 'Get-TestShardFilter.ps1'

        { 0..2 | ForEach-Object { & $filter -Manifest $script:out -ShardIndex $_ } } | Should -Not -Throw
    }
    It 'groups theory cases whose arguments contain dots under their class' {
        $rows = @($script:rows) + @(
            (New-Row 1 'Search.TheoryTests.Case(value: "a.b.c", other: 1.5)' 20000),
            (New-Row 1 'Search.TheoryTests.Case(value: "x.y", other: 2.5)' 10000))

        & $script:ScriptPath -ShardCount 3 -FromBuilds 1 -FromFile (New-Fixture $rows) -OutFile $script:out -DedicatedShardCount 0 | Out-Null

        $manifest = Get-Content $script:out -Raw | ConvertFrom-Json
        $listed = @($manifest.shards | ForEach-Object { $_.classes; $_.methods })
        $listed | Should -Contain "$script:Ns.Search.TheoryTests"
        @($listed | Where-Object { $_ -match '[(")]|a\.b|x\.y' }) | Should -BeNullOrEmpty
    }

    It 'packs dedicated classes only into the dedicated shards and records them in the manifest' {
        $rows = @($script:rows) + @(
            (New-Row 1 'Reindex.ReindexTests.A' 9000), (New-Row 1 'Reindex.ReindexTests.B' 8000), (New-Row 1 'Reindex.ReindexTests.C' 7000),
            (New-Row 1 'Search.CustomSearchParamTests.A' 500), (New-Row 1 'BulkUpdateTests.A' 1500))

        & $script:ScriptPath -ShardCount 4 -FromBuilds 1 -FromFile (New-Fixture $rows) -OutFile $script:out -DedicatedShardCount 2 | Out-Null

        $manifest = Get-Content $script:out -Raw | ConvertFrom-Json
        $manifest.dedicatedShards | Should -Be 2
        $manifest.dedicatedCategories | Should -Be @('IndexAndReindex', 'BulkUpdate')
        $dedicatedUnits = @($manifest.shards[0..1] | ForEach-Object { $_.classes; $_.methods })
        $dedicatedUnits | Should -Contain "$script:Ns.Reindex.ReindexTests.A"
        $dedicatedUnits | Should -Contain "$script:Ns.BulkUpdateTests"
        $dedicatedUnits | Should -Not -Contain "$script:Ns.Search.BigTests"
        @($dedicatedUnits | Where-Object { $_ -notmatch 'Reindex|CustomSearchParam|BulkUpdate' }) | Should -BeNullOrEmpty
        @($manifest.shards[2] | ForEach-Object { $_.classes; $_.methods } | Where-Object { $_ -match 'Reindex|CustomSearchParam|BulkUpdate' }) | Should -BeNullOrEmpty
        @($manifest.shards[0].methods).Count | Should -BeGreaterThan 0
        @($manifest.shards[1].methods).Count | Should -BeGreaterThan 0
    }

    It 'rejects a shard count with no general shard' {
        { & $script:ScriptPath -ShardCount 2 -FromBuilds 1 -FromFile (New-Fixture $script:rows) -OutFile $script:out -DedicatedShardCount 2 } | Should -Throw '*must be smaller*'
    }
}
