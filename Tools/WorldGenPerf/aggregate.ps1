<#
.SYNOPSIS
    Turns the raw results.jsonl of one run into the report files the plan asks for.

.DESCRIPTION
    Writes coverage.csv, operations.csv, output_fingerprints.json and report.md next
    to the raw results. Every catalogue identifier appears in coverage.csv, including
    the ones that were never executed: a missing run is reported as not_run with its
    reason, never as a pass.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Results
)

$ErrorActionPreference = 'Stop'
$Results = (Resolve-Path $Results).Path

$catalogPath = Join-Path $Results 'catalog.csv'
$rawPath = Join-Path $Results 'results.jsonl'

if (-not (Test-Path $catalogPath)) {
    Write-Warning "no catalog.csv in $Results; run a suite first"
    return
}

$catalog = Import-Csv $catalogPath

$records = @()
if (Test-Path $rawPath) {
    foreach ($line in Get-Content $rawPath) {
        if (-not $line.Trim()) { continue }
        try { $records += ($line | ConvertFrom-Json) } catch { Write-Warning "unparsable result line skipped" }
    }
}

$byId = @{}
foreach ($record in $records) {
    $id = $record.catalogId
    if (-not $byId.ContainsKey($id)) { $byId[$id] = @() }
    $byId[$id] += $record
}

$coverage = foreach ($entry in $catalog) {
    $runs = @(if ($byId.ContainsKey($entry.id)) { $byId[$entry.id] } else { @() })
    $expected = [int]$entry.caseCount

    if ($runs.Count -eq 0) {
        $status = 'not_run'
        $reason = 'no result recorded in this run'
    }
    else {
        $statuses = $runs | ForEach-Object { $_.result.status } | Sort-Object -Unique
        $reason = ($runs | Where-Object { $_.result.error } | ForEach-Object { $_.result.error } | Select-Object -First 1)

        if ($statuses -contains 'failed') { $status = 'failed' }
        elseif ($statuses -contains 'timeout') { $status = 'timeout' }
        elseif ($statuses -contains 'blocked_dependency') { $status = 'blocked_dependency' }
        elseif ($statuses -contains 'blocked_resource') { $status = 'blocked_resource' }
        elseif ($statuses -contains 'not_run') { $status = 'not_run' }
        else { $status = 'passed' }

        if ($runs.Count -lt $expected -and $status -eq 'passed') {
            $status = 'partial'
            $reason = "$($runs.Count) of $expected cases executed"
        }
    }

    [pscustomobject]@{
        id            = $entry.id
        priority      = $entry.priority
        mode          = $entry.mode
        op            = $entry.op
        expectedCases = $expected
        ranCases      = $runs.Count
        executionMode = ($runs | ForEach-Object { $_.executionMode } | Sort-Object -Unique) -join '|'
        status        = $status
        reason        = $reason
        note          = $entry.note
    }
}

$coverage | Export-Csv -Path (Join-Path $Results 'coverage.csv') -NoTypeInformation -Encoding UTF8

$operations = foreach ($record in $records) {
    $result = $record.result
    $wall = $result.wallMs

    foreach ($stageName in ($result.stages | Get-Member -MemberType NoteProperty | ForEach-Object { $_.Name })) {
        $stage = $result.stages.$stageName
        [pscustomobject]@{
            runId     = $record.runId
            caseId    = $record.caseId
            iteration = 'aggregate'
            stage     = $stageName
            metric    = 'totalMs'
            value     = $stage.totalMs
            unit      = 'ms'
            count     = $stage.count
        }
    }

    [pscustomobject]@{
        runId     = $record.runId
        caseId    = $record.caseId
        iteration = 'aggregate'
        stage     = 'wall'
        metric    = 'p50'
        value     = $wall.p50
        unit      = 'ms'
        count     = $wall.count
    }

    [pscustomobject]@{
        runId     = $record.runId
        caseId    = $record.caseId
        iteration = 'aggregate'
        stage     = 'wall'
        metric    = 'max'
        value     = $wall.max
        unit      = 'ms'
        count     = $wall.count
    }

    foreach ($workName in ($result.work | Get-Member -MemberType NoteProperty | ForEach-Object { $_.Name })) {
        [pscustomobject]@{
            runId     = $record.runId
            caseId    = $record.caseId
            iteration = 'aggregate'
            stage     = 'work'
            metric    = $workName
            value     = $result.work.$workName
            unit      = 'count'
            count     = 1
        }
    }
}

if ($operations) {
    $operations | Export-Csv -Path (Join-Path $Results 'operations.csv') -NoTypeInformation -Encoding UTF8
}

$fingerprints = @{}
foreach ($record in $records) {
    $prints = $record.result.fingerprints
    if (-not $prints) { continue }

    foreach ($name in ($prints | Get-Member -MemberType NoteProperty | ForEach-Object { $_.Name })) {
        $fingerprints["$($record.caseId).$name"] = $prints.$name
    }
}

$fingerprints | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Results 'output_fingerprints.json') -Encoding UTF8

$counts = $coverage | Group-Object status | Sort-Object Name
$report = New-Object System.Text.StringBuilder

[void]$report.AppendLine('# World generation performance run')
[void]$report.AppendLine()
[void]$report.AppendLine("Results directory: ``$Results``")
[void]$report.AppendLine()
[void]$report.AppendLine('## Coverage')
[void]$report.AppendLine()
[void]$report.AppendLine('| status | specifications |')
[void]$report.AppendLine('|---|---|')

foreach ($group in $counts) {
    [void]$report.AppendLine("| $($group.Name) | $($group.Count) |")
}

[void]$report.AppendLine()
[void]$report.AppendLine("Total specifications: $($coverage.Count)")
[void]$report.AppendLine()
[void]$report.AppendLine('## Slowest measured cases')
[void]$report.AppendLine()
[void]$report.AppendLine('| case | p50 ms | max ms | samples | status |')
[void]$report.AppendLine('|---|---|---|---|---|')

$slowest = $records |
    Where-Object { $_.result.status -eq 'passed' -and $_.result.wallMs.p50 } |
    Sort-Object { -[double]$_.result.wallMs.p50 } |
    Select-Object -First 20

foreach ($record in $slowest) {
    $wall = $record.result.wallMs
    [void]$report.AppendLine("| $($record.caseId) | $([math]::Round([double]$wall.p50, 3)) | $([math]::Round([double]$wall.max, 3)) | $($wall.count) | $($record.result.status) |")
}

[void]$report.AppendLine()
[void]$report.AppendLine('## Largest measured main thread stalls')
[void]$report.AppendLine()
[void]$report.AppendLine('One row per instrumented stage, taken from the worst single occurrence recorded for it.')
[void]$report.AppendLine()
[void]$report.AppendLine('| stage | worst ms | case | occurrences |')
[void]$report.AppendLine('|---|---|---|---|')

$stalls = @()

foreach ($record in $records) {
    if (-not $record.result.stages) { continue }

    foreach ($stageName in ($record.result.stages | Get-Member -MemberType NoteProperty | ForEach-Object { $_.Name })) {
        $stage = $record.result.stages.$stageName
        $stalls += [pscustomobject]@{
            stage  = $stageName
            maxMs  = [double]$stage.maxMs
            caseId = $record.caseId
            count  = [int]$stage.count
        }
    }
}

foreach ($group in ($stalls | Group-Object stage | Sort-Object { -($_.Group | Measure-Object maxMs -Maximum).Maximum } | Select-Object -First 12)) {
    $worst = $group.Group | Sort-Object maxMs -Descending | Select-Object -First 1
    [void]$report.AppendLine("| $($group.Name) | $([math]::Round($worst.maxMs, 3)) | $($worst.caseId) | $(($group.Group | Measure-Object count -Sum).Sum) |")
}

[void]$report.AppendLine()
[void]$report.AppendLine('## Managed allocation, largest cases')
[void]$report.AppendLine()
[void]$report.AppendLine('Current thread managed allocations only. This is not process memory and not native memory.')
[void]$report.AppendLine()
[void]$report.AppendLine('| case | alloc p50 bytes | alloc max bytes |')
[void]$report.AppendLine('|---|---|---|')

$allocations = $records |
    Where-Object { $_.result.allocBytes -and $_.result.allocBytes.p50 } |
    Sort-Object { -[double]$_.result.allocBytes.p50 } |
    Select-Object -First 10

foreach ($record in $allocations) {
    [void]$report.AppendLine("| $($record.caseId) | $([math]::Round([double]$record.result.allocBytes.p50)) | $([math]::Round([double]$record.result.allocBytes.max)) |")
}

if (-not $allocations) {
    [void]$report.AppendLine('| none | unavailable | unavailable |')
    [void]$report.AppendLine()
    [void]$report.AppendLine('No case reported a managed allocation figure. That is reported as unavailable rather than as zero: see the per-case notes for the reason.')
}

[void]$report.AppendLine()
[void]$report.AppendLine('## Specifications without a result')
[void]$report.AppendLine()

$missing = $coverage | Where-Object { $_.status -ne 'passed' }

if (-not $missing) {
    [void]$report.AppendLine('Every specification produced a result.')
}
else {
    [void]$report.AppendLine('| id | mode | status | reason |')
    [void]$report.AppendLine('|---|---|---|---|')

    foreach ($entry in $missing) {
        [void]$report.AppendLine("| $($entry.id) | $($entry.mode) | $($entry.status) | $($entry.reason) |")
    }
}

$report.ToString() | Set-Content -Path (Join-Path $Results 'report.md') -Encoding UTF8

Write-Host "aggregated $($records.Count) results over $($coverage.Count) specifications into $Results"
