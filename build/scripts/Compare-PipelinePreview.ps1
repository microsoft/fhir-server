<#
.SYNOPSIS
    Read-only comparison of the expanded (finalYaml) pipeline YAML for two refs.

.DESCRIPTION
    For each pipeline definition, calls the Azure Pipelines Preview endpoint
    (POST _apis/pipelines/{id}/preview with previewRun:true) for the base and head refs,
    normalizes the returned finalYaml (LF line endings, trailing whitespace trimmed) and
    compares them with `git diff --no-index`. Used to prove that template refactors render
    identically.

    READ-ONLY: only the Preview endpoint is called. It never queues a run, never calls
    build/runs, and never modifies definitions. The Azure DevOps token is held in a variable
    only; it is never printed or written to disk.

    Exit codes: 0 = every definition's normalized finalYaml is identical;
                1 = at least one definition differs (writes <id>.base.yml, <id>.head.yml, <id>.diff to -OutDir);
                2 = a preview call or other error occurred (nothing could be proven).

.PARAMETER DefinitionIds
    Pipeline definition ids to compare (see the consumer inventory below). Accepts an array or a
    comma-separated string, so `pwsh -File ... -DefinitionIds 9,27` works.

.PARAMETER BaseRef
    Base ref: a branch ref (refs/heads/x) or a commit SHA.

.PARAMETER HeadRef
    Head ref: a branch ref (refs/heads/x) or a commit SHA.

.PARAMETER ShaBranch
    Branch name sent as refName when a ref is a commit SHA (the SHA is sent as "version").
    The Preview API rejects a bare SHA as refName ("Unable to resolve the reference
    'refs/heads/<sha>'"), but accepts refName=<branch> + version=<sha>. The SHA must be
    reachable in the repository; the branch only supplies the ref name. Default: refs/heads/main.

.PARAMETER OutDir
    Directory for diff artifacts. Defaults to the system temp directory (outside the repo).

.PARAMETER SortStages
    Orders the top-level `stages:` entries by stage name before diffing, so a pure stage
    reordering is not reported as a difference. Text-based: entries are split on top-level
    "- stage:" boundaries within the top-level `stages:` block.

.EXAMPLE
    pwsh build/scripts/Compare-PipelinePreview.ps1 -DefinitionIds 9,27,29,49,100,109,118,120 `
        -BaseRef refs/heads/main -HeadRef refs/heads/my-branch

.NOTES
    Consumer inventory (org microsofthealthoss, project FhirServer). Source:
    GET _apis/build/definitions?includeAllProperties=true, filtered to repository
    microsoft/fhir-server and process.yamlFilename under build/ (leading slashes normalized).
    Inventory taken 2026-11-16.

    Previewable (enabled) - pass these to -DefinitionIds:
        9   PR Build & Deploy                          build/pr-pipeline.yml
        27  CI Build & Deploy                          build/ci-pipeline.yml
        29  Deploy CI if broken                        build/ci-deploy.yml
        49  Cleanup Images                             build/cleanup-images.yml
        100 Cleanup PR Environments                    build/cleanup-pr-environments.yml
        109 Build Docker Image                         build/build-image.yml
        118 Cleanup AAD                                build/cleanup-aad-resources.yml
        120 PR Build & Deploy - Cleanup Environment    build/pr-cleanup-pipeline.yml

    Disabled; Preview rejects them -> "manual review" line in each task's PR notes:
        28  CI Deployment MAG                          build/ci-pipeline-mag.yml
        31  Remove PR Environment                      build/remove-environment.yml
        56  Export Tests                               build/export-pipeline.yml
        96  PR Validate ARM Templates                  build/pr-arm.yml
        Legacy (Archive folder): 1 (.vsts-pr.yml), 3 (.vsts-ci.yml), 8 (.vsts-package-ci.yml),
        65 (inferno-test-pipeline.yml), 68 (import-pipeline.yml)

    Other repositories' consumers (e.g. 12 microsoft/iomt-fhir, 21 healthcare-components,
    25 healthcare-shared-components, 78 microsoft/fhir-server-samples, which also have build/
    YAML files): UNVERIFIED - not covered by this script and not checked for use of this
    repository's templates.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $DefinitionIds,
    [Parameter(Mandatory)] [string] $BaseRef,
    [Parameter(Mandatory)] [string] $HeadRef,
    [string] $ShaBranch = 'refs/heads/main',
    [string] $OutDir = [IO.Path]::GetTempPath(),
    [switch] $SortStages
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$organization = 'microsofthealthoss'
$project = 'FhirServer'
$adoResourceId = '499b84ac-1321-427f-aa17-267ca6975798'

$headers = $null

function Get-PreviewBody([string] $Ref) {
    $self = @{}
    if ($Ref -match '^[0-9a-fA-F]{40}$') {
        $self.refName = $ShaBranch
        $self.version = $Ref.ToLowerInvariant()
    }
    else {
        $self.refName = $Ref
    }

    return @{ previewRun = $true; resources = @{ repositories = @{ self = $self } } } | ConvertTo-Json -Depth 6 -Compress
}

function Get-FinalYaml([int] $Id, [string] $Ref) {
    # The only call this script makes: the read-only Preview endpoint.
    $uri = "https://dev.azure.com/$organization/$project/_apis/pipelines/$Id/preview?api-version=7.1"
    try {
        $response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' -Body (Get-PreviewBody $Ref)
    }
    catch {
        $detail = if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            try { ($_.ErrorDetails.Message | ConvertFrom-Json).message } catch { $_.ErrorDetails.Message }
        }
        else { $_.Exception.Message }
        throw "Preview failed for definition $Id at '$Ref': $detail"
    }

    if ([string]::IsNullOrWhiteSpace($response.finalYaml)) {
        throw "Preview returned empty finalYaml for definition $Id at '$Ref'."
    }

    return [string] $response.finalYaml
}

function Format-Yaml([string] $Yaml) {
    $lines = ($Yaml -replace "`r`n", "`n" -replace "`r", "`n") -split "`n" | ForEach-Object { $_.TrimEnd() }
    return (($lines -join "`n").TrimEnd("`n") + "`n")
}

function Sort-StageEntries([string] $Yaml) {
    $lines = [System.Collections.Generic.List[string]]($Yaml -split "`n")
    $start = $lines.IndexOf('stages:')
    if ($start -lt 0) { return $Yaml }

    # The stages block ends at the next top-level (column 0) line that is not a "- " list item.
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\S' -and $lines[$i] -notmatch '^-(\s|$)') { $end = $i; break }
    }

    $entries = [System.Collections.Generic.List[object]]::new()
    $current = $null
    for ($i = $start + 1; $i -lt $end; $i++) {
        if ($lines[$i] -match '^-\s+stage:\s*(?<name>\S.*?)\s*$') {
            $current = [pscustomobject]@{ Name = $Matches.name.Trim("'", '"'); Index = $entries.Count; Lines = [System.Collections.Generic.List[string]]::new() }
            $entries.Add($current)
        }
        elseif ($null -eq $current) {
            throw "Unexpected content before the first '- stage:' entry: '$($lines[$i])'"
        }
        $current.Lines.Add($lines[$i])
    }

    $sorted = $entries | Sort-Object -Property Name, Index
    $result = [System.Collections.Generic.List[string]]::new()
    $result.AddRange($lines.GetRange(0, $start + 1))
    foreach ($entry in $sorted) { $result.AddRange($entry.Lines) }
    $result.AddRange($lines.GetRange($end, $lines.Count - $end))
    return ($result -join "`n")
}

try {
    $ids = @($DefinitionIds | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { [int] $_.Trim() })
    if ($ids.Count -eq 0) { throw 'No -DefinitionIds were provided.' }

    $tokenText = az account get-access-token --resource $adoResourceId --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tokenText)) {
        throw 'Unable to get an Azure DevOps token; run "az login" first.'
    }
    $headers = @{ Authorization = "Bearer $tokenText" }

    $null = New-Item -ItemType Directory -Force -Path $OutDir
    $differing = [System.Collections.Generic.List[int]]::new()
    $errors = [System.Collections.Generic.List[int]]::new()

    foreach ($id in $ids) {
        $baseFile = Join-Path $OutDir "$id.base.yml"
        $headFile = Join-Path $OutDir "$id.head.yml"
        $diffFile = Join-Path $OutDir "$id.diff"

        try {
            $base = Format-Yaml (Get-FinalYaml $id $BaseRef)
            $head = Format-Yaml (Get-FinalYaml $id $HeadRef)
            if ($SortStages) {
                $base = Sort-StageEntries $base
                $head = Sort-StageEntries $head
            }
        }
        catch {
            Write-Host "ERROR  $id : $($_.Exception.Message)"
            $errors.Add($id)
            continue
        }

        if ($base -ceq $head) {
            # Remove artifacts left by an earlier differing run so they are not mistaken for current results.
            Remove-Item -LiteralPath $baseFile, $headFile, $diffFile -Force -ErrorAction SilentlyContinue
            Write-Host "SAME   $id"
            continue
        }

        [IO.File]::WriteAllText($baseFile, $base, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($headFile, $head, [Text.UTF8Encoding]::new($false))
        $diff = git --no-pager diff --no-index --no-color -- $baseFile $headFile 2>$null
        [IO.File]::WriteAllText($diffFile, (($diff -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
        Write-Host "DIFF   $id -> $diffFile"
        $differing.Add($id)
    }

    if ($errors.Count -gt 0) {
        Write-Host "Errors for definitions: $($errors -join ', ')"
        exit 2
    }
    if ($differing.Count -gt 0) {
        Write-Host "Differences in definitions: $($differing -join ', ')"
        exit 1
    }
    Write-Host 'All definitions render identically.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}