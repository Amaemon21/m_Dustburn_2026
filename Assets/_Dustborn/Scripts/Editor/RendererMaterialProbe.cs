using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class RendererMaterialProbe
{
    private const int REPORT_LIMIT = 40;

    private static readonly List<Material> Materials = new();

    [MenuItem("Tools/Dustborn/Diagnostics/Check Renderer Materials")]
    private static void Scan()
    {
        int reported = 0;

        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer.enabled)
                continue;

            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;

            Mesh mesh = MeshOf(renderer);
            renderer.GetSharedMaterials(Materials);

            int subMeshes = mesh == null ? 0 : mesh.subMeshCount;

            if (Materials.Count <= subMeshes)
                continue;

            string meshName = mesh == null ? "null" : $"'{mesh.name}'";
            Debug.LogWarning($"[RendererMaterialProbe] frame {Time.frameCount}: {PathOf(renderer.transform)} has {Materials.Count} materials "
                + $"[{NamesOf(Materials)}], mesh {meshName} with {subMeshes} submeshes, components [{ComponentsOf(renderer.gameObject)}]", renderer);

            if (++reported >= REPORT_LIMIT)
                break;
        }

        Materials.Clear();
        Debug.Log($"Renderer material check: {reported} mismatches reported (limit {REPORT_LIMIT}).");
    }

    private static Mesh MeshOf(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;

        return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
    }

    private static string NamesOf(List<Material> materials)
    {
        var names = new StringBuilder();

        foreach (Material material in materials)
        {
            if (names.Length > 0)
                names.Append(", ");

            names.Append(material == null ? "null" : $"{material.name} #{material.GetInstanceID()}");
        }

        return names.ToString();
    }

    private static string ComponentsOf(GameObject gameObject)
    {
        var names = new StringBuilder();

        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (names.Length > 0)
                names.Append(", ");

            names.Append(component == null ? "missing" : component.GetType().Name);
        }

        return names.ToString();
    }

    private static string PathOf(Transform transform)
    {
        var path = new StringBuilder(transform.name);

        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path.Insert(0, parent.name + "/");

        return path.ToString();
    }
}
