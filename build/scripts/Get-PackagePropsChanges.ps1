<#
.SYNOPSIS
    Returns the packages and version properties that a diff of the root Directory.Packages.props changes.

.DESCRIPTION
    Input for Get-PrValidationScope.ps1 -ChangedPackages and -ChangedPackageProperties (rulings R15b and R15c). Reads
    the lines of `git diff -U0 <base>...<head> -- Directory.Packages.props`; only lines starting with + or - (not the
    +++/--- file headers) count.

      Packages    each Include="<name>" on a changed line, plus every PackageVersion of Content whose Version
                  references a changed property ($(<property>)).
      Properties  each single-line property element (<Name>value</Name>) on a changed line.

    Pure: no I/O.

.PARAMETER DiffLines
    The diff output, one line per element.

.PARAMETER Content
    Directory.Packages.props at the head commit, one line per element; used to map changed properties to packages.

.OUTPUTS
    [pscustomobject] with Packages and Properties (string[], each name once, case-insensitive, in first-seen order).
#>
[CmdletBinding()]
param(
    [AllowEmptyCollection()]
    [string[]] $DiffLines = @(),

    [AllowEmptyCollection()]
    [string[]] $Content = @()
)

$ErrorActionPreference = 'Stop'

function Add-Unique([System.Collections.Generic.List[string]] $List, [string] $Name) {
    if (-not ($List | Where-Object { $_ -ieq $Name })) {
        $List.Add($Name)
    }
}

$packages = [System.Collections.Generic.List[string]]::new()
$properties = [System.Collections.Generic.List[string]]::new()
foreach ($line in $DiffLines) {
    if ($line -notmatch '^[+-](?![+-]{2} )') {
        continue
    }

    if ($line -match 'Include="([^"]+)"') {
        Add-Unique $packages $Matches[1]
    }
    elseif ($line -match '<(\w+)>[^<]*</\1>') {
        Add-Unique $properties $Matches[1]
    }
}

foreach ($property in $properties) {
    $reference = '$(' + $property + ')'
    foreach ($line in $Content) {
        if ($line -match 'Include="([^"]+)"' -and $line.Contains($reference, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Unique $packages $Matches[1]
        }
    }
}

[pscustomobject]@{
    Packages   = [string[]] $packages.ToArray()
    Properties = [string[]] $properties.ToArray()
}