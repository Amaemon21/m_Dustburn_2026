<#
.SYNOPSIS
    Compares two world generation performance runs and applies the starting gate policy.

.DESCRIPTION
    A regression is only a candidate when the relative and the absolute rule are both
    exceeded. Anything with too few samples, a changed fixture hash or a changed work
    count is reported as inconclusive or non_comparable rather than as a pass.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Candidate,
    [string]$Output = '',
    [double]$StagePercent = 10,
    [double]$StageFloorMs = 0.5,
    [double]$E2EFloorMs = 5,
    [int]$MinimumSamples = 5,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Read-Run {
    param([string]$Directory)

    $path = Join-Path $Directory 'results.jsonl'

    if (-not (Test-Path $path)) {
        throw "no results.jsonl in $Directory"
    }

    $map = @{}

    foreach ($line in Get-Content $path) {
        if (-not $line.Trim()) { continue }
        $record = $line | ConvertFrom-Json
        $map[$record.caseId] = $record
    }

    return $map
}

$Baseline = (Resolve-Path $Baseline).Path
$Candidate = (Resolve-Path $Candidate).Path
if (-not $Output) { $Output = Join-Path $Candidate 'comparison.json' }

$before = Read-Run $Baseline
$after = Read-Run $Candidate

$rows = @()

foreach ($caseId in ($before.Keys + $after.Keys | Sort-Object -Unique)) {
    $left = $before[$caseId]
    $right = $after[$caseId]

    if (-not $left -or -not $right) {
        $rows += [pscustomobject]@{
            caseId     = $caseId
            decision   = 'non_comparable'
            reason     = if (-not $left) { 'missing in the baseline' } else { 'missing in the candidate' }
            baselineMs = $null
            candidateMs = $null
            deltaPct   = $null
        }
        continue
    }

    if ($left.result.fixtureHash -ne $right.result.fixtureHash) {
        $rows += [pscustomobject]@{
            caseId     = $caseId
            decision   = 'non_comparable'
            reason     = 'the fixture hash changed'
            baselineMs = $left.result.wallMs.p50
            candidateMs = $right.result.wallMs.p50
            deltaPct   = $null
        }
        continue
    }

    if ($left.result.status -ne 'passed' -or $right.result.status -ne 'passed') {
        $rows += [pscustomobject]@{
            caseId     = $caseId
            decision   = 'non_comparable'
            reason     = "status $($left.result.status) against $($right.result.status)"
            baselineMs = $left.result.wallMs.p50
            candidateMs = $right.result.wallMs.p50
            deltaPct   = $null
        }
        continue
    }

    $workChanged = $false
    $workReason = ''

    foreach ($name in ($left.result.work | Get-Member -MemberType NoteProperty | ForEach-Object { $_.Name })) {
        $leftValue = $left.result.work.$name
        $rightValue = $right.result.work.$name

        if ($null -eq $rightValue) { continue }
        if ($name -like '*Ms') { continue }

        if ([math]::Abs([double]$leftValue - [double]$rightValue) -gt 1e-6) {
            $workChanged = $true
            $workReason = "work count '$name' moved from $leftValue to $rightValue"
            break
        }
    }

    if ($workChanged) {
        $rows += [pscustomobject]@{
            caseId     = $caseId
            decision   = 'non_comparable'
            reason     = $workReason
            baselineMs = $left.result.wallMs.p50
            candidateMs = $right.result.wallMs.p50
            deltaPct   = $null
        }
        continue
    }

    $baseMs = [double]$left.result.wallMs.p50
    $candMs = [double]$right.result.wallMs.p50
    $samples = [math]::Min([int]$left.result.wallMs.count, [int]$right.result.wallMs.count)

    $floor = if ($caseId -like 'E2E-*') { $E2EFloorMs } else { $StageFloorMs }
    $absolute = $candMs - $baseMs
    $relative = if ($baseMs -eq 0) { $null } else { 100 * $absolute / $baseMs }

    $decision = 'passed'
    $reason = ''

    if ($samples -lt $MinimumSamples) {
        $decision = 'inconclusive'
        $reason = "only $samples samples, the gate needs $MinimumSamples"
    }
    elseif ($absolute -gt $floor -and ($null -eq $relative -or $relative -gt $StagePercent)) {
        $decision = 'regression_candidate'
        $reason = "slower by $([math]::Round($absolute, 3)) ms"
    }
    elseif ($absolute -lt -$floor -and ($null -eq $relative -or $relative -lt - $StagePercent)) {
        $decision = 'improvement'
        $reason = "faster by $([math]::Round(-$absolute, 3)) ms"
    }

    $rows += [pscustomobject]@{
        caseId      = $caseId
        decision    = $decision
        reason      = $reason
        baselineMs  = $baseMs
        candidateMs = $candMs
        deltaMs     = $absolute
        deltaPct    = $relative
        samples     = $samples
    }
}

if ($SelfTest) {
    $synthetic = $rows | Select-Object -First 1

    if ($synthetic) {
        $fake = $synthetic.PSObject.Copy()
        $fake.candidateMs = $fake.baselineMs * 2 + 10
        $fake.deltaMs = $fake.candidateMs - $fake.baselineMs
        $fake.deltaPct = 100
        $fake.decision = 'regression_candidate'
        $fake.reason = 'synthetic regression injected by -SelfTest'
        $fake.caseId = $fake.caseId + '.selftest'
        $rows += $fake
    }
}

$summary = [pscustomobject]@{
    schemaVersion = 1
    baseline      = $Baseline
    candidate     = $Candidate
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('o')
    policy        = [pscustomobject]@{
        stagePercent   = $StagePercent
        stageFloorMs   = $StageFloorMs
        e2eFloorMs     = $E2EFloorMs
        minimumSamples = $MinimumSamples
    }
    cases         = $rows
    counts        = ($rows | Group-Object decision | ForEach-Object { [pscustomobject]@{ decision = $_.Name; count = $_.Count } })
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $Output -Encoding UTF8

foreach ($group in ($rows | Group-Object decision | Sort-Object Name)) {
    Write-Host "$($group.Name): $($group.Count)"
}

$regressions = $rows | Where-Object { $_.decision -eq 'regression_candidate' }

foreach ($row in $regressions) {
    Write-Warning "$($row.caseId): $($row.reason)"
}

Write-Host "comparison written to $Output"

if ($regressions -and -not $SelfTest) { exit 1 }
exit 0
