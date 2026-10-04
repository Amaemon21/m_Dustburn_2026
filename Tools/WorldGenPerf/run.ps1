<#
.SYNOPSIS
    Runs a world generation performance suite through the Unity Test Framework.

.DESCRIPTION
    Every suite maps onto a Unity test platform plus a category filter that the
    harness in Assets/_Dustborn/Tests/WorldGeneration declares. The script owns the
    process watchdog, the exit code and the result files: a coroutine timeout cannot
    stop a blocked main thread, so the watchdog kills the process itself.

.EXAMPLE
    ./Tools/WorldGenPerf/run.ps1 -Suite Smoke -Profile S512 -Output BenchmarkResults/smoke
#>
[CmdletBinding()]
param(
    [ValidateSet('Preflight', 'Smoke', 'Kernels', 'Bake', 'Runtime', 'Lifecycle', 'Stress', 'BuildPlayer', 'All')]
    [string]$Suite = 'Smoke',

    [ValidateSet('S512', 'M2048', 'P8192', 'SYN')]
    [string]$Profile = 'S512',

    [string]$Output = '',

    [string]$UnityPath = '',

    [string]$ProjectPath = '',

    [int]$Warmup = 1,

    [int]$Samples = 3,

    [string]$Filter = '',

    [int]$TimeoutMinutes = 60,

    [switch]$SkipAggregate
)

$ErrorActionPreference = 'Stop'

function Resolve-Project {
    param([string]$Given)

    if ($Given) { return (Resolve-Path $Given).Path }
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Resolve-Unity {
    param([string]$Given, [string]$Project)

    $candidates = @()
    if ($Given) { $candidates += $Given }
    if ($env:UNITY_EDITOR) { $candidates += $env:UNITY_EDITOR }

    $version = (Get-Content (Join-Path $Project 'ProjectSettings\ProjectVersion.txt') |
        Where-Object { $_ -like 'm_EditorVersion:*' }) -replace 'm_EditorVersion:\s*', ''
    $version = $version.Trim()

    $candidates += "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    $candidates += "E:\Unity\$version\Editor\Unity.exe"
    $candidates += "D:\Unity\Hub\Editor\$version\Editor\Unity.exe"

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return [pscustomobject]@{ Path = $candidate; Version = $version }
        }
    }

    throw "Unity $version was not found. Pass -UnityPath or set the UNITY_EDITOR environment variable."
}

function Invoke-Unity {
    param(
        [string]$Unity,
        [string[]]$Arguments,
        [string]$LogFile,
        [int]$TimeoutMinutes,
        [hashtable]$Environment
    )

    foreach ($key in $Environment.Keys) {
        Set-Item -Path "Env:$key" -Value $Environment[$key]
    }

    Write-Host "unity $($Arguments -join ' ')"
    $process = Start-Process -FilePath $Unity -ArgumentList $Arguments -PassThru -NoNewWindow

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not $process.HasExited) {
        if ((Get-Date) -gt $deadline) {
            Write-Warning "Timeout after $TimeoutMinutes minutes; killing the Unity process."
            try { $process.Kill() } catch { }
            Start-Sleep -Seconds 5
            return [pscustomobject]@{ ExitCode = 124; TimedOut = $true }
        }
        Start-Sleep -Seconds 2
    }

    $process.WaitForExit()

    $code = $process.ExitCode
    if ($null -eq $code) { $code = 0 }

    return [pscustomobject]@{ ExitCode = $code; TimedOut = $false }
}

$project = Resolve-Project $ProjectPath
$unity = Resolve-Unity $UnityPath $project

if (-not $Output) { $Output = "BenchmarkResults\$Suite-$Profile" }

if (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $project $Output }
$Output = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Suite + '-' + $Profile
$logFile = Join-Path $Output 'unity.log'
$xmlFile = Join-Path $Output 'unity_tests.xml'

$harnessCommit = ''
try { $harnessCommit = (git -C $project rev-parse HEAD).Trim() } catch { }

$common = @{
    'WORLDGENPERF_OUTPUT'         = $Output
    'WORLDGENPERF_RUNID'          = $runId
    'WORLDGENPERF_PROFILE'        = $Profile
    'WORLDGENPERF_WARMUP'         = "$Warmup"
    'WORLDGENPERF_SAMPLES'        = "$Samples"
    'WORLDGENPERF_FILTER'         = $Filter
    'WORLDGENPERF_HARNESS_COMMIT' = $harnessCommit
}

Write-Host "project    $project"
Write-Host "unity      $($unity.Path) ($($unity.Version))"
Write-Host "suite      $Suite"
Write-Host "profile    $Profile"
Write-Host "output     $Output"
Write-Host "runId      $runId"

$plan = switch ($Suite) {
    'Preflight' { @(@{ Kind = 'Method'; Method = 'WorldGenBenchBatch.Preflight'; Graphics = $false }) }
    'BuildPlayer' { @(@{ Kind = 'Method'; Method = 'WorldGenBenchBatch.BuildPlayer'; Graphics = $true }) }
    'Smoke' { @(
            @{ Kind = 'Method'; Method = 'WorldGenBenchBatch.Preflight'; Graphics = $false },
            @{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.Smoke'; Graphics = $false }
        ) }
    'Kernels' { @(
            @{ Kind = 'Method'; Method = 'WorldGenBenchBatch.Preflight'; Graphics = $false },
            @{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.EM'; Graphics = $false }
        ) }
    'Bake' { @(@{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.ED'; Graphics = $true }) }
    'Runtime' { @(@{ Kind = 'Tests'; Platform = 'PlayMode'; Category = 'WorldGen.PL'; Graphics = $true }) }
    'Lifecycle' { @(@{ Kind = 'Tests'; Platform = 'PlayMode'; Category = 'WorldGen.PM'; Graphics = $true }) }
    'Stress' { @(@{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.P2'; Graphics = $false }) }
    'All' { @(
            @{ Kind = 'Method'; Method = 'WorldGenBenchBatch.Preflight'; Graphics = $false },
            @{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.EM'; Graphics = $false },
            @{ Kind = 'Tests'; Platform = 'EditMode'; Category = 'WorldGen.ED'; Graphics = $true },
            @{ Kind = 'Tests'; Platform = 'PlayMode'; Category = 'WorldGen.PM'; Graphics = $true },
            @{ Kind = 'Tests'; Platform = 'PlayMode'; Category = 'WorldGen.PL'; Graphics = $true }
        ) }
}

$failed = $false
$step = 0

foreach ($entry in $plan) {
    $step++
    $stepLog = Join-Path $Output "unity.step$step.log"
    $stepXml = Join-Path $Output "unity_tests.step$step.xml"

    $arguments = @('-projectPath', $project, '-batchmode', '-logFile', $stepLog)

    if (-not $entry.Graphics) { $arguments += '-nographics' }

    if ($entry.Kind -eq 'Method') {
        $arguments += @('-quit', '-executeMethod', $entry.Method,
            '-worldGenPerfProfile', $Profile,
            '-worldGenPerfWarmup', "$Warmup",
            '-worldGenPerfSamples', "$Samples")

        if ($Filter) { $arguments += @('-worldGenPerfFilter', $Filter) }
    }
    else {
        $arguments += @('-runTests', '-testPlatform', $entry.Platform, '-testResults', $stepXml)

        if ($entry.Category) { $arguments += @('-testCategory', $entry.Category) }
        if ($entry.Filter) { $arguments += @('-testFilter', $entry.Filter) }
    }

    $result = Invoke-Unity -Unity $unity.Path -Arguments $arguments -LogFile $stepLog -TimeoutMinutes $TimeoutMinutes -Environment $common

    if ($result.TimedOut) {
        Write-Warning "step $step timed out"
        $failed = $true
        continue
    }

    if ($result.ExitCode -ne 0) {
        Write-Warning "step $step exited with $($result.ExitCode)"
        $failed = $true
    }

    if ($entry.Kind -eq 'Tests') {
        if (-not (Test-Path $stepXml)) {
            Write-Warning "step $step produced no test result file: that is a failure, not a pass"
            $failed = $true
        }
        else {
            [xml]$xml = Get-Content $stepXml
            $total = [int]$xml.'test-run'.total
            Write-Host "step $step discovered $total tests, passed $($xml.'test-run'.passed), failed $($xml.'test-run'.failed), skipped $($xml.'test-run'.skipped)"

            if ($total -eq 0) {
                Write-Warning "step $step discovered zero tests: that is a failure, not a pass"
                $failed = $true
            }

            $broken = [int]$xml.'test-run'.failed
            if ($broken -gt 0) {
                Write-Warning "step $step reported $broken failed tests: that is a failure, not a pass"
                $failed = $true
            }

            Copy-Item $stepXml $xmlFile -Force
        }
    }
}

if (-not $SkipAggregate) {
    & (Join-Path $PSScriptRoot 'aggregate.ps1') -Results $Output
}

if ($failed) {
    Write-Error "suite $Suite did not complete cleanly; see $Output"
    exit 1
}

Write-Host "suite $Suite finished; results in $Output"
exit 0
