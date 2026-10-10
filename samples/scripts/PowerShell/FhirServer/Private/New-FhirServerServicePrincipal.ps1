function New-FhirServerServicePrincipal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppId
    )

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            New-MgServicePrincipal -AppId $AppId -ErrorAction Stop | Out-Null
            return
        }
        catch {
            if ($attempt -eq 5 -or $_.Exception.Message -notlike '*does not reference a valid application object*') {
                throw
            }

            $delaySeconds = 5 * [math]::Pow(2, $attempt - 1)
            Write-Warning "Waiting for application $AppId to become available before creating its service principal (attempt $attempt of 5)."
            Start-Sleep -Seconds $delaySeconds
        }
    }
}
