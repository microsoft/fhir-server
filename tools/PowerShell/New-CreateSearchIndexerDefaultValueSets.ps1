<#
.SYNOPSIS
This script downloads the spec definitions and creates a mapping file of a FHIR Resource property, to its specified CodeSystem,
e.g. "DocumentReference.relatesTo.code":  "http://hl7.org/fhir/document-relationship-type"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [String]$DefinitionsJson = "https://www.hl7.org/fhir/definitions.json.zip",

    [Parameter(Mandatory = $false)]
    [String]$Version = "R4"
)

Set-StrictMode -Version Latest

$version = $Version
$basePath = "$PSScriptRoot\Specification\$version\"
$definitionFile = "$basePath\definitions.json.zip"

if(-not [System.IO.File]::Exists($definitionFile))
{
    New-Item -Path "$basePath" -ItemType Directory -Force
    Invoke-WebRequest -Uri $DefinitionsJson -OutFile $definitionFile

    Expand-Archive $definitionFile -DestinationPath $basePath
}

$mappings = [System.Collections.Specialized.OrderedDictionary]::new()

$profiles = Get-Content "$basePath\profiles-resources.json" | ConvertFrom-Json
$valueSets = (Get-Content "$basePath\valuesets.json" | ConvertFrom-Json).entry | Where-Object { $_.resource.resourceType -eq "ValueSet" }

foreach($resource in $profiles.entry.resource)
{
    $resourcesWithSnapshot = $resource | Where-Object { $_.PSobject.Properties.name -match "snapshot" } |  Where-Object { $_.snapshot.PSobject.Properties.name -match "element" }
    $sets =  $resourcesWithSnapshot.snapshot.element | Where-Object { $_.PSobject.Properties.name -match "binding" }

    foreach($set in $sets | Where-Object { $_.binding.PSobject.Properties.name -match "valueSet" -or $_.binding.PSobject.Properties.name -match "valueSetReference" })
    {
        if($set.binding.PSobject.Properties.name -match "valueSetReference") {
            # STU3 lookup
            $valueSetLookup = $set.binding.valueSetReference.reference.split('|')[0];
        }
        else{
            $valueSetLookup = $set.binding.valueSet.split('|')[0];
        }

        $valueSet = $valueSets | Where-Object { $_.fullUrl -eq $valueSetLookup `
            -or ($_.resource.PSobject.Properties.name -match "contained" -and $_.resource.contained[0].PSobject.Properties.name -match "targetCanonical" -and $_.resource.contained[0].targetCanonical -eq $valueSetLookup) }

        if($null -ne $set.id -and $null -ne $valueSet -and $null -ne $valueSet[0].resource.compose.include[0] -and $valueSet[0].resource.compose.include[0].PSobject.Properties.name -match "system")
        {
            $normalizedKey = $set.id -replace '\[\w+\]', ''
            $mappings[$normalizedKey] = $valueSet[0].resource.compose.include[0].system
        }
    }
} 

$mappings | ConvertTo-Json | Out-File "$PSScriptRoot\..\..\src\Microsoft.Health.Fhir.Core\Data\$($version)\resourcepath-codesystem-mappings.json"