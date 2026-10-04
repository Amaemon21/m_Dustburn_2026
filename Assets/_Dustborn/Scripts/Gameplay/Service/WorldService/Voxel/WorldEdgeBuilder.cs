using UnityEngine;
using UnityEngine.Rendering;

public static class WorldEdgeBuilder
{
    public const float WALL_THICKNESS = 16f;
    public const float WALL_HEADROOM = 256f;

    public static float OffshoreReach(WorldGenerationConfig config)
    {
        return config != null && CoastShaper.Active(config) ? config.Water.OffshoreReach : 0f;
    }

    public static GameObject Build(WorldGenerationConfig config, Material ground, Transform parent)
    {
        if (config == null)
            return null;

        var root = new GameObject("World Edge");
        root.transform.SetParent(parent, false);

        if (OffshoreReach(config) > 0f)
            Floor(config, ground, root.transform);

        Walls(config, root.transform);
        return root;
    }

    private static void Floor(WorldGenerationConfig config, Material ground, Transform parent)
    {
        if (ground == null)
        {
            Debug.LogWarning("The sea floor past the world border is not drawn: the baked world has no ground material");
            return;
        }

        WaterMeshPart part = WaterMeshes.OffshoreFloor(config.WorldSize, CoastShaper.Floor(config), OffshoreReach(config));
        var holder = new GameObject(part.Name);
        holder.transform.SetParent(parent, false);

        var mesh = new Mesh { name = part.Name, indexFormat = part.Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(part.Vertices);
        mesh.SetUVs(0, part.Uv);
        mesh.SetTriangles(part.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ground;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        GeneratedMesh.Own(holder);
    }

    private static void Walls(WorldGenerationConfig config, Transform parent)
    {
        float world = config.WorldSize;
        float height = config.MaxHeight + WALL_HEADROOM * 2f;
        float middle = config.MaxHeight * 0.5f;
        float half = WALL_THICKNESS * 0.5f;

        Wall(parent, "Wall West", new Vector3(-half, middle, world * 0.5f), new Vector3(WALL_THICKNESS, height, world + WALL_THICKNESS * 2f));
        Wall(parent, "Wall East", new Vector3(world + half, middle, world * 0.5f), new Vector3(WALL_THICKNESS, height, world + WALL_THICKNESS * 2f));
        Wall(parent, "Wall South", new Vector3(world * 0.5f, middle, -half), new Vector3(world + WALL_THICKNESS * 2f, height, WALL_THICKNESS));
        Wall(parent, "Wall North", new Vector3(world * 0.5f, middle, world + half), new Vector3(world + WALL_THICKNESS * 2f, height, WALL_THICKNESS));
    }

    private static void Wall(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = center;
        wall.AddComponent<BoxCollider>().size = size;
    }
}
