[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaselineDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReportDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WorkDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$NuGetConfigPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CheckBinaryCompatPath,

    [string[]]$SupportedFrameworks = @('net8.0', 'net9.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExistingAbsolutePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-ProviderAbsolutePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Resolve-CommandAbsolutePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$CommandName
    )

    $command = Get-Command -Name $CommandName -ErrorAction Stop | Select-Object -First 1
    foreach ($propertyName in @('Source', 'Path', 'Definition')) {
        $candidate = [string]$command.$propertyName
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }

    throw "Could not resolve command path for '$CommandName'."
}

function Add-ValidationError {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]]$Errors,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Message
    )

    $Errors.Add($Message) | Out-Null
}

function Invoke-ExternalCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FilePath,

        [string[]]$ArgumentList = @(),

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

function Get-NormalizedFileContent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    $content = [System.IO.File]::ReadAllText($Path)
    $normalized = [System.Text.RegularExpressions.Regex]::Replace($content, "`r`n?", "`n")
    return $normalized.TrimEnd("`n")
}

function Get-LibraryIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Identity
    )

    $separatorIndex = $Identity.IndexOf('/')
    if ($separatorIndex -lt 1) {
        throw "Library identity '$Identity' was not in the expected '<id>/<version>' format."
    }

    return [pscustomobject]@{
        Id = $Identity.Substring(0, $separatorIndex)
        Version = $Identity.Substring($separatorIndex + 1)
    }
}

function Get-SingleCaseInsensitiveDictionaryKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Dictionary,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ExpectedKey,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Context
    )

    $matches = @(
        $Dictionary.Keys |
            Where-Object { [string]::Equals([string]$_, $ExpectedKey, [System.StringComparison]::OrdinalIgnoreCase) }
    )

    if ($matches.Count -ne 1) {
        throw "$Context expected exactly one key named '$ExpectedKey', found $($matches.Count)."
    }

    return [string]$matches[0]
}

function Assert-AssetsRootPackageIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AssetsPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PackageVersion,

        [Parameter(Mandatory = $true)]
        [string[]]$Frameworks
    )

    if (-not (Test-Path -LiteralPath $AssetsPath -PathType Leaf)) {
        throw "Restore assets file '$AssetsPath' was not found."
    }

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
    $libraries = $assets['libraries']
    if ($null -eq $libraries) {
        throw "Restore assets file '$AssetsPath' did not contain a 'libraries' section."
    }

    $matchingLibraryKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($libraryKey in $libraries.Keys) {
        $identity = Get-LibraryIdentity -Identity ([string]$libraryKey)
        if ([string]::Equals($identity.Id, $PackageId, [System.StringComparison]::OrdinalIgnoreCase)) {
            $matchingLibraryKeys.Add([string]$libraryKey) | Out-Null
        }
    }

    if ($matchingLibraryKeys.Count -ne 1) {
        throw "Restore assets file '$AssetsPath' expected exactly one restored library for package '$PackageId', found $($matchingLibraryKeys.Count)."
    }

    $resolvedIdentity = Get-LibraryIdentity -Identity $matchingLibraryKeys[0]
    if (-not [string]::Equals($resolvedIdentity.Version, $PackageVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Restore assets file '$AssetsPath' resolved '$($matchingLibraryKeys[0])' instead of '$PackageId/$PackageVersion'."
    }

    $project = $assets['project']
    if ($null -eq $project) {
        throw "Restore assets file '$AssetsPath' did not contain a 'project' section."
    }

    $projectFrameworks = $project['frameworks']
    if ($null -eq $projectFrameworks) {
        throw "Restore assets file '$AssetsPath' did not contain project framework dependencies."
    }

    foreach ($framework in @($Frameworks)) {
        $frameworkKey = Get-SingleCaseInsensitiveDictionaryKey -Dictionary $projectFrameworks -ExpectedKey $framework -Context "Restore assets file '$AssetsPath'"
        $frameworkEntry = $projectFrameworks[$frameworkKey]
        $dependencies = $frameworkEntry['dependencies']
        if ($null -eq $dependencies) {
            throw "Restore assets file '$AssetsPath' did not contain dependencies for target framework '$framework'."
        }

        $dependencyKey = Get-SingleCaseInsensitiveDictionaryKey -Dictionary $dependencies -ExpectedKey $PackageId -Context "Restore assets file '$AssetsPath' target framework '$framework'"
        $dependency = $dependencies[$dependencyKey]
        if ($null -eq $dependency) {
            throw "Restore assets file '$AssetsPath' did not contain dependency details for package '$PackageId' in target framework '$framework'."
        }

        if (-not [string]::Equals([string]$dependency['target'], 'Package', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Restore assets file '$AssetsPath' dependency '$PackageId' in target framework '$framework' was not restored as a package."
        }

        $expectedVersionRange = "[$PackageVersion, $PackageVersion]"
        if ($dependency.Contains('version') -and -not [string]::Equals([string]$dependency['version'], $expectedVersionRange, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Restore assets file '$AssetsPath' dependency '$PackageId' in target framework '$framework' used version range '$($dependency['version'])' instead of '$expectedVersionRange'."
        }
    }
}

function Get-ReportBaselinePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$BaselineDirectoryPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EmptyBaselineDirectoryPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$BaselineName
    )

    $baselinePath = Join-Path $BaselineDirectoryPath $BaselineName
    if (Test-Path -LiteralPath $baselinePath -PathType Leaf) {
        return $baselinePath
    }

    $generatedPath = Join-Path $EmptyBaselineDirectoryPath $BaselineName
    if (-not (Test-Path -LiteralPath $generatedPath -PathType Leaf)) {
        [System.IO.File]::WriteAllText($generatedPath, [string]::Empty)
    }

    return $generatedPath
}

$resolvedPackageDirectory = $null
$resolvedBaselineDirectory = $null
$resolvedReportDirectory = $null
$resolvedWorkDirectory = $null
$resolvedNuGetConfigPath = $null
$resolvedCheckBinaryCompatPath = $null
$errors = [System.Collections.Generic.List[string]]::new()
$packageCount = 0
$closureCount = 0
$consumerRootDirectory = $null
$packageCacheDirectory = $null
$canProcessPackages = $true
$packagesToProcess = @()

try {
    $resolvedPackageDirectory = Resolve-ExistingAbsolutePath -Path $PackageDirectory
    $resolvedBaselineDirectory = Resolve-ProviderAbsolutePath -Path $BaselineDirectory
    $resolvedReportDirectory = Resolve-ProviderAbsolutePath -Path $ReportDirectory
    $resolvedWorkDirectory = Resolve-ProviderAbsolutePath -Path $WorkDirectory
    $resolvedNuGetConfigPath = Resolve-ExistingAbsolutePath -Path $NuGetConfigPath
    $resolvedCheckBinaryCompatPath = Resolve-CommandAbsolutePath -CommandName $CheckBinaryCompatPath

    Import-Module -Name (Join-Path $PSScriptRoot 'NuGetBinaryClosure.psm1') -Force

    $emptyBaselineDirectory = Join-Path $resolvedWorkDirectory 'empty-baseline'
    $consumerRootDirectory = Join-Path $resolvedWorkDirectory 'consumer'
    $packageCacheDirectory = Join-Path $resolvedWorkDirectory 'package-cache'
    $restoreConfigPath = Join-Path $resolvedWorkDirectory 'NuGet.config'

    foreach ($directoryPath in @(
        $resolvedBaselineDirectory,
        $resolvedReportDirectory,
        $resolvedWorkDirectory,
        $emptyBaselineDirectory,
        $consumerRootDirectory,
        $packageCacheDirectory
    )) {
        New-Item -ItemType Directory -Path $directoryPath -Force | Out-Null
    }

    $packageFiles = @(Get-NuGetPackageFiles -PackageDirectory $resolvedPackageDirectory)
    if ($packageFiles.Count -eq 0) {
        throw "No non-symbol NuGet packages were found in '$resolvedPackageDirectory'."
    }

    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($packageFile in $packageFiles) {
        try {
            $packages.Add((Get-NuGetPackageMetadata -PackagePath $packageFile.FullName -SupportedFrameworks $SupportedFrameworks)) | Out-Null
        }
        catch {
            Add-ValidationError -Errors $errors -Message "Package metadata discovery failed for '$($packageFile.FullName)': $($_.Exception.Message)"
        }
    }

    $packageCount = $packages.Count
    $closureCount = ($packages | ForEach-Object { @($_.Frameworks).Count } | Measure-Object -Sum).Sum

    $duplicatePackageGroups = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        $identityKey = "$($package.Id)/$($package.Version)"
        if (-not $duplicatePackageGroups.ContainsKey($identityKey)) {
            $duplicatePackageGroups[$identityKey] = [System.Collections.Generic.List[object]]::new()
        }

        $duplicatePackageGroups[$identityKey].Add($package) | Out-Null
    }

    $hasBlockingInvariantFailure = $false
    $invalidPackagePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($identityKey in ($duplicatePackageGroups.Keys | Sort-Object)) {
        $group = $duplicatePackageGroups[$identityKey]
        if ($group.Count -gt 1) {
            $hasBlockingInvariantFailure = $true
            $packagePaths = @($group | Sort-Object Path | ForEach-Object { "'$($_.Path)'" })
            foreach ($packagePath in @($group | Select-Object -ExpandProperty Path)) {
                $invalidPackagePaths.Add([string]$packagePath) | Out-Null
            }

            Add-ValidationError -Errors $errors -Message "Duplicate package identity '$identityKey' found in: $($packagePaths -join ', ')."
        }
    }

    $safePackageIdGroups = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        if (-not $safePackageIdGroups.ContainsKey($package.SafePackageId)) {
            $safePackageIdGroups[$package.SafePackageId] = [System.Collections.Generic.List[object]]::new()
        }

        $safePackageIdGroups[$package.SafePackageId].Add($package) | Out-Null
    }

    foreach ($safePackageId in ($safePackageIdGroups.Keys | Sort-Object)) {
        $group = $safePackageIdGroups[$safePackageId]
        $distinctPackageIdSet = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($packageId in @($group | Select-Object -ExpandProperty Id)) {
            $distinctPackageIdSet.Add([string]$packageId) | Out-Null
        }

        $distinctPackageIds = @($distinctPackageIdSet)

        if ($distinctPackageIds.Count -gt 1) {
            $hasBlockingInvariantFailure = $true
            foreach ($packagePath in @($group | Select-Object -ExpandProperty Path)) {
                $invalidPackagePaths.Add([string]$packagePath) | Out-Null
            }

            $quotedPackageIds = @($distinctPackageIds | ForEach-Object { "'$_'" })
            Add-ValidationError -Errors $errors -Message "Safe package id collision '$safePackageId' found for package ids: $($quotedPackageIds -join ', ')."
        }
    }

    $packageIdGroups = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        if (-not $packageIdGroups.ContainsKey($package.Id)) {
            $packageIdGroups[$package.Id] = [System.Collections.Generic.List[object]]::new()
        }

        $packageIdGroups[$package.Id].Add($package) | Out-Null
    }

    foreach ($packageId in ($packageIdGroups.Keys | Sort-Object)) {
        $group = $packageIdGroups[$packageId]
        $distinctVersionSet = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($version in @($group | Select-Object -ExpandProperty Version)) {
            $distinctVersionSet.Add([string]$version) | Out-Null
        }

        $distinctVersions = @($distinctVersionSet)
        if ($distinctVersions.Count -gt 1) {
            foreach ($packagePath in @($group | Select-Object -ExpandProperty Path)) {
                $invalidPackagePaths.Add([string]$packagePath) | Out-Null
            }

            $quotedVersions = @($distinctVersions | ForEach-Object { "'$_'" })
            Add-ValidationError -Errors $errors -Message "Multiple package versions found for package id '$packageId': $($quotedVersions -join ', ')."
        }
    }

    $inventory = Compare-BinaryClosureBaselineInventory -Closures $packages -BaselineDirectory $resolvedBaselineDirectory
    foreach ($baselineName in @($inventory.Missing | Sort-Object)) {
        Add-ValidationError -Errors $errors -Message "Missing baseline '$baselineName'."
        Get-ReportBaselinePath -BaselineDirectoryPath $resolvedBaselineDirectory -EmptyBaselineDirectoryPath $emptyBaselineDirectory -BaselineName $baselineName | Out-Null
    }

    $missingBaselineNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($baselineName in @($inventory.Missing)) {
        $missingBaselineNames.Add([string]$baselineName) | Out-Null
    }

    foreach ($baselineName in @($inventory.Orphaned | Sort-Object)) {
        Add-ValidationError -Errors $errors -Message "Orphaned baseline '$baselineName'."
    }

    if ($packages.Count -eq 0) {
        if ($errors.Count -eq 0) {
            Add-ValidationError -Errors $errors -Message "No package metadata could be read from '$resolvedPackageDirectory'."
        }
        $canProcessPackages = $false
    }

    $packagesToProcess = @(
        $packages |
            Where-Object { -not $invalidPackagePaths.Contains([string]$_.Path) }
    )

    if ($packagesToProcess.Count -eq 0) {
        $canProcessPackages = $false
    }

    if ($canProcessPackages) {
        New-BinaryClosureRestoreConfig -SourceConfigPath $resolvedNuGetConfigPath -DestinationPath $restoreConfigPath -PackageDirectory $resolvedPackageDirectory | Out-Null
        foreach ($package in $packagesToProcess) {
            $safeVersionSegment = ConvertTo-BinaryClosureSafePathSegment -Value ([string]$package.Version)
            $consumerDirectory = Join-Path $consumerRootDirectory (Join-Path $package.SafePackageId $safeVersionSegment)

            try {
                $consumerProject = New-BinaryClosureConsumerProject -Directory $consumerDirectory -PackageId $package.Id -PackageVersion $package.Version -Frameworks $package.Frameworks

                $restoreResult = Invoke-ExternalCommand -FilePath 'dotnet' -ArgumentList @(
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

                if ($restoreResult.ExitCode -ne 0) {
                    throw "dotnet restore failed for package '$($package.Id)' version '$($package.Version)' with exit code $($restoreResult.ExitCode). Output:$([System.Environment]::NewLine)$($restoreResult.Output)"
                }

                $assetsPath = Join-Path (Join-Path $consumerDirectory 'obj') 'project.assets.json'
                Assert-AssetsRootPackageIdentity -AssetsPath $assetsPath -PackageId $package.Id -PackageVersion $package.Version -Frameworks $package.Frameworks

                foreach ($framework in @($package.Frameworks)) {
                    $publishDirectory = Join-Path $consumerDirectory (Join-Path 'publish' $framework)
                    $packageReportDirectory = Join-Path $resolvedReportDirectory (Join-Path $package.SafePackageId $framework)
                    $baselineName = "$($package.SafePackageId).$framework.txt"
                    $baselinePath = Get-ReportBaselinePath -BaselineDirectoryPath $resolvedBaselineDirectory -EmptyBaselineDirectoryPath $emptyBaselineDirectory -BaselineName $baselineName
                    $binaryCompatReportPath = Join-Path $packageReportDirectory 'BinaryCompatReport.txt'
                    $assembliesReportPath = Join-Path $packageReportDirectory 'BinaryCompatReport.Assemblies.txt'
                    $comparisonPath = Join-Path $packageReportDirectory 'Comparison.txt'

                    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
                    New-Item -ItemType Directory -Path $packageReportDirectory -Force | Out-Null

                    try {
                        $publishResult = Invoke-ExternalCommand -FilePath 'dotnet' -ArgumentList @(
                            'publish'
                            $consumerProject.ProjectPath
                            '--configuration'
                            'Release'
                            '--framework'
                            $framework
                            '--no-restore'
                            '--output'
                            $publishDirectory
                            '-v'
                            'minimal'
                        ) -WorkingDirectory $consumerDirectory

                        if ($publishResult.ExitCode -ne 0) {
                            throw "dotnet publish failed for package '$($package.Id)' version '$($package.Version)' target framework '$framework' with exit code $($publishResult.ExitCode). Output:$([System.Environment]::NewLine)$($publishResult.Output)"
                        }

                        $comparisonResult = Invoke-ExternalCommand -FilePath $resolvedCheckBinaryCompatPath -ArgumentList @(
                            $publishDirectory
                            '-s'
                            '-l'
                            '-ignoreFrameworkAssemblies'
                            "-baseline:$baselinePath"
                            "-out:$binaryCompatReportPath"
                            '-outputNewWarnings'
                            '-outputSummary'
                        ) -WorkingDirectory $packageReportDirectory

                        Set-Content -LiteralPath $comparisonPath -Value $comparisonResult.Output -Encoding utf8

                        if (-not (Test-Path -LiteralPath $binaryCompatReportPath -PathType Leaf)) {
                            throw "checkbinarycompat did not produce '$binaryCompatReportPath' for package '$($package.Id)' target framework '$framework'."
                        }

                        if (-not (Test-Path -LiteralPath $assembliesReportPath -PathType Leaf)) {
                            throw "checkbinarycompat did not produce '$assembliesReportPath' for package '$($package.Id)' target framework '$framework'."
                        }

                        $actualReportContent = Get-NormalizedFileContent -Path $binaryCompatReportPath
                        $baselineContent = Get-NormalizedFileContent -Path $baselinePath
                        if ((-not $missingBaselineNames.Contains($baselineName)) -and -not [string]::Equals($actualReportContent, $baselineContent, [System.StringComparison]::Ordinal)) {
                            Add-ValidationError -Errors $errors -Message "Binary closure baseline drift detected for package '$($package.Id)' target framework '$framework'. Baseline: '$baselinePath'. Report: '$binaryCompatReportPath'."
                        }

                        if ($comparisonResult.ExitCode -ne 0) {
                            Add-ValidationError -Errors $errors -Message "checkbinarycompat failed for package '$($package.Id)' version '$($package.Version)' target framework '$framework' with exit code $($comparisonResult.ExitCode). See '$comparisonPath'."
                        }
                    }
                    catch {
                        Add-ValidationError -Errors $errors -Message $_.Exception.Message
                    }
                }
            }
            catch {
                Add-ValidationError -Errors $errors -Message $_.Exception.Message
            }
        }
    }
}
catch {
    Add-ValidationError -Errors $errors -Message $_.Exception.Message
}
finally {
    foreach ($temporaryPath in @($consumerRootDirectory, $packageCacheDirectory)) {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path -LiteralPath $temporaryPath)) {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$distinctErrors = @($errors | Sort-Object -Unique)
if ($distinctErrors.Count -gt 0) {
    foreach ($errorMessage in $distinctErrors) {
        Write-Output $errorMessage
    }

    exit 1
}

Write-Output "Validated $closureCount binary closures across $packageCount NuGet packages."
