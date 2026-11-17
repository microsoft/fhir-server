BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Invoke-ParallelUnitTests.ps1'

    # Lays out a source tree like the repository's: src/<Name>/<Name>.csproj for each unit test project, plus the
    # solution file beside src. A project is a bare MSBuild file, enough for the property evaluation the script does.
    # -Projects maps a project name to its target frameworks. Returns the source root.
    function New-SourceTree {
        param([hashtable] $Projects)
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $source = Join-Path $root 'src'
        $null = New-Item -ItemType Directory -Path $source
        Set-Content -Path (Join-Path $root 'Microsoft.Health.Fhir.sln') -Value ''
        foreach ($name in $Projects.Keys) {
            $directory = Join-Path $source $name
            $null = New-Item -ItemType Directory -Path $directory
            Set-Content -Path (Join-Path $directory "$name.csproj") -Value "<Project><PropertyGroup><TargetFrameworks>$($Projects[$name])</TargetFrameworks></PropertyGroup></Project>"
        }

        return $source
    }

    function New-Durations {
        param([hashtable] $Minutes)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        ConvertTo-Json -InputObject @{ suite = 'unit-tests'; minutes = $Minutes } | Set-Content -Path $path
        return $path
    }

    # Names of the projects listed for a shard.
    function Get-Shard {
        param([string] $Source, [string] $Durations, [string] $Shard, [string] $Framework = 'net11.0')
        $arguments = @{ Framework = $Framework; Configuration = 'Release'; SourceRoot = $Source; DurationsFile = $Durations; ListProjects = $true }
        if ($Shard) { $arguments.ModuleShard = $Shard }
        $names = [string[]] @(& $script:ScriptPath @arguments | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
        [Array]::Sort($names, [StringComparer]::Ordinal)
        $names
    }
}

Describe 'Invoke-ParallelUnitTests -ListProjects' {
    It 'fails with the evaluation output when a project cannot be evaluated' {
        $source = New-SourceTree @{ 'A.UnitTests' = 'net11.0' }
        Set-Content -Path (Join-Path $source 'A.UnitTests' 'A.UnitTests.csproj') -Value '<Project><NotClosed>'
        $durations = New-Durations @{ 'A.UnitTests' = 1 }

        $thrown = $null
        try { & $script:ScriptPath -Framework net11.0 -Configuration Release -SourceRoot $source -DurationsFile $durations -ListProjects 6>$null }
        catch { $thrown = $_ }

        $thrown | Should -Not -BeNullOrEmpty
        $thrown.Exception.Message | Should -Match 'Unable to evaluate 1 unit test project'
    }

    BeforeAll {
        $script:Both = 'net11.0;net10.0'
        $script:Source = New-SourceTree @{
            'A.UnitTests' = $script:Both
            'B.UnitTests' = $script:Both
            'C.UnitTests' = $script:Both
            'Old.UnitTests' = 'net10.0'
        }
        $script:Durations = New-Durations @{ 'A.UnitTests' = 10; 'B.UnitTests' = 6; 'C.UnitTests' = 5; 'Old.UnitTests' = 1 }
    }

    It 'lists the full path of each project that targets the framework' {
        $paths = @(& $script:ScriptPath -Framework net11.0 -Configuration Release -SourceRoot $script:Source -DurationsFile $script:Durations -ListProjects)

        $paths | Should -HaveCount 3
        $paths | ForEach-Object { Test-Path -LiteralPath $_ -PathType Leaf | Should -BeTrue }
        ($paths | ForEach-Object { [IO.Path]::GetFileName($_) }) | Should -Be @('A.UnitTests.csproj', 'B.UnitTests.csproj', 'C.UnitTests.csproj')
    }

    It 'includes a project that targets only the other framework when asked for that framework' {
        Get-Shard $script:Source $script:Durations $null 'net10.0' | Should -Contain 'Old.UnitTests'
    }
}

Describe 'Invoke-ParallelUnitTests -ModuleShard' {
    BeforeAll {
        $script:Both = 'net11.0;net10.0'
    }

    It 'partitions the module set: the shards cover every project once' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'C.UnitTests' = $script:Both; 'D.UnitTests' = $script:Both; 'E.UnitTests' = $script:Both }
        $durations = New-Durations @{ 'A.UnitTests' = 20; 'B.UnitTests' = 9; 'C.UnitTests' = 7; 'D.UnitTests' = 3; 'E.UnitTests' = 1 }

        $first = @(Get-Shard $source $durations '1/2')
        $second = @(Get-Shard $source $durations '2/2')
        $all = @(Get-Shard $source $durations $null)

        $first | Should -Not -BeNullOrEmpty
        $second | Should -Not -BeNullOrEmpty
        @($first | Where-Object { $_ -in $second }) | Should -BeNullOrEmpty
        @($first + $second | Sort-Object) | Should -Be @($all | Sort-Object)
    }

    It 'packs the longest module first into the lighter shard' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'C.UnitTests' = $script:Both; 'D.UnitTests' = $script:Both }
        # 10 -> shard 1; 6 -> shard 2; 5 -> shard 2 (6 < 10), now 11; 4 -> shard 1 (10 < 11).
        $durations = New-Durations @{ 'A.UnitTests' = 10; 'B.UnitTests' = 6; 'C.UnitTests' = 5; 'D.UnitTests' = 4 }

        Get-Shard $source $durations '1/2' | Should -Be @('A.UnitTests', 'D.UnitTests')
        Get-Shard $source $durations '2/2' | Should -Be @('B.UnitTests', 'C.UnitTests')
    }

    It 'breaks duration ties by ordinal project name, so listing order cannot change the split' {
        $source = New-SourceTree @{ 'a2.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'A.UnitTests' = $script:Both; 'C.UnitTests' = $script:Both }
        $durations = New-Durations @{ 'A.UnitTests' = 5; 'B.UnitTests' = 5; 'C.UnitTests' = 5; 'a2.UnitTests' = 5 }

        # Ordinal order is A, B, C, a2 (not the culture order A, a2, B, C): A -> 1, B -> 2, C -> 1, a2 -> 2.
        Get-Shard $source $durations '1/2' | Should -Be @('A.UnitTests', 'C.UnitTests')
        Get-Shard $source $durations '2/2' | Should -Be @('B.UnitTests', 'a2.UnitTests')
    }

    It 'is stable across runs' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'C.UnitTests' = $script:Both; 'D.UnitTests' = $script:Both }
        $durations = New-Durations @{ 'A.UnitTests' = 8; 'B.UnitTests' = 8; 'C.UnitTests' = 3; 'D.UnitTests' = 3 }

        $firstRun = Get-Shard $source $durations '2/2'
        $secondRun = Get-Shard $source $durations '2/2'

        $secondRun | Should -Be $firstRun
    }

    It 'puts a module missing from the durations file into the lighter shard' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'New.UnitTests' = $script:Both }
        # A (30) fills shard 1, B (2) is the lighter shard 2. The unlisted project joins B.
        $durations = New-Durations @{ 'A.UnitTests' = 30; 'B.UnitTests' = 2 }

        Get-Shard $source $durations '1/2' | Should -Be @('A.UnitTests')
        Get-Shard $source $durations '2/2' | Should -Be @('B.UnitTests', 'New.UnitTests')
    }

    It 'spreads several missing modules over the shards instead of piling them onto one' {
        $source = New-SourceTree @{ 'X.UnitTests' = $script:Both; 'Y.UnitTests' = $script:Both }
        $durations = New-Durations @{ 'Unrelated.UnitTests' = 1 }

        Get-Shard $source $durations '1/2' | Should -HaveCount 1
        Get-Shard $source $durations '2/2' | Should -HaveCount 1
    }

    It 'shards only the projects that target the framework' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both; 'B.UnitTests' = $script:Both; 'Old.UnitTests' = 'net10.0' }
        $durations = New-Durations @{ 'A.UnitTests' = 4; 'B.UnitTests' = 4; 'Old.UnitTests' = 100 }

        $net11 = @(Get-Shard $source $durations '1/2' 'net11.0') + @(Get-Shard $source $durations '2/2' 'net11.0')

        $net11 | Should -Not -Contain 'Old.UnitTests'
        $net11 | Should -HaveCount 2
    }

    It 'rejects a malformed or out-of-range shard' -ForEach @(
        @{ Shard = '0/2' }, @{ Shard = '3/2' }, @{ Shard = '1/0' }, @{ Shard = 'a/b' }, @{ Shard = '1' }
    ) {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both }
        $durations = New-Durations @{ 'A.UnitTests' = 1 }

        { Get-Shard $source $durations $Shard } | Should -Throw
    }

    It 'fails when the durations file is missing' {
        $source = New-SourceTree @{ 'A.UnitTests' = $script:Both }

        { Get-Shard $source (Join-Path $TestDrive 'missing.json') '1/2' } | Should -Throw
    }
}

Describe 'Invoke-ParallelUnitTests -SolutionFilterPath' {
    It 'writes a solution filter of the selected projects, relative to the solution' {
        $both = 'net11.0;net10.0'
        $source = New-SourceTree @{ 'A.UnitTests' = $both; 'B.UnitTests' = $both }
        $durations = New-Durations @{ 'A.UnitTests' = 10; 'B.UnitTests' = 6 }
        $filter = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.slnf')

        & $script:ScriptPath -Framework net11.0 -Configuration Release -SourceRoot $source -DurationsFile $durations -ModuleShard '2/2' -SolutionFilterPath $filter

        $content = Get-Content -Raw $filter | ConvertFrom-Json
        $solution = Join-Path (Split-Path $source) 'Microsoft.Health.Fhir.sln'
        $content.solution.path | Should -Be ([IO.Path]::GetRelativePath((Split-Path $filter), $solution).Replace('/', '\'))
        @($content.solution.projects) | Should -Be @('src\B.UnitTests\B.UnitTests.csproj')
    }
}
