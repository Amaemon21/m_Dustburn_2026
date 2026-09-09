# Baseline 865a6ed7, profile S512

The first reference run of the world generation harness. It is a **smoke and kernel** baseline:
`S512` is the correctness profile carried over from `WorldMapPipelineChecks`, so its absolute
numbers describe a 512 m world, not the shipping one. Use it to detect that something moved, and
`M2048` or `P8192` to say how much it costs in the real world.

## Environment

| | |
|---|---|
| source commit | `865a6ed75bbe9a96ac8a9f5615ceb2762dd5c23c` |
| Unity | 6000.3.20f1, Mono, development build, batch mode |
| CPU | Intel Core i7-11700K, 16 logical cores, 3.6 GHz |
| RAM | 32 603 MiB |
| OS | Windows 11 (10.0.26200) |
| Burst | enabled |
| job workers | 15 of a 128 thread maximum |
| quality level | PC |
| timer | 10 MHz, high resolution |
| graphics | Null device for `WorldGen.EM`, real device for `WorldGen.ED` |

## Protocol

- `WorldGen.EM` cases: 1 warmup and 3 measured samples per case, one process, `-nographics`.
- `WorldGen.ED` cases: 1 sample per case, one process, graphics enabled.
- Three measured samples are enough to see a stage move; they are **not** enough for a strict
  gate. `compare.ps1` reports anything under five samples as `inconclusive`, which is the correct
  answer for this baseline, and the reason the sample count has to rise before it gates anything.

## How to reproduce

```powershell
$env:UNITY_EDITOR = "<path to Unity 6000.3.20f1>\Editor\Unity.exe"
./Tools/WorldGenPerf/run.ps1 -Suite Kernels -Profile S512 -Output BenchmarkResults/baseline -Warmup 1 -Samples 3 -SkipAggregate
./Tools/WorldGenPerf/run.ps1 -Suite Bake    -Profile S512 -Output BenchmarkResults/baseline -Warmup 0 -Samples 1
```

`coverage.csv` and `report.md` of that run are the files worth keeping here. Raw traces, frame
tables and profiler captures stay out of the repository.

## What this baseline recorded

| | |
|---|---|
| EditMode cases discovered and passed | 153 of 153 |
| editor-process cases | 12 passed, 3 deliberately blocked |
| specifications covered | 74 passed, 2 `blocked_resource`, 1 `blocked_dependency`, 31 `not_run` |
| `not_run` | the 24 PlayMode and Player specifications, which were out of this session's scope |

## The baseline compared against a repeat of itself

`self-comparison.json` is `compare.ps1` run over this baseline and an independent repeat of it,
which is the check that has to pass before the policy is pointed at an optimisation.

- **0 regression candidates and 0 improvements.** No false positives.
- 164 cases `inconclusive`, correctly: three measured samples are below the five the gate demands.
- 4 cases `non_comparable`: three because a status is not `passed` on both sides, one because a
  work count moved. That fourth one was a harness defect and is fixed — `HGT-08` was filing a
  managed heap reading as a work count, so two identical runs looked like different work.

Worst drift between the two identical runs, for calibration: 3.7% on `MAT-05`, 1.4% on `VOX-10`,
0.5% on `MAT-06`, and 26% on `IO-04` — the last three being single-sample editor-process cases
where filesystem timing dominates. A 10% stage gate is therefore plausible for the multi-sample
kernels and clearly too tight for single-sample IO cases; raise their sample count before gating
them.
