# Coverage Comparison Script
# Compares code coverage between current branch and main branch
# Usage: .\Compare-Coverage.ps1 -ProjectPath "path\to\project.csproj" -TestProjectPath "path\to\test.csproj"

param(
    [string]$TestProjectPath = "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj",
    [string]$ReportType = "Project" # Options: "Project", "Class"
)

$ErrorActionPreference = "Stop"
$RepoRoot = "c:\Users\ribans\source\repos\fhir-server"
$CoverageDir = Join-Path $RepoRoot "TestResults\CoverageComparison"
$RunSettings = Join-Path $RepoRoot "CodeCoverage.runsettings"

# Clean up previous results
if (Test-Path $CoverageDir) {
    Remove-Item -Path $CoverageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $CoverageDir | Out-Null

$CurrentBranch = git rev-parse --abbrev-ref HEAD
Write-Host "Current branch: $CurrentBranch" -ForegroundColor Cyan

# Function to run tests and get coverage
function Get-Coverage {
    param([string]$BranchName, [string]$OutputDir)
    
    Write-Host "`n=== Getting coverage for $BranchName ===" -ForegroundColor Yellow
    
    git checkout $BranchName 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to checkout $BranchName" -ForegroundColor Red
        return $null
    }
    
    # Build
    dotnet build (Join-Path $RepoRoot $TestProjectPath) --configuration Release -v q
    
    # Run tests with coverage
    $coverageOutput = Join-Path $OutputDir $BranchName
    dotnet test (Join-Path $RepoRoot $TestProjectPath) `
        --no-build `
        --configuration Release `
        --collect "XPlat Code Coverage" `
        --results-directory $coverageOutput `
        -s $RunSettings `
        -v q
    
    # Find the coverage file
    $coverageFile = Get-ChildItem -Path $coverageOutput -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1
    return $coverageFile?.FullName
}

# Function to parse Cobertura coverage
function Parse-Coverage {
    param([string]$CoverageFile, [string]$ReportType)
    
    if (-not (Test-Path $CoverageFile)) {
        Write-Host "Coverage file not found: $CoverageFile" -ForegroundColor Red
        return $null
    }
    
    [xml]$xml = Get-Content $CoverageFile
    $results = @{}
    
    if ($ReportType -eq "Project") {
        # Project-level coverage
        foreach ($package in $xml.coverage.packages.package) {
            $name = $package.name
            $lineRate = [double]$package.'line-rate' * 100
            $branchRate = [double]$package.'branch-rate' * 100
            $results[$name] = @{
                LineRate = [math]::Round($lineRate, 2)
                BranchRate = [math]::Round($branchRate, 2)
            }
        }
    }
    else {
        # Class-level coverage
        foreach ($package in $xml.coverage.packages.package) {
            foreach ($class in $package.classes.class) {
                $name = $class.name
                $lineRate = [double]$class.'line-rate' * 100
                $branchRate = [double]$class.'branch-rate' * 100
                $results[$name] = @{
                    LineRate = [math]::Round($lineRate, 2)
                    BranchRate = [math]::Round($branchRate, 2)
                }
            }
        }
    }
    
    return $results
}

# Function to compare coverage
function Compare-CoverageResults {
    param(
        [hashtable]$MainCoverage,
        [hashtable]$BranchCoverage,
        [string]$ReportType
    )
    
    Write-Host "`n" + "="*80 -ForegroundColor Green
    Write-Host "COVERAGE COMPARISON REPORT ($ReportType Level)" -ForegroundColor Green
    Write-Host "="*80 -ForegroundColor Green
    
    $allKeys = ($MainCoverage.Keys + $BranchCoverage.Keys) | Sort-Object -Unique
    $degraded = @()
    $improved = @()
    
    foreach ($key in $allKeys) {
        $main = $MainCoverage[$key]
        $branch = $BranchCoverage[$key]
        
        if ($null -eq $main) {
            Write-Host "[NEW] $key - Line: $($branch.LineRate)%" -ForegroundColor Cyan
            continue
        }
        
        if ($null -eq $branch) {
            Write-Host "[REMOVED] $key" -ForegroundColor Gray
            continue
        }
        
        $lineDiff = $branch.LineRate - $main.LineRate
        $branchDiff = $branch.BranchRate - $main.BranchRate
        
        if ($lineDiff -lt -1) {
            $degraded += @{
                Name = $key
                MainLine = $main.LineRate
                BranchLine = $branch.LineRate
                Diff = $lineDiff
            }
        }
        elseif ($lineDiff -gt 1) {
            $improved += @{
                Name = $key
                MainLine = $main.LineRate
                BranchLine = $branch.LineRate
                Diff = $lineDiff
            }
        }
    }
    
    Write-Host "`n--- COVERAGE DEGRADATIONS (>1% drop) ---" -ForegroundColor Red
    if ($degraded.Count -eq 0) {
        Write-Host "None! Great job!" -ForegroundColor Green
    }
    else {
        foreach ($item in $degraded | Sort-Object Diff) {
            Write-Host "$($item.Name)" -ForegroundColor Red
            Write-Host "  Main: $($item.MainLine)% -> Branch: $($item.BranchLine)% (Diff: $($item.Diff)%)"
        }
    }
    
    Write-Host "`n--- COVERAGE IMPROVEMENTS (>1% increase) ---" -ForegroundColor Green
    if ($improved.Count -eq 0) {
        Write-Host "None"
    }
    else {
        foreach ($item in $improved | Sort-Object Diff -Descending) {
            Write-Host "$($item.Name)" -ForegroundColor Green
            Write-Host "  Main: $($item.MainLine)% -> Branch: $($item.BranchLine)% (Diff: +$($item.Diff)%)"
        }
    }
    
    return $degraded.Count -eq 0
}

# Main execution
try {
    # Get coverage for main branch
    $mainCoverageFile = Get-Coverage -BranchName "main" -OutputDir $CoverageDir
    
    # Get coverage for current branch
    git checkout $CurrentBranch
    $branchCoverageFile = Get-Coverage -BranchName $CurrentBranch -OutputDir $CoverageDir
    
    # Parse and compare
    $mainCoverage = Parse-Coverage -CoverageFile $mainCoverageFile -ReportType $ReportType
    $branchCoverage = Parse-Coverage -CoverageFile $branchCoverageFile -ReportType $ReportType
    
    $passed = Compare-CoverageResults -MainCoverage $mainCoverage -BranchCoverage $branchCoverage -ReportType $ReportType
    
    # Return to original branch
    git checkout $CurrentBranch
    
    if (-not $passed) {
        Write-Host "`n[WARNING] Coverage has degraded in some areas!" -ForegroundColor Yellow
        exit 1
    }
    else {
        Write-Host "`n[SUCCESS] No significant coverage degradation detected!" -ForegroundColor Green
        exit 0
    }
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    git checkout $CurrentBranch
    exit 1
}
