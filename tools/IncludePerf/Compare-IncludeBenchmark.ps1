<#
.SYNOPSIS
    Compares two FhirIncludeBenchmark result files and produces an A/B regression report.

.DESCRIPTION
    Joins baseline and branch results on case name + patient class and reports latency deltas.

    Interpreting the report requires care, because PR 5683 deliberately changes RESULTS as well as
    timing: it removes out-of-compartment resources from _include/_revinclude output. The report
    therefore surfaces returned-entry counts next to latency and classifies each case:

      REGRESSION        slower, and returning the same or fewer entries  -> real cost increase
      SLOWER (more data) slower while returning more entries             -> investigate, rarely expected
      IMPROVED          faster while returning the same number of entries
      FASTER (less data) faster but returning fewer entries              -> not a genuine win; the
                                                                            speedup is explained by the
                                                                            compartment fix removing rows
      UNCHANGED         within the noise threshold

    Cases in the "admin" auth family exercise SQL that PR 5683 does not modify, so any movement there is
    environmental noise and calibrates the threshold for the SMART families.

.EXAMPLE
    ./Compare-IncludeBenchmark.ps1 -BaselinePath baseline.json -BranchPath branch.json -OutputPath report.md
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $BaselinePath,

    [Parameter(Mandatory = $true)]
    [string[]] $BranchPath,

    [Parameter(Mandatory = $false)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [double] $NoiseThresholdPercent = 15.0
)

$ErrorActionPreference = 'Stop'

function Read-Cases {
    param([string[]] $Paths)

    $byKey = @{}

    foreach ($path in $Paths) {
        $run = Get-Content $path -Raw | ConvertFrom-Json
        foreach ($case in $run.cases) {
            $key = "$($case.Name)|$($case.PatientClass)"
            if (-not $byKey.ContainsKey($key)) { $byKey[$key] = @() }
            $byKey[$key] += $case
        }
    }

    # When multiple rounds are supplied, take the best (minimum) p50/p95 per case. Minimum is the most
    # robust summary against transient cloud noise, which is one-sided: interference only ever makes a
    # sample slower, never faster.
    $result = @{}
    foreach ($key in $byKey.Keys) {
        $samples = $byKey[$key]
        $first = $samples[0]
        $result[$key] = [pscustomobject]@{
            Name              = $first.Name
            Group             = $first.Group
            Auth              = $first.Auth
            PatientClass      = $first.PatientClass
            PathAndQuery      = $first.PathAndQuery
            Notes             = $first.Notes
            Rounds            = $samples.Count
            P50Ms             = ($samples | Measure-Object -Property P50Ms -Minimum).Minimum
            P95Ms             = ($samples | Measure-Object -Property P95Ms -Minimum).Minimum
            MeanMs            = ($samples | Measure-Object -Property MeanMs -Average).Average
            EntryCount        = $first.EntryCount
            MatchEntryCount   = $first.MatchEntryCount
            IncludeEntryCount = $first.IncludeEntryCount
            Errors            = ($samples | Measure-Object -Property Errors -Sum).Sum
            FirstError        = $first.FirstError
        }
    }

    return $result
}

$baseline = Read-Cases -Paths $BaselinePath
$branch = Read-Cases -Paths $BranchPath

$rows = @()

foreach ($key in ($baseline.Keys | Sort-Object)) {
    if (-not $branch.ContainsKey($key)) { continue }

    $b = $baseline[$key]
    $x = $branch[$key]

    $deltaP50 = $x.P50Ms - $b.P50Ms
    $deltaP95 = $x.P95Ms - $b.P95Ms
    $pctP50 = if ($b.P50Ms -gt 0) { 100.0 * $deltaP50 / $b.P50Ms } else { 0 }
    $pctP95 = if ($b.P95Ms -gt 0) { 100.0 * $deltaP95 / $b.P95Ms } else { 0 }
    $entryDelta = $x.EntryCount - $b.EntryCount

    $verdict =
        if ($b.Errors -gt 0 -or $x.Errors -gt 0) { 'ERROR' }
        elseif ([math]::Abs($pctP95) -le $NoiseThresholdPercent) { 'UNCHANGED' }
        elseif ($pctP95 -gt 0 -and $entryDelta -gt 0) { 'SLOWER (more data)' }
        elseif ($pctP95 -gt 0) { 'REGRESSION' }
        elseif ($entryDelta -lt 0) { 'FASTER (less data)' }
        else { 'IMPROVED' }

    $rows += [pscustomobject]@{
        Name            = $b.Name
        Group           = $b.Group
        Auth            = $b.Auth
        PatientClass    = $b.PatientClass
        BaseP50         = [math]::Round($b.P50Ms, 1)
        BranchP50       = [math]::Round($x.P50Ms, 1)
        BaseP95         = [math]::Round($b.P95Ms, 1)
        BranchP95       = [math]::Round($x.P95Ms, 1)
        DeltaP95Ms      = [math]::Round($deltaP95, 1)
        DeltaP95Pct     = [math]::Round($pctP95, 1)
        DeltaP50Pct     = [math]::Round($pctP50, 1)
        BaseEntries     = $b.EntryCount
        BranchEntries   = $x.EntryCount
        EntryDelta      = $entryDelta
        Verdict         = $verdict
        Errors          = $b.Errors + $x.Errors
        PathAndQuery    = $b.PathAndQuery
    }
}

$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("# _include / _revinclude A/B performance report")
$null = $sb.AppendLine()
$null = $sb.AppendLine("- Baseline: ``$($BaselinePath -join ', ')``")
$null = $sb.AppendLine("- Branch:   ``$($BranchPath -join ', ')``")
$null = $sb.AppendLine("- Noise threshold: +/- $NoiseThresholdPercent% on p95")
$null = $sb.AppendLine()

$regressions = @($rows | Where-Object { $_.Verdict -eq 'REGRESSION' -or $_.Verdict -eq 'SLOWER (more data)' })
$errors = @($rows | Where-Object { $_.Verdict -eq 'ERROR' })

$null = $sb.AppendLine("## Verdict")
$null = $sb.AppendLine()
if ($errors.Count -gt 0) {
    $null = $sb.AppendLine("**$($errors.Count) case(s) errored** - results are not trustworthy until fixed.")
}
if ($regressions.Count -eq 0) {
    $null = $sb.AppendLine("**No performance regressions detected** above the +/- $NoiseThresholdPercent% p95 noise threshold.")
} else {
    $null = $sb.AppendLine("**$($regressions.Count) potential regression(s):**")
    $null = $sb.AppendLine()
    foreach ($r in ($regressions | Sort-Object -Property DeltaP95Pct -Descending)) {
        $null = $sb.AppendLine("- ``$($r.Name)`` [$($r.PatientClass)] p95 $($r.BaseP95)ms -> $($r.BranchP95)ms (**+$($r.DeltaP95Pct)%**), entries $($r.BaseEntries) -> $($r.BranchEntries)")
    }
}
$null = $sb.AppendLine()

# The admin family is untouched by PR 5683, so it measures environmental noise.
$adminRows = @($rows | Where-Object { $_.Auth -eq 'Admin' })
if ($adminRows.Count -gt 0) {
    $adminMax = ($adminRows | ForEach-Object { [math]::Abs($_.DeltaP95Pct) } | Measure-Object -Maximum).Maximum
    $null = $sb.AppendLine("### Control calibration (non-SMART cases, unmodified SQL)")
    $null = $sb.AppendLine()
    $null = $sb.AppendLine("Largest absolute p95 movement across the $($adminRows.Count) non-SMART cases: **$adminMax%**.")
    $null = $sb.AppendLine("Treat SMART deltas smaller than this as indistinguishable from noise.")
    $null = $sb.AppendLine()
}

foreach ($auth in @('Admin', 'SmartPatient', 'SmartPatientV2')) {
    $section = @($rows | Where-Object { $_.Auth -eq $auth })
    if ($section.Count -eq 0) { continue }

    $null = $sb.AppendLine("## $auth")
    $null = $sb.AppendLine()
    $null = $sb.AppendLine("| Case | Patient | Base p50 | Branch p50 | Base p95 | Branch p95 | dp95 | dp95 % | Base entries | Branch entries | Verdict |")
    $null = $sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|")

    foreach ($r in ($section | Sort-Object Group, Name, PatientClass)) {
        $null = $sb.AppendLine("| $($r.Name) | $($r.PatientClass) | $($r.BaseP50) | $($r.BranchP50) | $($r.BaseP95) | $($r.BranchP95) | $($r.DeltaP95Ms) | $($r.DeltaP95Pct)% | $($r.BaseEntries) | $($r.BranchEntries) | $($r.Verdict) |")
    }

    $null = $sb.AppendLine()
}

$null = $sb.AppendLine("## Compartment-leak evidence")
$null = $sb.AppendLine()
$null = $sb.AppendLine("Cases where the branch returned fewer entries confirm the fix is removing out-of-compartment")
$null = $sb.AppendLine("resources that the baseline leaked. A latency drop on these cases is a side effect of returning")
$null = $sb.AppendLine("less data, not a genuine optimization.")
$null = $sb.AppendLine()
$leaks = @($rows | Where-Object { $_.EntryDelta -lt 0 })
if ($leaks.Count -eq 0) {
    $null = $sb.AppendLine("_No entry-count differences observed._")
} else {
    $null = $sb.AppendLine("| Case | Patient | Base entries | Branch entries | Removed |")
    $null = $sb.AppendLine("|---|---|---:|---:|---:|")
    foreach ($r in ($leaks | Sort-Object EntryDelta)) {
        $null = $sb.AppendLine("| $($r.Name) | $($r.PatientClass) | $($r.BaseEntries) | $($r.BranchEntries) | $([math]::Abs($r.EntryDelta)) |")
    }
}

$report = $sb.ToString()

if ($OutputPath) {
    Set-Content -Path $OutputPath -Value $report -Encoding UTF8
    $csvPath = [System.IO.Path]::ChangeExtension($OutputPath, '.csv')
    $rows | Export-Csv -Path $csvPath -NoTypeInformation
    Write-Host "Report : $OutputPath"
    Write-Host "CSV    : $csvPath"
}
else {
    Write-Output $report
}

$rows | Format-Table Name, PatientClass, BaseP95, BranchP95, DeltaP95Pct, BaseEntries, BranchEntries, Verdict -AutoSize
