Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NuGetBinaryClosure.Tests.$([guid]::NewGuid().ToString('N'))")
$script:ModulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'NuGetBinaryClosure.psm1'
$script:ValidatorPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Validate-NuGetBinaryClosure.ps1'
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

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExpectedSubstring,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (($null -eq $Actual) -or (-not $Actual.Contains($ExpectedSubstring, [System.StringComparison]::Ordinal))) {
        throw "$Message`nExpected substring: $ExpectedSubstring`nActual: $Actual"
    }
}

function Assert-DoesNotContain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnexpectedSubstring,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (($null -ne $Actual) -and $Actual.Contains($UnexpectedSubstring, [System.StringComparison]::Ordinal)) {
        throw "$Message`nUnexpected substring: $UnexpectedSubstring`nActual: $Actual"
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

function Get-PowerShellApplicationPath {
    $pwshCommand = Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $pwshPath = @($pwshCommand.Source, $pwshCommand.Path) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($pwshPath)) {
        throw 'Unable to resolve the pwsh application path.'
    }

    return $pwshPath
}

function Invoke-ValidatorScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageDirectory,

        [Parameter(Mandatory = $true)]
        [string]$BaselineDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ReportDirectory,

        [Parameter(Mandatory = $true)]
        [string]$WorkDirectory,

        [string]$NuGetConfigPath = $script:SourceConfigPath,

        [string]$CheckBinaryCompatPath = [System.Diagnostics.Process]::GetCurrentProcess().Path,

        [string[]]$SupportedFrameworks = @('net8.0', 'net9.0'),

        [string[]]$RequiredPackageIds = @()
    )

    $pwshPath = Get-PowerShellApplicationPath
    $arguments = @(
        '-NoProfile'
        '-File'
        $script:ValidatorPath
        '-PackageDirectory'
        $PackageDirectory
        '-BaselineDirectory'
        $BaselineDirectory
        '-ReportDirectory'
        $ReportDirectory
        '-WorkDirectory'
        $WorkDirectory
        '-NuGetConfigPath'
        $NuGetConfigPath
        '-CheckBinaryCompatPath'
        $CheckBinaryCompatPath
        '-SupportedFrameworks'
    ) + $SupportedFrameworks

    if ($RequiredPackageIds.Count -gt 0) {
        $arguments += '-RequiredPackageIds'
        $arguments += $RequiredPackageIds
    }

    $output = & $pwshPath @arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Invoke-TestNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [string]$WorkingDirectory
    )

    $output = @()
    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $output = & $FilePath @ArgumentList 2>&1
    }
    else {
        Push-Location -LiteralPath $WorkingDirectory
        try {
            $output = & $FilePath @ArgumentList 2>&1
        }
        finally {
            Pop-Location
        }
    }

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = (@($output | ForEach-Object { $_.ToString() })) -join [System.Environment]::NewLine
    }
}

function New-TestPackagedProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDirectory,

        [Parameter(Mandatory = $true)]
        [string]$PackageDirectory,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [string]$Framework = 'net8.0'
    )

    $projectDirectory = Join-Path $RootDirectory 'project'
    New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null

    $projectPath = Join-Path $projectDirectory 'TestPackage.csproj'
    $sourcePath = Join-Path $projectDirectory 'Class1.cs'

    $projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$Framework</TargetFramework>
    <PackageId>$PackageId</PackageId>
    <Version>$Version</Version>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@

    $sourceContent = @"
namespace TestPackage;

public sealed class Class1
{
}
"@

    Set-Content -LiteralPath $projectPath -Value $projectContent -Encoding utf8
    Set-Content -LiteralPath $sourcePath -Value $sourceContent -Encoding utf8

    $packResult = Invoke-TestNativeCommand -FilePath 'dotnet' -ArgumentList @(
        'pack'
        $projectPath
        '--configuration'
        'Release'
        '--output'
        $PackageDirectory
        '-v'
        'minimal'
    ) -WorkingDirectory $projectDirectory

    if ($packResult.ExitCode -ne 0) {
        throw "dotnet pack failed for '$PackageId' version '$Version'. Output:$([System.Environment]::NewLine)$($packResult.Output)"
    }

    return [pscustomobject]@{
        ProjectPath = $projectPath
        PackagePath = Join-Path $PackageDirectory "$PackageId.$Version.nupkg"
    }
}

function New-FakeCheckBinaryCompatScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [bool]$CreateReportFile = $true,

        [bool]$CreateAssembliesFile = $true,

        [string]$ReportContent = '',

        [int]$ExitCode = 0,

        [bool]$EchoInvocation = $false
    )

    $parentDirectory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
    }

    $escapedReportContent = $ReportContent.Replace("'", "''")
    $scriptLines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(
        'Set-StrictMode -Version Latest'
        '$ErrorActionPreference = ''Stop'''
        ''
        '$outPath = $null'
        '$baselinePath = $null'
        'foreach ($argument in $args) {'
        '    if ($argument.StartsWith(''-baseline:'', [System.StringComparison]::OrdinalIgnoreCase)) {'
        '        $baselinePath = $argument.Substring(10)'
        '        continue'
        '    }'
        '    if ($argument.StartsWith(''-out:'', [System.StringComparison]::OrdinalIgnoreCase)) {'
        '        $outPath = $argument.Substring(5)'
        '        break'
        '    }'
        '}'
        ''
        'if ([string]::IsNullOrWhiteSpace($outPath)) {'
        '    throw ''Missing -out argument.'''
        '}'
        ''
        '$reportDirectory = Split-Path -Parent $outPath'
        'New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null'
    )) {
        $scriptLines.Add($line) | Out-Null
    }

    if ($CreateReportFile) {
        $scriptLines.Add("Set-Content -LiteralPath `$outPath -Value '$escapedReportContent' -NoNewline -Encoding utf8") | Out-Null
    }

    if ($CreateAssembliesFile) {
        $scriptLines.Add('Set-Content -LiteralPath (Join-Path (Get-Location).Path ''BinaryCompatReport.Assemblies.txt'') -Value ''Fake.Assembly.dll'' -NoNewline -Encoding utf8') | Out-Null
    }

    if ($EchoInvocation) {
        $scriptLines.Add('Write-Output (''CWD='' + (Get-Location).Path)') | Out-Null
        $scriptLines.Add('if ($args.Count -gt 0) { Write-Output (''ARG0='' + $args[0]) }') | Out-Null
        $scriptLines.Add('if ($null -ne $baselinePath) { Write-Output (''BASELINE='' + $baselinePath) }') | Out-Null
        $scriptLines.Add('Write-Output (''OUT='' + $outPath)') | Out-Null
    }

    $scriptLines.Add('Write-Output ''Fake checker executed.''') | Out-Null
    if ($ExitCode -ne 0) {
        $scriptLines.Add("exit $ExitCode") | Out-Null
    }
    $scriptContent = $scriptLines -join [System.Environment]::NewLine

    Set-Content -LiteralPath $Path -Value $scriptContent -Encoding utf8
    return $Path
}

function Update-TestPackageNuspecVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $extractDirectory = Join-Path ([System.IO.Path]::GetDirectoryName($PackagePath)) ([System.IO.Path]::GetFileNameWithoutExtension($PackagePath))
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }

    Expand-Archive -LiteralPath $PackagePath -DestinationPath $extractDirectory
    try {
        $nuspecPath = (Get-ChildItem -LiteralPath $extractDirectory -Filter *.nuspec | Select-Object -First 1).FullName
        [xml]$nuspecXml = Get-Content -LiteralPath $nuspecPath -Raw
        $versionNode = $nuspecXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]')
        $versionNode.InnerText = $Version
        $nuspecXml.Save($nuspecPath)

        Remove-Item -LiteralPath $PackagePath -Force
        Compress-Archive -Path (Join-Path $extractDirectory '*') -DestinationPath $PackagePath
    }
    finally {
        if (Test-Path -LiteralPath $extractDirectory) {
            Remove-Item -LiteralPath $extractDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
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
            'Select-BinaryClosurePackages'
        ) $actualCommands 'Exported commands mismatch'
    }

    Invoke-TestCase 'package selection uses required ids and reports missing roots' {
        Import-TestModule

        $packages = @(
            [pscustomobject]@{ Id = 'Utility.Package'; Version = '1.0.0'; Path = 'utility.nupkg' }
            [pscustomobject]@{ Id = 'Microsoft.Health.Fhir.R4.Web'; Version = '1.0.0'; Path = 'r4.nupkg' }
            [pscustomobject]@{ Id = 'Microsoft.Health.Fhir.R5.Web'; Version = '1.0.0'; Path = 'r5.nupkg' }
        )

        $selection = Select-BinaryClosurePackages -Packages $packages -RequiredPackageIds @(
            'microsoft.health.fhir.r5.web'
            'Microsoft.Health.Fhir.R4.Web'
            'Microsoft.Health.Fhir.Stu3.Web'
            'Microsoft.Health.Fhir.R4.Web'
        )

        Assert-SequenceEqual @(
            'Microsoft.Health.Fhir.R4.Web'
            'Microsoft.Health.Fhir.R5.Web'
        ) @($selection.Selected | Select-Object -ExpandProperty Id) 'Selected deployment roots mismatch'
        Assert-SequenceEqual @(
            'Microsoft.Health.Fhir.Stu3.Web'
        ) $selection.Missing 'Missing deployment roots mismatch'
    }

    Invoke-TestCase 'empty required package ids select all packages' {
        Import-TestModule

        $packages = @(
            [pscustomobject]@{ Id = 'Z.Package'; Version = '1.0.0'; Path = 'z.nupkg' }
            [pscustomobject]@{ Id = 'A.Package'; Version = '1.0.0'; Path = 'a.nupkg' }
        )

        $selection = Select-BinaryClosurePackages -Packages $packages -RequiredPackageIds @()

        Assert-SequenceEqual @('A.Package', 'Z.Package') @($selection.Selected | Select-Object -ExpandProperty Id) 'Unfiltered package order mismatch'
        Assert-SequenceEqual @() $selection.Missing 'Unfiltered selection should not report missing packages'
    }

    Invoke-TestCase 'child PowerShell resolution uses pwsh application and launches commands' {
        $pwshPath = Get-PowerShellApplicationPath
        $pwshFileName = [System.IO.Path]::GetFileName($pwshPath)

        if (($pwshFileName -ne 'pwsh') -and ($pwshFileName -ne 'pwsh.exe')) {
            throw "Resolved child PowerShell executable should be pwsh or pwsh.exe, but was '$pwshFileName'."
        }

        Assert-DoesNotContain 'dotnet' $pwshFileName 'Resolved child PowerShell executable basename should never be dotnet'

        $launchResult = Invoke-TestNativeCommand -FilePath $pwshPath -ArgumentList @(
            '-NoLogo'
            '-NoProfile'
            '-Command'
            'Write-Output ready'
        )

        Assert-Equal 0 $launchResult.ExitCode 'Resolved child PowerShell executable should launch -NoProfile -Command successfully'
        Assert-Contains 'ready' $launchResult.Output 'Resolved child PowerShell executable should emit command output'
    }

    Invoke-TestCase 'validator rejects duplicate package identities and preserves reports' {
        $duplicateRoot = Join-Path $script:TempRoot 'validator-duplicate'
        $packageDirectory = Join-Path $duplicateRoot 'packages'
        $baselineDirectory = Join-Path $duplicateRoot 'baselines'
        $reportDirectory = Join-Path $duplicateRoot 'reports'
        $workDirectory = Join-Path $duplicateRoot 'work'

        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-a.nupkg') -Id 'Duplicate.Package' -Version '1.2.3' -EntryNames @(
            'lib/net8.0/a.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-b.nupkg') -Id 'Duplicate.Package' -Version '1.2.3' -EntryNames @(
            'lib/net8.0/b.dll'
        )

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail on duplicate package identities'
        Assert-Contains "Duplicate package identity 'Duplicate.Package/1.2.3'" $result.Output 'Duplicate package identity error mismatch'
        Assert-Equal $true (Test-Path -LiteralPath $reportDirectory -PathType Container) 'Report directory should be retained on failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned up on failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned up on failure'
    }

    Invoke-TestCase 'validator rejects safe package id collisions' {
        $safeIdRoot = Join-Path $script:TempRoot 'validator-safe-id'
        $packageDirectory = Join-Path $safeIdRoot 'packages'
        $baselineDirectory = Join-Path $safeIdRoot 'baselines'
        $reportDirectory = Join-Path $safeIdRoot 'reports'
        $workDirectory = Join-Path $safeIdRoot 'work'

        New-TestNupkg -Path (Join-Path $packageDirectory 'collision-a.nupkg') -Id 'Collision/Package' -Version '1.0.0' -EntryNames @(
            'lib/net8.0/a.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'collision-b.nupkg') -Id 'Collision_Package' -Version '2.0.0' -EntryNames @(
            'lib/net8.0/b.dll'
        )

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail on safe package id collisions'
        Assert-Contains "Safe package id collision 'Collision_Package'" $result.Output 'Safe package id collision error mismatch'
        Assert-Contains 'Collision/Package' $result.Output 'Safe package id collision should include the first package id'
        Assert-Contains 'Collision_Package' $result.Output 'Safe package id collision should include the second package id'
    }

    Invoke-TestCase 'validator rejects multiple versions for the same package id' {
        $multiVersionRoot = Join-Path $script:TempRoot 'validator-multi-version'
        $packageDirectory = Join-Path $multiVersionRoot 'packages'
        $baselineDirectory = Join-Path $multiVersionRoot 'baselines'
        $reportDirectory = Join-Path $multiVersionRoot 'reports'
        $workDirectory = Join-Path $multiVersionRoot 'work'

        New-TestNupkg -Path (Join-Path $packageDirectory 'multi-a.nupkg') -Id 'Multi.Version.Package' -Version '1.0.0' -EntryNames @(
            'lib/net8.0/a.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'multi-b.nupkg') -Id 'Multi.Version.Package' -Version '2.0.0' -EntryNames @(
            'lib/net8.0/b.dll'
        )

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when the same package id appears in multiple versions'
        Assert-Contains "Multiple package versions found for package id 'Multi.Version.Package'" $result.Output 'Multiple package version error mismatch'
        Assert-Equal $true (Test-Path -LiteralPath $reportDirectory -PathType Container) 'Report directory should be retained on multi-version failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned up on multi-version failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned up on multi-version failure'
    }

    Invoke-TestCase 'validator aggregates invariant failures deterministically' {
        $aggregateRoot = Join-Path $script:TempRoot 'validator-aggregate'
        $packageDirectory = Join-Path $aggregateRoot 'packages'
        $baselineDirectory = Join-Path $aggregateRoot 'baselines'
        $reportDirectory = Join-Path $aggregateRoot 'reports'
        $workDirectory = Join-Path $aggregateRoot 'work'

        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-a.nupkg') -Id 'Aggregate.Duplicate' -Version '3.4.5' -EntryNames @(
            'lib/net8.0/a.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-b.nupkg') -Id 'Aggregate.Duplicate' -Version '3.4.5' -EntryNames @(
            'lib/net8.0/b.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'collision-a.nupkg') -Id 'Aggregate/Collision' -Version '1.0.0' -EntryNames @(
            'lib/net8.0/c.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'collision-b.nupkg') -Id 'Aggregate_Collision' -Version '2.0.0' -EntryNames @(
            'lib/net8.0/d.dll'
        )

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0')
        $outputLines = @(
            $result.Output -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )

        Assert-Equal 1 $result.ExitCode 'Validator should fail when invariant violations are aggregated'
        Assert-Contains "Duplicate package identity 'Aggregate.Duplicate/3.4.5'" $result.Output 'Aggregate output should include duplicate identity failure'
        Assert-Contains "Safe package id collision 'Aggregate_Collision'" $result.Output 'Aggregate output should include safe package id collision failure'
        Assert-SequenceEqual @(
            "Duplicate package identity 'Aggregate.Duplicate/3.4.5' found in: '$((Resolve-Path -LiteralPath (Join-Path $packageDirectory 'duplicate-a.nupkg')).Path)', '$((Resolve-Path -LiteralPath (Join-Path $packageDirectory 'duplicate-b.nupkg')).Path)'."
            "Missing baseline 'Aggregate.Duplicate.net8.0.txt'."
            "Missing baseline 'Aggregate_Collision.net8.0.txt'."
            "Safe package id collision 'Aggregate_Collision' found for package ids: 'Aggregate/Collision', 'Aggregate_Collision'."
        ) $outputLines 'Aggregate error output should be ordinal and unique'
        Assert-Equal $true (Test-Path -LiteralPath $reportDirectory -PathType Container) 'Report directory should be retained after aggregated failures'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned after aggregated failures'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned after aggregated failures'
    }

    Invoke-TestCase 'validator restores publishes and preserves reports on success' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-success'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Success.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode 'Validator should succeed for a real package with a fake checker'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Success summary mismatch'
        $successReportDirectory = Join-Path (Join-Path $reportDirectory 'Success.Package') 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'BinaryCompatReport.txt')) 'BinaryCompatReport.txt should be retained'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'Comparison.txt')) 'Comparison.txt should be retained'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'BinaryCompatReport.Assemblies.txt should be retained'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned after success'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned after success'
    }

    Invoke-TestCase 'validator processes only required deployment roots' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-required-roots'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'selected') -PackageDirectory $packageDirectory -PackageId 'Selected.Web' -Version '1.0.0' -Framework 'net8.0' | Out-Null
        New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'utility') -PackageDirectory $packageDirectory -PackageId 'Ignored.Utility' -Version '1.0.0' -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory 'Selected.Web.net8.0.txt'), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0') -RequiredPackageIds @('Selected.Web')

        Assert-Equal 0 $result.ExitCode 'Selected deployment root should validate successfully'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Selected closure summary mismatch'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $reportDirectory 'Selected.Web/net8.0/BinaryCompatReport.txt')) 'Selected root report missing'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $reportDirectory 'Ignored.Utility')) 'Ignored utility should not produce reports'
    }

    Invoke-TestCase 'validator fails when a required deployment root is missing' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-missing-root'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory (Join-Path $rootDirectory 'utility') -PackageDirectory $packageDirectory -PackageId 'Available.Utility' -Version '1.0.0' -Framework 'net8.0' | Out-Null

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -SupportedFrameworks @('net8.0') -RequiredPackageIds @('Missing.Web')

        Assert-Equal 1 $result.ExitCode 'Missing deployment root should fail validation'
        Assert-Contains "Required package 'Missing.Web' was not found." $result.Output 'Missing root error mismatch'
    }

    Invoke-TestCase 'validator preserves empty reports when checker succeeds without writing them' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-empty-report-success'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Empty.Report.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -CreateReportFile $false | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode 'Validator should succeed when the checker omits an empty BinaryCompatReport.txt on a matching baseline'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Empty report success summary mismatch'
        $emptyReportDirectory = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        $emptyReportPath = Join-Path $emptyReportDirectory 'BinaryCompatReport.txt'
        Assert-Equal $true (Test-Path -LiteralPath $emptyReportPath) 'Validator should preserve a precreated empty BinaryCompatReport.txt'
        Assert-Equal 0 ([System.IO.File]::ReadAllBytes($emptyReportPath).Length) 'Precreated BinaryCompatReport.txt should remain empty when the checker omits it'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $emptyReportDirectory 'Comparison.txt')) 'Comparison.txt should be retained for empty reports'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $emptyReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'BinaryCompatReport.Assemblies.txt should be retained for empty reports'
    }

    Invoke-TestCase 'validator runs checker from the publish directory with dot input and relocates assemblies report' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-publish-checker-input'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'PublishInput.Package'
        $version = '1.2.3'
        $framework = 'net8.0'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework $framework | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -EchoInvocation $true | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.$framework.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @($framework)

        Assert-Equal 0 $result.ExitCode 'Validator should succeed while running the checker from the publish directory'
        $comparisonPath = Join-Path (Join-Path (Join-Path $reportDirectory $packageId) $framework) 'Comparison.txt'
        $comparisonLines = @(Get-Content -LiteralPath $comparisonPath)
        $cwdLine = $comparisonLines | Where-Object { $_ -like 'CWD=*' } | Select-Object -First 1
        $arg0Line = $comparisonLines | Where-Object { $_ -like 'ARG0=*' } | Select-Object -First 1
        $baselineLine = $comparisonLines | Where-Object { $_ -like 'BASELINE=*' } | Select-Object -First 1
        $outLine = $comparisonLines | Where-Object { $_ -like 'OUT=*' } | Select-Object -First 1
        $cwd = $cwdLine.Substring(4)
        $arg0 = $arg0Line.Substring(5)
        $baselineArg = $baselineLine.Substring(9)
        $outArg = $outLine.Substring(4)
        $expectedPublishPath = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $workDirectory 'consumer') $packageId) $version) 'publish') $framework
        $expectedBaselinePath = Join-Path $baselineDirectory "$packageId.$framework.txt"
        $expectedOutPath = Join-Path (Join-Path (Join-Path $reportDirectory $packageId) $framework) 'BinaryCompatReport.txt'
        $expectedAssembliesReportPath = Join-Path (Join-Path (Join-Path $reportDirectory $packageId) $framework) 'BinaryCompatReport.Assemblies.txt'
        $publishAssembliesPath = Join-Path $expectedPublishPath 'BinaryCompatReport.Assemblies.txt'

        Assert-Equal ([System.IO.Path]::GetFullPath($expectedPublishPath)) ([System.IO.Path]::GetFullPath($cwd)) 'Checker working directory should be the publish directory'
        Assert-Equal '.' $arg0 'Checker positional input should be dot when running from the publish directory'
        Assert-Equal ([System.IO.Path]::GetFullPath($expectedBaselinePath)) ([System.IO.Path]::GetFullPath($baselineArg)) 'Checker baseline argument should remain an absolute path'
        Assert-Equal ([System.IO.Path]::GetFullPath($expectedOutPath)) ([System.IO.Path]::GetFullPath($outArg)) 'Checker out argument should remain an absolute report path'
        Assert-Equal $true (Test-Path -LiteralPath $expectedAssembliesReportPath) 'Assemblies report should be retained in the package report directory'
        Assert-Equal $false (Test-Path -LiteralPath $publishAssembliesPath) 'Assemblies report should not remain in the publish directory after relocation'
    }

    Invoke-TestCase 'validator materializes non-empty baseline reports when checker matches without writing them' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-nonempty-report-success'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'NonEmpty.Report.Package'
        $version = '1.2.3'
        $baselineContent = "warning one`nwarning two"

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -CreateReportFile $false | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $baselineDirectory "$packageId.net8.0.txt"), $baselineContent, [System.Text.UTF8Encoding]::new($false))

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode 'Validator should succeed when the checker matches a non-empty baseline without writing the report'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Non-empty omitted report success summary mismatch'
        $reportPath = Join-Path (Join-Path (Join-Path $reportDirectory $packageId) 'net8.0') 'BinaryCompatReport.txt'
        Assert-Equal $baselineContent ([System.IO.File]::ReadAllText($reportPath)) 'Validator should materialize the baseline content into BinaryCompatReport.txt after a successful checker match'
    }

    Invoke-TestCase 'validator restores Microsoft.Health packages from exact local source mappings' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-microsoft-health-local'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $customConfigPath = Join-Path $rootDirectory 'NuGet.config'
        $emptyRemoteDirectory = Join-Path $rootDirectory 'empty-remote'
        $packageId = 'Microsoft.Health.Fhir.Local'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-Item -ItemType Directory -Path $emptyRemoteDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())
        Set-Content -LiteralPath $customConfigPath -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="empty-remote" value="$emptyRemoteDirectory" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.AspNetCore.App.*" />
      <package pattern="Microsoft.NETCore.App.*" />
    </packageSource>
    <packageSource key="empty-remote">
      <package pattern="Microsoft.Health.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -NuGetConfigPath $customConfigPath -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode "Validator should restore Microsoft.Health.* packages from local exact mappings even when a conflicting remote mapping exists. Output:$([System.Environment]::NewLine)$($result.Output)"
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Microsoft.Health local restore success summary mismatch'
        $successReportDirectory = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'BinaryCompatReport.txt')) 'Microsoft.Health local restore should retain BinaryCompatReport.txt'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'Comparison.txt')) 'Microsoft.Health local restore should retain Comparison.txt'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $successReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'Microsoft.Health local restore should retain BinaryCompatReport.Assemblies.txt'
    }

    Invoke-TestCase 'validator continues producing reports for valid packages when others are invalid' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-partial-success'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId 'Valid.Package' -Version '2.0.0' -Framework 'net8.0' | Out-Null
        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-a.nupkg') -Id 'Duplicate.Package' -Version '1.0.0' -EntryNames @(
            'lib/net8.0/a.dll'
        )
        New-TestNupkg -Path (Join-Path $packageDirectory 'duplicate-b.nupkg') -Id 'Duplicate.Package' -Version '1.0.0' -EntryNames @(
            'lib/net8.0/b.dll'
        )
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory 'Valid.Package.net8.0.txt'), [byte[]]@())
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory 'Duplicate.Package.net8.0.txt'), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when invalid packages are present'
        Assert-Contains "Duplicate package identity 'Duplicate.Package/1.0.0'" $result.Output 'Duplicate package error mismatch for mixed package set'
        $validReportDirectory = Join-Path (Join-Path $reportDirectory 'Valid.Package') 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $validReportDirectory 'BinaryCompatReport.txt')) 'Valid package report should still be produced'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $validReportDirectory 'Comparison.txt')) 'Valid package comparison should still be produced'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $validReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'Valid package assemblies report should still be produced'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned after mixed-package failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned after mixed-package failure'
    }

    Invoke-TestCase 'validator fails when checker omits the assemblies report' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-missing-assemblies'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Missing.Assemblies.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -CreateAssembliesFile $false | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when the checker omits BinaryCompatReport.Assemblies.txt'
        Assert-Contains 'BinaryCompatReport.Assemblies.txt' $result.Output 'Assemblies report failure mismatch'
        $reportPath = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $reportPath 'BinaryCompatReport.txt')) 'BinaryCompatReport.txt should still be retained on checker artifact failure'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $reportPath 'Comparison.txt')) 'Comparison.txt should still be retained on checker artifact failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $reportPath 'BinaryCompatReport.Assemblies.txt')) 'Missing assemblies report should remain missing for diagnosis'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'consumer')) 'Consumer directory should be cleaned after assemblies report failure'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $workDirectory 'package-cache')) 'Package cache directory should be cleaned after assemblies report failure'
    }

    Invoke-TestCase 'validator detects baseline drift for non-empty existing and actual reports' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-baseline-drift'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Drift.Detection.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -ReportContent 'actual report content' -ExitCode 1 | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $baselineDirectory "$packageId.net8.0.txt"), 'expected baseline content')

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when an existing non-empty baseline differs from the actual report'
        Assert-Contains "Binary closure baseline drift detected for package '$packageId' target framework 'net8.0'" $result.Output 'Non-empty baseline drift message mismatch'
        $driftReportDirectory = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        Assert-Equal 'actual report content' ([System.IO.File]::ReadAllText((Join-Path $driftReportDirectory 'BinaryCompatReport.txt'))) 'Actual BinaryCompatReport.txt should be preserved on drift'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $driftReportDirectory 'Comparison.txt')) 'Comparison.txt should be retained on drift'
    }

    Invoke-TestCase 'validator aggregates nonzero checker exits while preserving reports' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-checker-exit'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Checker.Exit.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -ExitCode 3 | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when checkbinarycompat exits nonzero'
        Assert-Contains "checkbinarycompat failed for package '$packageId' version '$version' target framework 'net8.0' with exit code 3" $result.Output 'Nonzero checker exit should be aggregated with the actual exit code'
        $checkerExitReportDirectory = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $checkerExitReportDirectory 'BinaryCompatReport.txt')) 'BinaryCompatReport.txt should be retained on checker nonzero exit'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $checkerExitReportDirectory 'Comparison.txt')) 'Comparison.txt should be retained on checker nonzero exit'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $checkerExitReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'BinaryCompatReport.Assemblies.txt should be retained on checker nonzero exit'
    }

    Invoke-TestCase 'validator reports missing baselines without adding synthetic drift failures' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-missing-baseline'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Missing.Baseline.Package'
        $version = '1.2.3'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version $version -Framework 'net8.0' | Out-Null
        New-FakeCheckBinaryCompatScript -Path $checkerPath -ReportContent 'warning: new report content' -ExitCode 1 | Out-Null

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 1 $result.ExitCode 'Validator should fail when a baseline is missing'
        Assert-Contains "Missing baseline '$packageId.net8.0.txt'." $result.Output 'Missing baseline error mismatch'
        Assert-DoesNotContain 'Binary closure baseline drift detected' $result.Output 'Synthetic empty baselines should not add a second drift failure'
        $missingBaselineReportDirectory = Join-Path (Join-Path $reportDirectory $packageId) 'net8.0'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $missingBaselineReportDirectory 'BinaryCompatReport.txt')) 'Missing baseline run should still retain BinaryCompatReport.txt'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $missingBaselineReportDirectory 'Comparison.txt')) 'Missing baseline run should still retain Comparison.txt'
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $missingBaselineReportDirectory 'BinaryCompatReport.Assemblies.txt')) 'Missing baseline run should still retain BinaryCompatReport.Assemblies.txt'
    }

    Invoke-TestCase 'validator accepts semantically equivalent normalized NuGet versions' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-version-normalization'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Normalized.Version.Package'
        $packageVersion = '1.2.3.0'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        $package = New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version '1.2.3' -Framework 'net8.0'
        Update-TestPackageNuspecVersion -PackagePath $package.PackagePath -Version $packageVersion
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode 'Validator should treat semantically equivalent NuGet versions as equal'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Normalized version success summary mismatch'
    }

    Invoke-TestCase 'validator accepts semantically equivalent NuGet versions with build metadata' {
        $rootDirectory = Join-Path $script:TempRoot 'validator-version-metadata'
        $packageDirectory = Join-Path $rootDirectory 'packages'
        $baselineDirectory = Join-Path $rootDirectory 'baselines'
        $reportDirectory = Join-Path $rootDirectory 'reports'
        $workDirectory = Join-Path $rootDirectory 'work'
        $checkerPath = Join-Path $rootDirectory 'fake-checkbinarycompat.ps1'
        $packageId = 'Metadata.Version.Package'
        $packageVersion = '1.2.3+AbC'

        New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
        $package = New-TestPackagedProject -RootDirectory $rootDirectory -PackageDirectory $packageDirectory -PackageId $packageId -Version '1.2.3' -Framework 'net8.0'
        Update-TestPackageNuspecVersion -PackagePath $package.PackagePath -Version $packageVersion
        New-FakeCheckBinaryCompatScript -Path $checkerPath | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $baselineDirectory "$packageId.net8.0.txt"), [byte[]]@())

        $result = Invoke-ValidatorScript -PackageDirectory $packageDirectory -BaselineDirectory $baselineDirectory -ReportDirectory $reportDirectory -WorkDirectory $workDirectory -CheckBinaryCompatPath $checkerPath -SupportedFrameworks @('net8.0')

        Assert-Equal 0 $result.ExitCode 'Validator should treat NuGet build metadata as semantically equivalent for exact restores'
        Assert-Contains 'Validated 1 binary closures across 1 NuGet packages.' $result.Output 'Build metadata normalization success summary mismatch'
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
        Assert-Equal 'false' $projectXml.Project.PropertyGroup.ManagePackageVersionsCentrally 'ManagePackageVersionsCentrally mismatch'
        Assert-Equal 'enable' $projectXml.Project.PropertyGroup.ImplicitUsings 'ImplicitUsings mismatch'
        Assert-Equal 'enable' $projectXml.Project.PropertyGroup.Nullable 'Nullable mismatch'
        Assert-Equal 'My.Package' $projectXml.Project.ItemGroup.PackageReference.Include 'PackageReference include mismatch'
        Assert-Equal '[1.2.3]' $projectXml.Project.ItemGroup.PackageReference.Version 'PackageReference version mismatch'

        $programText = Get-Content -LiteralPath $result.ProgramPath -Raw
        if ($programText -notmatch 'Console\.WriteLine') {
            throw 'Program.cs content mismatch'
        }
    }

    Invoke-TestCase 'consumer project restore under repo tree opts out of central package versions' {
        Import-TestModule

        $fixtureRoot = Join-Path (Join-Path $script:RepoRoot 'artifacts') ("NuGetBinaryClosure.Tests.$([guid]::NewGuid().ToString('N'))")
        $fixturePropsPath = Join-Path $fixtureRoot 'Directory.Build.props'
        $sourceConfigPath = Join-Path $fixtureRoot 'Source.NuGet.config'
        $restoreConfigPath = Join-Path $fixtureRoot 'Restore.NuGet.config'
        $consumerDirectory = Join-Path $fixtureRoot 'consumer'
        $packageCacheDirectory = Join-Path $fixtureRoot 'package-cache'
        $packageRoot = Join-Path $script:TempRoot 'repo-tree-consumer-package'
        $packageDirectory = Join-Path $packageRoot 'packages'
        $packageId = 'Repo.Tree.Consumer.Package'
        $packageVersion = '1.2.3'

        try {
            New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
            Set-Content -LiteralPath $fixturePropsPath -Encoding utf8 -Value @"
<Project>
  <Import Project="$script:RepoRoot\Directory.Build.props" />
  <ItemGroup>
    <PackageReference Remove="Microsoft.SourceLink.GitHub" />
    <PackageReference Remove="DotNet.ReproducibleBuilds" />
    <PackageReference Remove="StyleCop.Analyzers" />
  </ItemGroup>
</Project>
"@
            Set-Content -LiteralPath $sourceConfigPath -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.AspNetCore.App.*" />
      <package pattern="Microsoft.NETCore.App.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

            New-TestPackagedProject -RootDirectory $packageRoot -PackageDirectory $packageDirectory -PackageId $packageId -Version $packageVersion -Framework 'net8.0' | Out-Null
            New-BinaryClosureRestoreConfig -SourceConfigPath $sourceConfigPath -DestinationPath $restoreConfigPath -PackageDirectory $packageDirectory -LocalPackageIds @($packageId) | Out-Null
            $consumerProject = New-BinaryClosureConsumerProject -Directory $consumerDirectory -PackageId $packageId -PackageVersion $packageVersion -Frameworks @('net8.0')

            $restoreResult = Invoke-TestNativeCommand -FilePath 'dotnet' -ArgumentList @(
                'restore'
                $consumerProject.ProjectPath
                '--configfile'
                $restoreConfigPath
                '--packages'
                $packageCacheDirectory
                '--no-cache'
                '--force'
                '-v'
                'minimal'
            ) -WorkingDirectory $consumerDirectory

            Assert-Equal 0 $restoreResult.ExitCode "Consumer project under the repo tree should restore successfully with exact PackageReference versions. Output:$([System.Environment]::NewLine)$($restoreResult.Output)"
            Assert-DoesNotContain 'NU1008' $restoreResult.Output 'Consumer project should not inherit central package version enforcement'
        }
        finally {
            if (Test-Path -LiteralPath $fixtureRoot) {
                Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Invoke-TestCase 'restore config generation inserts local source and mapping first' {
        Import-TestModule

        $destinationDirectory = Join-Path $script:TempRoot 'restore-config'
        $destinationPath = Join-Path $destinationDirectory 'NuGet.config'
        $packageDirectory = Join-Path $script:TempRoot 'local-packages'
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

        New-BinaryClosureRestoreConfig -SourceConfigPath $script:SourceConfigPath -DestinationPath $destinationPath -PackageDirectory $packageDirectory -LocalPackageIds @(
            'Microsoft.Health.Fhir.Core'
            'Hl7.Fhir.R4'
            'Microsoft.Health.Fhir.Api'
            'Microsoft.Health.Fhir.Core'
        ) | Out-Null

        Assert-Equal $true (Test-Path -LiteralPath $destinationPath) 'Restore config was not created'

        [xml]$configXml = Get-Content -LiteralPath $destinationPath -Raw
        $packageSourceKeys = @($configXml.SelectNodes('/configuration/packageSources/add') | Select-Object -ExpandProperty key)
        $packageSourceValues = @($configXml.SelectNodes('/configuration/packageSources/add') | Select-Object -ExpandProperty value)
        Assert-SequenceEqual @('binary-closure-local', 'nuget.org', 'Microsoft Health OSS') $packageSourceKeys 'Package source order mismatch'
        Assert-SequenceEqual @((Resolve-Path -LiteralPath $packageDirectory).Path, 'https://api.nuget.org/v3/index.json', 'https://microsofthealthoss.pkgs.visualstudio.com/FhirServer/_packaging/Public/nuget/v3/index.json') $packageSourceValues 'Package source values mismatch'

        $mappingKeys = @($configXml.SelectNodes('/configuration/packageSourceMapping/packageSource') | Select-Object -ExpandProperty key)
        Assert-SequenceEqual @('binary-closure-local', 'nuget.org', 'Microsoft Health OSS') $mappingKeys 'Package source mapping order mismatch'

        $localPatterns = @($configXml.SelectNodes('/configuration/packageSourceMapping/packageSource[@key="binary-closure-local"]/package') | Select-Object -ExpandProperty pattern)
        Assert-SequenceEqual @('Hl7.Fhir.R4', 'Microsoft.Health.Fhir.Api', 'Microsoft.Health.Fhir.Core') $localPatterns 'Local package source mapping should use exact sorted unique package ids'
        Assert-Equal 0 (@($localPatterns | Where-Object { $_ -eq '*' }).Count) 'Local package source mapping must not include a wildcard pattern'
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

            New-BinaryClosureRestoreConfig -SourceConfigPath $script:SourceConfigPath -DestinationPath $relativeDestination -PackageDirectory $packageDirectory -LocalPackageIds @('Relative.Package') | Out-Null

            $expectedPath = Join-Path $relativeRoot $relativeDestination
            Assert-Equal $true (Test-Path -LiteralPath $expectedPath) 'Relative restore config was not created under the PowerShell location'

            [xml]$configXml = Get-Content -LiteralPath $expectedPath -Raw
            Assert-Equal 'binary-closure-local' ($configXml.SelectSingleNode('/configuration/packageSources/add').key) 'Relative restore config package source mismatch'
            Assert-Equal 'Relative.Package' ($configXml.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="binary-closure-local"]/package').pattern) 'Relative restore config local package id mismatch'
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
