<#
.SYNOPSIS
Turns an object read from an ASP.NET core JSON config file into a hashtable where each leaf value (string/int/bool) of the input object is added as a value in the hash table with the path to the value as the key.
.EXAMPLE
ConvertTo-FlattenedConfigurationHashtable.ps1 -InputObject (ConvertFrom-Json '{ "pets" : [ { "name": "Garfield" }, { "name": "Odie" } ] }') -PathPrefix "resource"
Returns:
Name                           Value
----                           -----
resource:pets:0:name           Garfield
resource:pets:1:name           Odie
.PARAMETER InputObject
The the deserialized JSON configuration object.
.PARAMETER PathPrefix
A path prefix to include in all paths
#>
param(
    [Parameter(Mandatory = $true)]
    $InputObject,

    [Parameter(Mandatory = $false)]
    $PathPrefix = ""
)

function Flatten ($Prefix, $Object) {

    if ($Object -is [System.Management.Automation.PSCustomObject]) {
        $Object.psobject.properties | ForEach-Object {
            Flatten -Prefix "$Prefix$(if ($Prefix) { ":" })$($_.Name)" -Object $_.Value
        }

        return
    }

    if ($Object -is [object[]]) {
        for ($i = 0; $i -lt $Object.Length; $i++) {
            Flatten -Prefix "$Prefix$(if ($Prefix) { ":" })$i" -Object $Object[$i]
        }

        return
    }

    @{$Prefix = $Object}
}

@(Flatten -Prefix $PathPrefix -Object $InputObject) | ForEach-Object { $hash = @{} } { $hash += $_ } { $hash }