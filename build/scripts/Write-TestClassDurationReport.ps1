param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReportName
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$trxFiles = Get-ChildItem -Path $ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue
if (-not $trxFiles) {
    Write-Host "No TRX files found under '$ResultsDirectory'."
    return
}

$records = foreach ($trxFile in $trxFiles) {
    [xml]$trx = Get-Content -Path $trxFile.FullName -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $namespaceManager.AddNamespace('trx', $trx.DocumentElement.NamespaceURI)

    $classByTestId = @{}
    foreach ($unitTest in $trx.SelectNodes('//trx:TestDefinitions/trx:UnitTest', $namespaceManager)) {
        $testMethod = $unitTest.SelectSingleNode('trx:TestMethod', $namespaceManager)
        if ($testMethod -and $testMethod.className) {
            $classByTestId[$unitTest.id] = $testMethod.className
        }
    }

    foreach ($result in $trx.SelectNodes('//trx:Results/trx:UnitTestResult', $namespaceManager)) {
        $className = $classByTestId[$result.testId]
        if ([string]::IsNullOrWhiteSpace($className)) {
            $testName = [string]$result.testName
            $lastDot = $testName.LastIndexOf('.')
            $className = if ($lastDot -gt 0) { $testName.Substring(0, $lastDot) } else { $testName }
        }

        $duration = if ($result.duration) { [TimeSpan]::Parse($result.duration) } else { [TimeSpan]::Zero }

        [PSCustomObject]@{
            ClassName = $className
            Outcome = [string]$result.outcome
            DurationSeconds = $duration.TotalSeconds
            TestName = [string]$result.testName
            TrxFile = $trxFile.Name
        }
    }
}

if (-not $records) {
    Write-Host "No test results found in TRX files under '$ResultsDirectory'."
    return
}

$summary = $records |
    Group-Object ClassName |
    ForEach-Object {
        $group = $_.Group
        [PSCustomObject]@{
            ClassName = $_.Name
            TotalSeconds = [Math]::Round(($group | Measure-Object DurationSeconds -Sum).Sum, 3)
            TestCount = $group.Count
            Passed = ($group | Where-Object Outcome -eq 'Passed').Count
            Failed = ($group | Where-Object Outcome -eq 'Failed').Count
            Skipped = ($group | Where-Object Outcome -in @('NotExecuted', 'Skipped')).Count
            MaxTestSeconds = [Math]::Round(($group | Measure-Object DurationSeconds -Maximum).Maximum, 3)
        }
    } |
    Sort-Object TotalSeconds -Descending

$csvPath = Join-Path $OutputDirectory "$ReportName.csv"
$summary | Export-Csv -Path $csvPath -NoTypeInformation

Write-Host "##[section]Test class duration report: $ReportName"
Write-Host "Report CSV: $csvPath"
$summary |
    Select-Object -First 40 ClassName, TotalSeconds, TestCount, Passed, Failed, Skipped, MaxTestSeconds |
    Format-Table -AutoSize |
    Out-String -Width 240 |
    Write-Host
