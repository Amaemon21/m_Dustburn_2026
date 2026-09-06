# CLAUDE.md

Instructions for Claude Code when working in this repository.

> A description of the game itself (what it is, what the gameplay is) is not filled in yet — to be added later.

## Environment

- **Unity 6000.3.20f1** (Unity 6.3), see `ProjectSettings/ProjectVersion.txt`
- **Render Pipeline:** URP (`com.unity.render-pipelines.universal` 17.3.0)
- **Input:** new Input System (`com.unity.inputsystem` 1.19.0)
- **Language:** C# (no `.asmdef` — all code lives in `Assembly-CSharp`)

## Stack and dependencies

### Unity Package Manager (`Packages/manifest.json`)

| Package | Version / source | Purpose |
|---|---|---|
| `com.github-glitchenzo.nugetforunity` | git (GlitchEnzo/NuGetForUnity) | Installing NuGet packages inside Unity |
| `com.cysharp.r3` | git (Cysharp/R3) | Next-gen reactive extensions (UniRx replacement) |
| `com.cysharp.unitask` | git (Cysharp/UniTask) | Allocation-free async/await for Unity |
| `com.arongranberg.astar` | 5.4.6 (embedded) | A* Pathfinding Project Pro — pathfinding, `FollowerEntity`, RVO |
| `com.unity.ai.navigation` | 2.0.13 | Built-in NavMesh (present, but navigation goes through A*) |
| `jp.hadashikick.vcontainer` | git 1.19.0 (hadashiA/VContainer) | DI container (`LifetimeScope`, `[Inject]`, `IObjectResolver`) |
| `com.unity.entities` | 1.4.8 | DOTS/ECS (pulled in by A*) |
| `com.williamschack.repetitionless` | 1.7.2 Free (embedded) | Removes visible texture tiling in the shader |

A* Pathfinding Project Pro and Repetitionless are **embedded packages** in `Packages/`, not in `Assets/`.

NaughtyAttributes is source-dropped into `Assets/Assets/NaughtyAttributes/` (not via UPM) — inspector attributes `[BoxGroup]`, `[HorizontalLine]` and so on.

Art content is Synty packs under `Assets/Synty/`: `PolygonNature` (grounds, grass, plants), `PolygonNatureBiomes/PNB_Tropical_Jungle` (ready-made `.terrainlayer` files as a settings reference), `PolygonApocalypse` (buildings and apocalyptic vegetation), `PolygonGeneric`, `SidekickCharacters`.

### NuGet (NuGetForUnity)

Config — `Assets/NuGet.config`, list — `Assets/packages.config`, binaries — `Assets/Packages/`.

- `R3` 1.3.1 — R3 core
- `ObservableCollections` / `ObservableCollections.R3` 3.3.4 — reactive collections
- `Microsoft.Bcl.AsyncInterfaces` 6.0.0, `Microsoft.Bcl.TimeProvider` 8.0.0, `System.Threading.Channels` 8.0.0, `System.ComponentModel.Annotations` 5.0.0 — transitive R3 dependencies

**Important:** install new NuGet dependencies only through the NuGetForUnity window (`NuGet → Manage NuGet Packages`), so that `packages.config` and the `.meta` files are updated correctly. Never drop files into `Assets/Packages/` by hand.

## Code layout

```
7DTDRef/
├── 7dtd-biomes-reference.jpg         # biomes.png reference from 7 Days to Die, palette matched against it
└── prototype-*.png                   # prototype-phase check renders: biomes, hillshade, roads, city, village

Tools/HeadlessCheck/                  # runs the whole pipeline without Unity: Stubs.cs, JobStubs.cs,
                                      # SimplexNoise.cs, AssetReader.cs, Harness.cs, Draw.cs, Png.cs
Tools/UnityCompileCheck/              # type-checks WorldGeneration against the real Unity DLLs

Assets/_Dustborn/Scripts/Utils/
└── DistanceUtility.cs                # SqrDistance / WithinRadius / GetClosest — distance comparison without sqrt

Assets/_Dustborn/
├── Content/
│   ├── Configs, Material, Prefabs    # Unit.prefab, UnitConfig.asset
│   ├── TerrainLayers/                # 14 layers: grounds, rock, road. Budget is MaxTerrainLayers; Repetitionless Pro allows 32
│   └── World/                        # WorldGenerationConfig, BiomeDatabase + Biomes/, PoiDatabase + Pois/ (32 of them)
├── Generated/                        # generator output: BiomeMap.png, HeightMap.bytes, HeightMapPreview.png, RoadMask.png, RoadNetwork.asset, PoiPlacement.asset, VoxelMaterial.mat + its *_RepetitionlessData/
├── Resources/
│   └── UnitDatabaseConfig.asset      # loaded via UnitDatabaseConfig.RESOURCES_PATH
└── Scripts/
    ├── PlayerController.cs
    ├── UnitService/
    │   ├── UnitLifetimeScope.cs      # VContainer LifetimeScope: player, config database, spawn points, UnitFactory, UnitSpawnService
    │   ├── UnitFactory.cs            # assembles UnitView + services + UnitController
    │   ├── UnitController.cs         # plain C# class: unit state machine, IDisposable
    │   ├── UnitHealth.cs             # IDamageable, health
    │   ├── Data/
    │   │   ├── UnitConfig.cs         # SO with unit settings (detection, chase, movement, RVO, health)
    │   │   ├── UnitDatabaseConfig.cs # SO: list of UnitConfig, lookup by UnitType
    │   │   ├── UnitState.cs          # state enum (Wander, Chase, Dead)
    │   │   └── UnitType.cs           # unit type enum
    │   ├── Interfaces/
    │   │   └── IDamageable.cs
    │   ├── Services/                 # plain C# classes, not MonoBehaviour
    │   │   ├── UnitSpawnService.cs           # IStartable: drives spawning across all UnitSpawnPoint
    │   │   ├── UnitMovementService.cs        # movement via A* FollowerEntity.destination
    │   │   ├── UnitDetectionService.cs       # can it see the player (distance + FOV + raycast)
    │   │   ├── UnitFieldOfViewService.cs
    │   │   ├── UnitChaseService.cs
    │   │   ├── UnitWanderService.cs
    │   │   └── RandomWalkablePointService.cs # random walkable point via AstarPath.active.GetNearest
    │   ├── View/                     # MonoBehaviour layer
    │   │   ├── UnitView.cs           # on the unit prefab: FollowerEntity, UnitCenter, owns UnitController
    │   │   └── UnitSpawnPoint.cs     # scene marker: type, count, delay + gizmo
    │   └── Utils/
    │       ├── UnitGizmosDraw.cs     # unit gizmos: view cone, wander zone, path
    │       └── UnitUniqueId.cs       # class UniqueId
    └── WorldGeneration/
        ├── WorldGenerator.cs         # MonoBehaviour entry point: generates the maps and saves them
        ├── RoadNetworkGizmo.cs       # MonoBehaviour: draws hubs, roads, streets and POI footprints
        ├── PoiSpawner.cs             # MonoBehaviour: instantiates POI from PoiPlacement.asset
        ├── Data/
        │   ├── BiomeType.cs              # enum: PineForest, BurntForest, Desert, Snow, Wasteland
        │   ├── BiomeDefinition.cs        # SO: type, map color, region weight, height profile, grounds, grass, trees, rocks
        │   ├── GroundLayer.cs            # one biome ground: TerrainLayer, weight, noise patches, slope and height ranges
        │   ├── ScatterLayer.cs           # one tree or rock: prefab, per hectare, grid step, patches, slope, scale, tint, footprint radius
        │   ├── GrassLayer.cs             # one grass kind: card texture (or prefab), density, patches, slope
        │   ├── BiomeDatabase.cs          # SO: biome list + "Create 7DTD Biome Set" button
        │   ├── WorldGenerationConfig.cs  # SO: seed, world size, warp, smoothing, output path
        │   ├── RoadNetworkAsset.cs       # SO: saved hubs, roads and streets
        │   ├── DistrictType.cs           # enum: Downtown, Residential, Industrial, Rural
        │   ├── SettlementTier.cs         # enum: City, Town, Village
        │   ├── SettlementProfile.cs      # tier build profile: radius, street step, branch depth, districts, required composition
        │   ├── PoiRequirement.cs         # composition entry: what, how many, in which radius ring
        │   ├── PoiDefinition.cs          # SO: prefab, footprint, district, weight, per-settlement and per-world limits
        │   ├── PoiDatabase.cs            # SO: list of PoiDefinition, district coverage
        │   └── PoiPlacementAsset.cs      # SO: saved placements (prefab, position, rotation)
        └── Generation/
            ├── BiomeMap.cs               # byte[] of biome indices + sampling by world coordinates
            ├── BiomeMapGenerator.cs      # warped Voronoi -> smoothing -> small-region cleanup
            ├── BiomeMapTexture.cs        # BiomeMap -> Texture2D through the palette
            ├── BiomeWeightField.cs       # blurred normalized biome weights, smooth sampling
            ├── HeightMap.cs              # float[] heights 0..1 + 16-bit raw export
            ├── HeightMapGenerator.cs     # prepares data and runs HeightMapJob
            ├── HeightMapJob.cs           # Burst IJobParallelFor: the height computation itself
            ├── HeightMapTexture.cs       # hillshade preview
            ├── GroundSplatPainter.cs     # ground layer weights: biome rules, slope, height, roads
            ├── PoiPadIndex.cs            # grid of POI pads: "is this point under a building?"
            ├── MaskTexture.cs            # float[] mask <-> Texture2D
            ├── BiomeClassifyJob.cs       # Burst IJobParallelFor: warped Voronoi per biome map row
            ├── MinHeap.cs                # binary heap for A* (no PriorityQueue in netstandard2.1)
            ├── RoadNetwork.cs            # Hub, Road and the road/street lists
            ├── HubPlacer.cs              # flattest spot in each sector
            ├── RoadPlanner.cs            # MST over hubs + A* on a 16-direction grid with slope and turn penalties
            ├── RoadSmoother.cs           # polyline -> smooth arc: Douglas-Peucker, Chaikin, corridor relaxation, corner fillet
            ├── TerrainCarver.cs          # three carving passes: highways and hubs, streets, POI pads
            ├── CityLayout.cs             # Lot (OBB with intersection test), CityLayout — boundary, districts, street step
            ├── CityPlanner.cs            # per hub: orientation, highway pieces inside, runs street growth and subdivision
            ├── StreetGrower.cs           # organic street growth: edges off the highway, branching, gap seeding
            ├── StreetStitcher.cs         # connectivity: finds detached street islands, links or removes them
            ├── GroundSplatJob.cs         # Burst IJobParallelFor: layer weights by biome, slope and roads
            ├── NativeBuffer.cs           # small NativeArray helpers: create from array, dispose
            ├── LotSubdivider.cs          # lots along the street frontage, width fits a prefab footprint or a composition request
            ├── LotIndex.cs               # lot grid, neighbour intersection test
            ├── SettlementComposition.cs  # required settlement composition: request queue, lot reservation
            ├── PoiCensus.cs              # what has been built: how many of this already stand per settlement and per world
            ├── PoiPlacement.cs           # serializable placement: prefab, position, rotation, footprint
            ├── PoiPlacer.cs              # prefab pick per lot, terrain check, POI along highways
            ├── RoadProximity.cs          # grid of road and street segments with half-width: "closer than N meters", "parallel nearby", crossing
            ├── DecorFilter.cs            # shared decor rules: road, building pad, biome weight, noise patch
            └── FractalNoise.cs           # fBm, billow, ridged on top of Unity.Mathematics noise
        └── Voxel/                        # voxel ground: density field, mesher, streaming, decor
            ├── VoxelConfig.cs            # SO: voxel and chunk size, bedrock, decor rings, skirt
            ├── VoxelDensityField.cs      # owns the native memory, editor-facing wrapper around the sampler
            ├── VoxelDensitySampler.cs    # struct for the job: density from HeightMap, positive inside rock
            ├── VoxelCellTables.cs        # eight cell corners and twelve edges + their NativeArray copy
            ├── VoxelMeshJob.cs           # Burst IJob: surface nets, one vertex per cell, quads on sign changes
            ├── VoxelChunkMesher.cs       # owns the scratch buffers, schedules the job for a chunk at any LOD
            ├── VoxelMeshQueue.cs         # mesher pool: N chunks computed in parallel, collected next frame
            ├── VoxelMesh.cs              # vertices, normals, UV0 and triangles of one chunk
            ├── VoxelSplatBaker.cs        # slope and height from the heightmap, layer weights into RGBA control textures
            ├── VoxelGroundMaterial.cs    # shared material and control-texture baking for builder and streamer
            ├── RepetitionlessVoxelSetup.cs # editor-only: creates the material, sets LayeredLit, fixes layer tiling
            ├── VoxelDecorLayer.cs        # shared decor layer description: scatter and grass reduced to one kind
            ├── GrassCardFactory.cs       # 2D grass: shared mesh of two crossed quads + cutout material per texture
            ├── VoxelDecorBuild.cs        # unfinished decor placement for a column: cursor over layers and instances
            ├── VoxelColliderQueue.cs     # Physics.BakeMesh in a job, MeshCollider attached next frame
            ├── VoxelDecorPlacer.cs       # jittered grid, filters, positions for grass, trees and rocks
            ├── VoxelDecorSpawner.cs      # per cell: ask for a plan and either combine or instantiate
            ├── MeshCombinePlan.cs        # "combine or not" rules, Unity-free, so the harness can check them
            ├── MeshCombiner.cs           # the combining itself, per material, batched under a vertex budget
            ├── VoxelStreamPlan.cs        # LOD rings around the player, Unity-free, so the harness can check it
            ├── VoxelTerrainStreamer.cs   # MonoBehaviour: holds the rings, builds and unloads across frames
            ├── VoxelBudget.cs            # estimates an area's weight before building, so the editor survives
            ├── VoxelDecorRenderer.cs     # Graphics.DrawMeshInstanced for grass: geometry is not duplicated
            ├── GeneratedMesh.cs          # owner of a built mesh: destroys it together with its GameObject
            └── VoxelTerrainBuilder.cs    # MonoBehaviour: builds a patch of chunks into GameObjects with MeshCollider
```

## Project conventions

**Architecture**
- MonoBehaviour is a thin layer only: `UnitView` holds scene references and owns the controller, `UnitSpawnPoint` is plain data. All logic lives in plain C# classes.
- A unit is assembled in a factory (`UnitFactory`), not in a MonoBehaviour `Awake`; dependencies go through the constructor.
- DI is VContainer: registration in `UnitLifetimeScope`, prefab instantiation via `IObjectResolver.Instantiate`.
- Scene MonoBehaviours are not injected: if there can be many of them they stay data, and a registered `IStartable` service drives the logic (see `UnitSpawnPoint` + `UnitSpawnService`).
- Scene object references are collected by the `LifetimeScope` through the inspector and handed to services via the constructor — no `FindObjectsByType` at runtime.
- Services take data through `UnitConfig` + `UnitView`, they never reach into the scene themselves.
- Gizmos live either in a separate component (`UnitGizmosDraw`) or in the marker itself, but always under `#if UNITY_EDITOR` and on `Handles`.

**R3**
- State is `ReactiveProperty<T>`; streams go into a `CompositeDisposable` owned by `UnitController` and disposed in its `Dispose()`.
- Polling is `Observable.EveryUpdate()` + `DistinctUntilChanged()`.

**UniTask**
- Async operations are `UniTask`, always with a `CancellationToken`.
- Fire-and-forget goes through `.Forget()`.
- On a state change the `CancellationTokenSource` is recreated (see `UnitController.RestartBehaviourCancellation`); the old one must be `Cancel()` + `Dispose()`.
- No `async void`, no coroutines.

**Inspector**
- Grouping is NaughtyAttributes: `BoxGroup` for two or three fields, `Foldout` for long blocks.
- `BoxGroup`, `Foldout`, `Required`, `ReadOnly`, `Dropdown` are declared for fields only. On a `[field: SerializeField]` property they must be written **inside** `[field: ...]`, otherwise it is a compile error.
- Computed values are shown via `[ShowNativeProperty]` on a plain property without `SerializeField` — handy for permissions, statuses and settings checks.
- Operations are `[Button]`, with a number in the name when order matters.

**Style**
- Private fields are `_camelCase`; constants are `UPPER_SNAKE_CASE`.
- Serialization is `[SerializeField] private` or `[field: SerializeField] public T Prop { get; private set; }`.
- Early returns instead of nested `if`.
- Namespaces are barely used (code sits in the global namespace) — keep new code in the same style until decided otherwise.
- **Compare distances squared.** `Vector3.Distance`, `.magnitude` and `Vector2.Distance` all pull in a square root, and it is only needed when the number itself feeds a further formula. If a distance is merely compared against a threshold or another distance, drop the root: `DistanceUtility.SqrDistance(a, b) < radius * radius`. Helpers live in `Assets/_Dustborn/Scripts/Utils/DistanceUtility.cs` — `SqrDistance`, `WithinRadius`, `GetClosest` (the last one compares squares, which is all a nearest-search ever needs). Ordering is preserved under squaring because distances are non-negative, so rewriting such a check does not change the result by a single bit.
- **No comments in code** — no explanatory `//`, no XML docs, no commented-out code. The code must read on its own: clear names and small methods instead of prose. Attributes such as `[Header]`/`[Tooltip]` are not comments and are fine.
- **No Cyrillic anywhere** — not in `Debug.Log`, not in attribute text, not in string literals, and not in this document. Everything is English.
- **A field hint is `[Tooltip]`, not `InfoBox`.** `InfoBox` draws a panel that always takes inspector space; a tooltip pops up on hover and stays out of the way. On a `[field: SerializeField]` property the tooltip goes inside `[field: ...]` on its own line.


## World generation

The goal is procedural generation in the spirit of 7 Days to Die. The world is 4096x4096 m and the ground is voxel: a density field meshed with surface nets per chunk, so it can be dug. Unity Terrain has been removed from the project.

**Layer pipeline.** Order matters: every later layer edits the earlier ones.

| # | Layer | State |
|---|---|---|
| 1 | `Biome map` — biome regions | done |
| 2 | `Heightmap` — terrain from biome profiles, thermal erosion | done, no hydraulic pass |
| 3 | `Hubs` — finding flat spots for towns | done |
| 4 | `Roads` — graph between hubs, A* over the terrain, carved into the heightmap | done |
| 5 | `POI` — street growth from the highway, lots along the frontage, buildings, pads in the terrain | done |
| 6 | `Splatmap` — biome + slope + roads and streets, into world control textures | done |
| 7 | `Voxel mesh` — surface nets per chunk + Repetitionless material | done |
| 8 | `Decor` — grass, trees and rocks per biome | done |
| 9 | `Streaming and LOD` — chunk rings around the player | done |
| 10 | `Digging` — sparse overlay of density edits | **not started** |
| 11 | `Caves` — 3D noise in the density field | **not started** |
| 12 | `Water` — sea level, lakes, rivers | **not started** |

POI sits **between** roads and splatmap, because building pads edit the heightmap while the splatmap and the mesh read the finished terrain.

**Quality is set once, in two places that must agree.** The `.asset` files in `Content/World/` and the `= ...` defaults on `WorldGenerationConfig`, `SettlementProfile` and `VoxelConfig` hold the same numbers, so a config created from scratch generates the world the project is tuned for instead of untuned code defaults. Keep them in step when tuning. The level chosen is the finest the pipeline's own budgets accept: `HeightCellSize` 2 (a 2049² map, 8.4 MB), `VoxelSize` 1 (the digging resolution 7DTD works at), five LOD rings out to 1536 m, `_controlResolution` 2048 to match the height cell, and `MaxTerrainLayers` at the Repetitionless Pro ceiling of 32. What is deliberately **not** raised: noise octaves and amplitudes, which were tuned down to kill small bumps and are a shape decision rather than a fidelity one; `BiomeCellSize`, because `BiomeWeightField` blurs the map anyway and a finer grid only costs memory at runtime; and the Synty ground textures at `maxTextureSize` 1024, which is already ~100 px per metre at these tile sizes.

Changing `HeightCellSize` invalidates everything in `Generated/`: the height map, the road mask and the POI placement all carry the old resolution, so press `Generate And Save All` before anything reads them.

**Where things are computed.** The biome map, heightmap, road graph and POI placement are baked in the editor and saved to `Assets/_Dustborn/Generated/`. Runtime reads them: `VoxelTerrainStreamer` builds chunk meshes from `HeightMap.bytes`, `PoiSpawner` instantiates buildings from `PoiPlacement.asset`. That way the map is visible to the eye and can be edited by hand.

In the editor `PoiSpawner` places buildings through `PrefabUtility.InstantiatePrefab`, so instances **keep their prefab link** — editing the prefab reaches every placed copy; spawn and clear mark the scene dirty and register with Undo. In play mode it is a plain `Instantiate`, which in the editor would give detached copies and an unsaved scene.

`WorldGenerator` buttons follow the pipeline: 1 biomes, 2 terrain, 3 hubs and roads, 4 cities and POI, 5 save. Each step runs the previous one when needed and **recreates its own copy of the terrain**, so the buttons can be pressed in any order any number of times: `_rawHeightMap` -> `_roadsHeightMap` -> `_cityHeightMap`. Keeping the intermediate maps is mandatory — otherwise pressing step 4 again would carve streets on top of already carved pads.

`HeightMap.bytes` is raw 16-bit little-endian. The `.bytes` extension makes Unity import the file as a `TextAsset`; renamed to `.raw` it loads into a Terrain via Import Raw. The file has no header and resolution comes from the config — so a map saved with a different `HeightCellSize` is read silently and wrongly: `FromRaw16` took `min(length/2, samples)` and left the tail zeroed, meaning half the world became a flat zero slab while the generator happily built cities on it. A length mismatch now logs an error. At `HeightCellSize` 2 the map is 2049x2049 and 8.4 MB, so everything in `Generated/` baked at another cell size has to be re-saved before it is read.

**Hubs.** The world is split into `ceil(sqrt(HubCount))` sectors and the flattest point (height spread within `HubSampleRadius`) is taken in each, then candidates are filtered by `MinHubDistance`. Sectors are mandatory: with a plain flatness sort all hubs clump into one plain and half the map stays empty.

The sector grid is laid **not over the whole world but over a rectangle inset** by `HubEdgeMargin` from the border (no less than `MaxHubRadius * CityRadiusScale * (1 + CityShapeJitter)` — the radius of the largest possible city). Without the inset a hub landed 64 m from the map edge and half the city hung off the map; clipping blocks after the fact still leaves a stump pressed against the edge. The inset is on the sector grid rather than on candidate rejection: with rejection the edge sectors shrink into a narrow strip and hand back the flattest point of that strip, so the hub still sticks to the border. At the cost of one or two hubs out of `HubCount`, cities sit entirely inside the map.

**Roads.** Edges are a minimum spanning tree over the hubs plus `RoadExtraEdges` short edges for loops. Each edge is routed by A* over a `RoadCellSize` grid with cost `length * (1 + RoadSlopePenalty * slope²)` — so roads go around mountains instead of straight up them. Already-routed cells are `RoadReuseDiscount` times cheaper, which makes roads merge into shared stretches.

**A road remembers which way it was going and pays for turning.** The A* state is not a cell but a pair "cell + entry direction" (`STATES = 17`: 16 directions plus "start with no heading"), and switching direction costs `RoadTurnPenalty` meters per radian. Without it a cell-based search cannot tell a straight line from a zigzag: both cost the same, and the highway wobbles over every bump. There are 16 neighbours rather than 8: eight regular plus eight knight moves (±1,±2). That gives a 22.5° angular step instead of 45°, so a long shallow arc can be laid out in cells, whereas on eight directions it falls apart into a staircase. Knight moves jump over the intermediate cell — imprecise for the `_used` reuse mark, but there are no impassable cells here so nothing breaks.

**Road ends are pinned to the hubs themselves**, not to A* cell centres. Previously highways converging on a hub could diverge by `RoadCellSize` (32 m) and only joined thanks to the hub pad.

**After A* the polyline is driven by `RoadSmoother`**, a separate pass:

1. **Douglas-Peucker** with tolerance `RoadSimplifyTolerance` — removes the raster staircase while keeping real turns.
2. **Chaikin** `RoadSmoothPasses` times — cuts the corners.
3. **Resample** at `RoadPointSpacing` — after Chaikin points bunch up at corners and thin out on straights, and relaxation needs even spacing.
4. **Relaxation** `RoadRelaxPasses` times: a point is pulled towards the midpoint of its neighbours with strength `RoadRelaxStrength`. The displacement is clamped to a `RoadCorridor` corridor around the original polyline — otherwise over a couple of dozen passes the road would straighten out and leave the pass A* had found. A step is rejected if it makes the longitudinal grade steeper than `RoadMaxGrade` **and** worse than before: straightening must not drive the highway into a wall.
5. Final resample.

Harness measurement (`CheckCurvature`, mean turn per 30 m): was 6.9° with a 54° maximum and 2.7% of turns sharper than 25°; now **1.8°, 32° maximum, 0.2% sharper than 25°**. The network also got shorter, 18.1 -> 17.4 km.

**Streets get a corner fillet instead of smoothing.** `RoadSmoother.RoundCorners` does not move existing points; it inserts a quadratic arc of radius `StreetCornerRadius` around each one. That matters: a street polyline holds the **exact junction points**, and `StreetStitcher` computes connectivity from them while `LotSubdivider` cuts frontage along them. Chaikin would shift a junction by several meters and could break connectivity; a fillet on a 12° corner deviates by centimeters. Measurement: maximum street turn 69° -> 19°, street edge with buildings 38° -> 27°.

**Carving into the terrain.** `TerrainCarver` builds a height profile along the road with a moving average (`RoadProfileSmoothing`), otherwise the road repeats every bump. Then it stamps discs along the polyline: weight 1 within `RoadHalfWidth`, falling off over `RoadShoulder`. Hub pads are stamped the same way. A by-product is the road mask for the splatmap.

**Hub pads are carved in a separate pass, before the roads.** Stamped into one buffer they both take height from the original terrain, the road wins along the centreline and the pad wins beside it, and a ridge with cliffs on both sides runs along the highway through the settlement. `Carve` levels every hub pad and calls `Apply` first, so highways are routed over the **already levelled** map.

**Embankment width is computed from actual earthworks across the road, not along the centreline.** `skirt = shoulder + moved * RoadEmbankmentSlope`, and `moved` taken at the centreline is zero on a hillside — the centreline sits at its own level while the ground five metres aside differs by the whole fill limit, so the embankment never widened and the roadbed ended in a wall. `LateralEarthworks` samples the ground at the point and on both sides at `halfWidth + shoulder / 2` and takes the largest deviation.

Harness measurement (`CheckShoulders`, drop from roadbed edge to the foot of the embankment): was up to 40° for highways and 58° for streets with 4% of samples over 35°; now 32° and 27° with nothing over 35°. Measure it on the map **before** POI pads are carved: they sit 6.5 m from the axis and their embankments spoil the reading, so the harness prints three lines — highways, streets, and streets with buildings.

**A road crossing an existing one adopts its level.** Before stamping, `LevelToExistingRoads` pulls the profile toward the current ground in proportion to the road mask: where the mask is 1 the profile equals the ground, so the height does not change and there is no step. The mask falls off past the roadbed edge over `RoadShoulder * RoadSurfaceFraction`, stretching the transition across the shoulder. This works between highways (which `RoadReuseDiscount` makes share stretches) and for streets branching off one.

**Earthworks are limited per cell**, not along the centreline. Limiting along the highway does not help: on a hillside the centreline sits correctly while the ground five meters aside is 15 m lower and gets raised to roadbed level — a shelf on stilts. So `Stamp` clamps the target height into `[ground - MaxRoadCut, ground + MaxRoadFill]` per cell (hubs have their own `MaxHubCut`/`MaxHubFill`). Fill is cheaper to keep small, cuts can go deeper — as in real construction. The other half of the solution is the cross-slope penalty in `RoadPlanner` (`RoadCrossSlopePenalty`): without it A* happily traverses a slope, where the longitudinal grade is zero and the cost minimal while across the road there is a cliff.

**Settlement tiers.** Eight identical grids read as procedural filler, so hubs are split into `City`, `Town` and `Village`. The tier is handed out by `HubPlacer` **by pad flatness**: candidates are already sorted, so the first `CityCount` become cities, the next `TownCount` towns, the rest villages. A large flat piece of land under a large city makes physical sense and is free, because the sort already exists.

The tier immediately **multiplies the hub radius** by the profile's `RadiusScale`. That matters more than it looks: `hub.Radius` feeds the hub pad carving, the city radius and `RuralClearance`, so one multiplier correctly shifts everything. Applied later, in `CityPlanner`, a village would get a levelled pad the size of a city.

`SettlementProfile` sets: `RadiusScale`, block size in the centre and on the outskirts (also the minimum gap between streets), `StreetBranchDepth`, district fractions, the industrial sector, `OuterDistrict`, `LotMargin` and the `Composition` list. A village is expressed through the same fields as a degenerate case — `DowntownFraction` 0, `ResidentialFraction` 0, `IndustrialSpan` 0, `OuterDistrict = Rural`, `StreetBranchDepth` 1 — and `DistrictAt` hands the whole settlement to rural prefabs. There is no separate village branch in the code.

**Cities are planned by growing streets, not by a grid** — a lattice reads as one, eight identical grids at different angles with the highway cutting them diagonally. `CityPlanner` cuts nothing: it prepares a frame (orientation, radius, wavy boundary, districts) and hands it to `StreetGrower`. Blocks do not exist as objects; buildings sit **along the street frontage**.

Settlement orientation comes from the **highway point nearest the hub centre** and now only sets the direction for gap seeding. The radius is `hub.Radius * CityRadiusScale`; the boundary is a circle with a two-harmonic wave (`CityShapeJitter`) living in `CityLayout.Contains`, so streets and lots are clipped by the same line.

Districts are computed by `CityLayout.DistrictAt` from a world point: `Downtown` when `distance/radius < DowntownFraction`, then `Residential`, and `Industrial` only inside a **sector** of width `IndustrialSpan` at a random angle. A continuous industrial ring around the edge looks implausible: factories belong on one side of town.

**Streets.** `StreetGrower` grows each street in `StreetStepLength` steps. At each step it tries `TURN_CANDIDATES` directions within `StreetMaxTurn` and takes the one with the smallest height change (plus `StreetStraightBias` for turning, otherwise the street wobbles over every bump); a step steeper than `MaxStreetSlope` is not taken. Hence streets that wind along the terrain instead of driving through a hill.

`StreetStraightBias` is read against `HeightCellSize`: the candidates are compared by the height they gain, so a finer heightmap feeds the choice finer detail and the street wanders more. At the 2 m cell 0.05 gave 4.8–5.2° of turn per 30 m and 0.09 gives 3.6–4.7° with more streets and more lots, so the bias sits at 0.09.

Seeding comes from three sources, in this order:

1. **Edges off the highway.** The pieces inside the settlement (`CityPlanner.CollectInside`) emit a pair of perpendicular streets every `CoreBlockSize`, so a settlement grows **out of the road** rather than happening to sit on it.
2. **Branching.** Every `BlockSizeAt` metres a street spawns a perpendicular branch with probability `StreetBranchChance` (and a second one the other way, less often) until depth hits `StreetBranchDepth`: 1 for a village, 3 for a city.
3. **Gap seeding.** Once the queue empties, `SeedGaps` walks the settlement on a half-block grid and plants a seed wherever no road is closer than `BlockSizeAt * StreetSpacingFraction * 1.15`, taking its direction **from the nearest road** — a street that picks its own angle in an empty area runs into its neighbours at once and dies on length. `StreetFillPasses` passes.

**A road must not run alongside another road** — the main growth rule. A step is rejected if the new point is closer than `BlockSizeAt * StreetSpacingFraction` to a road **parallel** to our direction (`|cos| >= 0.55`): the street dead-ends before reaching its neighbour. The check is about parallelism rather than proximity in general, because a street must be allowed to approach and cross a perpendicular road. If a step crosses a road at an angle over `StreetJunctionAngle`, the **exact intersection point** is inserted into the polyline, growth continues, and the checks are disabled for a step and a half — otherwise the street dies on the road it has just crossed.

The parent road is **excluded geometrically** from both checks: segments passing closer than `RoadHalfWidth + StreetHalfWidth` to the seed point are ignored. Without that a street branching off a highway dies on its first step — it stands on the highway, i.e. at zero distance. This used to be "skip the first N meters", and on a highway bend the street had time to run alongside it at point-blank range: the headless check caught 56 such streets of 91.

**That exclusion is on a leash: it only holds while the street is still leaving its seed** (`next` within `RoadHalfWidth + StreetHalfWidth + StreetStepLength` of the seed point), and past that the parent is checked like any other road. Excluding it for the whole walk let a short wiggly street curl back and hug the highway it was born on, since the segments it was hugging were exactly the excluded ones. The leash costs nothing for a street that leaves at a right angle — a perpendicular street is not parallel and the check does not fire — and it removes the artifact: `CheckSpacing` goes from 3 streets of 117 lying on a foreign road with a 0.0 m gap to 0 of 107, minimum gap 23 m, on all three test seeds.

Seeds are additionally filtered by `IsCrowded`: two seeds with the **same** direction (not opposite — that is one street growing both ways) and a lateral offset smaller than the block step count as a duplicate. Otherwise a junction of several highways at the hub produces a fan of five nearly coincident streets.

A gap-seeded street grows **both ways from the seed as one road**, not two. `SeedGaps` used to plant two opposing seeds at the same point: the first grew and entered `_index`, then the second was rejected by `IsCrowded` against its own twin — so every street in an empty area came out as a half sticking out of the middle of a block. Now there is one seed and `GrowStreet` calls `Walk` twice: backwards (points reversed) and forwards.

A finished street shorter than `MinStreetLength` is discarded, and street count per settlement is capped by `MaxStreetsPerSettlement`.

**Connectivity is guaranteed by a separate pass.** The "do not run alongside a parallel road" rule stops a street before it can reach its neighbour, and a gap-seeded street may never cross anything. With default settings that gave **23 streets of 48 with no connection to the highways**.

`StreetStitcher` decomposes a settlement into connected components — highways and streets count as connected if their roadbeds overlap — and treats all highway pieces as one anchor, since by MST construction the roads are connected outside the settlement. Every detached island is linked from its end **nearest the network** to the nearest point on the anchor, routed with the same terrain-aware stepping (`StreetMaxTurn`, `MaxStreetSlope`) but choosing candidates by direction to the target, and the last point is **the target itself** — a link that stops a metre short gives no connectivity. The budget is `2.5 * distance`; an island that does not fit is removed entirely.

Hence `RoadProximity.Remove`: removing a street must also remove it from the index, otherwise it keeps rejecting lots and neighbouring streets as a ghost. Segments are not evicted from cells but switched off with an `_alive` flag — cheaper than rebuilding the grid.

A link legitimately leaves a dead end **along its own direction**, so the harness `CheckSpacing` skips the first and last `RoadHalfWidth + StreetHalfWidth + StreetStepLength` meters of a street; without that skip nearly every link came under suspicion.

**Lots are cut along the frontage.** `LotSubdivider` walks every frontage line (streets **and** highway pieces inside the settlement) on both sides and at each step draws a random `PoiDefinition` for that point's district: lot width is `FootprintWidth + 2 * LotMargin`, depth is `FootprintDepth + LotSetback + LotMargin`, and the front edge sits at the road half-width plus `LotFrontGap` from the axis. A lot is accepted if it lies entirely inside the settlement and the world, none of its 25 probe points comes closer than `LotFrontGap / 2` to a foreign road, and it does not intersect any already placed lot (an OBB test through `LotIndex`, shared across all settlements). If it does not fit, the cursor advances by `LotProbeStep` and tries again.

Sizing the lot to a specific prefab is the same decision as in the grid version: cutting the frontage into equal pieces is not an option, because a building occupies half the lot and the street comes out gap-toothed. What went away is the "strip for two rows" math: a row along a street has no back yards, depth is exactly the building, and empty block centres no longer appear.

**Required settlement composition.** A `PoiDefinition` weight answers "how often", not "at all": with a purely random draw a city could end up with no church and five identical shops in a row. So each tier has a `PoiRequirement` list — what must stand in the settlement, how many, and in which radius ring (`MinRadius`/`MaxRadius` as fractions of the settlement radius). There is no district in a request; it comes from the `PoiDefinition` itself.

Requests are satisfied **during lot subdivision, not during placement**, and that is essential. If the frontage were first cut for random prefabs and only then searched for a spot for a 12x17 church, there would be nowhere to put it in a residential block of 9 m lots. So at every cursor step `LotSubdivider` first asks `SettlementComposition.Claim`: is there an unfulfilled request whose district and ring match this frontage point? If so, the lot is cut **to its size** and `Lot.Reserved` remembers who it is promised to. If it does not fit, the request goes back into the queue and waits for the next point. Requests are processed from large to small, and two of them never land closer than `BlockSizeAt` to each other — otherwise every required building would sit in a row on the first frontage line the cursor reached.

**Limits are the other half of the rule.** `PoiDefinition.MaxPerSettlement` and `MaxPerWorld` (zero means no limit) restrict not the requests but the random top-up: one church per settlement, `Market_Large` one per settlement and three per world. `PoiCensus` counts, one instance per settlement and per world. Reservations are counted **in advance**, before anything is built on them, otherwise the random top-up of the first city would eat the limit of a unique building promised to the third. Hence the order in `PoiPlacer.Place`: first recount all reservations across all settlements, then inside each settlement handle reservations first and free lots second.

A side effect: a lot cut for a building that has by then exhausted its limit stays empty if no prefab **no larger** exists in that district — the lot size is already fixed. So a limit must not be put on the smallest prefab of a district: `MaxPerSettlement` 3 on `Poi_Cafe` (5.8x7.2, the narrowest `Downtown` one) added a couple of dozen empty lots and was removed.

**If the composition does not close, the settlement is replanned.** Regenerating must be applied to **streets, not highways**: highways are already carved into the terrain by then (`TerrainCarver` runs before `CityPlanner`) and recomputing them would drag the whole of layer 4 along. So `CityPlanner.PlanCity` restarts what is cheap and local: street growth and lot subdivision. Up to `SettlementPlanAttempts` attempts, each with its own derived seed; the first one that closes the composition is accepted, otherwise the best by the pair (requests closed, then lots).

The rollback is mandatory and honest: an attempt's streets are removed from the shared `RoadProximity` and its lots from the shared `LotIndex`. Without that the second attempt would grow around the first attempt's streets and each attempt would make the city sparser. The attempt seed is derived from a **single** draw of the shared stream per hub (`random.NextUInt()`), not pulled from the stream as it goes — otherwise the number of attempts in the first settlement would shift the random numbers for all the following ones, and editing one city's composition would change the look of all the others.

Measured usefulness: on a badly shaped profile, 35 required buildings across 10 settlements closed 32 at `SettlementPlanAttempts` 1 and 33 at 3, with no further gain. With the profile fixed the composition closes fully with zero discarded attempts — the mechanism is insurance for bad seeds, not a knob.

**A composition that fails to close is almost always fixed by the profile, not by code.** It always comes down to **how much street frontage lies inside the required district**. `AutoRepair` is `Industrial`, and a town's industrial zone lived in a 100° sector past the 0.72 radius mark, i.e. a 48 m strip a street might never enter. `Diner` and `Church` are both `Downtown`, and a town centre at `DowntownFraction` 0.3 with radius 171 m is a disc of radius 51 m, while the "no closer than `BlockSizeAt`" rule demands 62 m between them. Hence the characteristic pattern while tuning: fix the centre and `Church` goes missing instead of `Diner` — they fight over the same place.

What helped was `RadiusScale` 0.62 -> 0.66 with `DowntownFraction` 0.3 -> 0.38: the town centre became a disc of radius 69 m and the two requests separate. `DowntownFraction` 0.42 alone also closes it, but 42% of the radius as centre is a small city, not a town.

The order of knobs: first look at which **district** ran out of room (it is printed in the warning next to the POI name), then widen that district in the profile — `DowntownFraction`, `ResidentialFraction`, `IndustrialSpan` — then the tier's `RadiusScale`, and only as a last resort weaken the request itself. `SettlementPlanAttempts` is not a tuning knob but insurance against bad terrain: if the composition fails on every attempt, the problem is the profile.

Requests are for rare and large buildings a random draw skips, not for ordinary development: that is what the weights already produce, and asking for houses would only take lots away from them.

**Placement.** For a reserved lot the prefab is already known — `PoiPlacer` takes `Lot.Reserved` and only checks terrain and roads. On a free lot it picks a prefab that fits, weighted by `Weight * footprint area` — otherwise a shack lands on a wide lot. The building is shifted toward the street by `LotSetback` and rotated to face it (+Z). Pad height is taken **from the street point opposite the lot**, not from the ground under the building: a building must stand at the level of its street. Along highways outside cities, `Rural` points are scattered: step `RuralSpacing`, offset `RuralOffset`, alternating sides, never closer than `RuralClearance` to a city.

**A prefab's origin does not coincide with its footprint centre, and that must be compensated.** The pipeline assumes the placement point is the footprint centre — the pad is carved by it, roads and terrain are checked by it — while Synty puts the pivot wherever. `PoiDefinition.PivotOffset` holds the footprint centre in prefab-local coordinates and `PoiPlacement.PrefabPosition` subtracts it with rotation applied, so the pad stays put and only the mesh shifts. Without it a building slides half its width off the lot and both checks lie, having measured somewhere the building is not.

The **`Measure Footprints From Prefabs`** button on `PoiDatabase` fills this in: it merges the meshes of every child into a bounding box in root space and writes `FootprintWidth`/`FootprintDepth`, `PivotOffset` from the centre and `GroundOffset` from `-bounds.min.y`. Meshes rather than colliders, because Synty `MeshCollider`s have no bounds. Press it after adding any prefab.

**Pad carving.** `TerrainCarver` runs three passes: highways and hub pads, then streets, then POI pads. The order is mandatory — POI check the terrain and take their height from the **already carved streets**, otherwise a building sits on untouched ground and ends up half a meter above fresh asphalt.

A pad is a rectangle with a signed distance: weight 1 inside `footprint + PoiPadMargin`, then falling off over `PoiPadSkirt`. The `PoiPadMargin` slack (no less than `HeightCellSize`) is mandatory: at the 2 m heightmap cell an 11x9 m building covers five or six samples and at the old 4 m cell only two or three, and without slack the cell nearest a corner already lands on the skirt — the corner hangs several meters in the air.

Neighbouring pads settle a contested cell **by proximity, not by order**: whoever has the smaller signed distance paints it. "Greater weight wins" breaks here, because the flat zones of neighbours overlap at weight 1 and the cell would go to whoever was processed first.

Earthworks are clamped per cell (`MaxPoiCut`/`MaxPoiFill`) just as for roads. `PoiPlacer.Fits` checks the terrain on a 4x4 grid not over the footprint itself but over **the footprint plus the pad slack** — otherwise a building passes and its skirt does not.

**Buildings must not stand on a road.** The highway runs to the hub centre, i.e. straight through the settlement, and without a check ~12% of buildings ended up on the roadbed. The check works **on polyline geometry, not on the road mask**: the mask lives on the heightmap grid (4 m) while the gap between a building and a street axis is only 7 m — a raster cannot resolve that. Checking against the mask rejected 237 lots instead of 73, two thirds of them falsely, against the block's own streets; bilinear sampling only smeared the blob one more cell.

`RoadProximity` is a shared index holding the segments of **both highways and streets** with their half-widths, so one query "closer than N meters from the roadbed" works for a road of any width. The bulk of the work is done by `LotSubdivider` at subdivision time; `PoiPlacer.IsOnRoad` remains as insurance and probes the footprint at `ROAD_PROBE_STEP`, which must be no coarser than the roadbed width or a narrow road slips between the probes.

**There is not a single square root left in `RoadProximity`, and it shows: it is queried at every step of every street and for every lot.** `IsParallelWithin` compares squared — `dot² >= cosLimit² * |line|²`; `TryNearestDirection` and `Crosses` remember the best segment and normalise **once after the loop**. `StreetStitcher` follows the same rule (`Touches` and `NearestSqr` work in squares), as does `CityLayout.Contains`. Separately, `LotIndex.Overlaps` must not look for duplicates with a linear `scratch.Contains`: a lot lives in several grid cells at once, so on a dense block that is a quadratic walk. It keeps a dictionary "lot -> query number" instead, the number incremented per call so nothing needs clearing, and `Remove` also evicts the lot from it.

**Checking the world boundary is mandatory.** Going off the map does not crash by itself: both `HeightMap.SampleWorld` and the carving clamp the coordinate into `[0, Resolution-1]`. That is exactly why the bug is invisible — buildings with a negative X silently get the height of the edge column, all land at the same level and hang several meters above the terrain. The main defence is the `HubPlacer` sector inset, but it rests on `MaxHubRadius`; so `StreetGrower` does not step outside the world, `LotSubdivider` rejects a lot with any corner outside it, and `PoiPlacer.Fits` rejects any probe point outside `[0, WorldSize]`.

**Knobs for building density**, measured headless against a 61-street, 474-POI baseline: `StreetSpacingFraction` 0.55 gives 93 streets and 597 POI while 0.9 gives 49 and 412; `CityRadiusScale` 1.15 gives 41 and 362 while 1.6 gives 75 and 688; `LotGap` 12 leaves the streets alone (60) and cuts POI to 362; `MinStreetLength` 20 nearly doubles the streets to 107 and still loses POI (456).

The order of knobs: `StreetSpacingFraction` and the profile block sizes (`CoreBlockSize` / `OuterBlockSize`) set how dense the street network is; `LotGap` and the profile's `LotMargin` set how densely buildings stand along a street; `CityRadiusScale`, `HubCount` and `MinHubDistance` set how much development there is overall. `LotGap` and `LotMargin` affect neither the number of settlements nor (much) the number of streets — the safest way to thin out the frontage.

**The gap between neighbouring buildings along a street:**

```
cursor step = FootprintWidth + 2 * LotMargin + LotGap * random(0.5, 2)
air between buildings = 2 * LotMargin + LotGap * random(0.5, 2)
```

`LotMargin` lives in `SettlementProfile` (one per tier), `LotGap` in the config. The city default is `2 * 1.5 + 1.5 * (0.5..2)`, i.e. **3.75–6 m between buildings**: with a 14 m wide building that reads as a solid terrace. `LotMargin` gives a constant gap, `LotGap` a random one, and it is `LotGap` that makes development ragged rather than comb-like. Raise both: one without the other gives either an even picket fence with wide gaps or clumps standing shoulder to shoulder.

`LotMargin` does not affect prefab choice: `PoiPlacer.Pick` receives a budget of `lot.Width - 2 * LotMargin`, i.e. exactly the original footprint, so `LotMargin` is pure air. Building size is set only by `FootprintWidth`/`FootprintDepth` and the prefab scale; with one prefab per district there is no size variation at all.

Two traps in that table. First: **more streets does not mean more buildings**. `MinStreetLength` 20 gives twice the streets but fewer buildings than the default — short streets eat space with roadbed and `LotFrontGap` gaps. Second: `Random` is shared across the whole planning run and flows through `LotSubdivider.Fill`, so a lot knob shifts the random stream and changes the streets of subsequent settlements. A ±10% spread in street count between adjacent runs is that, not the knob.

**`HubCount` is a ceiling; `MinHubDistance` decides.** Candidates are taken one per sector, and the winners of neighbouring sectors often stand closer than `MinHubDistance`, so the greedy filter drops them. On a 4096 world with `HubEdgeMargin` 500: up to 700 m the filter barely interferes, at 800 m only 5 of 9 requested survive. If fewer cities came out than requested, `HubPlacer` logs it.


**Splatmap.** A biome has not one ground but a **list of `GroundLayer` rules**, plus the shared `CliffLayer` and `RoadLayer` from the config. The biome weight comes from the same `BiomeWeightField` as the terrain, so the ground changes exactly where the height profile changes. Slope is computed from the heightmap by central differences in world scale, not as a gradient in normalized coordinates. The cliff layer displaces the others via `SmoothStep` between `CliffSlopeStart` and `CliffSlopeFull`, the road layer displaces even the cliff, and then the weights are normalized.

**How a ground rule is computed.** Rule weight = `Weight * slope band * height band * noise patch`. A band is `smoothstep(Min - Fade, Min)` on entry and `1 - smoothstep(Max, Max + Fade)` on exit, so the borders are soft and tunable separately (`SlopeFade`, `HeightFade`). A patch is `FractalNoise` with period `PatchFrequency` meters, cutoff `PatchThreshold`, feather `PatchFade`; at `PatchThreshold` 0 the noise is not evaluated at all — that marks a base layer and is also the main saving in the job.

Rule weights are **normalized within the biome** and only then multiplied by the biome weight. Hence a simple tuning rule: the list sets proportions, not absolute values. A base layer is usually `Weight` 1 over the whole area, patches 1.4–1.9 (once a patch kicks in it takes ~60% and the base shows through), slope layers 2.4–2.6 (they must win on their own slope). If no rule fired for a pixel, the whole biome share goes to the **first** rule in the list — so the base ground must be first, otherwise a patch shows up on rare slope/height combinations.

The job computes a rule weight twice — once for the sum, once to divide by it. There is nowhere to store intermediate weights: you cannot allocate a `NativeArray` inside an `IJobParallelFor`, and a `FixedList` would drag stubs into the harness. Double evaluation is cheap because biomes with weight below 0.001 are skipped entirely (usually one or two live in a pixel) and base layers do not touch the noise.

**Tiling is removed by Repetitionless, not by a second layer.** `BiomeDefinition` used to have a `GroundVariantLayer` — the same texture at a different `TileSize`, blended in by low-frequency noise. Now repetition is broken by the `RepetitionlessLayeredLit` shader, and the macro layers are gone along with the `GroundVariantLayer`, `VariantPatchSize` and `VariantAmount` fields. That is not only cleaner but forced: there are five times fewer layers now, and spending two of them on duplicates is not an option.

**The layer ceiling is set by the installed Repetitionless version, and the code asks the material for it.** The shader has no limit (it declares `_Control0.._Control7` and the keywords `_MAX_LAYERS_4 .. _MAX_LAYERS_32`); the limit sits in the C# tooling: `Constants.MAX_LAYERS_TERRAIN` is 4 in Free and 32 in Pro. The constant is `internal` and invisible from `Assembly-CSharp`, so `RepetitionlessVoxelSetup` takes the capacity from the material's own `Properties.asset`: `RepetitionlessMaterialCreator` sizes the `Data` array by exactly that constant. This way the code does not guess about the license and needs no edit on upgrade.

What matters: **capacity is fixed at material creation time**. A material baked under Free stays four-layer even after Pro is installed — `Generated/VoxelMaterial.mat` together with the `VoxelMaterial_RepetitionlessData` folder must be deleted and rebuilt. The setup warns about this with the exact text of what to delete.

The generator's own budget is `WorldGenerationConfig.MaxTerrainLayers`, set to the Pro ceiling of 32 with 14 slots taken. Raising the ceiling costs nothing on its own: the control textures and the bake buffer are sized by the layers actually registered, not by the budget.

**The material is one for all chunks**, and that is a necessity: Repetitionless bakes two `Texture2DArray` with three slices per layer per material, so a material per chunk would be gigabytes of video memory. The layer budget is therefore distributed globally — which also removes the chunk-border seams that different layer sets in neighbours would produce.

**The price of 14 layers is the size of the baked arrays, and it is paid by keeping the textures at 1024.** There are 42 slices instead of 12; at 2048² the baked `TextureArray_AVTextures.asset` and `TextureArray_NSOTextures.asset` weigh 157 and 123 MB, at `maxTextureSize` **1024** about 70 MB together. All 22 textures the 14 `.terrainlayer` files reference are set to 1024 and should stay there — at a `TileSize` around 10 m that is already 100 pixels per metre and Synty's stylized facets hold nowhere near that detail. A change reaches the material only **after a rebake**: delete `Generated/VoxelMaterial.mat` with its `VoxelMaterial_RepetitionlessData` folder and rebuild.

How the budget is split in `GroundSplatPainter`: cliff and road take their slots first (they are structural — without them neither slopes nor highways read), then the biome ground rules, with biomes walked **by their share of world area** (`BiomeWeightField.Coverage`) and rules inside a biome in list order. Rules that do not fit are skipped with a warning; their weight spreads over the remaining grounds of the same biome during normalization — no extra code needed. Layers are deduplicated by asset, so one `.terrainlayer` used in three biomes takes one slot.

The current 14-slot layout: **shared** `Cliff` (`Cliff_Wall_01`, 24 m) and `Road` (`Mud_01`, 4 m); **grounds** `Ground_Forest` (`Grass_01`, 10 m), `Ground_Grass_Dry` (`Grass_02`, 9), `Ground_Moss` (`Moss_01`, 7), `Ground_Leaves` (`Leaves_01`, 6), `Ground_Leaves_Dead` (`Leaves_03`, 6), `Ground_Flowers` (`Flowers_01`, 8), `Ground_Pebbles` (`Pebbles_01`, 5), `Ground_Gravel` (`Pebbles_Sand_01`, 7), `Ground_Dust` (`Sand_01_Darker`, 12), `Ground_Sand` (`Sand_01_Desert`, 14), `Ground_Sand_Pale` (`Sand_01`, 13), `Rock_Wall` (`Rock_Wall_01`, 18).

Who takes what is in the `Biome_*.asset` ground lists, and every biome has its own base ground plus its own patches rather than sharing one: forest on `Ground_Forest` with moss, leaves and flowers over it, burnt forest on `Ground_Leaves_Dead`, desert on `Ground_Sand`, wasteland on `Ground_Dust`, each with a rock layer entering at its own slope.

**In Repetitionless, `TileSize` means the opposite of the built-in terrain, and the code compensates.** With world UV the shader does `UV = WorldPosition.xz / 1000` and then `UV = UV * tiling + offset`, where `tiling` is `TilingOffset.xy`; `RepetitionlessTerrainDataSO.UpdateLayerMaterialData` puts the raw `terrainLayer.tileSize` into `TilingOffset`. So the texture period is **`1000 / TileSize` meters**: on the built-in terrain `TileSize` 4 means a texture every 4 m, here it means every 250 m. The shader's own comment claiming parity with the standard terrain is wrong — they coincide only around `TileSize` 31.6, where `TileSize² = 1000`.

So after syncing the package, `RepetitionlessVoxelSetup.ApplyWorldTiling` rewrites `Data[i].BaseMaterialData.TilingOffset` to `1000 / TileSize` and pushes the result into the properties texture via `UpdateMaterialTexture`. That way the `Size` field of a `.terrainlayer` keeps its usual "meters per texture" meaning and stays correct even with Repetitionless disabled. The price is that the Repetitionless material inspector shows the layer tiling inverted (250 for 4 m), which is expected.

The same place sets `AutoSyncLayers = false`, or the package inspector re-syncs on any touch and restores the raw `TileSize`, stretching textures over hundreds of metres again.

**`TileSize` sets not a "repeat frequency" but the physical size of what is drawn in the texture.** Compute it from the detail: if a rock occupies 4% of the image width, then at `TileSize` 8 m it comes out 32 cm — a boulder, not gravel. Hence the current values: grass and grounds 5–14 m, cliff 24, rock wall 18, road 4. Ground textures are flat Synty facets; at 10–12 m they give 1–2 m blobs, right for stylized ground. The cliff is vertical columns about 1/10 of the image, so at 24 m those are 2–3 m columns (Synty's own cliff layer sits at 30 m). The road is where a scale error is most glaring — gravel reads as cobblestones — so it uses `Mud_01` at `TileSize` 4 m, making the stones 8–18 cm; the dark brown dirt also contrasts with both the olive forest and the sandy wasteland, whereas the pale `Pebbles_Sand_01` blended into `Ground_Dust`.

The biome map is read back from `BiomeMap.png` through the palette — exact colour match, nearest colour with a warning for misses — so a hand edit in an image editor reaches the terrain textures.

**Grass.** Nothing is baked or saved: placement is deterministic from the seed, so grass is recomputed when each chunk column loads.

**A fresh `Grass` list element arrives zeroed and no grass shows up.** Unity adds an element to a serialized list via `arraySize++`: it zero-fills and does **not** run C# field initializers, so `Density`, `MaxSlope` and the bush sizes come out zero and the layer draws nothing while still counting as present. Two guards: `BiomeDefinition.OnValidate` substitutes defaults into a layer whose numeric fields are **all** zero (`GrassLayer.IsUnset` marks an untouched element rather than a deliberately disabled one), and `VoxelDecorPlacer` skips such a layer via `IsMute`.

**Grass is 2D cards, not Synty meshes.** `GrassLayer` has a `Card` field: a texture of one tuft with alpha, which `GrassCardFactory` draws on **two crossed quads** — 8 vertices and 4 triangles instead of the hundred-plus of a mesh bush. The `Prefab` field remains for mesh grass but the card overrides it: with `Card` set, `VoxelDecorLayer.FromGrass` puts `Prefab = null` into the layer, otherwise a sparse cell (fewer than `MIN_INSTANCES` cards) would fall through to `Instantiate` and place a real Synty bush among the cards.

The card mesh is one for the whole world and lives in `GrassCardFactory`; materials are one per "texture + colour" pair, made from `Dustborn/GrassCard` — a small URP shader that adds two things Simple Lit cannot do. It **dissolves with distance**: `clip` against a screen-space R2 dither over `_FadeStart.._FadeEnd`, so grass thins out instead of ending on a line, and no transparent queue or sorting is involved. And it **bends in the wind**: the card's `uv.y` runs 0 at the root to 1 at the tip, so `uv.y²` plants the root and moves only the top, phase taken from world position so neighbours do not sway in lockstep. A shader that fails to compile is still found by name and would paint the world magenta, so `Pick` takes it only when `Shader.isSupported`, otherwise it falls back to `Universal Render Pipeline/Simple Lit` with an error in the log. Because the material is built at runtime and no scene asset references the shader, a player build needs it in **Always Included Shaders**; the editor finds it either way. The normals of all eight vertices point **up** rather than out of the quad — the standard grass trick, otherwise half the tuft goes black in sunlight. `VoxelDecorSpawner` owns all of this (it is `IDisposable`), the streamer releases it in `OnDisable` and the builder in `Clear`.

The Synty textures (`PolygonNature/Textures/Grass/`) are **white**; colour comes from the layer's `HealthyColor` through `_BaseColor`. So a layer moved from a prefab to a card must be given a real colour — a prefab carried its green in its own material, a card does not. Eight forest layers hold shades of green (0.35..0.56 red), burnt forest ashen `(0.72, 0.66, 0.55)`, wasteland ochre, desert bleached. `DryColor` is unused by cards.

For a card, `MinWidth`/`MaxWidth` and `MinHeight`/`MaxHeight` are **meters**, not a multiplier on the prefab scale: at the current 0.85–1.25 a tuft comes out roughly a meter square.

Densities are forest **0.36 bushes per m²**, burnt forest 0.15, wasteland 0.08, desert 0.04, cut sixfold from the original 2.14 in the forest; tree `PerHectare` was untouched (forest 120/ha, burnt forest 79, wasteland 31, desert 10). Density and draw distance trade against each other — doubling `GrassDistance` quadruples the area actually drawn — and cutting proportionally is valid because count is linear in `Density`: the grid step is `0.8 / sqrt(Density)` and only the number of cells changes. Thin unevenly: when one layer is an order of magnitude denser than the rest it reads as a solid carpet and hides all the others.

The harness cannot verify this: `CheckDecor` injects **synthetic** layers into every biome (`Inject`) so the per-biome numbers are comparable, and does not read the real asset lists — `AssetReader` only parses scalar biome fields. Density edits in `Biome_*.asset` must be checked by eye in the scene.

Biomes are separated not only by density but by `PatchThreshold`: 0.26–0.52 in the forest, 0.56–0.66 in the desert, so there grass sits in rare islands rather than a carpet. And by card color. The same texture in different biomes gives different materials, because the cache key is the "texture + color" pair.

**A material needs `enableInstancing` raised, otherwise `DrawMeshInstanced` throws every frame.** Synty ships `m_EnableInstancingVariants: 0`, and a rock — one mesh, one material, no `LODGroup` — is exactly the profile the plan sends to `Instance`. Closed from both sides: the flag is raised on the four Synty materials that reach instancing (`PolygonNature_01`, `PolygonNature_Moss_01`, `PolygonNature_Tree_Trunk_01`, `PolygonNature_Tree_01`), and `MeshCombiner.Probe` asks the material — `Instanceable` requires `_materials[0].enableInstancing`, so without the flag the layer is routed to `Instantiate` instead: slower, but no exception.

**A grass prefab's root must carry a `MeshRenderer` itself**, for mesh grass and for scatter alike: instancing reads `MeshFilter`/`MeshRenderer` **from the given object** and does not look into children. Synty mostly complies, but not always — `SM_Plant_Undergrowth_01` keeps its mesh in a child. In the `.prefab`, the `--- !u!23` block must have `m_GameObject` matching `m_RootGameObject`.

**Every layer has its own `PatchFrequency`, and that is not cosmetic.** The noise offset in `Count` is built as `float2(seed, PatchFrequency)`, so the patch frequency doubles as the field's seed. Two layers with the same `PatchFrequency` get **the same patch** and will always grow in the same places; different frequencies (26, 38, 47, 58, 74, 95 in the pine forest) give independent patches, and layers overlap in pieces like real undergrowth.

The bush count in a cell is `Density * cell area * biome weight * patch * (1 - road) * slope falloff`, clamped by `MAX_PER_CELL`; the biome weight comes from the same `BiomeWeightField` as the terrain and the splatmap, so grass changes exactly where the ground texture changes. Patchiness comes from `FractalNoise` at `PatchFrequency` — below `PatchThreshold` no grass at all, above it a smooth ramp. Without patches grass lays down as an even carpet and the world looks like a golf course.

Grass does not grow on roads (the `RoadMask.png` mask, streets included), on slopes over `MaxSlope`, or **under buildings** — `DecorFilter` asks `PoiPadIndex`, built from `PoiPlacement.asset`. Without that asset a warning goes to the log and grass grows through the floor.

**Trees and rocks.** Both biome lists (`Trees` and `Rocks`) are `ScatterLayer` and both go through `VoxelDecorPlacer` in the same code. The split is purely editorial, convenient for tuning and disabling.

Scattering is a **jittered grid, not Poisson**: the world is cut into cells of side `Spacing` with one randomly offset point per cell. That gives both a minimum gap between trunks (equal to `Spacing`) and determinism for free: the RNG is seeded by a hash of `(cellX, cellY, rule index, Seed)`, so a point depends neither on chunk traversal order nor on which other layers are enabled.

A cell belongs to **exactly one decor cell**, decided by the jittered point rather than the cell index: `VoxelDecorPlacer` walks the cells overlapping a decor cell and takes only the points falling inside `[origin, origin + span)`. Otherwise trees on chunk borders would either double up or drop out.

Density is given in **units per hectare** (`PerHectare`) and the grid converts it into a probability: `Chance = PerHectare / 10000 * Spacing²`. More than one tree cannot fit into a cell, so `Spacing` is also the density ceiling: at 7 m that is 204/ha. If more is requested, the painter logs how much the grid actually holds.

A candidate then passes the filters: biome weight (against a random number, so the border comes out ragged rather than cut), distance from the roadbed (the mask is sampled at the point and four points around it, hence a cleared strip rather than trees on the shoulder) and POI pads via `PoiPadIndex`, then height range, slope, and last the `FractalNoise` patch. Only then are scale, rotation and tint computed.

**Filter order goes from cheap-and-rejecting to expensive.** The checks are ANDed, so order does not affect the result at all but affects the cost a lot: there are about 2.5 million candidates and tens of thousands survive. Biome weight is four array reads and removes a pine forest layer across the whole half of the map with no forest; the road mask is one read; height is one bilinear sample; slope is four; the patch is three octaves of simplex, more expensive than everything else combined. Height and slope used to come first, meaning five heightmap samples were paid for a candidate immediately discarded by biome.

**A layer is probed against the column before its cells are walked.** A decor layer belongs to exactly one biome and a chunk column usually sits in one, so a few dozen weight samples over the column footprint throw out three layers in four before a single candidate is generated: 1.90 M rejected candidates fall to 0.63 M with the placement output unchanged to four instances. That matters because the decor scan is what the streaming budget is actually spent on.

`DecorFilter.InBiome` asks `BiomeWeightField.SampleOne` — the weight of **one** biome. A layer always belongs to exactly one biome, while `Sample` filled an array for all four. The bilinear indices and coefficients are computed once (`Taps`) and reused by both methods. The surface height under a candidate is computed once and handed out through `out`, instead of `Fits` taking it for the range check and `Build` taking it again for the position.

`PoiPadIndex` is needed precisely because of the candidate count: hundreds of thousands against some seven hundred pads, so a brute-force check is hundreds of millions of comparisons. A 64 m grid reduces that to a couple per point. Grass does without an index: `ClearPads` goes from buildings to cells, not the other way round.

**A tree prefab must carry a `MeshRenderer` or a `LODGroup` on its root** — the same requirement as for mesh grass. Synty trees ship with a `LODGroup` (`SM_Tree_Pine_*`, `SM_Tree_Birch_*`, `SM_Tree_Dead_*`), rocks and apocalypse trees are flat prefabs with a `MeshRenderer`. Both are fine.

The set: forest — pines, birches, round trees and stumps (7 layers, 120/ha before patches); burnt forest — dead trunks and logs with an ashen tint (7 layers, 79); wasteland — apocalypse dead trees (3 layers, 31); desert — almost nothing (2 layers, 10). All four have rocks, living on slopes up to 55–60°, i.e. reaching onto rocky slopes where grass no longer goes. `Tint` multiplies the prefab material, so the same assets are grey in the burnt forest and bleached in the desert.

**Scatter has its own `Footprint` — a pad radius in meters.** Without it the "not under a building" and "not on a road" checks worked on a single point, the object centre, and a four-meter boulder placed three meters from a wall legitimately passed both. Now `PoiPadIndex.Covers` expands the building rectangle by `PoiPadMargin + Footprint`, and an object never comes closer to a road than `max(RoadClearance, Footprint)`. Small things are unaffected: a grass tuft or bush has a `Footprint` of about a meter, a rock cluster seven.

The pad index is built with slack for the **widest** `Footprint` of all layers (`WidestFootprint`), because cell binning happens once while queries arrive with their own radius. Otherwise a large rock near a cell border would not find a building in the neighbouring cell, and the check would silently skip exactly the case it exists for.

The asset values were eyeballed. The **`Measure Scatter Footprints`** button on `BiomeDefinition` takes the real ones from the prefab meshes — the maximum radius in the XZ plane, i.e. the crown of a tree rather than its trunk. For "do not reach into a building" the crown is what is needed; if it thins the forest along roads too much, lower `RoadClearance` — it is taken as a max with `Footprint` and does not interfere separately.

**There end up being tens of thousands of trees, and that hits rendering, not generation.** Forest covers half the map, and at 133/ha before patches that is on the order of 12 thousand trunks in the rings around the player. Range is set by `TreeMaxLod`, not in meters: trees reach the second ring. Synty prefabs are **not SpeedTree**, so distance rendering rests on the prefab's own `LODGroup`, and it is precisely because of `LODGroup` that the combiner keeps trees as separate objects. If it stutters, lower `PerHectare` rather than `TreeMaxLod`: an empty horizon is more noticeable than a sparse forest.

**Erosion is thermal, and it is gather-formulated so it can stay parallel and deterministic.** `HeightMapErosion` runs `ErosionPasses` iterations over the finished height map; each is two Burst jobs over rows. `ErosionFlowJob` computes, per cell, how much material leaves it — the classic talus rule: over the four neighbours take the drops that exceed `T = tan(ErosionTalusAngle) * cellSize`, move `ErosionStrength * (largest excess)` and share it in proportion to each excess. `ErosionSettleJob` then reads that per-cell figure back from the four neighbours and writes `h - loss + gain` into the other buffer. Nothing is scattered, so the result does not depend on thread order, and mass is conserved exactly because both ends of a transfer compute it from the same two heights.

**What the pass is for is killing impossible slopes, not for reshaping the world.** Material moves one cell per pass, so 32 passes reach 64 m at a 2 m cell — crests get rounded and faces get a scree foot, while the large forms are untouched. Measured on the reference seed: maximum slope **61° -> 42°**, cliff area 4.8% -> 5.5%, bumps per km² 12.9 at 11.0 m -> 12.8 at 10.7 m, height range unchanged at 133 m. Development gains rather than suffers: 702 POI against 680, the road embankment maximum 29° -> 24°, composition still 34 of 34.

The talus angle is the knob that matters and it cuts both ways. At 26° with 48 passes the cliffs are eaten — 4.8% of the world steeper than 28° falls to 1.6%, and the cliff layer is what makes rock read as rock. At 30° the world keeps its rock faces and loses only the noise spikes. Below the angle nothing is touched at all, which is why the flattest sites — where hubs go — come out bit-identical.

**Terrain** is computed not as separate noise per biome but as a set of world-wide fields: `continent`, `hills` (billow), `ridge` (ridged), `dune` (stretched ridged), `detail`. A biome only sets the amplitudes of those fields and its base height, and what is blended between biomes is **parameters**, not finished heights. Otherwise steps appear at the joints, and bilinear interpolation of biome weights over a grid produces visible square facets — the derivative of a bilinear is discontinuous and hillshade shows it. Weights are blurred (`BiomeWeightField`) and interpolated with a quintic fade. Mountain ridges are additionally multiplied by a low-frequency mask, otherwise ridged noise turns the whole biome into crumpled foil.

**Performance.** Height computation lives in `HeightMapJob` — an `IJobParallelFor` under `[BurstCompile]`. Biome weights and profiles are copied into `NativeArray` (`Allocator.TempJob`, disposed in `finally`), and noise is called straight from `FractalNoise`: it is a static class over bare `Unity.Mathematics` with no reference types, so Burst compiles it as is and a job needs no private copy of it.

**The biome map moved to Burst too.** `BiomeClassifyJob` is an `IJobParallelFor` over **rows**, not cells: the job index is the row and a plain loop over x runs inside, so a cell costs no integer division and writes into `Cells` are sequential. Rows write into non-overlapping parts of the array, hence `[NativeDisableParallelForRestriction]` — the "only write to `array[index]`" rule is satisfied in spirit, not in letter. The erosion jobs are built the same way.

Smoothing, weight blurring and normalisation stayed managed but run through `Parallel.For`; the blur buffer is handed out through `localInit`, one per thread rather than one shared.

**Harness numbers mean nothing for job speed.** The stub `Schedule` runs `Execute` on one thread over a `T[]` wrapper, so a job is **slower** there than the loop it replaced. Only the Unity profiler can measure these — the same caveat as `VoxelMeshJob` and the erosion passes.

**Burst, Collections and Mathematics are listed in `manifest.json` explicitly.** They used to arrive transitively (through `com.unity.entities` and A*), meaning the project silently depended on someone else's dependency list: had a transitive reference disappeared, the jobs would have silently fallen back to managed mode with no compile error. The manifest versions match what was already resolved in `packages-lock.json` (Burst 1.8.29, Collections 2.6.8, Mathematics 1.3.3), so the actual build does not change.

Four jobs are under `[BurstCompile]` — `HeightMapJob`, `BiomeClassifyJob`, `GroundSplatJob` and `VoxelMeshJob`. `VoxelDecorPlacer` is deliberately managed: scattering is millions of cell visits of which tens of thousands survive to the expensive checks, and its inputs are reference types (`BiomeWeightField`, `PoiPadIndex`, `DecorFilter`). Moving it into a job means moving all of that into `NativeArray` for a fraction of a second.

Data flows into the jobs like this: biome weights via `BiomeWeightField.ToNativeArray`, with bilinear sampling factored into a shared `BiomeWeightSampler` (indices and coefficients computed once per pixel, not per biome); grass layers unrolled into a flat `NativeArray<GrassCountRule>` with a per-biome range (`RuleStart`/`RuleCount`); noise called straight from `FractalNoise`.

The painters are `IDisposable`: weights, rules and the road mask live in `Allocator.Persistent` for the whole build and `Build` frees them in a `finally`. Paths where a painter is created and immediately rejected (no `TerrainLayer`, no grass prototypes) must call `Dispose`, otherwise Unity complains about a `NativeArray` leak. `ClearPads` stayed managed: it only walks POI pads, not the whole grid.

**Careful with texture import:** heightmap and road mask resolutions are a power of two **plus one** (2049 at `HeightCellSize` 2), i.e. NPOT. `TextureImporter` defaults to `npotScale = ToNearest` and Unity silently shrinks such a texture to the nearest power of two. So `ConfigureTextureImporter` must set `npotScale = None`, otherwise the mask arrives one pixel smaller than the heightmap and is not applied. Biome maps are unaffected — their resolution is a power of two.

**How to check without Unity — `Tools/HeadlessCheck/`.** `Stubs.cs` holds the Unity API stubs (`UnityEngine`, `UnityEditor`, `Unity.Mathematics.Random`, NaughtyAttributes attributes) and the csproj references the **real files** from `Assets/_Dustborn/Scripts/WorldGeneration/` — not copies, so `dotnet run` immediately catches compile errors in production code. It runs the `HubPlacer` -> `RoadPlanner` -> `TerrainCarver` -> `CityPlanner` -> `PoiPlacer` pipeline, prints the generator logs and renders PNGs with a hand-written encoder. That is how three bugs invisible in a picture were caught: the footprint corner diverging from the pad, neighbouring pads fighting over a cell, and streets running point-blank along a highway on a bend. The last is measured by `CheckSpacing`: for each street it computes the fraction of its length where the nearest **parallel** road is closer than `StreetHalfWidth + RoadHalfWidth + 12` m; checking proximity without the angle is meaningless, because a street legitimately starts on a highway and legitimately crosses its neighbours.

**The harness runs the real assets and generates the whole world.** `AssetReader` parses scalar fields out of `WorldGenerationConfig.asset` and the four `Biome_*.asset` files (lines of the form `<Name>k__BackingField: value`) and assigns them by reflection. `VoxelConfig` is the exception — the harness builds one from code defaults, which is why those defaults have to match the asset. `Stubs.cs` contains an Ashima/Gustavson simplex port (`SimplexNoise.cs`) in place of `noise.snoise`, so `BiomeMapGenerator` and `HeightMapGenerator` run for real: a run starts from the biome map and the terrain, not from reading `HeightMap.bytes`.

This is **not a bit-exact copy of Unity**: the same noise algorithm, but whether it converges with `Unity.Mathematics` to the last bit cannot be established headlessly. Read the numbers as "the character of the world", not as a prediction of specific pixels. What the harness prints: area fractions per biome, height range, mean and maximum slope, the fraction of area steeper than 28° (painted by the cliff layer), the bump count (`ReportBumps`), road edge steepness (`CheckShoulders`), road smoothness (`CheckCurvature`), and then the usual road, street, lot and connectivity metrics. At the end come **per-stage timings** (`Stage` / `ReportStages`) — a hint about where to dig, not a Unity measurement: jobs in the stub are single-threaded and Burst-free, so `terrain` and `biomes` are inflated. The spread between runs is about a hundred milliseconds, so compare several consecutive runs.

**`CheckCurvature` measures smoothness, not length.** It first resamples the polyline at 30 m and only then measures the angle between adjacent segments — otherwise the metric would depend on point density rather than shape. It prints mean turn per 30 m, the maximum, the fraction of turns sharper than 25°, and total network length.

**`ReportBumps` counts local maxima on a grid of a given step.** That is the direct answer to "too many small hills": mean slope does not show it — it is the same for a gentle mountain and for ripple. A 40 m step catches the small stuff, 120 m catches real hills; the ratio of the two shows what the terrain is noisy with. `CheckShoulders` measures the ground drop from the roadbed edge to the foot of the embankment in degrees.

**The harness compiles the jobs too.** `JobStubs.cs` stubs `Unity.Burst`, `Unity.Jobs` and `Unity.Collections` (including `NativeArray<T>`), and `Stubs.cs` stubs `Unity.Mathematics.math`/`noise`. So `HeightMapJob`, `GroundSplatJob`, `VoxelMeshJob`, `GroundSplatPainter`, `BiomeWeightField`, `FractalNoise` and `NativeBuffer` build with everything else, and a typo in a job is caught by a plain `dotnet build`. The stub `Schedule` just runs `Execute` in a loop — a type and compilation check, not a behaviour check: `noise.snoise` returns zero and some of these files are never executed. `Exclude` holds only the files that pull in Unity wholesale: `VoxelTerrainBuilder`, `VoxelTerrainStreamer`, `VoxelDecorSpawner`, `VoxelDecorRenderer`, `MeshCombiner`, `RepetitionlessVoxelSetup`, `BiomeMapTexture`, `HeightMapTexture`.

**`AssetReader` also reads the real `PoiDatabase` and the nested `SettlementProfile`.** `LoadPois` parses the `_definitions` list from `PoiDatabase.asset`, resolves guids through an index of `.meta` files under `Assets/_Dustborn/` and loads all 32 `PoiDefinition`; the prefab is substituted with a stub through reflection so `IsValid()` passes. The made-up 14-definition set with inflated sizes it replaced gave 411 POI where the real one gives 546, because Synty buildings are half the size. Separately, `Apply` only parses the top level of an asset (two-space indent), and tier profiles sit at four — so profiles used to be read from **code** (`SettlementProfile.DefaultCity()`), silently, with `CoreBlockSize` 78 against the asset's 62. `ApplyProfiles` now parses all three profile blocks with their `Composition` lists (request guids resolved through the `LoadPois` index), and the numbers match the project: 127 streets and 809 lots instead of 91 and 673. `Tune` understands dotted paths, so a tier profile can be tuned without editing the asset: `dotnet run -c Debug -- TownProfile.RadiusScale=0.66 TownProfile.DowntownFraction=0.38`.

**The second check is `Tools/UnityCompileCheck/`.** Stubs are good in that they do not need Unity and bad in that they force files to be excluded — and the Repetitionless integration lives precisely in the excluded ones. So alongside sits a csproj that builds **all** of `WorldGeneration/` against the real DLLs: `UnityEngine*`, `UnityEditor` from the installed Unity, plus `com.williamschack.Repetitionless.*`, `NaughtyAttributes.Core`, `Unity.Burst/Collections/Mathematics` from `Library/ScriptAssemblies/`. Nothing executes — types only — but a typo in a `RepetitionlessMaterialCreator` or `TerrainData` call is caught in a second instead of an editor recompile. Referencing `UnityEditor.dll` and `UnityEditor.CoreModule.dll` at the same time is not allowed: types such as `AssetDatabase` are declared in both and the compiler fails with `CS0433`. Take the modules (`UnityEditor.CoreModule`, `UnityEditor.TerrainModule`), not the `UnityEditor.dll` facade: Repetitionless overloads take a `MaterialProperty`, and without `CoreModule` overload resolution fails with `CS0012` even though the same code compiles inside Unity.

**Current world character (measured on the real config, seed 1981929481).** This is the reference point: if a change moves the numbers far from it, something else is probably broken. The "4 m cell" column is the same config at `HeightCellSize` 4 and `StreetStraightBias` 0.05, kept because it is what most of the tuning above was measured on.

| | 4 m cell | 2 m cell |
|---|---|---|
| height range | 133 m | 133 m |
| mean slope | 10.9° | 11.3° |
| area steeper than 28° (cliff) | 3.6% | 4.8% |
| pine forest | 53% | 53% |
| hubs | 8 | 9 (2 cities, 3 towns, 4 villages) |
| streets / POI | 110 / 707 | 107 / 680 |
| required buildings | 33 of 33 | 34 of 34 |
| bumps per km² (40 m step) | 12.9 at 11.0 m | 12.9 at 11.0 m |
| road edge, maximum | 28° highways, 29° streets | 29° highways, 33° streets |
| highway curvature, mean turn per 30 m | 1.8° (18° maximum) | 2.3° (37° maximum) |
| street curvature, mean turn per 30 m | 4.5° (28° maximum) | 3.6° (20° maximum) |
| worst building corner over its pad | 1.30 m | 0.81 m |
| streets lying along a foreign road | 0 of 110 | 0 of 107 |

The 2 m cell costs one metric and pays for it in three: the A\* grid sees finer slope, so a highway holds one corner of 37° where the 4 m cell held 18°, and 4 samples of 499 turn harder than 25°. In exchange the pads are honest (nothing hangs over a metre against 1.30–3.70 m across three seeds), the streets are smoother, and the terrain the voxel mesher reads has twice the detail it can show.

POI by district: Downtown 103, Residential 447, Industrial 63, Rural 67. Industrial is small by construction — `IndustrialSpan` gives it a sector, not a ring. Most rejected lots cover a foreign road or a neighbour; only a handful are refused by the terrain, and the ones that stay empty are lots no prefab of their district fits.

**Small bumps are removed with amplitudes, not smoothing.** They come from the higher octaves of the `hills` billow layer and from the `detail` layer, so the cure is `HillOctaves` and the biomes' `DetailAmplitude`, not a blur — a blur takes the large forms with it. `ReportBumps` on a 40 m grid went from 23.8 per km² at 13.1 m to 12.7 at 9.7 m while the height range held, and mean slope fell from 13.5° to 10.5°. Erosion is a different tool and does not replace this one: it only touches slopes past the talus angle, and a 10 m bump on gentle ground is nowhere near it.

**Biome profiles are separated by character**, otherwise all four come out as the same hilly mush: the forest is the most rugged (`Hill` 0.22, `Ridge` 0.14) and at `RegionWeight` 1.9 covers half the map, as in 7DTD where it binds the other biomes together; the desert is flat with dunes (`Hill` 0.06, `Dune` 0.08); the wasteland is broken and fine-detailed (`Ridge` 0.08, `Detail` 0.04). `RegionWeight` divides the Voronoi distance, so weight 1.9 against 1.0 is roughly twice the region radius.

**Terrain is constrained by development, not by looks.** At `HillFrequency` 4.5 and `MaxHeight` 400 the measurement gave 17.9° mean slope and 15.7% cliffs — pretty, but a quarter fewer lots were cut and streets ran into slopes. 13–15° mean slope and 5–8% cliffs is the ceiling past which settlements start to choke. The config sits below it — 11.3° mean and 4.8% cliffs at `HeightCellSize` 2 — because the terrain was toned down to remove small bumps; the finer cell resolves steeper local slopes than the 4 m one (10.9° and 3.6%) without changing the shape underneath.

**Harness numbers are accurate to a couple of percent.** Between rebuilds the same configuration gives 61 or 63 streets, though within one run the result is strictly deterministic. Read the density numbers as direction and order of magnitude, and compare variants only within a single run.

**Burst compiles asynchronously in the editor, and one-shot jobs were running without it.** By default the first call to a job runs managed while Burst compiles in the background. For `VoxelMeshJob` that is right — it is called thousands of times per session. But `HeightMapJob`, `BiomeClassifyJob` and `GroundSplatJob` are called **once per button press**, i.e. the one run a human is waiting for went past Burst. All three now set `CompileSynchronously = true`.

**Careful with noise:** the seed offset must be added **after** multiplying by the octave frequency (`noise(p * freq + offset)`, not `noise((p + offset) * freq)`). Otherwise coordinates go past 100,000 on high octaves, float precision runs out, and rectangular seams appear in the terrain.

**The biome map** is built as a warped Voronoi rather than by noise classification: one seed point per biome is laid out on a jittered grid, each cell goes to the nearest seed, and the coordinate is distorted by a multi-octave domain warp before sampling. Hence both large connected regions and ragged borders as in 7DTD. Then a 3x3 majority smoothing and cleanup of regions smaller than `MinRegionCells`. The palette was eyedropped from `7DTDRef/7dtd-biomes-reference.jpg`: pine forest `#004000`, burnt forest `#BA00FF`, desert `#FFE477`, snow `#FFFFFF`, wasteland `#FFA800`.


## Voxel ground

Unity Terrain has been removed: the ground must be diggable, and a heightmap cannot do that. Layers 1-5 (biomes, terrain, hubs, roads, POI) survived the move without a single edit — they sit as data in `Generated/` and do not care how the ground is drawn. Layers 6-8 were rewritten for the mesh, and Repetitionless moved over via `RepetitionlessLayeredLit`.

`TerrainLayer` survived the move and nothing else of Unity Terrain did: it is an ordinary asset with a texture and tiling, Repetitionless reads exactly it, and all 14 project grounds are defined with it. "Terrain" left the names of the painters — `GroundSplatPainter`, `GroundSplatJob` — because there it meant ground, not the component.

What there will **not** be, unlike 7DTD: their buildings are voxel too, so everything is destructible. Here POI are Synty meshes, so the ground is diggable while the buildings stand intact until they are separately converted to voxels.

**Density is a function, not an array.** `VoxelDensityField.Sample(x, y, z)` returns `height(x, z) - y`: positive inside rock, negative in air, zero at the surface. That is the key to memory: a 4096x4096 world at 1 m voxels and 384 m of vertical is 6.4 billion voxels, which cannot be stored densely. Only what the player has changed, and only around them, will be stored; everything else is computed from the ready `HeightMap`.

**The heightmap is read bilinearly for voxels.** `HeightMap.SampleWorld` takes the nearest sample — at a 4 m cell that is 4 m steps, which would show on 1 m voxels. `SampleWorld` cannot be changed: all road and POI metrics are computed on it. So `SampleWorldSmooth` sits next to it, and the voxel field calls only that.

**A 32x32x32 voxel chunk computes one more layer than it draws.** Cells run from local -1 to N-1 (N+1 of them), density samples from -1 to N (N+2 of them). The slack on the negative side is mandatory: in a dual method a quad is built on the four cells around an edge, and without cell -1 the bottom face of a chunk would produce no quads at all, leaving a one-cell gap between chunks. Edges belong to exactly one chunk (grid points 0..N-1), so chunks join with neither overlap nor gaps. Vertices on a border are duplicated in both chunks but sit at the same point, because density is a pure function of coordinate.

**Surface nets rather than marching cubes, decided by measurement.** A canonical Lorensen MC gave **754 unclosed edges inside chunks** over a 256 m patch — the known MC ambiguity, where two neighbouring cells with complementary configurations triangulate their shared face differently. The holes are sub-voxel but you can see through them, and no watertight collider comes out of that. Closing it means MC33 with ~730 sub-cases; a dual method has no ambiguity by construction, so that is what runs.

`VoxelChunkMesher` works like this: every cell with a sign change on at least one of its 12 edges gets **one** vertex — the average of the crossing points; then for each grid edge with a sign change a quad is emitted over the four cells around that edge. The surface is always closed, because every such edge gives exactly one quad.

**Quad winding is mirrored on two axes out of three.** Walking the four cells around an edge in the order (0,0), (0,1), (1,1), (1,0) gives the correct normal for a Y edge but the inverse for X and Z, because those axis triples have different chirality. Get it wrong and 28% of the triangles face into the ground, which `CheckWinding` reports.

**Watertightness must be checked per edge, and the metric is easy to misread.** An edge shared by two triangles is closed; one means a hole; four means a pinch. Two things make the check lie: dual-method vertices sit **inside** cells rather than on chunk faces, so the patch-border tolerance has to be a couple of voxels rather than a centimetre, and an edge key hashed from coordinates reports its own collisions as holes — coordinates are quantised to 1/64 m and packed exactly into a `long`.

Current measurement on a 256x256 m patch around (1024, 1024), 32³ chunks at 1 m (seed 278376917 — at another seed mesh density shifts by tens of percent; that is terrain, not code):

| | value |
|---|---|
| vertices / triangles | 98 150 / 182 862 |
| mesh density | 2.79 triangles per m², ~47 M for the whole world |
| unclosed edges inside the patch | **0** |
| non-manifold edges (shared by 4 triangles) | 37 (0.013%) |
| vertex distance from the isosurface | 1.2 cm maximum |
| triangles with reversed winding | 1 of 182 862 |
| meshing, C# in Release without Burst | 2.7 ms per chunk with geometry |

Those 37 non-manifold edges are a surface pinch inside a cell, a standard quirk of naive surface nets. The surface is still closed, `MeshCollider` and rendering handle it fine; it only bothers algorithms that require strict manifoldness (some mesh simplifiers).

**Meshing speed is bound by heightmap sampling, not by the cell walk.** 2.5 ms per chunk with geometry, 0.7 ms averaged over all chunks, down from 5.3 ms in Release. An early exit on the eight corner signs (`Straddles`) gave only 5.3 -> 4.2 ms. The rest came from the sampling: 34³ = 39304 bilinear reads per chunk, **for every chunk including empty ones**, while only 67 of 256 produce geometry. Density is `surface(x, z) - y`, so height is needed once per column — 1156 reads — and a chunk entirely above or below the surface range of its footprint is skipped before the block is filled at all. The same rule shapes `VoxelDensitySampler.Normal`: the vertical difference of a column-separable field is a constant, so the normal is `normalize(-dh/dx, 2 * step, -dh/dz)` from four surface reads rather than six.

Block filling lives in `VoxelDensityField.Fill` rather than in the mesher, deliberately: the field knows how to compute a block fastest. When caves arrive, density will stop being column-separable and the optimization will move inside `Fill` without touching the mesher.

**A few milliseconds is still too much for the main thread.** One pickaxe hit touches one to eight chunks, i.e. 3-20 ms against a 16 ms frame, so meshing lives in jobs.

**`VoxelTerrainBuilder` builds a patch, not the world.** The `Build Voxel Terrain` button takes `HeightMap.bytes`, a centre and a chunk count per side, computes the vertical range from the minimum and maximum surface in the patch footprint, and lays the finished meshes out into `GameObject`s with `MeshFilter`, `MeshRenderer` and (optionally) `MeshCollider`. More than 65000 vertices in a chunk switches the mesh to 32-bit indices — at 32³ that never happens, but with a larger chunk it would happen silently.

The whole world is not built with one button and should not be: 4096x4096 at 1 m voxels is ~47 M triangles, which must be streamed. The builder exists to look at a piece of the world. It is excluded from the harness in `HeadlessCheck.csproj` — it pulls in `TextAsset`, `Mesh` and `MeshCollider`, which the stub does not have; compilation is checked by `Tools/UnityCompileCheck`.

**Meshing lives in a job under Burst.** `VoxelMeshJob` is an `IJob`, one job per chunk. What runs in parallel is chunks, not cells within a chunk: vertex indices inside a chunk are assigned sequentially and cannot be parallelized, and there are always many chunks.

The move needed three fixes, all about things Burst cannot do:

1. **Managed static arrays are not readable from a job.** `VoxelCellTables` with its `static readonly int[]` stays as the human-readable definition, while `VoxelCellLayout` copies the same tables into a `NativeArray` once per mesher and is passed to the job as a field. Deriving corners and edges arithmetically would be cheaper but unreadable, and the tables are copied only once anyway.
2. **The density field was split into a class and a struct.** `VoxelDensitySampler` is a `struct` with a `NativeArray<float>` of heights and all the math (`Surface`, `Sample`, `Normal`, `Fill`); that is what goes into the job. `VoxelDensityField` is the managed wrapper that owns the native memory, lives in the editor and hands out the `Sampler`. The formula is not duplicated: only the struct computes.
3. **`VoxelMesh` moved to `NativeList`.** `Mesh.SetVertices` and `SetIndices` also take a `NativeArray` directly through `AsArray()`, with no intermediate copy.

The arithmetic moved from `Mathf` to `Unity.Mathematics.math`, as in the project's other jobs. `math.lerp` does not clamp `t`, unlike `Mathf.Lerp`, but here `t` lies in [0, 1) by construction, and the output after the move is **bit-for-bit identical**: the same 98150 vertices, 182862 triangles, zero unclosed edges, the same 37 pinches.

**One mesher meshes one chunk at a time, guaranteed by the code.** The scratch buffers (`Columns`, `Density`, `VertexAt`) are shared per mesher, so two jobs from one mesher would fight over them. `Schedule` does not take an external dependency but chains the job onto the previous one (`_pending`). Parallelism comes from a mesher pool — one per thread — not from a pool of jobs on one mesher. Speed under Burst cannot be measured by the harness: in the stub `Schedule` calls `Execute()` on the same thread and `NativeList` is a wrapper over `List<T>`, so the 2.7 ms is still plain C# in Release.

**Materials go through world projection, not vertex color.** Vertex colour has four channels against the project's 14 layers, and a per-chunk palette would multiply materials, each of which Repetitionless bakes with its own texture arrays. So what already works is reused: `RepetitionlessLayeredLit` declares exactly the same `_Control0.._Control7` and `_MAX_LAYERS_*` as the terrain shader, and `_UVSpace` supports world UV. The splat map is baked in world coordinates by the same `GroundSplatJob` rules without touching a single ground setting. The honest limitation: it is a top-down projection, so cave ceilings and vertical walls get the ground of whatever is above them; they will later need triplanar or vertex color as a second channel.

**Pink ground means the shader, not a setting.** Two cases, both closed in code: `RepetitionlessLayeredLit` sat in a hidden package folder and was not found by `Shader.Find`, and `RepetitionlessVoxelSetup` switched the shader only on a **new** material — an existing one kept the terrain shader forever, which does not render on a plain `MeshRenderer`. Now the shader is checked on an existing material too, and if it is not found the log says exactly where to look for it in the package.

**Material and splat map are made by one piece of code for builder and streamer** (`VoxelGroundMaterial`): both show the same ground and must not diverge. Repetitionless bakes texture arrays with editor API, so for the streamer it is a `Bake Ground Material` button rather than startup work: the material is made once and stored as a reference in the component.

**The splat map in world coordinates.** Ground rules are neither rewritten nor duplicated — `GroundSplatPainter` computes them on one grid for the whole world and returns a flat buffer. Biomes, patches, slope, height, roads, the layer budget, the by-area order — all shared with the voxel path. `VoxelSplatBaker` packs those weights into an RGBA32 `Texture2D`: channel `k` of texture `Control{k}` is layer `k * 4 + channel`, exactly as in Unity Terrain alphamaps. At 14 layers that makes 4 control textures, assigned to `_Control0.._Control3` with `_LayersCount` set.

**The mesh needed a UV0, found by reading the shader rather than guessing.** `_UVSpace` controls where the UV of the **ground layers** comes from, but the control texture is sampled in `SampleRepetitionlessTerrain` directly with the incoming `input.uv`, i.e. the mesh UV0. Surface nets have no UV, so the job writes `uv = (x, z) / WorldSize` per vertex and the world splat map lands on top without an unwrap. `_UVSpace` is still 1: ground tiling must come from the world position, otherwise textures would stretch across all 4096 m following UV0.

**Slope is no longer asked of Unity, the only new math.** The terrain path used `TerrainData.GetSteepness`; voxels have no `Terrain`, so `VoxelSplatBaker.Surface` computes slope from the heightmap by central differences — `atan(|grad|)` in degrees — plus the normalized height for the height-band rules. Cross-checked against the harness on the same world at `_controlResolution` 2048: mean slope **11.2° against 11.3°**, maximum **60° against 61°**, area steeper than 28° **4.5% against 4.8%**. That agreement is the point of matching the control resolution to the height cell — at 1024 the same world read 10.9° and 3.6%, i.e. the cliff layer was painted from a smoother terrain than the one being meshed.

**Decor: grass, trees and rocks on a mesh.** Unity Terrain gave GPU instancing, LOD and distance culling for `detailPrototypes` and `treeInstances`; a mesh gives none of it, so all three are reduced to one description, `VoxelDecorLayer`. `ScatterLayer` maps one to one; `GrassLayer` specifies bushes per square meter, so the grid step is derived as `0.8 / sqrt(Density)` and the probability as `Density * step²`. The 0.8 factor keeps the probability noticeably below one: at a step of exactly `1/sqrt(Density)` a bush lands in every cell and the grid reads by eye. Grass width and height come as separate ranges, scatter as a single scale; they are reconciled through `HeightScale` — a constant vertical multiplier — while `Squash` stays a random spread in both. The "where can it be placed" rules are factored into `DecorFilter`: road, building pad, biome weight, noise patch in one place, with slope and height arriving from the density field.

Harness measurement on synthetic layers (512x512 m patch, layers injected into all four biomes so per-biome sums are comparable with the requested density):

| | requested | placed |
|---|---|---|
| trees | 120 /ha | 112.6 /ha |
| rocks | 20 /ha | 18.9 /ha |
| grass | 2.4 bushes/m² | 2.30 bushes/m² |

The shortfall is slope rejection (`MaxSlope` 30° for trees, 32° for grass), and it shows: the maximum slope under a placed object lands exactly on the layer limit. In total 1.9 M candidates of ~2.5 M were rejected.

**The combiner decides what to combine.** `MeshCombinePlan.Decide` is separated from the combining itself: the rules are plain C# while `Mesh.CombineMeshes` only exists in Unity, so the decisions are harness-checked and the combining is not. Rules in descending priority:

| prefab profile | decision | why |
|---|---|---|
| has a `LODGroup` | do not combine | combining bakes LOD 0 forever and the forest renders at full detail a kilometer away |
| has a collider and decor colliders are on | do not combine | a combined mesh has no per-object collider |
| fewer than 4 copies | do not combine | combining saves nothing |
| one copy heavier than the batch budget | do not combine | not even one instance fits a batch |
| mesh has Read/Write off | do not combine | `CombineMeshes` reads geometry from the CPU and it is not there |
| no mesh | skip | nothing to bake |
| everything else | combine | in batches of `budget / vertices per copy` |

**The Read/Write point is not theory: every Synty decor mesh has `isReadable: 0`**, the importer default. `CombineMeshes` cannot read such a mesh, so combining silently produced empty batches and flooded the console. The fix is routing, not the import setting: a non-readable prefab with one mesh and one material goes to `Instance` (`DrawMeshInstanced` does not need readability), the rest to `Instantiate`. Enabling Read/Write would put a second copy of every mesh in system memory for the worse of the two paths.

Combining happens **inside a chunk-sized cell**, not across the whole patch: a batch is culled as a whole, so one mesh for the entire world would kill culling. `_batchVertexBudget` is the size of that compromise: a bigger batch means fewer draw calls but coarser culling.

**Grass on a mesh is expensive:** at full asset density a 512 m patch carries **603 thousand bushes**, and a mesh has no `detailObjectDistance` to cut them. Three knobs multiply into that number — `GrassDistance`, the biome layers' `Density`, and the builder's `_grassDensity`.

**Streaming and LOD.** The whole world does not fit in a scene — 4096x4096 at 1 m voxels is 45.8 M triangles and 1.34 GB of mesh data. So `VoxelTerrainStreamer` keeps rings of chunks around the player and builds them as they move, while `VoxelTerrainBuilder` stays a button to look at a piece in the editor.

The rings are computed by `VoxelStreamPlan`, which is **Unity-free** and therefore harness-checked. Level L doubles both the voxel size and the radius: with a 96 m base and five levels that gives 32 m chunks of 1 m voxels up close and 512 m chunks of 16 m voxels out at 1536 m. Five is the ceiling worth paying for on a 4096 m world — ring 4 already covers 13.6 of the 16.8 km², and a sixth ring only meshes the clamped map border.

**Ring boundaries are aligned to the next level's grid, otherwise there is a hole or an overlap.** The rectangle of level L is rounded so that the minimum index is even and the maximum odd: then it consists of a whole number of level L+1 chunks, and "cut out the middle" of the next ring is exact. Measurement: 76729 probes around the player, **0 holes, 0 overlaps**, no point with too coarse a level up close.

What LOD buys, measured by actually meshing the rings around a point:

| level | voxel | columns | triangles | per m² | area |
|---|---|---|---|---|---|
| 0 | 1 m | 64 | 151 110 | 2.31 | 0.07 km² |
| 1 | 2 m | 48 | 136 880 | 0.70 | 0.20 km² |
| 2 | 4 m | 48 | 133 772 | 0.17 | 0.79 km² |
| 3 | 8 m | 32 | 88 988 | 0.04 | 2.10 km² |
| 4 | 16 m | 52 | 144 038 | 0.01 | 13.63 km² |

That is **655 thousand triangles over 16.8 km²** against 45.8 M if everything were built at level 0 — a **70x** win. Note the distribution: every ring costs roughly the same, 90–150 thousand triangles, even though area grows fourfold. That is a sign of a correctly chosen doubling step, and it is also why the fifth ring is worth its price — it doubles the horizon to 1536 m for 28% more triangles. A sixth would not: ring 4 already covers 13.6 of the world's 16.8 km², so ring 5 would mesh mostly the clamped map border.

**At a ring join the two chunks must not both cover the strip between them.** Chunk geometry runs over cells -1..Size-1, i.e. it hangs one cell past its own origin on the `-X` and `-Z` faces. Inside a ring that overhang is exactly how neighbours stitch — each grid edge belongs to one chunk and the shared cell only donates a vertex. Across a ring boundary the grids do not line up, so the overhang lies on top of the finer ring: a strip one coarse voxel wide, up to 16 m at lod 4, of two near-coplanar surfaces z-fighting, and it slides around as the player moves. That is the line people see along chunk borders.

`VoxelColumnKey.Trim` carries two bits, set by `VoxelStreamPlan` when the `-X` or `-Z` neighbour column belongs to a different ring, and `VoxelMeshJob` then refuses to place a vertex in that cell. Every quad that would have used it drops out through the existing "no vertex, no quad" guard, so the surface now ends exactly on the boundary and the crack left behind is the one the skirt exists for. The bits live in the key rather than beside it because the boundary moves with the player, and a column has to be rebuilt when its trim changes. Harness (`CheckTrim`): the overhang of a lod-1 chunk goes from 1.95 m — one full voxel — to -0.02 m, and 89 of 244 columns carry a bit.

**Cracks at level joins are covered by skirts, not fixed.** A neighbouring chunk of another LOD builds its surface on its own grid, and a discrepancy remains at the border — larger the coarser the grid. `VoxelMeshJob` hangs a curtain along the chunk edge, `SkirtDepth` voxels down; it does not reconcile the surfaces, it hides the crack. That is the standard trade in voxel worlds: honest stitching needs transition cells (Transvoxel or a dual-method equivalent) and costs incomparably more.

**The curtain must run along the mesh edge, not as a patch per vertex.** A separate quad per border vertex with a **horizontal** top edge protrudes above the surface by roughly half the per-voxel drop — at an 8 m voxel and a 20° slope that is a metre and a half, standing along every chunk border of the far rings and reading as a grid of seams. `BuildSkirt` instead walks the border cell columns, takes the topmost vertex in each and stretches a quad **between adjacent columns**: the curtain's top edge is the mesh edge itself, so it has nowhere to protrude, and the bottom lies flat at `min(heights) - SkirtDepth`, covering any vertical step between columns. The skirt also moved from cell 0 to cell -1 on the `-X` and `-Z` faces, where the mesh extends one cell outward (see the negative-side slack above).

Harness measurement (`CheckSkirtFit`, vertices more than 0.25 m above the surface; base voxel 1 m, which is what the project runs):

| | before | after | bare mesh, no skirt |
|---|---|---|---|
| lod 1 (2 m voxel) | 38, max 0.43 m | **0** | 0 |
| lod 2 (4 m voxel) | 63, max 0.84 m | **0** | 0 |
| lod 3 (8 m voxel) | 283, max 2.57 m | **158, max 0.83 m** | 148, max 0.83 m |

The remainder on lod 3 is not the skirt but the coarse grid's own error: the same chunk without a skirt gives 148 vertices at the same maximum. Comparing against the bare mesh is mandatory for exactly that reason.

Curtain winding had to be derived per face: the outward normal points negative on `-X` and positive on `+X`, so the quad walk direction depends on the face. The first version emitted a double-sided quad to avoid thinking and cost **+27%** triangles per chunk; single-sided with correct winding costs **+13%**, and after the move to the mesh edge **+11%**. Skirts are not built at level 0 — there is no coarser neighbour below it.

**Occlusion culling has to be the GPU one.** Baked occlusion (Umbra) cannot see this world at all: it bakes static geometry in the editor and every chunk here is created at runtime. Unity 6's GPU Resident Drawer can, and it is the reason to keep the ground and the decor as ordinary renderers — `m_GPUResidentDrawerMode` 1 with `m_GPUResidentDrawerEnableOcclusionCullingInCameras` 1 in the pipeline asset builds a depth pyramid and culls per instance on the GPU, runtime objects included. It does not reach the grass, which goes through immediate-mode `DrawMeshInstanced` rather than through renderers.

**Streaming builds ahead of the walk, not around the stand.** `_lookAhead` pushes the ring centre along the direction of travel and the build queue is sorted from that same point. The heading follows **travel, not gaze** (`Steer` smooths the position delta): tying it to the camera would move the rings on every look around.

**What the scene has to hold up its end of, or none of this shows.** Four settings outside the streamer decide whether the seams it avoids are visible anyway:

- **Camera far clip must exceed the view distance.** At `_lodCount` 5 the rings reach 1536 m; a 1000 m far plane cuts the outer ring in half and chunks appear and vanish at that radius as the camera turns. The scene camera is at 1700.
- **Fog has to be on and end near the view distance.** Without it the outermost ring is drawn at full contrast and every column that streams in is plainly visible. Linear 700..1500 puts the build edge inside the fade.
- **Shadow distance** in the URP asset was 50 m, so shadows appeared right in front of the player; 150 m puts the switch far enough back to read as distance rather than as a bug.
- **The `VoxelTerrainBuilder` preview must not be left in the scene.** Its chunks and its decor are saved with the scene, and decor placement is deterministic — so in play mode the streamer builds the same trees in the same places and every one is drawn twice, z-fighting on the foliage. `Awake` disables the leftovers and says so in the log; `Clear` removes them.

**Synty `LODGroup`s pop because they do not cross-fade.** The prefabs ship with `fadeMode` None and a last level that culls at about 1.9% of screen height — a 5.7 m tree therefore vanishes in a single frame at roughly 260 m, and walking back and forth across that radius makes it blink. `BiomeDatabase.Fade Scatter LOD Switches` walks every prefab the biomes reference and switches its group to `CrossFade` with a transition width, which URP dithers over the band (`m_EnableLODCrossFade` is already on in the pipeline asset).

**What the streamer decides on its own:** a chunk mesh takes 16-bit indices unless it needs more than 65000 vertices, which at 32³ it never does. Colliders are built only on ring 0. You can only walk on the near ring, and a collider on a coarse chunk would not match the ground you see. Columns start at `_columnsPerFrame` per frame, so jumping across the map costs frames rather than a freeze, and recomputing the desired set only runs when the player has moved `_refreshStep`.

**Streaming meshes one frame ahead.** `Schedule().Complete()` per chunk means jobs on paper and none in practice, so the frame is split: `Dispatch` queues chunks into `VoxelMeshQueue` and calls `JobHandle.ScheduleBatchedJobs()`, and `Collect` at the **start of the next** frame picks up the results and uploads them into `Mesh` — uploading stays on the main thread, that is Unity API.

Parallelism comes from a mesher pool (`_meshWorkers`, 8 by default) rather than a job pool: a mesher has shared scratch buffers, so two jobs from one mesher would fight over them. A slot is a mesher plus its own `VoxelMesh`, about 0.3 MB. For the pool's sake a mesher is not tied to a LOD — that would need `LOD x workers` of them — so LOD and trim are arguments of `Schedule`.

**A column may not fit into the pool, and it must not be truncated** — that leaves a hole in the ground exactly where the chunks ran out. Column height comes from the terrain in its footprint, so there can be more chunks than free slots; the streamer remembers the current column (`_active`) and a cursor `_activeY` and tops it up over the following frames. `Refresh` can evict a column that is still building, so it clears `_hasActive` if it unloads `_active`; in-flight chunks need no separate guard, because `Collect` runs in `Update` **before** `Refresh`.

**A column's decor is spread across frames, and `_columnsPerFrame` is not enough for it.** `Open` only creates a `VoxelDecorBuild` — a request with a cursor over layers and over instances within a layer — and queues it; the work is done by `Decorate` in `Update` with a budget of `_decorPerFrame` (128 units by default). Without it one coarse-ring column froze the game solid: a ring-4 column is 26 hectares, and at 120 trees per hectare `Open` did **three thousand `Instantiate`** of Synty prefabs with `LODGroup` in a single frame.

**The decor budget is milliseconds, not work units, because the three paths cost wildly different amounts.** Placing one prefab, scanning a layer over a column footprint and filling a matrix array for instancing were all charged in the same invented unit, and the scale was wrong by more than ten times in both directions — a near-ring grass column was priced at twenty frames of work that takes a fraction of one. `Decorate` now runs a stopwatch to `_decorBudget` (6 ms) and stops between slices.

**And the column rate has to be capped by the decor queue, not only by `_columnsPerFrame`.** A column is not shown until its decor is done, so opening columns faster than they can be decorated does not make ground appear sooner — it makes the queue grow, the retired columns pile up past `RETIRED_LIMIT`, and the eviction there punch the holes it exists to prevent. `Dispatch` refuses to open another column while `DECOR_BACKLOG` of them are still waiting, and `Evict` picks a retired column that covers nothing unfinished, falling back to the farthest one rather than the oldest.

**Decor range is expressed in rings, so it is read against `VoxelSize`.** At the project's `VoxelSize` 1 a ring-0 column is 32 m and the near ring is 0.07 km², which is what `GrassMaxLod` 0, `RockMaxLod` 1 and `TreeMaxLod` 2 were sized for: 157 thousand grass cards, 527 rocks and 12 thousand trees around the player. Raising `VoxelSize` widens every ring by the same factor and squares the area — at `VoxelSize` 4, `TreeMaxLod` 2 would mean 4.2 km² covered in prefabs — so a coarser voxel has to be paid for by lowering these.

**A column stays hidden until it is finished, and its predecessor lives until its replacement is finished.** `Refresh` used to demolish an outdated column immediately while its replacement appeared several frames later — a hole gaping ahead of the running player, worst at the boundary of rings 0 and 1. The swap is now atomic, resting on two rules:

1. `Open` creates the column **disabled** and remembers how many chunks it is waiting for (`Column.Remaining`). `Collect` decrements the counter for every collected chunk — **including empty ones**, otherwise a column over a cliff would never finish counting. `Column.Ready` also waits for the column's `VoxelDecorBuild`.
2. An outdated column is not demolished but moved to `_retired` and **keeps rendering**. `Release` frees it when no unfinished column intersects its rectangle — neither from `_pending` nor from `_loaded`. Intersection is computed in meters, because a 256 m ring-1 column is overlapped by **four** ring-0 columns and must survive until the last of them.
3. A finished column is revealed by `Promote`, not by whoever finished it, and **what it waits for depends on what is underneath**. Where a retired column still overlaps, it waits for `Ready` — decor included — and `Release` runs first in the same frame, so the group swaps at once with no doubled surface. Revealing each replacement as it finished was the half that was missing: the old column kept drawing the same ground and the same deterministic decor under the new one, two surfaces a centimetre apart z-fighting and every tree drawn twice. Where nothing is retired underneath, the column is shown as soon as it has geometry: waiting for its grass on fresh ground buys nothing and costs a hole.

The swap is therefore atomic in both directions: no hole, and no doubled surface. If the player turns back, `Revive` pulls the column out of `_retired` whole, with no rebuild. Both `Release` and `Promote` cost `retired x (queue + loaded)`, so they run behind `_releaseDue` — set when a column gets its last chunk, when a decor build finishes and when the wanted set changes, which is every way the answer can change.

**The build queue is sorted by distance to the player**, because `VoxelStreamPlan.Around` emits columns row by row and a column dead ahead would otherwise be built after one left behind. `_byDistance` is cached in a field, or `List.Sort(Comparison)` allocates a delegate per call. Ring order survives the sort by itself: a ring-0 column is closer than any ring-1 column by construction.

**Trim churn is the price of the ring-join fix.** A column whose `-X` or `-Z` neighbour changes ring gets a new key and is rebuilt, and ring rectangles shift every two chunks of their own level — so walking moves a band of ring-edge columns through a rebuild. They are covered while they rebuild, so nothing is visible, but it is real work on top of the columns genuinely entering the view. If it ever needs to go, setting `Trim` to zero brings back the overlapping strip and nothing else.

**Unloading mirrors loading and is also spread out.** `Demolish` disables the column with `SetActive(false)`, marks its decor request done and queues it for demolition, which proceeds at `_decorPerFrame` children per frame. A child is detached from its parent (`SetParent(null)`) before `Destroy` — in play mode `Destroy` is deferred, and without detaching `childCount` would not drop within the frame, so the demolition loop would spin forever.

**Collision cooking is off the main thread.** `MeshCollider.sharedMesh` cooks synchronously, milliseconds per chunk, so `Spawn` only puts the "holder + mesh" pair into `VoxelColliderQueue`; `Dispatch` at the end of the frame runs `Physics.BakeMesh` in an `IJobParallelFor`, and `Collect` at the start of the next frame attaches the `MeshCollider`. The cooking options of the job and the collider must match (`EnableMeshCleaning | UseFastMidphase`), or the baked result is not picked up and the mesh is cooked again on the main thread. `CookForFasterSimulation` and `WeldColocatedVertices` are off: the ground is static and surface nets already weld their vertices.

**The builder refuses to build what would kill the editor.** `VoxelBudget` estimates a patch's weight before starting: 2.73 triangles per m² at a 1 m voxel, then as the square of the voxel size, plus 32 bytes per vertex and 12 per triangle, plus the same again for colliders. Mesh density depends on terrain (measured 2.3..2.8 triangles per m² at a 1 m voxel), and the constant is deliberately the upper bound — the estimate must err toward refusal rather than a crash, leaving about 20% headroom. Over `_memoryBudget` the build does not start and the log says what it would cost and which `VoxelSize` or patch size would fit.

**Decor travels with the chunks, and that also fixes grass.** The streamer builds a column's decor when the column loads and attaches it to the same `GameObject`, so unloading takes the grass and trees with it — no separate bookkeeping.

Each kind's range is set in `VoxelConfig` by the number of the coarsest ring: grass to ring 0, rocks to ring 1, trees to ring 2. That is what the old terrain's `detailObjectDistance` was, expressed in LOD rings. Trees reach furthest because they read as the silhouette of the landscape.

Measured around the player at 1536 m (synthetic layers at forest density in every biome, an upper bound): **157 082 grass** in ring 0 alone (0.07 km²), **12 548 trees** over rings 0–2 (1.06 km²), 518 rocks over rings 0–1. The point is not the numbers but that **the amount of grass no longer depends on draw distance** — the builder used to give 603 thousand bushes on a 512 m patch and grow quadratically, while the near ring holds its own. Hence `_grassDensity` 1 rather than 0.25.

**LOD rings are too coarse a measure for grass.** A ring is a rectangle of whole columns and `VoxelStreamPlan` clamps `_nearDistance` from below by the column size, so the ring that carries grass is always at least as wide as a column and its corners reach a good deal further than its edges.

So grass has its own range in metres — `VoxelConfig.GrassDistance`, 96 m by default, which the card shader dissolves against. Its ceiling is the near ring: grass lives only there (`GrassMaxLod` 0), and at `VoxelSize` 1 with `_nearDistance` 96 the guaranteed radius is exactly 96 m. Past that the rectangular ring shows through instead of a circle. Whatever still instances is culled by `VoxelDecorRenderer` **per batch, not per holder** — a holder is column-sized and the camera is almost always inside it. `Setup` lays matrices into grid cells of `drawDistance * TILE_FRACTION` (no smaller than `MIN_TILE`), remembers each batch's `Bounds` and skips those beyond the limit, with a `HYSTERESIS` band so a batch on the boundary does not blink; the holder's `Bounds` is checked first.

Hence a requirement on the plan: grass must go to `Instance`. A combined `MeshRenderer` does not know its own distance, so `MeshCombinePlan.Decide` takes `distanceCulled` and returns `Instance` for an instanceable profile regardless of copy count.

Grass shadow casting is off (`ShadowCastingMode.Off`), receiving is left on: cascaded rendering of hundreds of thousands of cards costs more than everything else combined, and grass without its own shadow does not read as a bug in a stylized world.

`VoxelDecorRenderer` is marked `[ExecuteAlways]` — otherwise instanced decor from the builder does not render in the editor at all (`LateUpdate` does not run outside play mode). It takes the viewpoint from `Camera.main`, or in the editor from `SceneView.lastActiveSceneView`; with no camera it draws everything, since extra grass beats invisible grass.

**The combiner gained a fourth decision — instancing.** 157 thousand bushes by combining is 9.4 M duplicated vertices, on the order of 300 MB: combining trades geometry copying for fewer draw calls, and at those counts the trade stops being worth it. If a prefab is instanceable (exactly one mesh and one material, no `LODGroup`) and copies times vertices exceeds `DUPLICATE_CEILING`, the plan returns `Instance` and `VoxelDecorRenderer` draws them via `Graphics.DrawMeshInstanced` in batches of 1023 — geometry is not copied at all, only matrices are paid for.

**Instancing is not free per frame, and for a tiny mesh it is the expensive option.** `Graphics.DrawMeshInstanced` hands its whole matrix array to the GPU on every call on every frame; the near ring at 157 thousand cards is 10 MB a frame, and the profiler put `VoxelDecorRenderer` at **30.8%** of the frame. A combined mesh is an ordinary renderer: uploaded once, then culled and batched by the engine for nothing. So a mesh at or under `TINY_MESH` vertices — a grass card is eight — is combined even when its layer has a draw distance, and only the `DUPLICATE_CEILING` still forces instancing on it, which per column it never reaches (2400 cards is 19 thousand vertices against a 400 thousand ceiling). What is lost is the per-frame distance cull: grass now ends at the ring boundary rather than on a circle, which is where the ring put it anyway.

The final system choice, all eleven cases harness-checked (rules top to bottom):

| profile | decision |
|---|---|
| `LODGroup` | objects, LOD must survive |
| collider needed | objects |
| fewer than four copies | objects |
| one copy heavier than the batch budget | objects |
| mesh without Read/Write | instancing if the material supports it, otherwise objects |
| layer culled by its own distance, mesh over `TINY_MESH` | instancing |
| a small mesh in huge numbers | instancing |
| moderate numbers | combining in batches |
| no mesh | skip |

`Instanceable` means not only "one mesh and one material without `LODGroup`" but also **`enableInstancing` raised on the material itself**. A material without the flag makes `DrawMeshInstanced` throw every frame, so the check sits in `MeshCombiner.Probe`, not in the calling code.

**A mesh does not die with its GameObject, and that is the leak that took the editor down at 3.6 GB of `VertexData`.** `Destroy(gameObject)` removes the object from the scene, but a `Mesh` is an asset, not a component: Unity frees such objects only through `Resources.UnloadUnusedAssets`, which nobody calls in play mode and which arrives on its own schedule in the editor. So every unloaded streamer column, every combined decor batch and every repeat press of `Build Voxel Terrain` left its vertices in memory forever. `MeshCombiner` added to it by creating an intermediate `Mesh` per material per batch and never destroying it — hundreds of dangling meshes per build on grass.

Ownership is expressed by a `GeneratedMesh` component: `Own(holder)` is attached next to the `MeshFilter` in exactly the three places where a mesh is actually created by code — `VoxelTerrainStreamer.Spawn`, `VoxelTerrainBuilder.Spawn`, `MeshCombiner.Build`. The marker is needed because walking the hierarchy by `MeshFilter` is categorically wrong here: decor the combiner left as separate objects has the **prefab's mesh** in its `MeshFilter`, and such a walk would erase Synty assets from the project.

**Freeing is called explicitly, not from `OnDestroy`, and that is essential.** The first version rested on `OnDestroy` and failed exactly where the leak hurt most: **lifecycle events do not run in edit mode for a MonoBehaviour without `[ExecuteAlways]`**, and `VoxelTerrainBuilder.Clear` demolishes chunks with `DestroyImmediate` precisely in the editor — after `Clear` nothing was freed. So demolition goes through `GeneratedMesh.Destroy(holder)`, which walks the `GeneratedMesh` components in the hierarchy, frees their meshes and only then removes the object. `OnDestroy` is kept as a backstop for play mode.

Verify this in the Memory Profiler by the `Mesh` row in the `Unity Objects` tab, not by total process size: `Clear` does not and should not free the baked Repetitionless arrays, the control textures, or Synty assets loaded for decor, so the final number never returns to the original. Take snapshots before the build, after the build and after `Clear`: `Mesh` must return to its original value.

The second mine in the same place: the mesh is **not stored in a field**. A private non-serialized field is nulled on domain reload, so after the first recompile ownership would be lost and the leak would return. `Release` takes the mesh from its own `MeshFilter`, keeps no state and survives recompilation. There is an `AssetDatabase.Contains` check in case a project mesh ends up in the `MeshFilter`: `DestroyImmediate` would delete the file. `WorldGenerator` previews are fixed the same way — `Replace(ref field, texture)` destroys the previous `Texture2D` before assigning, but only if it is not in the project, since the preview fields are serialized and a real asset can be dropped into them. One `Generate And Save All` made three 1025² hillshades plus a mask and a biome map, about 13 MB per press.

**Control textures are saved as assets rather than living in memory.** `VoxelSplatBaker` used to return fresh `Texture2D`, set on the material via `SetTexture` and lost on the first domain reload — so after every entry into play mode the material had no splat map, it had to be rebaked, and every bake left the previous set in memory. `VoxelGroundMaterial.Persist` puts them next to the material (`VoxelMaterial_Control0.asset` and so on), `Release` destroys the previous ones only if they are not assets, and the material is marked dirty and saved after assignment — otherwise the references would not survive an editor restart.

**Control texture resolution follows `HeightCellSize`, it is not a free knob.** The splat is computed from the heightmap and from the road mask, both at the height cell, so anything finer is pure upsampling and anything coarser throws away source data: at `HeightCellSize` 2 over a 4096 m world that is `_controlResolution` 2048, two metres per texel. It is not cheap — four control textures of 16 MB instead of 4 MB, and the intermediate weight buffer in `GroundSplatJob` is `resolution² * layers` floats, i.e. 235 MB at 2048 against 59 MB at 1024, held for the length of the bake. Roads are what the step buys most visibly: a 10 m roadbed is 2.5 texels at a 4 m texel and 5 at a 2 m one. The `_controlResolution` field is serialized, so components already in the scene keep the old value — fix it by hand. One more copy left the path there: `GroundSplatPainter.BakeWorld` used to return a `float[]` (the same 235 MB in the managed heap and the LOH) and now returns a `NativeArray<float>` read directly by `VoxelSplatBaker.Pack`, which the caller must dispose. `Surface` and `Pack` moved to `Parallel.For` — 2048² texels is 21 M bilinear heightmap samples, previously on one thread.

**What is next in this layer**, in dependency order:

1. **Edits and storage.** Digging proper. A sparse overlay over the procedural density: only what was dug is stored.
2. **Caves.** 3D noise in the density field, inside `VoxelDensitySampler.Fill`. Needs `noise.snoise(float3)` — the harness stub currently has only the 2D one, so an Ashima 3D port has to be written.
3. **A\*.** `NavmeshCut` on dug-out areas, `UpdateGraphs(Bounds)` with `batchGraphUpdates` for newly walkable surface. Never `Scan()` at runtime.


## Work plan

### Next step: run the pipeline in the editor

The data is laid out: four biomes with their grounds, grass, trees and rocks, 32 `PoiDefinition` across four districts, 14 `.terrainlayer`. What remains is what can only be done in Unity.

1. **Rebake the ground material.** `Generated/VoxelMaterial.mat` and its `VoxelMaterial_RepetitionlessData` folder were baked before the `.terrainlayer` tile sizes were corrected, and capacity cannot be widened after the fact. Delete both and let `RepetitionlessVoxelSetup` build them again on the next `Build Voxel Terrain` or `Bake Ground Material`.
2. **Measure the POI.** Open `Content/World/PoiDatabase.asset` and press `Measure Footprints From Prefabs`. The sizes in the assets were eyeballed; the button takes the real ones from the meshes and also fills `PivotOffset` and `GroundOffset`. Without it buildings slide off their lots.
   While there, go through the four `Biome_*.asset` with `Measure Scatter Footprints` — it takes tree and rock radii from the meshes, so large boulders stop reaching into walls.
3. **Check the tier compositions.** Every profile in `WorldGenerationConfig` has a filled `Composition` list — nine required buildings for a city, four for a town, one for a village. A request must reference a `PoiDefinition` that is **in `PoiDatabase`**, or it is silently skipped with a warning in the log.
4. **Generate the world.** On `WorldGenerator`, press `Generate And Save All`. This is mandatory before anything else reads `Generated/`: the files there were baked at `HeightCellSize` 4 and every consumer now expects 2049². Check in the log that there are eight or nine hubs, around seven hundred POI, and a `Composition: 34 of 34` line.
5. **Build a piece of terrain.** Put `VoxelTerrainBuilder` in the scene, assign `WorldGenerationConfig`, `VoxelConfig` (created via `Create/World/Voxel Config`), `HeightMap.bytes`, and for the splat map and decor also `BiomeDatabase`, `BiomeMap.png`, `RoadMask.png` and `PoiPlacement.asset`. Press `Build Voxel Terrain`. By default it builds an 8x8 chunk patch (256 m) around (1024, 1024); the `BuildCost` field in the inspector shows what the current settings will cost, and a build over `_memoryBudget` will not start.
6. **Place the buildings.** Put `PoiSpawner` in the scene, assign `PoiPlacement.asset`, press `Spawn POI`.
7. **Enable streaming.** Put `VoxelTerrainStreamer` in the scene with the same assets plus `Viewer` — the player transform. It keeps the rings around them by itself; after that the builder is only for looking at a static piece.

The first build is slow: `RepetitionlessVoxelSetup` creates `Generated/VoxelMaterial.mat` and the package bakes texture arrays for it on a compute shader — one progress bar per layer, and there are 14 layers, not 4. Later builds reuse the material.

### Beyond that

- **Water.** Sea level in the config, a water plane per chunk, a coastal biome.
- **A POI mask for the splatmap.** Building pads currently do not reach the mask, so a building stands on the biome texture. A separate layer (concrete/yard) is needed, and then `GroundSplatPainter` will paint the pad too. The slots exist now — the budget is 32 with 14 taken.
- **A building's extent for scatter is its `Footprint`, not its mesh.** `PoiPadIndex` cuts a rectangle from `PoiPlacement` plus `PoiPadMargin` plus the object's `Footprint`, but a building can stick out beyond its own footprint with a canopy or a porch. If that shows up, raise `PoiPadMargin`.
- **A highway ends in the middle of a leaf hub.** All MST edges converge exactly on the hub point, so two opposing highways join geometrically and are level-matched (the hub pad is carved before the roads, `LevelToExistingRoads` handles the rest). The problem is only at a tree leaf: if one road enters a settlement, it ends in the middle of the square. The logical next step is to extend it to the nearest street junction.
- **Hydraulic erosion** on top of the thermal pass — gullies need flow accumulation, which is what the thermal pass cannot give: water has to travel the whole slope, and that is a sequential dependency where the talus rule is purely local. Either a droplet simulation (scattered writes, so non-deterministic in parallel) or an iterative flux field over many passes.
- **A\* after generation.** A Recast over 16 km² takes minutes, so the graph is built per chunk around the player: `NavmeshCut` on dug-out areas, `UpdateGraphs(Bounds)` with `batchGraphUpdates` on newly walkable surface. Never `Scan()` at runtime — it is fully blocking. A* 5.x has the cheap path: `NavmeshCut` carves a hole in a finished navmesh with no tile rebuild (the graph needs `enableNavmeshCutting`), and `UpdateGraphs(Bounds)` rebuilds a tile only where geometry actually changed.
- **Burst for the remaining layers** — if the biome map or painting becomes a bottleneck. It is not one now.

### Known limitations

- `UnitProperty.cs` was deleted, but `BiomeType.Snow` remains unused in the enum — remove it once it is clear whether the biome comes back.
- **The `.terrainlayer` files are not in git** (the `TerrainLayers/` folder is not committed), so their `Size` fields live only on disk. They now carry the metres-per-texture of the table above; they used to hold the importer default of 2 m on every ground, which made Synty details centimetre-sized and the ground read as fine noise. If the folder is ever recreated, enter the table again and re-run `RepetitionlessVoxelSetup` — the code handles the `1000 / TileSize` inversion, so the numbers are entered in plain metres.
- **`HubCount` 12 gives 8 or 9 settlements depending on the seed.** Candidates are cut by `MinHubDistance` 700 together with the `HubEdgeMargin` 500 inset from the border; the generator logs it. That is expected behaviour (`HubCount` is a ceiling, not an order), but if it is too few, turn `MinHubDistance`, not `HubCount`: the sector grid is already 4x4.
- **Composition rests on the tier profile, not on the generator.** It currently closes fully on all three checked seeds (34, 34 and 33 requests), but a narrow district breaks that instantly: a town centre of radius 51 m could not hold two `Downtown` requests, because they never land closer than `BlockSizeAt` to each other. When adding a request, check that its district in that tier is wider than a couple of blocks, and watch the `Composition` line in the log.
- **A limit on a district's smallest prefab produces empty lots.** Lot size is fixed at subdivision time; if by placement time the building has hit `MaxPerSettlement` and there is nothing smaller in the district, the lot stays empty. Put limits on large and rare POI, not on small ones.
- **Composition requests do not check whether a building fits the terrain.** `SettlementComposition` works on frontage geometry, while the terrain cut under a pad is checked later by `PoiPlacer.Fits` — so a reservation can be lost at the last step; those get their own line in the `PoiPlacer` log.
- **Repetitionless survives the move: the package has a layered shader for a regular mesh.** Besides `RepetitionlessLayeredTerrain` the package ships `RepetitionlessLayeredLit` — the same `_Control0.._Control7` machinery and `_MAX_LAYERS_4 .. _MAX_LAYERS_32` keywords, but **without** the `"TerrainCompatible"` tag, i.e. usable on a plain `MeshRenderer`. Plus `_UVSpace` (world UV), the `_REPETITIONLESS_TRIPLANAR` keyword and `_VertexColourBlendMode` — layer blending by vertex color instead of splat textures. The caveat that cost a pink world: in the URP version this shader sat in a **hidden** `Shaders/URP~/` folder next to the active `Shaders/URP/`, so Unity did not import it and `Shader.Find` returned null. `RenderPipelineChecker.MergeURP` is supposed to merge those folders but its state was stuck. The file was moved to `Shaders/URP/` by hand — exactly what the package itself does. If the shader disappears again after a Repetitionless update, look here.
- **Repetitionless material capacity is fixed at creation time.** Installing Pro over Free does not widen an already baked `VoxelMaterial.mat` — it must be deleted together with its `*_RepetitionlessData` folder. `RepetitionlessVoxelSetup` detects capacity from `Properties.asset` and warns, but cannot fix it itself.
- **14 layers means 42 slices in the texture arrays per material.** The first build is noticeably longer than a four-layer one, and at the original 2048² the arrays weigh hundreds of megabytes. If it hurts, set `maxTextureSize` 1024 on the Synty ground textures.
- **The material is shared across all chunks**: a per-chunk layer set is impossible, but there are no seams at chunk borders and the texture arrays are baked once.
- **Repetitionless can only be configured in the editor.** `RepetitionlessVoxelSetup` is entirely under `#if UNITY_EDITOR`, because the package bakes texture arrays with editor API. In play mode chunks keep whatever material is assigned; bake it in the editor.
- **Pad accuracy is limited by the heightmap cell, and small buildings suffer more.** At `HeightCellSize` 2 the worst corner across 680 buildings hangs 0.81 m and nothing exceeds a metre; at the old 4 m cell the same three seeds gave 1.30, 2.02 and 3.70 m with up to three buildings over a metre. A small footprint covers only a couple of samples and the `MaxPoiCut`/`MaxPoiFill` clamp fires per cell, so the effect scales with the cell. Going below 2 m costs the heightmap file and every carve pass fourfold; a plinth on the prefab is the cheaper remedy.
- On rugged terrain some lots stay empty — the terrain refuses them. Those are the outskirts, and a ragged city edge looks right, but if dense development is needed, raise `MaxPoiCut`/`MaxPoiFill` or lower `CityRadiusScale`.
- **There are few junctions.** Detached pieces are gone — `StreetStitcher` sews them up — but the network stays a tree: 76 streets carry 5 real junctions, everything else is joined end to end or by links. The "do not run alongside a parallel road" rule stops a street before it can cross its neighbour at a usable angle. It reads as development with dead ends; if a dense connected centre is needed, lower `StreetSpacingFraction` (0.55 gives noticeably more crossings) or raise `StreetJunctionAngle`.
- Empty pockets remain inside a settlement: a gap-seeded street dies on terrain or on length, and the next request for the same place is rejected as crowded. More `StreetFillPasses` barely helps — it is the rejection that binds.
- POI are not checked for intersection **with each other** between cities and the highway: `Rural` points are placed per road separately, and thanks to `RoadReuseDiscount` roads can run almost side by side. No intersections have been observed, but there is no guarantee.
- **The ground's LOD switch is still visible, it is just no longer a hole.** The atomic swap removed the drop into nothing, but the silhouette at a ring border changes abruptly: the voxel doubles and the surface jumps by a meter or so. A smooth transition needs geomorphing (a coarse chunk's vertex crawls toward its position on the fine grid as the player approaches) — that is a `VoxelMeshJob` change plus an extra vertex attribute, not done.
- **Trees in coarse rings are still `GameObject`s with `LODGroup`.** Spreading them across frames removed the freeze but not the cost: at `TreeMaxLod` 2 the scene holds around 12 thousand trees, and a Synty prefab has several children for LOD levels, i.e. close to forty thousand transforms. The real fix is to draw distant trees with instancing of the coarsest LOD, as grass does; until then range is turned through `TreeMaxLod`.
- **Grass tuning lives in the shader file.** `_WindStrength`, `_WindSpeed` and `_WindFrequency` are shader defaults: the material is built in code and only `_FadeStart`/`_FadeEnd` are written to it, from `VoxelConfig.GrassDistance`. To retune the wind, edit `GrassCard.shader`.
- **A grass card does not turn to face the camera.** It is two crossed quads, not a billboard: at a steep angle from above a tuft reads as a cross. For grass underfoot that is fine, for tall bushes it is not; those are better left as mesh prefabs.
- **Card tint is per layer, not per instance.** `DecorInstance.Tint` is applied on no path — instancing without a `MaterialPropertyBlock`, combining without vertex colours — so the `TintVariance` spread is lost and colour varies only between biome layers.
- **Trees and rocks are not saved to an asset** — they are deterministic from the seed and recomputed on every build, like grass. So an individual tree cannot be moved by hand either: the next build brings it back.
- **Synty has no tree billboards.** `treeBillboardDistance` only works with SpeedTree, so the far plane rests on the prefab's `LODGroup` and `treeDistance` culling. Past 1200 m the forest simply disappears.
- **Roads can still meet at an angle where they join at a hub.** `RoadSmoother` smooths each MST edge separately and knows nothing about its neighbours, so two highways converging on a hub meet at whatever angle they arrived. Inside a settlement the hub pad hides this; outside it is visible.

## Navigation (A*)

- Unit movement goes through `FollowerEntity.destination` + waiting on `reachedEndOfPath`.
- Wander points are validated via `AstarPath.active.GetNearest(pos).node.Walkable`.
- The scene must contain an object with an `AstarPath` component.

## Git

- `.gitignore` is the standard Unity template; `.idea/` and `.claude/` are additionally ignored.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.sln`, `*.csproj` are not committed. One exception is written into `.gitignore`: `Tools/HeadlessCheck/HeadlessCheck.csproj` must stay in the repository, otherwise the harness cannot be built.
- `.meta` files are always committed together with their asset.

