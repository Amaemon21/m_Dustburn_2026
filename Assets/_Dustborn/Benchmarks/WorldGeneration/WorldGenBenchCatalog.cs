using System.Collections.Generic;
using System.Text;

public sealed class WorldGenBenchSpec
{
    public string Id { get; }

    public string Priority { get; }

    public string Mode { get; }

    public string Op { get; }

    public string[] Cases { get; }

    public string Note { get; }

    public WorldGenBenchSpec(string id, string priority, string mode, string op, string[] cases, string note)
    {
        Id = id;
        Priority = priority;
        Mode = mode;
        Op = op;
        Cases = cases;
        Note = note;
    }
}

public static class WorldGenBenchCatalog
{
    private static readonly string[] NONE = { string.Empty };

    private static List<WorldGenBenchSpec> _all;

    public static IReadOnlyList<WorldGenBenchSpec> All => _all ??= Build();

    public static WorldGenBenchSpec Find(string id)
    {
        foreach (WorldGenBenchSpec spec in All)
        {
            if (spec.Id == id)
                return spec;
        }

        return null;
    }

    public static string Csv()
    {
        var text = new StringBuilder();
        text.AppendLine("id,priority,mode,op,caseCount,cases,note");

        foreach (WorldGenBenchSpec spec in All)
        {
            text.Append(spec.Id).Append(',')
                .Append(spec.Priority).Append(',')
                .Append(spec.Mode).Append(',')
                .Append(spec.Op).Append(',')
                .Append(spec.Cases.Length).Append(',')
                .Append('"').Append(string.Join("|", spec.Cases)).Append('"').Append(',')
                .Append('"').Append(spec.Note.Replace("\"", "\"\"")).Append('"')
                .AppendLine();
        }

        return text.ToString();
    }

    private static List<WorldGenBenchSpec> Build()
    {
        var all = new List<WorldGenBenchSpec>
        {
            new("PRE-01", "P0", "EM", "pre.environment", NONE, "slice, unity version, packages and source assets"),
            new("PRE-02", "P0", "EM", "pre.fixture", NONE, "deep clone of the mutable config graph, sources untouched"),
            new("PRE-03", "P0", "EM", "pre.selftest", NONE, "marker overhead, percentile correctness, synthetic hitch, queue stages"),
            new("PRE-04", "P0", "EM", "pre.overhead", new[] { "capture=false", "capture=true" }, "same workload with the recorder on and off"),
            new("PRE-05", "P1", "EM", "pre.budget", NONE, "working set preflight and refusal of impossible sizes"),

            new("BIO-01", "P0", "EM", "bio.generate", NONE, "whole biome map, size, histogram, determinism"),
            new("BIO-02", "P1", "EM", "bio.classify", new[] { "seedsPerBiome=1", "seedsPerBiome=4", "seedsPerBiome=16", "warp=false" }, "seed placement and the classify job"),
            new("BIO-03", "P1", "EM", "bio.smooth", new[] { "passes=0", "passes=1", "passes=3", "passes=1;shape=uniform" }, "majority smoothing passes"),
            new("BIO-04", "P1", "EM", "bio.regions", new[] { "shape=islands", "shape=uniform" }, "small region cleanup"),
            new("BIO-05", "P0", "EM", "bio.weights", new[] { "radius=0", "radius=14", "radius=28", "radius=56" }, "weight field init, blur and normalise"),
            new("BIO-06", "P1", "EM", "bio.sample", new[] { "points=100000" }, "Sample, SampleOne and Coverage agreement"),
            new("BIO-07", "P1", "EM", "bio.native", NONE, "ToNativeArray copy and release"),
            new("BIO-08", "P2", "EM", "bio.edges", new[] { "seed=0", "seed=-1", "seed=2147483647", "jitter=1;warp=1.5" }, "seed boundaries and extreme warp"),

            new("HGT-01", "P0", "EM", "hgt.generate", NONE, "whole height generator on a frozen biome map"),
            new("HGT-02", "P0", "EM", "hgt.noise", NONE, "HeightMapJob alone, erosion off"),
            new("HGT-03", "P0", "EM", "hgt.hydraulic", new[] { "passes=0", "passes=1", "passes=16", "passes=48" }, "hydraulic erosion passes"),
            new("HGT-04", "P1", "EM", "hgt.hydraulicStages", new[] { "coarse=4", "coarse=8", "coarse=16" }, "the five hydraulic job stages at different coarse steps"),
            new("HGT-05", "P0", "EM", "hgt.thermal", new[] { "passes=0", "passes=1", "passes=8", "passes=32", "passes=64" }, "thermal erosion passes"),
            new("HGT-06", "P1", "EM", "hgt.passes", new[] { "thermal=1;hydraulic=1", "thermal=2;hydraulic=2", "thermal=3;hydraulic=3" }, "odd and even ping-pong from a pristine input"),
            new("HGT-07", "P1", "EM", "hgt.sample", new[] { "smooth=false", "smooth=true" }, "SampleWorld and SampleWorldSmooth batches"),
            new("HGT-08", "P1", "EM", "hgt.path", NONE, "setup, native allocations and the copy back"),
            new("HGT-09", "P1", "EM", "hgt.scaling", new[] { "world=512;cell=2", "world=1024;cell=2", "world=2048;cell=2", "world=2048;cell=1" }, "map scaling at fixed passes"),
            new("HGT-10", "P2", "EM", "hgt.extreme", new[] { "shape=plane", "shape=ridge", "shape=sea" }, "worst case octaves and shapes"),

            new("SET-01", "P0", "EM", "set.hubs", NONE, "hub candidates, relief probes and tiers"),
            new("SET-02", "P1", "EM", "set.hubsEmpty", new[] { "shape=flooded", "shape=steep" }, "no suitable hub, and the empty downstream"),
            new("SET-03", "P0", "EM", "set.roads", new[] { "hubs=4", "hubs=16", "" }, "planner constructor and Plan"),
            new("SET-04", "P1", "EM", "set.edges", new[] { "extraEdges=0", "extraEdges=15", "extraEdges=60" }, "MST plus extra edges"),
            new("SET-05", "P0", "EM", "set.path", new[] { "route=short", "route=long" }, "A star routing with a direction state"),
            new("SET-06", "P1", "EM", "set.smooth", new[] { "jagged=true", "jagged=false", "points=1200" }, "simplify, Chaikin, relax and resample"),
            new("SET-07", "P0", "EM", "set.carve", NONE, "hub pads and trunk roads carved into the terrain"),
            new("SET-08", "P0", "EM", "set.cities", NONE, "city planning over frozen roads and heights"),
            new("SET-09", "P1", "EM", "set.streets", new[] { "fillPasses=1", "fillPasses=3", "spacing=0.55" }, "street growth, branching and gap seeding"),
            new("SET-10", "P1", "EM", "set.stitch", NONE, "connectivity of detached street islands"),
            new("SET-11", "P1", "EM", "set.lots", new[] { "", "lotGap=12" }, "lot subdivision and rollback"),
            new("SET-12", "P1", "EM", "set.retry", new[] { "attempts=1", "attempts=3", "attempts=3;impossible=true" }, "settlement replanning"),
            new("SET-13", "P1", "EM", "set.index", new[] { "segments=0", "segments=1000", "segments=10000" }, "RoadProximity, LotIndex and MinHeap"),
            new("SET-14", "P0", "EM", "set.carveStreets", NONE, "street carving after the city planner"),

            new("POI-01", "P0", "EM", "poi.place", NONE, "placement over frozen layouts and network"),
            new("POI-02", "P1", "EM", "poi.lot", new[] { "lots=200", "lots=400" }, "PlaceOnLot, Pick and Fits"),
            new("POI-03", "P1", "EM", "poi.rural", new[] { "ruralChance=0", "", "ruralChance=1" }, "rural placement along highways"),
            new("POI-04", "P0", "EM", "poi.pads", NONE, "CarvePads and ApplyHeights as two stages"),
            new("POI-05", "P1", "EM", "poi.padIndex", new[] { "queries=200000" }, "pad index build and batch queries"),
            new("POI-06", "P0", "PM", "poi.instances", new[] { "batch=1", "batch=16", "batch=64" }, "PoiInstanceBuilder batches with real prefabs"),
            new("POI-07", "P2", "PM", "poi.spawner", NONE, "legacy spawner, repeat spawn and clear"),

            new("MAT-01", "P0", "EM", "mat.painter", NONE, "painter constructor and rule building"),
            new("MAT-02", "P0", "EM", "mat.surface", new[] { "resolution=256", "resolution=512", "resolution=1024", "resolution=2048" }, "slope and elevation maps"),
            new("MAT-03", "P0", "EM", "mat.bakeWorld", new[] { "resolution=256", "resolution=512", "resolution=1024" }, "GroundSplatJob over the world"),
            new("MAT-04", "P1", "EM", "mat.pack", new[] { "resolution=256", "resolution=512" }, "packing into RGBA control textures"),
            new("MAT-05", "P0", "ED", "mat.material", new[] { "resolution=256" }, "VoxelGroundMaterial.Bake end to end"),
            new("MAT-06", "P1", "ED", "mat.repetitionless", new[] { "resolution=256" }, "Repetitionless material creation and resync"),
            new("MAT-07", "P1", "EM", "mat.oneGround", new[] { "oneGround=true", "oneGround=false", "oneGround=false;noRoadMask=true" }, "OneGroundPerBiome and a missing road mask"),
            new("MAT-08", "P2", "EM", "mat.rebake", NONE, "repeat control texture bake at a changed resolution"),

            new("IO-01", "P0", "EM", "io.raw16", new[] { "direction=encode", "direction=decode" }, "raw16 encode and decode"),
            new("IO-02", "P1", "EM", "io.textures", NONE, "biome, mask and hillshade conversion"),
            new("IO-03", "P0", "EM", "io.png", new[] { "kind=biomes", "kind=hillshade", "kind=mask" }, "PNG encoding of every generated map"),
            new("IO-04", "P0", "ED", "io.write", new[] { "megabytes=8", "megabytes=8;existing=true" }, "GeneratedAssetFile write and replace"),
            new("IO-05", "P1", "ED", "io.lock", NONE, "a permanently locked destination"),
            new("IO-06", "P0", "ED", "io.import", NONE, "ImportAsset and SaveAndReimport"),
            new("IO-07", "P1", "ED", "io.assets", NONE, "road and placement asset serialisation"),
            new("IO-08", "P0", "ED", "io.bake", new[] { "allowSourceWrite=false" }, "full editor bake, new and repeated"),
            new("IO-09", "P1", "ED", "io.bakeFail", NONE, "failure before the final EditorSetup"),

            new("VOX-01", "P0", "EM", "vox.field", NONE, "density field constructor and the lowest surface scan"),
            new("VOX-02", "P1", "EM", "vox.sampler", new[] { "morph=0", "morph=1" }, "Surface, Sample, Normal and Height batches"),
            new("VOX-03", "P0", "EM", "vox.plan", new[] { "where=centre", "where=edge", "where=corner", "lods=1", "lods=3" }, "stream plan around a viewer"),
            new("VOX-04", "P1", "EM", "vox.range", new[] { "columns=16", "columns=32" }, "vertical range per column"),
            new("VOX-05", "P0", "EM", "vox.mesh", new[] { "lod=0", "lod=1", "lod=2", "lod=3" }, "chunk meshing at every level"),
            new("VOX-06", "P1", "EM", "vox.meshKinds", NONE, "empty air, solid rock and a surface chunk"),
            new("VOX-07", "P1", "EM", "vox.seams", new[] { "seams=1;morph=1", "seams=2;morph=2", "seams=5;morph=5", "seams=15;morph=15", "seams=1;morph=1;skirt=0" }, "seam and morph masks with and without a skirt"),
            new("VOX-08", "P1", "EM", "vox.grid", new[] { "chunkSize=16", "chunkSize=32", "chunkSize=64", "voxelSize=0.5", "voxelSize=2" }, "chunk and voxel size sweeps"),
            new("VOX-09", "P0", "PM", "vox.spawn", NONE, "Unity mesh creation, upload call and activation"),
            new("VOX-10", "P1", "ED", "vox.builder", new[] { "", "allowFullPreview=true" }, "whole preview build and clear"),

            new("QUE-01", "P0", "PM", "que.workers", new[] { "workers=1", "workers=2", "workers=4", "workers=8", "workers=16" }, "mesh queue saturation"),
            new("QUE-02", "P0", "PM", "que.drain", NONE, "Kick, Drain and Collect boundaries"),
            new("QUE-03", "P1", "PM", "que.batch", NONE, "many empty chunks against a few heavy ones"),
            new("QUE-04", "P0", "PM", "que.colliderDispatch", new[] { "batch=1", "batch=8", "batch=32", "batch=64" }, "Physics.BakeMesh dispatch"),
            new("QUE-05", "P0", "PM", "que.colliderAttach", new[] { "batch=32" }, "collider attach and readiness"),
            new("QUE-06", "P1", "PM", "que.colliderLag", NONE, "colliders trailing the meshes"),
            new("QUE-07", "P1", "PM", "que.dispose", NONE, "disposal with jobs in flight"),
            new("QUE-08", "P2", "PM", "que.contention", new[] { "load=2", "load=4" }, "diagnostic CPU contention at a fixed pool"),

            new("DEC-01", "P0", "EM", "dec.place", new[] { "kind=Grass", "kind=Tree", "kind=Rock" }, "placement per decor kind"),
            new("DEC-02", "P1", "EM", "dec.filter", new[] { "points=100000" }, "road, pad, biome, slope and patch filters"),
            new("DEC-03", "P0", "PM", "dec.step", new[] { "budget=1", "budget=48", "budget=100000" }, "Step and Advance under a budget"),
            new("DEC-04", "P1", "EM", "dec.plan", new[] { "vertexBudget=12000", "vertexBudget=48000", "vertexBudget=96000" }, "every combine decision boundary"),
            new("DEC-05", "P0", "PM", "dec.combine", new[] { "vertexBudget=12000", "vertexBudget=48000", "vertexBudget=96000" }, "mesh combining into batches"),
            new("DEC-06", "P0", "PM", "dec.instance", new[] { "instances=1022", "instances=1023", "instances=1024", "instances=8000" }, "instancing and matrix batches"),
            new("DEC-07", "P1", "PM", "dec.instantiate", new[] { "instances=200" }, "the Instantiate fallback with real prefabs"),
            new("DEC-08", "P1", "PM", "dec.cards", NONE, "grass card mesh and material cache"),
            new("DEC-09", "P0", "PM", "dec.renderer", new[] { "instances=4096" }, "decor renderer submit cost"),
            new("DEC-10", "P1", "EM", "dec.reach", new[] { "grassDistance=48", "grassDistance=96", "grassDistance=192", "treeMaxLod=1", "treeMaxLod=3" }, "grass distance and rock and tree reach"),
            new("DEC-11", "P1", "EM", "dec.seams", NONE, "decor sitting on the coarse morphed surface"),

            new("E2E-01", "P0", "PL", "runner.load", NONE, "full fresh load with every milestone"),
            new("E2E-02", "P0", "PL", "runner.reload", NONE, "cold process against a warm reload"),
            new("E2E-03", "P1", "PL", "runner.decor", new[] { "decor=none", "decor=grass", "decor=rocks", "decor=trees", "decor=all" }, "decor scope sweep"),
            new("E2E-04", "P0", "PL", "runner.budget", new[] { "loadingBudget=8", "loadingBudget=16", "loadingBudget=30", "loadingBudget=60" }, "loading budget and worker sweep"),
            new("E2E-05", "P0", "PL", "runner.idle", new[] { "seconds=60" }, "idle after stabilisation"),
            new("E2E-06", "P0", "PL", "runner.route", new[] { "speed=4", "speed=8" }, "deterministic walk and run"),
            new("E2E-07", "P1", "PL", "runner.route", new[] { "speed=25", "speed=60" }, "vehicle and fast route"),
            new("E2E-08", "P0", "PL", "runner.teleport", new[] { "points=10" }, "teleports between biomes, city and edge"),
            new("E2E-09", "P1", "PL", "runner.oscillate", NONE, "oscillation around the decor step"),
            new("E2E-10", "P1", "PL", "runner.traverse", NONE, "biome, LOD seam and world edge traversal"),
            new("E2E-11", "P2", "PL", "runner.gameplay", NONE, "the same route with the scene gameplay systems"),

            new("LIFE-01", "P0", "EM", "life.cancel", new[] { "stopAt=1", "stopAt=2", "stopAt=4" }, "pipeline cancellation latency"),
            new("LIFE-02", "P1", "ED", "life.previewCancel", new[] { "stopAt=4", "stopAt=6" }, "editor cancellation during preview"),
            new("LIFE-03", "P0", "PL", "runner.cycles", new[] { "cycles=20" }, "twenty load, settle and dispose cycles"),
            new("LIFE-04", "P1", "PL", "runner.unload", NONE, "unload, rebuild, disable and enable"),
            new("LIFE-05", "P0", "EM", "life.invalid", NONE, "invalid bake, missing viewer and prefab"),
            new("LIFE-06", "P1", "ED", "life.preview", new[] { "", "allowFullPreview=true" }, "preview rebuild and clear without a bake"),
            new("LIFE-07", "P2", "PM", "life.resources", NONE, "domain reload, repeated play mode entrances and resources")
        };

        return all;
    }
}
