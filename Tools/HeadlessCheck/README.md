# HeadlessCheck

Compiles and runs the world generation layers without Unity.

- `Stubs.cs` — Unity API stubs (UnityEngine, UnityEditor, Unity.Mathematics.Random, NaughtyAttributes).
- `JobStubs.cs` — Unity.Burst, Unity.Jobs and Unity.Collections stubs, so jobs and terrain painters compile; `Schedule` runs `Execute` in a loop, single-threaded.
- `Missing.cs` — placeholders for the Burst and Unity.Mathematics generators the stubs do not cover.
- `AssetReader.cs` — reads the real `.asset` files (the config with its nested settlement profiles, the biomes, the PoiDatabase) instead of code defaults.
- `SimplexNoise.cs` — an Ashima snoise port standing in for `Unity.Mathematics.noise.snoise`.
- `Harness.cs` — the entry point: biome map, heights, `HubPlacer`, `RegionalGraphPlanner`, `SettlementPlanner` (tiles, topology, streets), settlement terrain, `RoadPlanner`, `DirtAccessPlanner`, carving, lots, `PoiPlacer`, pads, then `RoadNetworkDiagnostics` and `RoadAudit`.
- `WorldMapPipelineChecks.cs` — drives the real `WorldMapPipeline` on a tiny world.
- `RoadTopologyChecks.cs` — road and settlement unit checks, then small and medium worlds over three seeds.
- `RoadAudit.cs` — a geometric audit of a road network that reads the same for an exported `RoadNetwork.asset` and a generated world.
- `RoadPaintChecks.cs`, `GroundAppearanceChecks.cs`, `GroundPreview.cs` — the ground paint contracts and the top-down preview.
- `VoxelSeamChecks.cs` — `--voxel-seams`: meshes LOD pairs on all four sides, ring corners and whole six-ring sets on synthetic terrain, and fails on any crack, hole, overlap, stray vertex, spike, vertical wall, or a skirt that shows, sits on the coarse side or is doubled. Exit code 1 on failure.
- `Draw.cs`, `Png.cs` — PNG rendering.

`Assets/_Dustborn/Scripts/Utils/` is compiled too, and the files under `Assets/_Dustborn/Scripts/Gameplay/Service/WorldService/` are referenced directly rather than copied, so the build catches compile errors in production code.

```
dotnet run -c Release
dotnet run -c Release -- --settlements-only --raw-cache path/to/raw.bytes
dotnet run -c Release -- --settlements-only --raw-cache path/to/raw.bytes --problem-dir problems
dotnet run -c Release -- --pipeline-smoke
dotnet run -c Release -- --road-topology
dotnet run -c Release -- --road-topology --problem-dir problems
dotnet run -c Release -- --road-audit ../../Assets/_Dustborn/Generated/RoadNetwork.asset
dotnet run -c Release -- --road-paint
dotnet run -c Release -- --ground-appearance
dotnet run -c Release -- --ground-preview out
dotnet run -c Release -- --voxel-seams
dotnet run -c Release -- --voxel-seams --real 2036 4577
```

- The default run generates the whole world and prints relief, sightlines, voxel, decor, settlement, road and POI metrics with per-stage timings. Read the timings as "where is it expensive at all", not as a Unity measurement: the stub runs jobs single-threaded and without Burst, so terrain and biomes are inflated.
- `--settlements-only` stops after the buildings, skipping voxels and decor. `--raw-cache` saves the raw height map on the first run and reads it on the next ones; relief is nearly all of a run. The cache does not know which relief settings made it, so delete it after touching relief or the seed.
- `--problem-dir <dir>` renders a zoom of the first problems of every violation kind and prints, per problem, the route record, the gateway description, the crossing segments, a curve trace, or a grade trace of every road near the point taken from the carver's own profiles (`TerrainCarver.Profiles`) in carve order.
- `--pipeline-smoke` runs the real pipeline twice on a 512 m world and requires identical results, working cancellation and zero hard road violations.
- `--road-topology` fails on a fingerprint mismatch between two runs of one seed, on any hard violation, on an implausible network, or on a medium world slower than 120 s. It writes `road_topology_small.png` and `road_topology_medium.png`; with `--problem-dir <dir>` a failing world lists its first problems of each kind, traces them and writes zooms named `<world>_<seed>_<kind>_<n>.png`.
- `--road-audit` measures an exported network: roads and kilometres by kind, nodes, junctions, components, crossings away from road ends, parallel runs and turn per 30 m.

A full run writes `unity_world.png`, `road_topology.png` (tiles by district, roads by kind, junctions, gateways and red circles on problems), `road_mask_preview.png` and the settlement zooms next to the project. PNG files are ignored by git.

Any `WorldGenerationConfig` field is overridden by a `Name=value` argument, so layouts can be tuned without Unity:

```
dotnet run -c Release -- --settlements-only --raw-cache raw.bytes TileSize=120 TargetLinkRatio=1.4
dotnet run -c Release -- --settlements-only --raw-cache raw.bytes MaxRoadFill=8 HighwayMinCurveRadius=60
```

The stub does not replace Unity: it checks types and arithmetic, not engine behaviour. Anything that depends on textures, meshes or asset import is empty here.
