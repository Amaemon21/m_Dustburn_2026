using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterSurfaceBuilder
{
    public static GameObject Build(WaterMap water, Material material, Transform parent)
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
        int triangles = 0;

        foreach (WaterMeshPart part in parts)
        {
            var holder = new GameObject(part.Name);
            holder.transform.SetParent(root.transform, false);

            var mesh = new Mesh { name = part.Name };

            if (part.Vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(part.Vertices);
            mesh.SetUVs(0, part.Uv);
            mesh.SetTriangles(part.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            holder.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            GeneratedMesh.Own(holder);
            triangles += part.Triangles.Count / 3;
        }

        Debug.Log($"Water: {water.Rivers.Count} rivers, {water.Bodies.Count} lakes and ponds, sea at {water.SeaLevel:0} m, "
            + $"{parts.Count} meshes, {triangles} triangles");

        return root;
    }
}
