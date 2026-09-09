# Baselines

Small versioned summaries only: `coverage.csv`, `report.md` and the `wallMs` rows of
`operations.csv` for a run that is being kept as a reference. Raw traces, profiler captures,
frame tables and built players stay out of the repository.

A baseline is only comparable against a run made with the same harness commit, the same profile,
the same fixture hash and the same machine. `compare.ps1` refuses to compare across a changed
fixture hash and reports `non_comparable`.

Store each baseline in its own folder named `<sourceCommit>-<profile>-<machine>`, with a
`notes.md` recording the environment: CPU, RAM, GPU and driver, OS, Unity revision, scripting
backend, development flag, Burst and safety options, job worker count, quality level, resolution,
VSync and power mode.
