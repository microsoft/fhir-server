function Get-LongResourceIdAppName {
    param(
        [Parameter(Mandatory = $true)] [ValidateSet('Stu3', 'R4', 'R5')] [string] $Version,
        [Parameter(Mandatory = $true)] [ValidateSet('PR', 'CI')] [string] $Lifecycle,
        [Parameter(Mandatory = $true)] [ValidatePattern('^[0-9]+$')] [string] $BuildId
    )
    $name = if ($Lifecycle -eq 'PR') { "f$BuildId-$Version-l128" } else { "msh-fhir-ci-$Version-long128" }
    if ($name.Length -gt 32) { throw 'The build-owned Long128 app name exceeds the ACA limit.' }
    return $name.ToLowerInvariant()
}

function Get-LongResourceIdRoleName {
    param([Parameter(Mandatory = $true)] [string] $AppResourceId)
    $bytes = [Text.Encoding]::UTF8.GetBytes("$($AppResourceId.ToLowerInvariant())/long128-keyvault")
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [guid]::new([byte[]]$hash[0..15]).ToString()
}

function Get-ResourceIdEnv {
    param($App, [string] $Name)
    $settings = @($App.Properties.template.containers[0].env | Where-Object name -CEQ $Name)
    if ($settings.Count -gt 1) { throw "Duplicate environment setting '$Name'." }
    if ($settings.Count -eq 1) { return [string]$settings[0].value }
    return $null
}

function Assert-ResourceIdApp {
    param($App, [string] $ExpectedImage, [ValidateSet('Standard64', 'Long128')] [string] $Profile)
    if ($App.Properties.template.containers.Count -ne 1) { throw 'Resource ID endpoints require exactly one FHIR container.' }
    $image = [string]$App.Properties.template.containers[0].image
    $matchesImage = if ($ExpectedImage.Contains('@')) {
        $image -ceq $ExpectedImage
    } else {
        $image -cmatch ('^' + [regex]::Escape($ExpectedImage) + '@sha256:[a-f0-9]{64}$')
    }
    if (-not $matchesImage) { throw 'The endpoint does not run the expected version/build image.' }
    if ((Get-ResourceIdEnv $App 'DataStore') -cne 'SqlServer') { throw 'The endpoint must use SQL.' }
    if ((Get-ResourceIdEnv $App 'FhirServer__Security__Enabled') -cne 'true') { throw 'The endpoint must retain authentication.' }
    $long = Get-ResourceIdEnv $App 'FhirServer__CoreFeatures__UseLongResourceIds'
    if ($Profile -eq 'Standard64') {
        if ($long -and $long -cne 'false') { throw 'The baseline must retain Standard64 behavior.' }
    } else {
        $required = @{
            FhirServer__CoreFeatures__UseLongResourceIds = 'true'
            TaskHosting__Enabled = 'false'
            SqlServer__AllowDatabaseCreation = 'false'
            SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'false'
            SqlServer__Initialize = 'true'
            SqlServer__DeleteAllDataOnStartup = 'false'
        }
        foreach ($name in $required.Keys) {
            if ((Get-ResourceIdEnv $App $name) -cne $required[$name]) { throw "Long128 requires $name=$($required[$name])." }
        }
    }
}
