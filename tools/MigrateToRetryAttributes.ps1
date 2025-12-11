# MigrateToRetryAttributes.ps1
# Run from repository root: .\tools\MigrateToRetryAttributes.ps1

param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$rootPath = "c:\fhir-server"

Write-Host "=== Migration to RetryFact/RetryTheory ===" -ForegroundColor Cyan

# Find all test .cs files (exclude bin/obj)
$testFiles = Get-ChildItem -Path @(
    "$rootPath\src",
    "$rootPath\test"
) -Filter "*.cs" -Recurse -File | Where-Object { 
    $_.FullName -notmatch '\\(bin|obj)\\' 
}

Write-Host "Found $($testFiles.Count) .cs files to scan`n"

$stats = @{
    FilesUpdated = 0
    UsingAdded = 0
    FactReplaced = 0
    TheoryReplaced = 0
}

foreach ($file in $testFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Skip if no [Fact] or [Theory] attributes
    if ($content -notmatch '\[(Fact|Theory)[\(\]]') {
        continue
    }
    
    # Count replacements before making them
    $factMatches = ([regex]::Matches($content, '\[Fact[\(\]]')).Count
    $theoryMatches = ([regex]::Matches($content, '\[Theory[\(\]]')).Count
    
    # Replace [Fact] and [Fact( with [RetryFact] and [RetryFact(
    $content = $content -replace '\[Fact\]', '[RetryFact]'
    $content = $content -replace '\[Fact\(', '[RetryFact('
    
    # Replace [Theory] and [Theory( with [RetryTheory] and [RetryTheory(
    $content = $content -replace '\[Theory\]', '[RetryTheory]'
    $content = $content -replace '\[Theory\(', '[RetryTheory('
    
    # Add using statement if not already present - insert in correct position without reordering
    if ($content -notmatch 'using Microsoft\.Health\.Extensions\.Xunit;') {
        $usingLine = "using Microsoft.Health.Extensions.Xunit;`n"
        
        # Find the right insertion point - before first Microsoft.Health.Fhir.* using
        if ($content -match '(?m)^using Microsoft\.Health\.Fhir\.') {
            $match = [regex]::Match($content, '(?m)^using Microsoft\.Health\.Fhir\.')
            $content = $content.Insert($match.Index, $usingLine)
            $stats.UsingAdded++
        }
        # Or before first Microsoft.Health.* using (if no Fhir namespace)
        elseif ($content -match '(?m)^using Microsoft\.Health\.') {
            $match = [regex]::Match($content, '(?m)^using Microsoft\.Health\.')
            $content = $content.Insert($match.Index, $usingLine)
            $stats.UsingAdded++
        }
        # Or before using Xunit;
        elseif ($content -match '(?m)^using Xunit;') {
            $match = [regex]::Match($content, '(?m)^using Xunit;')
            $content = $content.Insert($match.Index, $usingLine)
            $stats.UsingAdded++
        }
    }
    
    if ($content -ne $originalContent) {
        $stats.FilesUpdated++
        $stats.FactReplaced += $factMatches
        $stats.TheoryReplaced += $theoryMatches
        
        if ($WhatIf) {
            Write-Host "Would update: $($file.FullName)" -ForegroundColor Yellow
        } else {
            Set-Content -Path $file.FullName -Value $content -NoNewline
            Write-Host "Updated: $($file.FullName)" -ForegroundColor Green
        }
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "  Files updated: $($stats.FilesUpdated)"
Write-Host "  Using statements added: $($stats.UsingAdded)"
Write-Host "  [Fact] replacements: $($stats.FactReplaced)"
Write-Host "  [Theory] replacements: $($stats.TheoryReplaced)"

if ($WhatIf) {
    Write-Host "`nRun without -WhatIf to apply changes" -ForegroundColor Yellow
}
