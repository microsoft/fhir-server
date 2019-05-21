[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageName
)

Set-StrictMode -Version Latest

$projects = Get-Childitem -Path *.csproj -File -Recurse
Write-Host "Updating $($projects)"

foreach($file in $projects) {
    [xml] $csproj = Get-Content $file
    $packageReference = $csproj.Project.SelectNodes("ItemGroup/PackageReference") | Where-Object Include -eq $PackageName

    if ($null -ne $packageReference) {
        Write-Host "Updating $($file)"
        $packageReference.setAttribute('Version', $packageVersion) | Out-Null
        $csproj.Save($file) | Out-Null
    }
}