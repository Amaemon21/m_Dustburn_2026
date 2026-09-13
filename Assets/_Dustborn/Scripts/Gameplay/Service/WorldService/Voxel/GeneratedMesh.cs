using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class GeneratedMesh : MonoBehaviour
{
    public static void Own(GameObject holder)
    {
        holder.AddComponent<GeneratedMesh>();
    }

    public static void Destroy(GameObject holder)
    {
        if (holder == null)
            return;

        foreach (GeneratedMesh owner in holder.GetComponentsInChildren<GeneratedMesh>(true))
            owner.Release();

        if (Application.isPlaying)
            Object.Destroy(holder);
        else
            DestroyImmediate(holder);
    }

    public void Release()
    {
        if (!TryGetComponent(out MeshFilter filter))
            return;

        Mesh mesh = filter.sharedMesh;

        if (mesh == null)
            return;

#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.Contains(mesh))
            return;
#endif

        filter.sharedMesh = null;

        if (Application.isPlaying)
            Object.Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }

    private void OnDestroy()
    {
        Release();
    }
}
