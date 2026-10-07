using UnityEngine;

public static class WaterCrossingBuilder
{
    private const float PIPE_MIN = 0.6f;
    private const float PIPE_MAX = 1.2f;
    private const float PIPE_SHARE = 0.8f;
    private const float PIPE_RISE = 0.2f;
    private const float PIPE_OVERHANG = 1f;

    public static GameObject Build(WaterMap water, Material bridgeMaterial, GameObject culvertPrefab, Transform parent)
    {
        if (water == null || water.Crossings.Count == 0)
            return null;

        var root = new GameObject("Water Crossings");
        root.transform.SetParent(parent, false);

        int bridges = 0, culverts = 0;

        foreach (WaterCrossing crossing in water.Crossings)
        {
            if (crossing.Span <= 0f)
                continue;

            if (crossing.Bridge)
            {
                Bridge(crossing, bridgeMaterial, root.transform);
                bridges++;
                continue;
            }

            if (culvertPrefab != null && Culvert(crossing, culvertPrefab, root.transform))
                culverts++;
        }

        Debug.Log($"Water crossings: {bridges} bridges, {culverts} culverts of {water.Crossings.Count} road crossings");
        return root;
    }

    private static void Bridge(WaterCrossing crossing, Material material, Transform parent)
    {
        BridgeMesh geometry = BridgeMesh.Build(crossing);

        var mesh = new Mesh { name = "Bridge" };
        mesh.SetVertices(geometry.Vertices);
        mesh.SetNormals(geometry.Normals);
        mesh.SetUVs(0, geometry.Uv);
        mesh.SetTriangles(geometry.Triangles, 0);
        mesh.RecalculateBounds();

        var holder = new GameObject("Bridge");
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(new Vector3(crossing.Center.x, crossing.DeckHeight, crossing.Center.y), Facing(crossing.Direction));

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        holder.AddComponent<MeshRenderer>().sharedMaterial = material;
        holder.AddComponent<MeshCollider>().sharedMesh = mesh;

        GeneratedMesh.Own(holder);
    }

    private static bool Culvert(WaterCrossing crossing, GameObject prefab, Transform parent)
    {
        var holder = new GameObject("Culvert");
        holder.transform.SetParent(parent, false);

        GameObject instance = Object.Instantiate(prefab, holder.transform, false);
        Bounds raw = Measure(holder);

        if (raw.size.x <= 1e-3f || raw.size.y <= 1e-3f || raw.size.z <= 1e-3f)
        {
            if (Application.isPlaying)
                Object.Destroy(holder);
            else
                Object.DestroyImmediate(holder);

            return false;
        }

        float diameter = Mathf.Clamp(crossing.RiverWidth * PIPE_SHARE, PIPE_MIN, PIPE_MAX);
        float length = crossing.Span + 2f * PIPE_OVERHANG;
        bool alongX = raw.size.x > raw.size.z;

        instance.transform.localScale = alongX
            ? new Vector3(length / raw.size.x, diameter / raw.size.y, diameter / raw.size.z)
            : new Vector3(diameter / raw.size.x, diameter / raw.size.y, length / raw.size.z);
        instance.transform.localRotation = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        instance.transform.localPosition -= Measure(holder).center;

        holder.transform.SetPositionAndRotation(new Vector3(crossing.Center.x, crossing.DeckHeight + PIPE_RISE * diameter, crossing.Center.y), Facing(crossing.Flow));
        return true;
    }

    private static Bounds Measure(GameObject holder)
    {
        Renderer[] renderers = holder.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return default;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private static Quaternion Facing(Vector2 direction)
    {
        return direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y)) : Quaternion.identity;
    }
}
