Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NuGetBinaryClosure.Tests.$([guid]::NewGuid().ToString('N'))")
$script:ModulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'NuGetBinaryClosure.psm1'
$script:RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$script:SourceConfigPath = Join-Path $script:RepoRoot 'nuget.config'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not [object]::Equals($Expected, $Actual)) {
        throw "$Message`nExpected: $Expected`nActual:   $Actual"
    }
}

function Assert-SequenceEqual {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)]
        [object[]]$Expected,

        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)]
        [object[]]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $expectedItems = @($Expected)
    $actualItems = @($Actual)

    if ($expectedItems.Count -ne $actualItems.Count) {
        throw "$Message`nExpected count: $($expectedItems.Count)`nActual count:   $($actualItems.Count)"
    }

    for ($index = 0; $index -lt $expectedItems.Count; $index++) {
        if (-not [object]::Equals($expectedItems[$index], $actualItems[$index])) {
            throw "$Message`nIndex:    $index`nExpected: $($expectedItems[$index])`nActual:   $($actualItems[$index])"
        }
    }
}

function Invoke-TestCase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock
    )

    try {
        & $ScriptBlock
        Write-Host "PASS: $Name"
    }
    catch {
        $script:Failures.Add($Name) | Out-Null
        Write-Host "FAIL: $Name"
        Write-Host $_.Exception.Message
    }
}

function New-TestNupkg {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Id,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [string[]]$EntryNames = @()
    )

    $parentDirectory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
    }

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $nuspecEntry = $archive.CreateEntry('package.nuspec')
        $nuspecWriter = [System.IO.StreamWriter]::new($nuspecEntry.Open())
        try {
            $escapedId = [System.Security.SecurityElement]::Escape($Id)
            $escapedVersion = [System.Security.SecurityElement]::Escape($Version)
            $nuspecWriter.WriteLine('<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">')
            $nuspecWriter.WriteLine('  <metadata>')
            $nuspecWriter.WriteLine("    <id>$escapedId</id>")
            $nuspecWriter.WriteLine("    <version>$escapedVersion</version>")
            $nuspecWriter.WriteLine('  </metadata>')
            $nuspecWriter.WriteLine('</package>')
        }
        finally {
            $nuspecWriter.Dispose()
        }

        foreach ($entryName in @($EntryNames)) {
            $entry = $archive.CreateEntry($entryName)
            $entryWriter = [System.IO.StreamWriter]::new($entry.Open())
            try {
                $entryWriter.WriteLine('dummy')
            }
            finally {
                $entryWriter.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Import-TestModule {
    Import-Module -Name $script:ModulePath -Force
}

New-Item -ItemType Directory -Path $script:TempRoot -Force | Out-Null

try {
    Invoke-TestCase 'module import' {
        Import-TestModule
        $actualCommands = @(
            Get-Command -Module NuGetBinaryClosure |
                Where-Object { $_.CommandType -eq 'Function' } |
                Select-Object -ExpandProperty Name |
                Sort-Object
        )

        Assert-SequenceEqual @(
            'Compare-BinaryClosureBaselineInventory'
            'ConvertTo-BinaryClosureSafePathSegment'
            'Get-NuGetPackageFiles'
            'Get-NuGetPackageMetadata'
            'New-BinaryClosureConsumerProject'
            'New-BinaryClosureRestoreConfig'
        ) $actualCommands 'Exported commands mismatch'
    }

    Invoke-TestCase 'package metadata discovery filters frameworks and safe id' {
        Import-TestModule

        $packageDirectory = Join-Path $script:TempRoot 'metadata'
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        $packagePath = Join-Path $packageDirectory 'sample.package.nupkg'
        New-TestNupkg -Path $packagePath -Id 'My Package/Name' -Version '1.2.3' -EntryNames @(
            'lib/net7.0/ignore.dll'
            'lib/net8.0/a.dll'
            'ref/net9.0/b.dll'
            'lib/net9.0/c.dll'
        )

        $metadata = Get-NuGetPackageMetadata -PackagePath $packagePath -SupportedFrameworks @('net9.0', 'net8.0')

        Assert-Equal 'My Package/Name' $metadata.Id 'Package id mismatch'
        Assert-Equal 'My_Package_Name' $metadata.SafePackageId 'Safe package id mismatch'
        Assert-Equal '1.2.3' $metadata.Version 'Package version mismatch'
        Assert-SequenceEqual @('net8.0', 'net9.0') $metadata.Frameworks 'Framework discovery mismatch'
        Assert-Equal ((Resolve-Path -LiteralPath $packagePath).Path) $metadata.Path 'Resolved path mismatch'
    }

    Invoke-TestCase 'package metadata rejects unsupported TFMs' {
        Import-TestModule

        $packageDirectory = Join-Path $script:TempRoot 'unsupported'
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        $packagePath = Join-Path $packageDirectory 'unsupported.nupkg'
        New-TestNupkg -Path $packagePath -Id 'Unsupported.Package' -Version '1.0.0' -EntryNames @(
            'lib/net7.0/only.dll'
        )

        $threw = $false
        try {
            Get-NuGetPackageMetadata -PackagePath $packagePath -SupportedFrameworks @('net8.0', 'net9.0') | Out-Null
        }
        catch {
            $threw = $true
        }

        Assert-Equal $true $threw 'Expected unsupported package metadata discovery to throw'
    }

    Invoke-TestCase 'package file discovery excludes symbols and sorts deterministically' {
        Import-TestModule

        $packageDirectory = Join-Path $script:TempRoot 'package-files'
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        foreach ($name in @('a.nupkg', 'B.NUPKG', 'c.nupkg', 'z.snupkg', 'x.symbols.nupkg', 'Y.SYMBOLS.NUPKG')) {
            New-Item -ItemType File -Path (Join-Path $packageDirectory $name) -Force | Out-Null
        }

        $actualFiles = Get-NuGetPackageFiles -PackageDirectory $packageDirectory
        Assert-SequenceEqual @('B.NUPKG', 'a.nupkg', 'c.nupkg') (@($actualFiles | Select-Object -ExpandProperty Name)) 'Package file discovery mismatch'
    }

    Invoke-TestCase 'baseline inventory missing directory is empty' {
        Import-TestModule

        $baselineDirectory = Join-Path $script:TempRoot 'missing-baselines'
        $inventory = Compare-BinaryClosureBaselineInventory -Closures @(
            [pscustomobject]@{
                SafePackageId = 'Package-One'
                Frameworks = @('net8.0')
            }
        ) -BaselineDirectory $baselineDirectory

        Assert-SequenceEqual @('Package-One.net8.0.txt') $inventory.Missing 'Missing inventory mismatch'
        Assert-SequenceEqual @() $inventory.Orphaned 'Orphaned inventory mismatch'
    }

    Invoke-TestCase 'baseline inventory reports missing and orphaned deterministically' {
        Import-TestModule

        $baselineDirectory = Join-Path $script:TempRoot 'baselines'
        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null

        foreach ($name in @(
            'Pkg-A.net8.0.txt'
            'Pkg-B.net9.0.txt'
            'A-orphan.txt'
            'z-orphan.txt'
        )) {
            New-Item -ItemType File -Path (Join-Path $baselineDirectory $name) -Force | Out-Null
        }

        $inventory = Compare-BinaryClosureBaselineInventory -Closures @(
            [pscustomobject]@{
                SafePackageId = 'Pkg-B'
                Frameworks = @('net9.0', 'net8.0')
            }
            [pscustomobject]@{
                SafePackageId = 'Pkg-A'
                Frameworks = @('net8.0')
            }
        ) -BaselineDirectory $baselineDirectory

        Assert-SequenceEqual @('Pkg-B.net8.0.txt') $inventory.Missing 'Missing inventory mismatch'
        Assert-SequenceEqual @('A-orphan.txt', 'z-orphan.txt') $inventory.Orphaned 'Orphaned inventory mismatch'
    }

    Invoke-TestCase 'consumer project generation writes expected files' {
        Import-TestModule

        $projectDirectory = Join-Path $script:TempRoot 'consumer'
        $result = New-BinaryClosureConsumerProject -Directory $projectDirectory -PackageId 'My.Package' -PackageVersion '1.2.3' -Frameworks @('net8.0', 'net9.0')

        Assert-Equal (Join-Path $projectDirectory 'Consumer.csproj') $result.ProjectPath 'Project path mismatch'
        Assert-Equal (Join-Path $projectDirectory 'Program.cs') $result.ProgramPath 'Program path mismatch'
        Assert-Equal $true (Test-Path -LiteralPath $result.ProjectPath) 'Consumer.csproj was not created'
        Assert-Equal $true (Test-Path -LiteralPath $result.ProgramPath) 'Program.cs was not created'

        [xml]$projectXml = Get-Content -LiteralPath $result.ProjectPath -Raw
        Assert-Equal 'Microsoft.NET.Sdk' $projectXml.Project.Sdk 'Project SDK mismatch'
        Assert-Equal 'Exe' $projectXml.Project.PropertyGroup.OutputType 'OutputType mismatch'
        Assert-Equal 'net8.0;net9.0' $projectXml.Project.PropertyGroup.TargetFrameworks 'TargetFrameworks mismatch'
        Assert-Equal 'true' $projectXml.Project.PropertyGroup.CopyLocalLockFileAssemblies 'CopyLocalLockFileAssemblies mismatch'
        Assert-Equal 'enable' $projectXml.Project.PropertyGroup.ImplicitUsings 'ImplicitUsings mismatch'
        Assert-Equal 'enable' $projectXml.Project.PropertyGroup.Nullable 'Nullable mismatch'
        Assert-Equal 'My.Package' $projectXml.Project.ItemGroup.PackageReference.Include 'PackageReference include mismatch'
        Assert-Equal '[1.2.3]' $projectXml.Project.ItemGroup.PackageReference.Version 'PackageReference version mismatch'

        $programText = Get-Content -LiteralPath $result.ProgramPath -Raw
        if ($programText -notmatch 'Console\.WriteLine') {
            throw 'Program.cs content mismatch'
        }
    }

    Invoke-TestCase 'restore config generation inserts local source and mapping first' {
        Import-TestModule

        $destinationDirectory = Join-Path $script:TempRoot 'restore-config'
        $destinationPath = Join-Path $destinationDirectory 'NuGet.config'
        $packageDirectory = Join-Path $script:TempRoot 'local-packages'
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        New-BinaryClosureRestoreConfig -SourceConfigPath $script:SourceConfigPath -DestinationPath $destinationPath -PackageDirectory $packageDirectory | Out-Null

        Assert-Equal $true (Test-Path -LiteralPath $destinationPath) 'Restore config was not created'

        [xml]$configXml = Get-Content -LiteralPath $destinationPath -Raw
        $packageSourceKeys = @($configXml.SelectNodes('/configuration/packageSources/add') | Select-Object -ExpandProperty key)
        $packageSourceValues = @($configXml.SelectNodes('/configuration/packageSources/add') | Select-Object -ExpandProperty value)
        Assert-SequenceEqual @('binary-closure-local', 'nuget.org', 'Microsoft Health OSS') $packageSourceKeys 'Package source order mismatch'
        Assert-SequenceEqual @((Resolve-Path -LiteralPath $packageDirectory).Path, 'https://api.nuget.org/v3/index.json', 'https://microsofthealthoss.pkgs.visualstudio.com/FhirServer/_packaging/Public/nuget/v3/index.json') $packageSourceValues 'Package source values mismatch'

        $mappingKeys = @($configXml.SelectNodes('/configuration/packageSourceMapping/packageSource') | Select-Object -ExpandProperty key)
        Assert-SequenceEqual @('binary-closure-local', 'nuget.org', 'Microsoft Health OSS') $mappingKeys 'Package source mapping order mismatch'

        $localPattern = $configXml.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="binary-closure-local"]/package').pattern
        Assert-Equal '*' $localPattern 'Local package source mapping mismatch'
    }

    Invoke-TestCase 'restore config generation resolves relative destination against PowerShell location' {
        Import-TestModule

        $originalLocation = Get-Location
        $originalCurrentDirectory = [System.IO.Directory]::GetCurrentDirectory()
        $relativeRoot = Join-Path $script:TempRoot 'relative-location'
        $relativeDestination = 'relative/NuGet.config'
        $packageDirectory = Join-Path $script:TempRoot 'relative-packages'
        New-Item -ItemType Directory -Path $relativeRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        try {
            Set-Location -LiteralPath $relativeRoot
            [System.IO.Directory]::SetCurrentDirectory($script:RepoRoot)

            New-BinaryClosureRestoreConfig -SourceConfigPath $script:SourceConfigPath -DestinationPath $relativeDestination -PackageDirectory $packageDirectory | Out-Null

            $expectedPath = Join-Path $relativeRoot $relativeDestination
            Assert-Equal $true (Test-Path -LiteralPath $expectedPath) 'Relative restore config was not created under the PowerShell location'

            [xml]$configXml = Get-Content -LiteralPath $expectedPath -Raw
            Assert-Equal 'binary-closure-local' ($configXml.SelectSingleNode('/configuration/packageSources/add').key) 'Relative restore config package source mismatch'
        }
        finally {
            Set-Location -LiteralPath $originalLocation.Path
            [System.IO.Directory]::SetCurrentDirectory($originalCurrentDirectory)
        }
    }
}
finally {
    if (Test-Path -LiteralPath $script:TempRoot) {
        Remove-Item -LiteralPath $script:TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failures.Count -gt 0) {
    throw "$($script:Failures.Count) test case(s) failed: $($script:Failures -join ', ')"
}
