# WorldGenPerf launcher

Scripts that drive the world generation performance harness through Unity and turn its raw
output into the report files. Full description: `Documentation/WorldGenPerformance.md`.

| Script | Purpose |
|---|---|
| `run.ps1` | runs one suite in Unity batch mode with a process watchdog |
| `aggregate.ps1` | joins `results.jsonl` with `catalog.csv` into coverage, operations, fingerprints and `report.md` |
| `compare.ps1` | applies the gate policy to two runs and writes `comparison.json` |
| `CatalogDump/` | prints the 108 specifications without Unity |

`run.ps1` needs the editor path in `-UnityPath` or `UNITY_EDITOR`, and the project must not be
open in another Unity instance.

Zero discovered tests, a missing result XML, a non-zero exit code and a watchdog timeout are all
failures, never a pass.
