Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BinaryClosureSafePathSegment {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value) {
        return $null
    }

    return [System.Text.RegularExpressions.Regex]::Replace($Value, '[^A-Za-z0-9._-]', '_')
}

function Get-NuGetPackageFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageDirectory
    )

    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Package directory '$PackageDirectory' was not found."
    }

    $sortedFiles = [System.Collections.Generic.SortedDictionary[string, System.IO.FileInfo]]::new([System.StringComparer]::Ordinal)

    foreach ($file in Get-ChildItem -LiteralPath $PackageDirectory -File) {
        $name = $file.Name

        if ($name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($name.EndsWith('.nupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
            $sortedFiles[$name] = $file
        }
    }

    return @($sortedFiles.Values)
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $stream = $Entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-NuGetPackageMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackagePath,

        [string[]]$SupportedFrameworks
    )

    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "Package '$PackagePath' was not found."
    }

    $resolvedPath = (Resolve-Path -LiteralPath $PackagePath).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)

    try {
        $rootNuspecEntries = @(
            $archive.Entries |
                Where-Object {
                    -not $_.FullName.Contains('/') -and
                    -not $_.FullName.Contains('\') -and
                    $_.FullName.EndsWith('.nuspec', [System.StringComparison]::OrdinalIgnoreCase)
                }
        )

        if ($rootNuspecEntries.Count -ne 1) {
            throw "Expected exactly one root nuspec in '$PackagePath', found $($rootNuspecEntries.Count)."
        }

        [xml]$nuspecXml = Read-ZipEntryText -Entry $rootNuspecEntries[0]
        $idNode = $nuspecXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]')
        $versionNode = $nuspecXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]')

        if ($null -eq $idNode -or [string]::IsNullOrWhiteSpace($idNode.InnerText)) {
            throw "Package metadata id was not found in '$PackagePath'."
        }

        if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
            throw "Package metadata version was not found in '$PackagePath'."
        }

        $discoveredFrameworks = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
        $frameworkPattern = [System.Text.RegularExpressions.Regex]::new('^(?:lib|ref)/([^/\\]+)/[^/\\]+\.dll$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

        foreach ($entry in $archive.Entries) {
            $match = $frameworkPattern.Match($entry.FullName)
            if ($match.Success) {
                [void]$discoveredFrameworks.Add($match.Groups[1].Value)
            }
        }

        $supportedFrameworkSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($framework in @($SupportedFrameworks)) {
            if (-not [string]::IsNullOrWhiteSpace($framework)) {
                [void]$supportedFrameworkSet.Add($framework)
            }
        }

        $frameworks = [System.Collections.Generic.List[string]]::new()
        foreach ($framework in $discoveredFrameworks) {
            if ($supportedFrameworkSet.Contains($framework)) {
                $frameworks.Add($framework)
            }
        }

        if ($frameworks.Count -eq 0) {
            throw "No supported managed TFMs were found in '$PackagePath'."
        }

        return [pscustomobject]@{
            Id = $idNode.InnerText
            SafePackageId = ConvertTo-BinaryClosureSafePathSegment $idNode.InnerText
            Version = $versionNode.InnerText
            Frameworks = @($frameworks)
            Path = $resolvedPath
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Compare-BinaryClosureBaselineInventory {
    [CmdletBinding()]
    param(
        [object[]]$Closures,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$BaselineDirectory
    )

    $expectedNames = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($closure in @($Closures)) {
        if ($null -eq $closure) {
            continue
        }

        $safePackageId = [string]$closure.SafePackageId
        foreach ($framework in @($closure.Frameworks)) {
            if ([string]::IsNullOrWhiteSpace($safePackageId) -or [string]::IsNullOrWhiteSpace($framework)) {
                continue
            }

            [void]$expectedNames.Add("$safePackageId.$framework.txt")
        }
    }

    $actualNames = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    if (Test-Path -LiteralPath $BaselineDirectory -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $BaselineDirectory -File) {
            if ($file.Name.EndsWith('.txt', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$actualNames.Add($file.Name)
            }
        }
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $expectedNames) {
        if (-not $actualNames.Contains($name)) {
            $missing.Add($name)
        }
    }

    $orphaned = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $actualNames) {
        if (-not $expectedNames.Contains($name)) {
            $orphaned.Add($name)
        }
    }

    return [pscustomobject]@{
        Missing = @($missing)
        Orphaned = @($orphaned)
    }
}

function New-BinaryClosureConsumerProject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageVersion,

        [string[]]$Frameworks
    )

    if (-not $Frameworks -or $Frameworks.Count -eq 0) {
        throw 'At least one target framework is required.'
    }

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null

    $projectPath = Join-Path $Directory 'Consumer.csproj'
    $programPath = Join-Path $Directory 'Program.cs'

    $escapedPackageId = [System.Security.SecurityElement]::Escape($PackageId)
    $escapedPackageVersion = [System.Security.SecurityElement]::Escape($PackageVersion)
    $escapedFrameworks = [System.Security.SecurityElement]::Escape(($Frameworks -join ';'))

    $projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>$escapedFrameworks</TargetFrameworks>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$escapedPackageId" Version="[$escapedPackageVersion]" />
  </ItemGroup>
</Project>
"@

    $programContent = @"
Console.WriteLine("Hello, World!");
"@

    Set-Content -LiteralPath $projectPath -Value $projectContent -Encoding utf8
    Set-Content -LiteralPath $programPath -Value $programContent -Encoding utf8

    return [pscustomobject]@{
        ProjectPath = $projectPath
        ProgramPath = $programPath
    }
}

function New-BinaryClosureRestoreConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SourceConfigPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$DestinationPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceConfigPath -PathType Leaf)) {
        throw "Source config '$SourceConfigPath' was not found."
    }

    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Package directory '$PackageDirectory' was not found."
    }

    $destinationParent = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }

    $resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path

    [xml]$configXml = Get-Content -LiteralPath $SourceConfigPath -Raw
    $configurationNode = $configXml.SelectSingleNode('/configuration')

    if ($null -eq $configurationNode) {
        throw "Invalid NuGet config '$SourceConfigPath'."
    }

    $packageSourcesNode = $configXml.SelectSingleNode('/configuration/packageSources')
    if ($null -eq $packageSourcesNode) {
        $packageSourcesNode = $configXml.CreateElement('packageSources')
        [void]$configurationNode.AppendChild($packageSourcesNode)
    }

    $localSourceNode = $configXml.CreateElement('add')
    [void]$localSourceNode.SetAttribute('key', 'binary-closure-local')
    [void]$localSourceNode.SetAttribute('value', $resolvedPackageDirectory)

    $firstPackageSource = $packageSourcesNode.SelectSingleNode('add')
    if ($null -ne $firstPackageSource) {
        [void]$packageSourcesNode.InsertBefore($localSourceNode, $firstPackageSource)
    }
    else {
        [void]$packageSourcesNode.AppendChild($localSourceNode)
    }

    $mappingNode = $configXml.SelectSingleNode('/configuration/packageSourceMapping')
    if ($null -eq $mappingNode) {
        $mappingNode = $configXml.CreateElement('packageSourceMapping')
        [void]$configurationNode.AppendChild($mappingNode)
    }

    $localMappingNode = $configXml.CreateElement('packageSource')
    [void]$localMappingNode.SetAttribute('key', 'binary-closure-local')
    $localPatternNode = $configXml.CreateElement('package')
    [void]$localPatternNode.SetAttribute('pattern', '*')
    [void]$localMappingNode.AppendChild($localPatternNode)

    $firstMappingNode = $mappingNode.SelectSingleNode('packageSource')
    if ($null -ne $firstMappingNode) {
        [void]$mappingNode.InsertBefore($localMappingNode, $firstMappingNode)
    }
    else {
        [void]$mappingNode.AppendChild($localMappingNode)
    }

    $configXml.Save($DestinationPath)

    return $DestinationPath
}

Export-ModuleMember -Function @(
    'Compare-BinaryClosureBaselineInventory'
    'ConvertTo-BinaryClosureSafePathSegment'
    'Get-NuGetPackageFiles'
    'Get-NuGetPackageMetadata'
    'New-BinaryClosureConsumerProject'
    'New-BinaryClosureRestoreConfig'
)
