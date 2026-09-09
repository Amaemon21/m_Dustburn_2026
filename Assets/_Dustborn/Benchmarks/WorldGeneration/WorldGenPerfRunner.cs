using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldGenPerfRunner : MonoBehaviour
{
    public const float DEFAULT_TIMEOUT = 900f;

    public string Result { get; private set; }

    public string Status { get; private set; } = "idle";

    public bool Finished { get; private set; }

    private WorldGenBenchFrames _frames;
    private WorldGenerator _generator;
    private Transform _viewer;
    private GameObject _host;
    private WorldGenBenchProfile _profile;
    private WorldGenBenchArgs _args;
    private WorldGenBenchJson _json;

    private readonly List<string> _notes = new();

    public static WorldGenPerfRunner Create()
    {
        var host = new GameObject("WorldGenPerfRunner");
        DontDestroyOnLoad(host);
        return host.AddComponent<WorldGenPerfRunner>();
    }

    public IEnumerator Run(string scenario, string profileId, string args)
    {
        Finished = false;
        Result = null;
        _notes.Clear();
        _args = new WorldGenBenchArgs(args);
        _json = new WorldGenBenchJson();

        _json.Text("op", scenario).Text("profile", profileId).Text("args", args ?? string.Empty);

        string status = "passed";
        string error = null;

        try
        {
            _profile = WorldGenBenchProfile.Create(profileId, _args);
            _json.Text("fixtureHash", _profile.Fingerprint);
        }
        catch (Exception exception)
        {
            _json.Null("fixtureHash");
            Finish("blocked_dependency", exception.Message);
            yield break;
        }

        WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

        if (fixture == null || !fixture.HasBake)
        {
            Finish("blocked_dependency", "The runtime scenarios need a baked world in the fixture.");
            yield break;
        }

        _frames = new WorldGenBenchFrames();

        IEnumerator body = scenario switch
        {
            "runner.load" => Load(fixture, false),
            "runner.reload" => Load(fixture, true),
            "runner.decor" => Load(fixture, false),
            "runner.budget" => Load(fixture, false),
            "runner.idle" => Idle(fixture),
            "runner.route" => Route(fixture),
            "runner.teleport" => Teleport(fixture),
            "runner.oscillate" => Oscillate(fixture),
            "runner.traverse" => Traverse(fixture),
            "runner.gameplay" => Route(fixture),
            "runner.cycles" => Cycles(fixture),
            "runner.unload" => Unload(fixture),
            _ => null
        };

        if (body == null)
        {
            Finish("not_run", $"Unknown runtime scenario '{scenario}'.");
            yield break;
        }

        while (true)
        {
            object current;

            try
            {
                if (!body.MoveNext())
                    break;

                current = body.Current;
            }
            catch (Exception exception)
            {
                status = "failed";
                error = exception.GetType().Name + ": " + exception.Message;
                break;
            }

            yield return current;
        }

        Teardown();
        Finish(status, error);
    }

    private void Finish(string status, string error)
    {
        _json.Text("status", status);

        if (error == null)
            _json.Null("error");
        else
            _json.Text("error", error);

        _json.Object("milestones");

        for (int index = 0; index < (int)WorldGenMilestone.Count; index++)
            _json.Value(((WorldGenMilestone)index).ToString(), WorldGenProbe.MilestoneMs((WorldGenMilestone)index));

        _json.End();

        _json.Object("stages");

        for (int index = 0; index < (int)WorldGenStage.Count; index++)
        {
            WorldGenStageStat stat = WorldGenProbe.Stat((WorldGenStage)index);

            if (stat.Count == 0)
                continue;

            _json.Object(((WorldGenStage)index).ToString())
                .Value("count", stat.Count)
                .Value("totalMs", WorldGenProbe.ToMs(stat.Ticks))
                .Value("maxMs", WorldGenProbe.ToMs(stat.MaxTicks))
                .End();
        }

        _json.End();

        if (_frames != null)
        {
            _frames.Write(_json, "loadFrames", "load");
            _frames.Write(_json, "runFrames", "run");
            _json.Text("framesCsv", _frames.Csv(WorldGenBenchPaths.RunId));
        }

        _json.Array("notes");

        foreach (string note in _notes)
            _json.Item(note);

        _json.End();

        _json.Value("traceOverflow", WorldGenProbe.Overflow);

        Result = _json.ToString();
        Status = status;
        Finished = true;

        _frames?.Dispose();
        _frames = null;
        _profile?.Release();
        _profile = null;
        WorldGenBenchFixtures.Release();
    }

    private IEnumerator Load(WorldGenBenchFixture fixture, bool reload)
    {
        yield return Boot(fixture);

        _json.Value("firstLoadWallMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady));
        Report();

        if (!reload)
            yield break;

        double first = WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady);
        Teardown();

        yield return null;

        yield return Boot(fixture);

        _json.Value("coldLoadWallMs", first)
            .Value("warmLoadWallMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady));

        Report();
    }

    private IEnumerator Idle(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        float seconds = _args.Float("seconds", 60f);
        _frames.Phase = "run";
        _frames.Segment = "idle";

        float until = Time.realtimeSinceStartup + seconds;

        while (Time.realtimeSinceStartup < until)
        {
            Sample();
            yield return null;
        }

        _json.Value("idleSeconds", seconds);
    }

    private IEnumerator Route(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        float speed = _args.Float("speed", 4f);
        float seconds = _args.Float("seconds", 120f);
        float world = _profile.Config.WorldSize;

        _frames.Phase = "run";

        Vector3 start = _viewer.position;
        var waypoints = new List<Vector3>
        {
            start,
            start + new Vector3(world * 0.05f, 0f, 0f),
            start + new Vector3(world * 0.05f, 0f, world * 0.05f),
            start + new Vector3(0f, 0f, world * 0.05f),
            start
        };

        int leg = 0;
        float travelled = 0f;
        float until = Time.realtimeSinceStartup + seconds;
        Vector3 position = start;

        while (Time.realtimeSinceStartup < until)
        {
            Vector3 target = waypoints[(leg + 1) % waypoints.Count];
            Vector3 delta = target - position;
            float step = speed * Time.unscaledDeltaTime;

            if (delta.magnitude <= step)
            {
                position = target;
                leg = (leg + 1) % waypoints.Count;
            }
            else
            {
                position += delta.normalized * step;
            }

            travelled += step;
            _viewer.position = Ground(position);
            _frames.Segment = "leg" + leg;

            Sample();
            yield return null;
        }

        _json.Value("routeSpeed", speed)
            .Value("routeSeconds", seconds)
            .Value("routeMetres", travelled)
            .Value("decorSettledMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.DecorSettled));
    }

    private IEnumerator Teleport(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        int points = _args.Int("points", 10);
        float world = _profile.Config.WorldSize;
        _frames.Phase = "run";

        var random = new Unity.Mathematics.Random(4242u);
        var recoveries = new List<double>();

        for (int index = 0; index < points; index++)
        {
            var target = new Vector3(random.NextFloat(world * 0.1f, world * 0.9f), 0f,
                random.NextFloat(world * 0.1f, world * 0.9f));

            _viewer.position = Ground(target);
            _frames.Segment = "teleport" + index;

            long start = WorldGenProbe.Now;
            WorldGenProbe.Forget(WorldGenMilestone.DecorSettled);

            float timeout = Time.realtimeSinceStartup + 30f;

            while (!WorldGenProbe.Reached(WorldGenMilestone.DecorSettled) && Time.realtimeSinceStartup < timeout)
            {
                Sample();
                yield return null;
            }

            recoveries.Add(WorldGenProbe.ToMs(WorldGenProbe.Now - start));

            for (int settle = 0; settle < 10; settle++)
            {
                Sample();
                yield return null;
            }
        }

        WorldGenBenchStats.Of(recoveries).Write(_json, "recoveryMs");
        _json.Value("teleports", points);
    }

    private IEnumerator Oscillate(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        float step = _args.Float("step", 24f);
        float seconds = _args.Float("seconds", 60f);
        _frames.Phase = "run";
        _frames.Segment = "oscillate";

        Vector3 origin = _viewer.position;
        float until = Time.realtimeSinceStartup + seconds;
        float phase = 0f;

        while (Time.realtimeSinceStartup < until)
        {
            phase += Time.unscaledDeltaTime * 2f;
            _viewer.position = Ground(origin + new Vector3(Mathf.Sin(phase) * step, 0f, 0f));
            Sample();
            yield return null;
        }

        _json.Value("oscillationStep", step).Value("oscillationSeconds", seconds);
    }

    private IEnumerator Traverse(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        float world = _profile.Config.WorldSize;
        _frames.Phase = "run";

        var stops = new List<Vector3>
        {
            new(world * 0.1f, 0f, world * 0.1f),
            new(world * 0.5f, 0f, world * 0.1f),
            new(world * 0.9f, 0f, world * 0.5f),
            new(world * 0.5f, 0f, world * 0.9f),
            new(4f, 0f, 4f),
            new(world - 4f, 0f, world - 4f)
        };

        int hits = 0;

        for (int index = 0; index < stops.Count; index++)
        {
            _viewer.position = Ground(stops[index]);
            _frames.Segment = "stop" + index;

            for (int settle = 0; settle < 60; settle++)
            {
                Sample();
                yield return null;
            }

            if (Physics.Raycast(_viewer.position + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 600f))
                hits++;
        }

        _json.Value("stops", stops.Count).Value("raycastHits", hits);
    }

    private IEnumerator Cycles(WorldGenBenchFixture fixture)
    {
        int cycles = _args.Int("cycles", 20);
        var latencies = new List<double>();
        var managed = new List<double>();

        for (int index = 0; index < cycles; index++)
        {
            yield return Boot(fixture);
            latencies.Add(WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady));

            Teardown();

            yield return null;
            yield return null;

            managed.Add(GC.GetTotalMemory(false));
        }

        WorldGenBenchStats.Of(latencies).Write(_json, "cycleLatencyMs");
        WorldGenBenchStats.Of(managed).Write(_json, "managedBytes");

        _json.Value("cycles", cycles)
            .Value("managedFirst", managed.Count > 0 ? managed[0] : double.NaN)
            .Value("managedLast", managed.Count > 0 ? managed[managed.Count - 1] : double.NaN)
            .Value("liveMeshes", Resources.FindObjectsOfTypeAll<Mesh>().Length);
    }

    private IEnumerator Unload(WorldGenBenchFixture fixture)
    {
        yield return Boot(fixture);
        Report();

        VoxelTerrainStreamer streamer = _generator.GetComponentInChildren<VoxelTerrainStreamer>(true);

        if (streamer == null)
        {
            _notes.Add("no streamer to unload");
            yield break;
        }

        long start = WorldGenProbe.Now;
        streamer.Unload();
        _json.Value("unloadMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));

        yield return null;

        start = WorldGenProbe.Now;
        streamer.Rebuild();
        _json.Value("rebuildRequestMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));

        float timeout = Time.realtimeSinceStartup + 120f;

        while (!streamer.Ready && Time.realtimeSinceStartup < timeout)
        {
            Sample();
            yield return null;
        }

        _json.Value("rebuiltReady", streamer.Ready);

        start = WorldGenProbe.Now;
        streamer.enabled = false;
        _json.Value("disableMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));

        yield return null;

        streamer.enabled = true;
        _json.Value("reEnabled", true);
    }

    private IEnumerator Boot(WorldGenBenchFixture fixture)
    {
        Teardown();

        WorldGenProbe.BeginRun(true);
        _frames.Reset();
        _frames.Phase = "load";
        _frames.Segment = "boot";

        _host = new GameObject("WorldGenBench Runtime");
        var viewer = new GameObject("Viewer");
        viewer.transform.SetParent(_host.transform, false);
        _viewer = viewer.transform;

        Vector2 centre = _profile.Settings.PreviewCenter;
        _viewer.position = new Vector3(centre.x, _profile.Config.MaxHeight + 10f, centre.y);

        _generator = _host.AddComponent<WorldGenerator>();

        WorldBuildSettings settings = Settings(fixture);

        WorldGenBenchProfile.Assign(_generator, "Settings", settings);
        WorldGenBenchProfile.Assign(_generator, "World", fixture.World);
        WorldGenBenchProfile.Assign(_generator, "Viewer", _viewer);
        WorldGenBenchReflect.SetField(_generator, "_loadOnStart", false);

        _generator.LoadWorld();

        float timeout = Time.realtimeSinceStartup + _args.Float("timeout", DEFAULT_TIMEOUT);

        while (!_generator.Ready && Time.realtimeSinceStartup < timeout)
        {
            Sample();
            yield return null;
        }

        if (!_generator.Ready)
            throw new TimeoutException("The world did not reach WorldReady within the configured timeout.");

        WorldGenProbe.Mark(WorldGenMilestone.WorldReady);

        for (int settle = 0; settle < 5; settle++)
        {
            Sample();
            yield return null;
        }
    }

    private WorldBuildSettings Settings(WorldGenBenchFixture fixture)
    {
        WorldBuildSettings settings = Instantiate(_profile.Settings);
        settings.hideFlags = HideFlags.HideAndDontSave;

        WorldGenBenchProfile.Assign(settings, "Voxels", _profile.Voxels);
        WorldGenBenchProfile.Assign(settings, "Biomes", fixture.World.Biomes);
        WorldGenBenchProfile.Assign(settings, "Config", fixture.World.Config);
        WorldGenBenchProfile.Assign(settings, "ShowLoadingScreen", false);

        if (_args.Has("loadingBudget"))
            WorldGenBenchProfile.Assign(settings, "LoadingBudget", _args.Float("loadingBudget", 60f));

        if (_args.Has("workers"))
            WorldGenBenchProfile.Assign(settings, "MeshWorkers", _args.Int("workers", 8));

        if (_args.Has("decorBudget"))
            WorldGenBenchProfile.Assign(settings, "DecorBudget", _args.Float("decorBudget", 6f));

        if (_args.Has("poisPerFrame"))
            WorldGenBenchProfile.Assign(settings, "PoisPerFrame", _args.Int("poisPerFrame", 16));

        string decor = _args.Text("decor", "all");

        if (decor == "none")
        {
            WorldGenBenchProfile.Assign(settings, "SpawnDecor", false);
        }
        else if (decor != "all")
        {
            VoxelConfig voxels = _profile.Voxels;
            WorldGenBenchProfile.Assign(voxels, "GrassDistance", decor == "grass" ? voxels.GrassDistance : 0f);
            WorldGenBenchProfile.Assign(voxels, "RockMaxLod", decor == "rocks" ? voxels.RockMaxLod : 0);
            WorldGenBenchProfile.Assign(voxels, "TreeMaxLod", decor == "trees" ? voxels.TreeMaxLod : 0);
        }

        return settings;
    }

    private void Report()
    {
        _json.Value("readyMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady))
            .Value("constructMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.RuntimeConstructed))
            .Value("plannedMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.Planned))
            .Value("firstTerrainMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.FirstTerrain))
            .Value("firstColliderMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.FirstCollider))
            .Value("terrainReadyMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.TerrainReady))
            .Value("poiReadyMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.PoiReady));

        bool grounded = Physics.Raycast(_viewer.position + Vector3.up * 300f, Vector3.down, 900f);
        _json.Value("spawnCollisionReady", grounded);
    }

    private Vector3 Ground(Vector3 position)
    {
        return new Vector3(position.x, _profile.Config.MaxHeight + 10f, position.z);
    }

    private void Sample()
    {
        VoxelTerrainStreamer streamer = _generator == null ? null : _generator.GetComponentInChildren<VoxelTerrainStreamer>(true);
        _frames.Sample(streamer == null ? 0 : 1, 0, 0);
    }

    private void Teardown()
    {
        if (_host == null)
            return;

        Destroy(_host);
        _host = null;
        _generator = null;
        _viewer = null;
    }
}
