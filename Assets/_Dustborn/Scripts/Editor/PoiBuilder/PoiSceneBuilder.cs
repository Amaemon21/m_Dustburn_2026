using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

internal static class PoiSceneBuilder
{
    private const string UNDO_LABEL = "Build POI";
    private const string MARKER_NAME = "POI Builder Ownership [f1b374c6]";
    private const string MARKER_TAG = "EditorOnly";

    public static string GetAnchorError(Transform anchor)
    {
        if (anchor == null)
            return PoiBuilderText.Get("pickAnchor");

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return PoiBuilderText.Get("playMode");

        GameObject owner = anchor.gameObject;
        Scene scene = owner.scene;

        if (EditorUtility.IsPersistent(owner) || !scene.IsValid() || !scene.isLoaded ||
            EditorSceneManager.IsPreviewScene(scene) || (owner.hideFlags & HideFlags.NotEditable) != 0)
            return PoiBuilderText.Get("invalidAnchor");

        for (Transform parent = anchor; parent != null; parent = parent.parent)
        {
            if (FindMarker(parent) != null)
                return PoiBuilderText.Get("generatedAnchor");
        }

        Matrix4x4 matrix = anchor.localToWorldMatrix;

        if (matrix.determinant <= 0f || !PoiMesh.IsOrthogonal(matrix) ||
            (matrix.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            return PoiBuilderText.Get("scaledAnchor");

        return string.Empty;
    }

    public static bool TryFindResult(Transform anchor, out GameObject result)
    {
        if (anchor != null)
        {
            foreach (Transform child in anchor)
            {
                if (FindMarker(child) != null)
                {
                    result = child.gameObject;
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    public static GameObject Build(Transform anchor, PoiBuildPreparation preparation, string name)
    {
        if (!preparation.CanBuild || GetAnchorError(anchor).Length > 0)
            throw new InvalidOperationException("The POI build plan or scene anchor is invalid.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UNDO_LABEL);
        var root = new GameObject(name);

        try
        {
            SceneManager.MoveGameObjectToScene(root, anchor.gameObject.scene);
            Populate(root.transform, preparation);
            var marker = new GameObject(MARKER_NAME) { tag = MARKER_TAG };
            marker.transform.SetParent(root.transform, false);

            if (TryFindResult(anchor, out GameObject previous))
                Undo.DestroyObjectImmediate(previous);

            root.transform.SetParent(anchor, false);
            Undo.RegisterCreatedObjectUndo(root, UNDO_LABEL);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
            return root;
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);

            if (root != null)
                Object.DestroyImmediate(root);

            throw;
        }
    }

    public static GameObject SavePrefab(GameObject source, string path)
    {
        if (source == null || FindMarker(source.transform) == null)
            throw new InvalidOperationException("There is no generated scene object to save.");

        Scene preview = EditorSceneManager.NewPreviewScene();

        try
        {
            var container = new GameObject("POI Prefab Export");
            SceneManager.MoveGameObjectToScene(container, preview);
            GameObject copy = Object.Instantiate(source, container.transform, false);
            copy.transform.SetParent(null, false);
            copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            Object.DestroyImmediate(FindMarker(copy.transform).gameObject);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(copy, path, out bool success);

            if (!success || prefab == null)
                throw new InvalidOperationException($"Could not save the POI prefab at '{path}'.");

            return prefab;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void Populate(Transform root, PoiBuildPreparation preparation)
    {
        var sections = new Dictionary<PoiSection, Transform>();
        int index = 0;

        foreach (PoiPlacement placement in preparation.Plan.Parts)
        {
            if (!sections.TryGetValue(placement.Section, out Transform parent))
            {
                parent = new GameObject(placement.Section.ToString()).transform;
                parent.SetParent(root, false);
                sections.Add(placement.Section, parent);
            }

            if (placement.Section == PoiSection.Furniture)
            {
                PlaceFurniture(parent, placement, preparation.Furniture[placement.Part]);
                continue;
            }

            PoiMesh model = preparation.Meshes[placement.Part];
            Material material = preparation.Materials[placement.Section];

            if (placement.Part == PoiPart.InteriorStairSection)
                CreateStairSection(parent, placement, model, preparation.Meshes[PoiPart.InteriorStairRamp], material, index++);
            else
                CreatePart(parent, placement, model, material, index++);
        }
    }

    private static void PlaceFurniture(Transform parent, PoiPlacement placement, GameObject prefab)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetLocalPositionAndRotation(new Vector3(placement.X, placement.Y, placement.Z),
            Quaternion.Euler(0f, placement.Yaw, 0f));
    }

    private static void CreatePart(Transform parent, PoiPlacement placement, PoiMesh model, Material material, int index)
    {
        var part = new GameObject($"{placement.Part}_{index:D4}");
        part.transform.SetParent(parent, false);
        var position = new Vector3(placement.X, placement.Y, placement.Z);
        Quaternion rotation = Quaternion.Euler(0f, placement.Yaw + model.Alignment, 0f);
        part.transform.SetLocalPositionAndRotation(position + rotation * model.Offset, rotation);
        MeshRenderer renderer = AddRenderer(part, model.Source, material);

        if (placement.Section is PoiSection.WallInteriors or PoiSection.Wainscot or PoiSection.Glazing)
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (placement.Section is PoiSection.WallInteriors or PoiSection.Wainscot or PoiSection.Trim or PoiSection.Glazing ||
            placement.Part is PoiPart.Plinth or PoiPart.RoofFascia)
            return;

        if (placement.Part is PoiPart.Cube or PoiPart.Wall or PoiPart.WallPanel or PoiPart.PartitionTopPanel or PoiPart.Slab or PoiPart.SlabHalf
            or PoiPart.PorchDeck or PoiPart.PorchFoundation or PoiPart.PorchPost or PoiPart.StairNewel)
        {
            var collider = part.AddComponent<BoxCollider>();
            collider.center = model.Source.bounds.center;
            collider.size = model.Source.bounds.size;
        }
        else
            part.AddComponent<MeshCollider>().sharedMesh = model.Source;
    }

    private static void CreateStairSection(Transform parent, PoiPlacement placement, PoiMesh steps, PoiMesh ramp, Material material, int index)
    {
        GameObject section = CreateChild(parent, $"{placement.Part}_{index:D4}");
        section.transform.SetLocalPositionAndRotation(new Vector3(placement.X, placement.Y, placement.Z), Quaternion.Euler(0f, placement.Yaw, 0f));
        GameObject treads = CreateChild(section.transform, "Treads");
        SetMeshTransform(treads.transform, steps, steps.Alignment);
        AddRenderer(treads, steps.Source, material);
        GameObject surface = CreateChild(section.transform, "Walk Surface");
        SetMeshTransform(surface.transform, ramp, ramp.Alignment);
        surface.AddComponent<MeshCollider>().sharedMesh = ramp.Source;
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void SetMeshTransform(Transform transform, PoiMesh model, int alignment)
    {
        Quaternion rotation = Quaternion.Euler(0f, alignment, 0f);
        transform.SetLocalPositionAndRotation(rotation * model.Offset, rotation);
    }

    private static MeshRenderer AddRenderer(GameObject geometry, Mesh mesh, Material material)
    {
        geometry.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = geometry.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Transform FindMarker(Transform root)
    {
        foreach (Transform child in root)
        {
            if (child.name == MARKER_NAME && child.CompareTag(MARKER_TAG) && child.childCount == 0)
                return child;
        }

        return default;
    }
}
