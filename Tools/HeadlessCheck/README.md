# HeadlessCheck

Compiles and runs the world generation layers without Unity.

- `Stubs.cs` — Unity API stubs (UnityEngine, UnityEditor, NaughtyAttributes). `Mathf`, `Vector2` and `Vector3` follow Unity's own code; in the default mode their bodies are replaced by Unity's IL.
- `JobStubs.cs` — Unity.Burst, Unity.Jobs and Unity.Collections stubs, so jobs and terrain painters compile; `Schedule` runs `Execute` in a loop, single-threaded.
- `Missing.cs` — placeholders for the Burst and Unity.Mathematics generators the stubs do not cover.
- `AssetReader.cs` — reads the real `.asset` files (the config with its nested settlement profiles, the biomes, the PoiDatabase) instead of code defaults.
- `MonoRewrite.cs` — runs the harness the way the Unity editor computes: rewrites its own IL so managed float math follows Mono, keeps Burst jobs strict and copies `Mathf`/`Vector2`/`Vector3` bodies from Unity. On by default; `--net` turns it off. `Unity.Mathematics` itself is compiled from the package source.
- `Harness.cs` — the entry point: biome map, heights, `HubPlacer`, `RegionalGraphPlanner`, `SettlementPlanner` (tiles, topology, streets), settlement terrain, `RoadPlanner`, `DirtAccessPlanner`, carving, lots, `PoiPlacer`, pads, then `RoadNetworkDiagnostics` and `RoadAudit`.
- `WorldMapPipelineChecks.cs` — drives the real `WorldMapPipeline` on a tiny world.
- `RoadTopologyChecks.cs` — road and settlement unit checks, then small and medium worlds over three seeds.
- `RoadAudit.cs` — a geometric audit of a road network that reads the same for an exported `RoadNetwork.asset` and a generated world.
- `RoadPaintChecks.cs`, `GroundAppearanceChecks.cs`, `GroundPreview.cs` — the ground paint contracts and the top-down preview.
- `VoxelSeamChecks.cs` — `--voxel-seams`: meshes LOD pairs on all four sides, ring corners and whole six-ring sets on synthetic terrain, and fails on any crack, hole, overlap, stray vertex, spike, vertical wall, or a skirt that shows, sits on the coarse side or is doubled. Exit code 1 on failure.
- `WorldFeatureChecks.cs`, `WaterChecks.cs` — `--stamps` (RAW16 format, sampling, add/subtract, rotation, edges, ceiling, deterministic placement) and `--water` (monotone rivers, flow and width downstream, flat lakes with a bed, no NaN, determinism, serialization, meshes, queries); `--grass-density` lives in `Harness.cs`.
- `WaterAudit.cs`, `WaterShoreChecks.cs`, `WaterView.cs` — `--water-audit`: artifact audit of the water mesh against the ground and the LOD rings, shoreline checks (fragments, holes, grid-aligned shoreline, waterline accuracy, spikes, mouth gaps, river cross-sections, T-junctions), debug maps and a software renderer for top-down and oblique views.
- `Draw.cs`, `Png.cs` — PNG rendering.
- `BakeParity.cs` — parity with the Unity bake: heights, water, roads and POI against `Generated/`, stamp traces against `WaterStampDatabase.asset`, the exact `.f32` relief cache and a per-stage height trace.

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
dotnet run -c Release -- --stamps
dotnet run -c Release -- --water
dotnet run -c Release -- --water-audit --generate --raw-cache raw.bytes --profile
dotnet run -c Release -- --water-audit --generate --raw-cache raw.bytes --water-debug out --view views [views/scenes.txt] [--view-lod 700] [Seed=123]
dotnet run -c Release -- --grass-density
dotnet run -c Release -- --water-audit --pipeline --stamped --raw-cache relief.f32 --compare-bake diff.png
dotnet run -c Release -- --water-audit --pipeline --stamped --raw-cache relief.f32 --stage-trace 4316 4899 4330 4899
dotnet run -c Release -- --stamp-parity
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

## Ground modes

`-- --splat` (`SplatChecks.cs`): bounds, determinism, tiles in any order and size, independent patches, road and cliff priority, relief/slope/macro response, no 8 m grid in weights or hydraulic cut. `-- --road-paint` (`RoadPaintChecks.cs`) paints straight roads at seven angles over 2 m control texels and fails if the 50% edge line wanders more than 10 cm, the paint leaves its line or a junction leaves a gap. `-- --ground-appearance` (`GroundAppearanceChecks.cs`) pins the splat contract on tiny synthetic worlds: stacking by opacity, biome cliffs, road priority, full coverage, tile continuity, and that `DecorFilter.SurfaceWeight` matches the painted border to 0.003. `-- --ground-preview <dir>` (`GroundPreview.cs`) is the eye: it reads the real `Ground` and `Grass` lists, `.terrainlayer` files and textures, decodes `Generated/HeightMap.bytes`, `BiomeMap.png` and `RoadMask.png`, reads the road polylines from `RoadNetwork.asset`, runs the real `GroundSplatPainter` at `ControlResolution`, and composites the textures top-down with hillshade — a 384 m and a 72 m view of every biome pair's border (plus one with grass tufts from the real `VoxelDecorPlacer`), a 1024 m view of each biome's interior, and `grass_swatches.png` with every card recoloured over its biome's base ground. It also prints layer shares and slope percentiles per biome, and takes `Name=value` overrides; `--heights <raw>` swaps the height map and `--dump <x> <z> <size>` writes one window's weights as raw bytes. About 16 seconds. It does not emulate Repetitionless anti-tiling, so a regular lattice in the far views is the preview, not the game.

## Parity with Unity

`--compare-bake [diff.png]` after `--water-audit --pipeline --stamped` prints the mean and largest height difference, the share of cells whose 16-bit height is identical, every river and body whose points, levels or areas differ, how many highways, streets and buildings landed in the same place, and where the larger height differences sit (under roads, beside buildings, in water or elsewhere). `diff.png` colours differences (blue under 1 m, orange under 5 m, red above) and `diff_steps.png` shows where the 16-bit heights differ at all; a skewed lattice there points at noise, a valley network at water.

A `--raw-cache` path ending in `.f32` stores the relief as raw floats. A raw16 cache rounds to 6 mm, and hydrology is sensitive enough that this alone moves lakes, so only the `.f32` cache reproduces the Unity world. `--stamp-parity` retraces every water stamp mask and lists where the trace differs from the one stored in the asset; `--stamped` uses the stored traces either way. `--stage-trace x z ...` runs the real pipeline stage by stage and prints the heights at the given points after each one, plus the roads within 80 m of the first point.

## Metrics of the default run

It prints biome area fractions, height range, mean and maximum slope, area steeper than 28Â°, `ReportBumps`, `CheckShoulders`, `CheckCurvature`, then road, street, lot and connectivity metrics and per-stage timings (`Stage` / `ReportStages`).

- **`CheckCurvature`** resamples the polyline at 30 m **before** measuring the angle between adjacent segments, or the metric would depend on point density rather than shape. Prints mean turn per 30 m, maximum, the fraction over 25°, and network length.
- **`ReportSightlines`** answers "how far can the player see": 24 dry viewpoints, 16 rays each at 1.7 m eye height, marched in 8 m steps keeping the largest elevation angle; it prints median and mean horizon, the share closed inside 300 m and open at the 2 km ceiling (a floor — rays stop at the map edge).
- **`ReportBumps`** counts local maxima on a grid of a given step — the direct answer to "too many small hills", which mean slope cannot show. 40 m catches the small stuff, 120 m real hills; the ratio shows what the terrain is noisy with.
- **`CheckShoulders`** measures the drop from roadbed edge to the foot of the embankment. Measure it **before** POI pads are carved — they sit 6.5 m from the axis — which is why the harness prints highways, streets and streets-with-buildings separately.
- **`CheckSpacing`** measures, for streets and highways separately, the fraction of a road's length where the nearest **parallel** road lies between 1.5 m and `StreetHalfWidth + RoadHalfWidth + 12` m — close enough to read as a twin, far enough not to be merged into it. **`CheckHighwaysInSettlements`** samples highways every 8 m, skipping the 40 m before each gateway, and counts samples inside a tile.

## Voxel seams

**`-- --voxel-seams`** (`VoxelSeamChecks.cs`) meshes real column sets: a 2x2 fine block against one coarse column on all four sides at every level pair, a fine quadrant among three coarse in all four rotations, same-level pairs, and the whole six-ring set around three viewers on synthetic rough terrain. It fails on an open edge that does not lie on another triangle, a hole or double cover under a vertical probe, a vertex outside its column, a surface point outside the terrain's range over two voxels, and a skirt that shows from either side, hangs on the coarse side or is doubled. `--real <x> <z>` uses the real map. About 10 seconds.
