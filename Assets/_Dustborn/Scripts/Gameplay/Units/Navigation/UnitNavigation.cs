using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Pathfinding;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UnitNavigation : IDisposable
{
    private const string STATUS = "Навигация для противников";

    private const float TILE_INSET = 0.25f;

    private readonly NavigationSettings _settings;
    private readonly IPlayerProvider _player;
    private bool[] _tiles = Array.Empty<bool>();
    private GameObject _root;
    private RecastGraph _graph;

    public bool Ready => _graph != null && _graph.isScanned;

    public UnitNavigation(UnitDatabaseConfig units, IPlayerProvider player)
    {
        _settings = units.Navigation ?? new NavigationSettings();
        _player = player;
    }

    public async UniTask Build(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        Transform player = _player.Player;
        if (player == null)
            throw new InvalidOperationException($"{nameof(UnitNavigation)}: the navigation graph is built around the player, spawn it first");

        Exclude(player.gameObject);
        AstarPath astar = AstarPath.active != null ? AstarPath.active : Create();
        _graph = Configure(astar, player.position);

        foreach (Pathfinding.Progress step in astar.ScanAsync(_graph))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new SceneLoadStatus(step.progress, STATUS));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        WarmUpdates(astar, player.position);
        Follow(astar, player);
    }

    public void Refresh(IReadOnlyList<Vector2Int> cells, float cellSize)
    {
        if (!Ready || AstarPath.active == null || !MarkTiles(cells, cellSize))
            return;

        int width = _graph.tileXCount;

        for (int z = 0; z < _graph.tileZCount; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (_tiles[z * width + x])
                    AstarPath.active.UpdateGraphs(Inner(TakeRect(x, z)));
            }
        }
    }

    private bool MarkTiles(IReadOnlyList<Vector2Int> cells, float cellSize)
    {
        int width = _graph.tileXCount;

        if (_tiles.Length != width * _graph.tileZCount)
            _tiles = new bool[width * _graph.tileZCount];

        Vector3 centre = _graph.forcedBoundsCenter;
        Vector3 size = _graph.forcedBoundsSize;
        var graph = new Rect(centre.x - size.x * 0.5f, centre.z - size.z * 0.5f, size.x, size.z);
        float border = TileBorder;
        bool marked = false;

        foreach (Vector2Int cell in cells)
        {
            if (!graph.Overlaps(new Rect(cell.x * cellSize, cell.y * cellSize, cellSize, cellSize)))
                continue;

            var bounds = new Bounds(new Vector3((cell.x + 0.5f) * cellSize, centre.y, (cell.y + 0.5f) * cellSize), new Vector3(cellSize, size.y, cellSize));
            IntRect tiles = _graph.GetTouchingTiles(bounds, border);

            if (!tiles.IsValid())
                continue;

            for (int z = tiles.ymin; z <= tiles.ymax; z++)
                for (int x = tiles.xmin; x <= tiles.xmax; x++)
                    _tiles[z * width + x] = true;

            marked = true;
        }

        return marked;
    }

    private IntRect TakeRect(int x, int z)
    {
        int width = _graph.tileXCount;
        int xmax = x;
        int zmax = z;

        while (xmax + 1 < width && _tiles[z * width + xmax + 1])
            xmax++;

        while (zmax + 1 < _graph.tileZCount && RowMarked(zmax + 1, x, xmax))
            zmax++;

        for (int row = z; row <= zmax; row++)
            for (int column = x; column <= xmax; column++)
                _tiles[row * width + column] = false;

        return new IntRect(x, z, xmax, zmax);
    }

    private bool RowMarked(int z, int xmin, int xmax)
    {
        for (int x = xmin; x <= xmax; x++)
        {
            if (!_tiles[z * _graph.tileXCount + x])
                return false;
        }

        return true;
    }

    private Bounds Inner(IntRect tiles)
    {
        Bounds bounds = _graph.GetTileBounds(tiles);
        float insetX = TileBorder + _graph.TileWorldSizeX * TILE_INSET;
        float insetZ = TileBorder + _graph.TileWorldSizeZ * TILE_INSET;

        bounds.Expand(new Vector3(-2f * insetX, 0f, -2f * insetZ));
        return bounds;
    }

    private float TileBorder => (Mathf.CeilToInt(_graph.characterRadius / _graph.cellSize - 0.1f) + 3) * _graph.cellSize;

    private void WarmUpdates(AstarPath astar, Vector3 position)
    {
        IntRect tile = _graph.GetTouchingTiles(new Bounds(position, Vector3.zero));

        if (!tile.IsValid())
            return;

        astar.UpdateGraphs(Inner(tile));
        astar.FlushGraphUpdates();
    }

    public static void Exclude(GameObject target)
    {
        if (!target.TryGetComponent(out RecastNavmeshModifier modifier))
            modifier = target.AddComponent<RecastNavmeshModifier>();

        modifier.includeInScan = RecastNavmeshModifier.ScanInclusion.AlwaysExclude;
    }

    private AstarPath Create()
    {
        _root = new GameObject("Navigation");
        _root.SetActive(false);
        AstarPath astar = _root.AddComponent<AstarPath>();
        astar.scanOnStartup = false;
        astar.logPathResults = PathLog.OnlyErrors;
        _root.SetActive(true);
        return astar;
    }

    private RecastGraph Configure(AstarPath astar, Vector3 centre)
    {
        RecastGraph graph = null;
        foreach (NavGraph existing in astar.data.graphs)
            if (existing is RecastGraph recast)
                graph = recast;

        graph ??= astar.data.AddGraph(typeof(RecastGraph)) as RecastGraph;

        graph.forcedBoundsCenter = centre;
        graph.forcedBoundsSize = new Vector3(_settings.GraphSize.x, _settings.GraphHeight, _settings.GraphSize.y);
        graph.rotation = Vector3.zero;
        graph.cellSize = _settings.CellSize;
        graph.useTiles = true;
        graph.editorTileSize = _settings.TileVoxels;
        graph.tileSizeX = _settings.TileVoxels;
        graph.tileSizeZ = _settings.TileVoxels;
        graph.characterRadius = _settings.CharacterRadius;
        graph.walkableHeight = _settings.WalkableHeight;
        graph.walkableClimb = _settings.WalkableClimb;
        graph.maxSlope = _settings.MaxSlope;
        graph.collectionSettings.layerMask = _settings.Layers;
        graph.collectionSettings.rasterizeColliders = true;
        graph.collectionSettings.rasterizeMeshes = false;
        graph.collectionSettings.rasterizeTerrain = false;
        graph.collectionSettings.rasterizeTrees = false;
        return graph;
    }

    private void Follow(AstarPath astar, Transform player)
    {
        if (!astar.TryGetComponent(out ProceduralGraphMover mover))
            mover = astar.gameObject.AddComponent<ProceduralGraphMover>();

        mover.graph = _graph;
        mover.target = player;
    }

    public void Dispose()
    {
        if (_root != null)
            Object.Destroy(_root);

        _root = null;
        _graph = null;
    }
}
