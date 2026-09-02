function Assert-EffectiveFhirSdkProvider {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [array] $EnvironmentSettings,

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string] $ExpectedProvider = '',

        [Parameter(Mandatory = $true)]
        [string] $ContainerAppName
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedProvider)) {
        return
    }

    $settingName = 'FhirServer__CoreFeatures__FhirSdkProvider__Default'
    $matchingSettings = @($EnvironmentSettings | Where-Object { $_.name -eq $settingName })
    if ($matchingSettings.Count -eq 0) {
        throw "Container App '$ContainerAppName' does not define '$settingName'; expected '$ExpectedProvider'."
    }

    if ($matchingSettings.Count -gt 1) {
        throw "Container App '$ContainerAppName' defines '$settingName' more than once."
    }

    $actualProvider = [string]$matchingSettings[0].value
    if ($actualProvider -ne $ExpectedProvider) {
        throw "Container App '$ContainerAppName' has '$settingName' set to '$actualProvider'; expected '$ExpectedProvider'."
    }

    Write-Host "Verified Container App '$ContainerAppName' uses FHIR SDK provider '$actualProvider'."
}
