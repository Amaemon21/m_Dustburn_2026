using NaughtyAttributes;
using UnityEngine;

public class PoiSpawner : MonoBehaviour
{
    [BoxGroup("Source"), Required("Assets/_Dustborn/Generated/PoiPlacement.asset"), SerializeField]
    private PoiPlacementAsset _placement;

    [BoxGroup("Source"), SerializeField] private bool _spawnOnStart;

    [BoxGroup("Ground"), SerializeField] private float _heightOffset;

    private int _spawned;

    [ShowNativeProperty]
    public string SpawnStatus => _placement == null
        ? "asset is not assigned"
        : $"{_placement.Placements.Count} in the asset, {_spawned} in the scene";

    private void Start()
    {
        if (_spawnOnStart)
            Spawn();
    }

    [Button("Spawn POI")]
    public void Spawn()
    {
        if (_placement == null)
        {
            Debug.LogError("No POI will appear: PoiPlacementAsset is not assigned", this);
            return;
        }

        if (_placement.Placements.Count == 0)
        {
            Debug.LogWarning("No POI will appear: the asset is empty, run generator step 4 and save the maps", this);
            return;
        }

        Clear();

        int missingPrefabs = 0;
        int notPrefabAssets = 0;
        int withoutRenderer = 0;

        var bounds = new Bounds();

        foreach (PoiPlacement placement in _placement.Placements)
        {
            if (placement.Prefab == null)
            {
                missingPrefabs++;
                continue;
            }

            Vector3 position = placement.PrefabPosition + Vector3.up * _heightOffset;

            GameObject instance = Create(placement.Prefab, ref notPrefabAssets);

            if (instance == null)
                continue;

            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, placement.Rotation, 0f));
            instance.name = $"{placement.Prefab.name}_{placement.District}";

            if (instance.GetComponentInChildren<Renderer>() == null)
                withoutRenderer++;

            if (_spawned == 0)
                bounds = new Bounds(position, Vector3.zero);
            else
                bounds.Encapsulate(position);

            _spawned++;
        }

        MarkSceneDirty();

        if (missingPrefabs > 0)
            Debug.LogWarning($"{missingPrefabs} entries skipped: their PoiDefinition had no Prefab assigned at generation time", this);

        if (notPrefabAssets > 0)
            Debug.LogWarning($"{notPrefabAssets} entries placed as plain copies: their PoiDefinition points at a scene object instead of a project prefab, so the prefab link is lost", this);

        if (_spawned == 0)
        {
            Debug.LogError("No POI was created even though the asset has entries. Check that the PoiDefinition entries point at prefabs from the Project folder", this);
            return;
        }

        if (withoutRenderer == _spawned)
            Debug.LogWarning($"All {_spawned} objects were created but none has a Renderer, so they sit in the hierarchy and show nothing in the scene. Put a mesh in the prefabs", this);
        else if (withoutRenderer > 0)
            Debug.LogWarning($"{withoutRenderer} of {_spawned} objects have no Renderer and are invisible in the scene", this);

        Debug.Log($"Spawned {_spawned} POI, bounds X {bounds.min.x:F0}..{bounds.max.x:F0}, Z {bounds.min.z:F0}..{bounds.max.z:F0}, Y {bounds.min.y:F0}..{bounds.max.y:F0}", this);
    }

    [Button("Clear POI")]
    public void Clear()
    {
        _spawned = 0;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        MarkSceneDirty();
    }

    private GameObject Create(GameObject prefab, ref int notPrefabAssets)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var linked = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform) as GameObject;

            if (linked != null)
            {
                UnityEditor.Undo.RegisterCreatedObjectUndo(linked, "Spawn POI");

                return linked;
            }

            notPrefabAssets++;
        }
#endif

        return Instantiate(prefab, transform);
    }

    private void MarkSceneDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

}
