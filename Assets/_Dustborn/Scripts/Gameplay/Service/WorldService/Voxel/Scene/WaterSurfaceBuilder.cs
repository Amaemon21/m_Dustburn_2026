using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterSurfaceBuilder
{
    private const float ICE_TILE = 6f;
    private const float COLLIDER_EDGE = 64f;

    public static GameObject Build(WaterMap water, Material material, Transform parent)
    {
        return Build(water, material, null, parent);
    }

    public static GameObject Build(WaterMap water, Material material, Material ice, Transform parent, float offshoreReach = 0f)
    {
        if (water == null)
            return null;

        if (material == null)
        {
            Debug.LogWarning("Water is not drawn: assign WaterMaterial in WorldBuildSettings");
            return null;
        }

        var root = new GameObject("Water");
        root.transform.SetParent(parent, false);

        List<WaterMeshPart> parts = WaterMeshes.Build(water);

        if (offshoreReach > 0f)
            parts.Add(WaterMeshes.Offshore(water, parts, offshoreReach));
        int triangles = 0, frozen = 0;

        foreach (WaterMeshPart part in parts)
        {
            var holder = new GameObject(part.Name);
            holder.transform.SetParent(root.transform, false);

            var mesh = new Mesh { name = part.Name };

            if (part.Vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(part.Vertices);
            mesh.SetUVs(0, part.Frozen ? WorldUv(part.Vertices) : part.Uv);
            mesh.SetTriangles(part.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            holder.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = part.Frozen && ice != null ? ice : material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            if (part.Frozen)
            {
                holder.AddComponent<MeshCollider>().sharedMesh = Collider(part);
                frozen++;
            }

            GeneratedMesh.Own(holder);
            triangles += part.Triangles.Count / 3;
        }

        Debug.Log($"Water: {water.Rivers.Count} rivers, {water.Bodies.Count} lakes and ponds, sea at {water.SeaLevel:0} m, "
            + $"{parts.Count} meshes ({frozen} frozen), {triangles} triangles");

        return root;
    }

    private static Mesh Collider(WaterMeshPart part)
    {
        var vertices = new List<Vector3>(part.Vertices);
        var triangles = new List<int>(part.Triangles);
        WaterMeshes.Tessellate(vertices, triangles, COLLIDER_EDGE);

        var mesh = new Mesh { name = part.Name + " collider", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static List<Vector2> WorldUv(List<Vector3> vertices)
    {
        var uv = new List<Vector2>(vertices.Count);

        foreach (Vector3 vertex in vertices)
            uv.Add(new Vector2(vertex.x / ICE_TILE, vertex.z / ICE_TILE));

        return uv;
    }
}
