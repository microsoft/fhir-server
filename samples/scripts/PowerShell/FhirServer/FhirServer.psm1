$Public = @( Get-ChildItem -Path "$PSScriptRoot\Public\*.ps1" )
$Private = @( Get-ChildItem -Path "$PSScriptRoot\Private\*.ps1" )
$Shared = @( Get-ChildItem -Path "$PSScriptRoot\..\..\..\..\release\scripts\PowerShell\FhirServerRelease\Private\SharedModuleFunctions.ps1")

@($Public + $Private + $Shared) | ForEach-Object {
    Try {
        . $_.FullName
    } Catch {
        Write-Error -Message "Failed to import function $($_.FullName): $_"
    }
}
