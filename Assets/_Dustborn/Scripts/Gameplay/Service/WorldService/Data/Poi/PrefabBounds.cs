#if UNITY_EDITOR
using UnityEngine;

public static class PrefabBounds
{
    public static bool TryMeasure(GameObject prefab, out Bounds bounds)
    {
        bounds = default;

        bool any = false;
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;

        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled)
                continue;

            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 point = matrix.MultiplyPoint3x4(offset);

                if (!any)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                    continue;
                }

                bounds.Encapsulate(point);
            }
        }

        return any;
    }
}
#endif
