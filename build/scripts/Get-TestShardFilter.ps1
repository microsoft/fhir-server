<#
.SYNOPSIS
    Returns the Microsoft.Testing.Platform filter arguments that select one shard of a test-shard manifest.

.DESCRIPTION
    A manifest is { "suite": "...", "shards": [ { "classes": [...], "methods": [...] }, ..., {} ] }.
    The last shard must be the catch-all `{}`: it runs everything the other shards do not list, so a test
    added after the manifest was written still runs somewhere.

    A listed shard gets  --filter-method <Ns.Class>.*   per class and  --filter-method <Ns.Class.Method>  per method.
    The catch-all gets   --filter-not-method <Ns.Class>.*  per listed class and  --filter-not-method <exact>  per listed method.
    Only --filter-method / --filter-not-method are used, so the result can be combined with --filter-trait and
    --filter-not-trait but never with --filter-class (AND'd with --filter-method) or --filter-query.

    Throws when a class or method is listed twice (including a method of an already listed class), when the
    catch-all is not the last shard, or when the shard index is out of range.

.PARAMETER Manifest
    Path of the manifest JSON file.

.PARAMETER ShardIndex
    Zero-based index of the shard.

.OUTPUTS
    string[] of test application arguments.
#>
[CmdletBinding()]
[OutputType([string[]])]
param(
    [Parameter(Mandatory)]
    [string] $Manifest,

    [Parameter(Mandatory)]
    [int] $ShardIndex
)

$ErrorActionPreference = 'Stop'

function Get-ShardEntries {
    param($Shard, [string] $Name)
    if ($null -eq $Shard) { return @() }
    $property = $Shard.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return @() }
    return @($property.Value | ForEach-Object { [string] $_ })
}

$definition = Get-Content -Path $Manifest -Raw | ConvertFrom-Json
$shards = @($definition.shards)

if ($shards.Count -eq 0) {
    throw "Manifest '$Manifest' has no shards."
}

if ($ShardIndex -lt 0 -or $ShardIndex -ge $shards.Count) {
    throw "Shard index $ShardIndex is out of range for $($shards.Count) shard(s)."
}

$lastIndex = $shards.Count - 1
$seen = @{}
$allClasses = [System.Collections.Generic.List[string]]::new()
$allMethods = [System.Collections.Generic.List[string]]::new()

for ($index = 0; $index -lt $shards.Count; $index++) {
    $classes = Get-ShardEntries $shards[$index] 'classes'
    $methods = Get-ShardEntries $shards[$index] 'methods'
    $isCatchAll = $classes.Count -eq 0 -and $methods.Count -eq 0

    if ($isCatchAll -and $index -ne $lastIndex) {
        throw "Shard $index is a catch-all ({}) but is not the last shard; the catch-all must be last."
    }

    if (-not $isCatchAll -and $index -eq $lastIndex) {
        throw "The last shard ($index) must be the catch-all ({}), but it lists tests."
    }

    foreach ($class in $classes) {
        if ($seen.ContainsKey($class)) {
            throw "Duplicate entry '$class' (shard $($seen[$class]) and shard $index)."
        }

        $seen[$class] = $index
        $allClasses.Add($class)
    }

    foreach ($method in $methods) {
        if ($seen.ContainsKey($method)) {
            throw "Duplicate entry '$method' (shard $($seen[$method]) and shard $index)."
        }

        $seen[$method] = $index
        $allMethods.Add($method)
    }
}

# A method of a listed class is selected twice, whichever shard lists the class.
foreach ($method in $allMethods) {
    $owner = $method.Substring(0, $method.LastIndexOf('.'))
    if ($allClasses.Contains($owner)) {
        throw "Duplicate entry '$method': its class '$owner' is also listed (shard $($seen[$owner]))."
    }
}

$arguments = [System.Collections.Generic.List[string]]::new()

if ($ShardIndex -eq $lastIndex) {
    foreach ($class in $allClasses) { $arguments.Add('--filter-not-method'); $arguments.Add("$class.*") }
    foreach ($method in $allMethods) { $arguments.Add('--filter-not-method'); $arguments.Add($method) }
}
else {
    foreach ($class in (Get-ShardEntries $shards[$ShardIndex] 'classes')) { $arguments.Add('--filter-method'); $arguments.Add("$class.*") }
    foreach ($method in (Get-ShardEntries $shards[$ShardIndex] 'methods')) { $arguments.Add('--filter-method'); $arguments.Add($method) }
}

return [string[]] $arguments
