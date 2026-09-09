# World generation performance harness

This is the reproducible measurement bench for the world generator, built against the
`865a6ed75bbe9a96ac8a9f5615ceb2762dd5c23c` slice and the specification in
`Promts/WorldGenerator_Performance_TestPlan_865a6ed.md`. It measures the existing pipeline; it
does not change it. The only edits inside `Assets/_Dustborn/Scripts/WorldGeneration` are
read-only probes.

## What is where

| Path | Purpose |
|---|---|
| `Assets/_Dustborn/Scripts/WorldGeneration/Diagnostics/` | `WorldGenProbe`: static `ProfilerMarker`s, a preallocated span and queue-event buffer, latched milestones |
| `Assets/_Dustborn/Benchmarks/WorldGeneration/` | the benchmark bridge, profiles, frozen fixtures, the operation registry and the runtime scenario runner |
| `Assets/_Dustborn/Benchmarks/WorldGeneration/Editor/` | fixture builder, benchmark scene factory and the batch-mode entry points |
| `Assets/_Dustborn/Tests/WorldGeneration/` | the EditMode, PlayMode and shared test assemblies |
| `Tools/WorldGenPerf/` | `run.ps1`, `aggregate.ps1`, `compare.ps1`, profiles, schema and the catalogue dumper |
| `Tools/UnityCompileCheck/` | two projects that model the real assembly split: `UnityCompileCheck.Runtime.csproj` for `Assembly-CSharp` (Editor folders excluded) and `UnityCompileCheck.Editor.csproj` for `Assembly-CSharp-Editor` |
| `Tools/UnityTestCompileCheck/` | type-checks the test assemblies against the real Unity, NUnit and Performance Testing DLLs |
| `BenchmarkResults/<suite>-<profile>/` | raw results, never committed |
| `Benchmarks/WorldGeneration/Baselines/` | small versioned baseline summaries |

## Why a bridge and not an assembly definition

`Assets/_Dustborn/Scripts/WorldGeneration` has no `.asmdef`, so the generator lives in
`Assembly-CSharp`, and a test `.asmdef` cannot reference a predefined assembly. Splitting the
generator into its own assembly would drag `DistanceUtility`, NaughtyAttributes and the
Repetitionless editor API along with it, which is a project-wide change this task is not allowed
to make.

So the harness uses the other option the plan allows: the benchmark code sits **next to** the
production code inside `Assembly-CSharp` (`Assets/_Dustborn/Benchmarks/`), and the test
assemblies bind to it **once, in setup**, through `Dustborn.WorldGen.Testing.WorldGenBridge`.
`WorldGenBridge` resolves `WorldGenBench.Run`, `WorldGenBenchRecorder.Record` and the runtime
facade into pre-bound delegates; nothing inside a measured region ever touches reflection.

## The compile check had to be split, and that is not cosmetic

`Tools/UnityCompileCheck` used to compile the whole of `WorldGeneration` into one assembly,
which cannot see the boundary Unity actually enforces: `Assets/**/Editor/**` becomes
`Assembly-CSharp-Editor`, and `Assembly-CSharp` may not reference it. Anything in
`Assembly-CSharp` that names `GeneratedAssetFile` or `WorldGenerationBake` compiles there and
fails in Unity with `CS0103`, and the only way to find out was an editor recompile.

So it is now two projects. `UnityCompileCheck.Runtime.csproj` excludes every `Editor` folder and
stands for `Assembly-CSharp`; `UnityCompileCheck.Editor.csproj` compiles only the `Editor`
folders and references the first, standing for `Assembly-CSharp-Editor`. The benchmark ops that
need the editor-only generator types live in
`Assets/_Dustborn/Benchmarks/WorldGeneration/Editor/WorldGenBenchOpsEditor.cs` and register
themselves into `WorldGenBench` through `WorldGenBench.Extend` under `[InitializeOnLoad]`,
because the registry itself sits on the runtime side of that boundary.

## Instrumentation contract

- Time is `Stopwatch.GetTimestamp`, converted with a single stored `MsPerTick`.
- Every stage is a static `ProfilerMarker` with a fixed name under `WorldGen.Map.*`,
  `WorldGen.Bake.*`, `WorldGen.Preview.*`, `WorldGen.Runtime.*`, `WorldGen.Voxel.*`,
  `WorldGen.Collider.*`, `WorldGen.Decor.*`, `WorldGen.POI.*`, `WorldGen.Cleanup.*`.
- Aggregate counters (count, total, max) run always. The detailed span and queue buffers only
  fill while `WorldGenProbe.Capture` is set, and they are preallocated: an overflow sets
  `Overflow` and the trace is reported as incomplete rather than silently truncated.
- `Complete`/`Drain` are measured as **caller blocking time**, not as job duration. Worker
  duration is not claimed anywhere: there is no supported instrumentation for it here, so it is
  reported as unavailable rather than substituted with schedule-to-collect.
- Milestones are latched timestamps, and a milestone that was never reached reports `null`, not
  zero. `PRE-03` asserts exactly that.
- `ProfilerRecorder` handles are checked for `Valid` and disposed; a counter the platform does
  not provide is reported as unavailable.

## Profiles

`Tools/WorldGenPerf/profiles/profiles.json` documents them, and
`WorldGenBenchProfile` implements them by **cloning** the serialised assets of this commit. The
source assets are never written: every run works on `Instantiate`d copies with
`HideFlags.HideAndDontSave`, and `PRE-02` asserts that the sources survive unchanged.

- `S512` carries every override of the existing `WorldMapPipelineChecks`, biome relief included,
  not merely a smaller world.
- `M2048` is the daily representative benchmark.
- `P8192` is the production profile with no simplification.
- `SYN` is the synthetic fixture set for single algorithms.

Extra overrides are passed per case as `set.<target>.<member>=<value>`, for example
`set.config.HubCount=16` or `set.voxels.ChunkSize=64`.

## Running it

The launcher needs the Unity editor path, either as `-UnityPath` or in the `UNITY_EDITOR`
environment variable; without either it probes the usual hub locations for the version in
`ProjectSettings/ProjectVersion.txt`. **The Unity editor must not have the project open** —
a second instance cannot take the project lock.

```powershell
./Tools/WorldGenPerf/run.ps1 -Suite Preflight -Profile S512 -Output BenchmarkResults/preflight
./Tools/WorldGenPerf/run.ps1 -Suite Smoke     -Profile S512 -Output BenchmarkResults/smoke
./Tools/WorldGenPerf/run.ps1 -Suite Kernels   -Profile M2048 -Output BenchmarkResults/kernels
./Tools/WorldGenPerf/run.ps1 -Suite Bake      -Profile P8192 -Output BenchmarkResults/bake
./Tools/WorldGenPerf/run.ps1 -Suite Runtime   -Profile P8192 -Output BenchmarkResults/runtime
./Tools/WorldGenPerf/run.ps1 -Suite Lifecycle -Profile M2048 -Output BenchmarkResults/lifecycle
./Tools/WorldGenPerf/compare.ps1 -Baseline BenchmarkResults/baseline -Candidate BenchmarkResults/candidate
```

`run.ps1` owns a process watchdog, because a coroutine timeout cannot stop a blocked main
thread: on `-TimeoutMinutes` it kills the Unity process, keeps the log and reports `timeout`.
A missing result XML, a non-zero exit code or **zero discovered tests** are all failures.

Graphics suites (`Bake`, `Runtime`, `Lifecycle`, `BuildPlayer`) run without `-nographics`;
CPU kernel suites run with it. The split is enforced by category, not by hope: every case carries
`WorldGen.<mode>` alongside its priority and family, and `Kernels` selects `WorldGen.EM` while
`Bake` selects `WorldGen.ED`.

That distinction is load bearing. Repetitionless bakes its texture arrays on a **compute
shader**, so under the Null graphics device `MAT-05` and `MAT-06` fail with
`ArgumentException: Kernel 'CSMain' not found` — a property of the run, not of the code. Running
them in the headless suite would have recorded a false failure.

## The catalogue

All 108 specifications of section four of the plan live in `WorldGenBenchCatalog`, each with its
priority, execution mode, operation and case arguments. `aggregate.ps1` joins them with the raw
results and writes `coverage.csv` where **every** identifier appears, including the ones that
never ran, with a reason. A specification with no result is `not_run`; it is never a pass.

`Tools/WorldGenPerf/CatalogDump` prints the same catalogue without Unity, which is how
`coverage.csv` can be prepared and checked on a machine with no editor.

## Result files

| File | Content |
|---|---|
| `run_manifest.<mode>.json` | run id, mode, source and harness commit, command line |
| `environment.<mode>.json` | Unity version, backend, Burst, job workers, CPU, GPU, quality, timer frequency |
| `catalog.csv` | the 108 specifications with their cases |
| `results.jsonl` | one line per executed case, the raw record |
| `coverage.csv` | every specification with its status and reason |
| `operations.csv` | per-case stage timings and work counts |
| `frames.csv` | frame intervals for the runtime scenarios, inside `results.jsonl` as `framesCsv` |
| `output_fingerprints.json` | output hashes per case |
| `unity_tests.xml` | the real Unity Test Runner result |
| `comparison.json` | gate decisions |
| `report.md` | the readable summary |

## Comparison policy

`compare.ps1` implements the starting policy of section six: a regression is a candidate only
when the relative **and** the absolute rule are both exceeded. Anything with fewer than the
required independent samples is `inconclusive`; a changed fixture hash, a changed work count, a
non-passing status or a case missing on one side is `non_comparable`. Neither is a pass.

The script has been exercised against an injected regression, a truncated sample count, a
changed fixture hash, a changed work count and a case missing from the candidate; each produced
the expected decision and the regression set a non-zero exit code.

## Deliberately blocked cases

Three specifications refuse to run by default, and each says so with its own reason rather than
quietly shrinking:

- `IO-08` (full editor bake) writes into the project's own `Generated` folder. It needs
  `allowSourceWrite=true`, so a batch suite can never overwrite a developer's world by accident.
- `VOX-10` and `LIFE-06` (whole-world preview) print the measured estimate — at the current
  settings 1.3 M triangles, 0.7 M vertices and 70 MB with colliders over 448 columns against a
  512 MiB geometry budget — and need `allowFullPreview=true`. The catalogue carries that second
  case explicitly, so the `Bake` suite measures the real build as well as the refusal.

## Known limits of this harness

- Worker-thread job duration is not measured. Only schedule cost and caller blocking time are.
- **Managed allocation came back unavailable on this runtime.** In the Unity 6000.3.20f1 Mono
  editor, `GC.GetAllocatedBytesForCurrentThread` does not move, so every delta was zero. The
  sampler probes the counter once with a deliberate one-megabyte allocation and, when it does not
  move, reports `allocBytes` as `null` with a note instead of publishing a column of zeros. To
  measure allocation on this project, use the Memory Profiler in a separate diagnostic run, or a
  Player where the counter is implemented.
- A synchronous peak of process memory is not proven from a before and after pair; where only
  those two exist they are labelled as such.
- The headless `Tools/HeadlessCheck` stubs remain a compile and behaviour check of the
  algorithms. Its stage timings are single threaded and Burst free; they are never a Unity
  baseline and are not mixed into these results.
