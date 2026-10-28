using UnityEngine;
using UnityEngine.Rendering;

public static class RoadSurfaceBuilder
{
    public static void Build(RoadSurfaceMesh planner, VoxelMesh source, Material material, Transform parent)
    {
        if (planner == null || material == null)
            return;

        Build(planner.Build(source), material, parent);
    }

    public static void Build(RoadSurfaceMesh.Geometry geometry, Material material, Transform parent)
    {
        if (geometry == null || material == null || geometry.Triangles.Count == 0)
            return;

        var mesh = new Mesh
        {
            name = "Road Surface",
            indexFormat = geometry.Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(geometry.Vertices);
        mesh.SetNormals(geometry.Normals);
        var tangents = new Vector4[geometry.Tangents.Count];

        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 tangent = geometry.Tangents[i];
            tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
        }

        mesh.tangents = tangents;
        mesh.SetUVs(0, geometry.Uv);
        mesh.SetUVs(1, geometry.Profile);
        mesh.SetUVs(2, geometry.Markings);
        mesh.SetTriangles(geometry.Triangles, 0);
        mesh.RecalculateBounds();

        var holder = new GameObject("Road Surface");
        holder.transform.SetParent(parent, false);
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        GeneratedMesh.Own(holder);
    }
}
