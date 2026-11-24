using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

internal sealed class PoiBuildPreparation
{
    public PoiBuildPlan Plan { get; private set; }
    public string RoofNote { get; private set; } = string.Empty;
    public Dictionary<PoiPart, PoiMesh> Meshes { get; } = new();
    public Dictionary<PoiSection, Material> Materials { get; } = new();
    public Dictionary<PoiPart, GameObject> Furniture { get; } = new();
    public List<string> Errors { get; } = new();
    public bool CanBuild => Plan != null && Errors.Count == 0;

    public static PoiBuildPreparation Create(PoiPresetSettings settings)
    {
        var result = new PoiBuildPreparation();

        switch (settings.Kind)
        {
            case PoiBuildingKind.House:
                result.PrepareHouse(settings);
                break;
            default:
                result.Errors.Add(PoiBuilderText.Get("unsupportedType"));
                break;
        }

        if (result.Plan == null)
            return result;

        foreach (PoiPlacement placement in result.Plan.Parts)
        {
            if (placement.Section == PoiSection.Furniture)
            {
                result.PrepareFurniture(placement.Part);
                continue;
            }

            if (result.Meshes.TryAdd(placement.Part, default))
                result.PrepareModel(settings, placement.Part);

            if (!result.Materials.ContainsKey(placement.Section))
                result.PrepareMaterial(settings, placement.Section);
        }

        if (result.Plan.Stairwell.HasOpening)
        {
            result.PrepareModel(settings, PoiPart.InteriorStairRamp);

            if (result.CanBuild && !PoiStairClearance.TryValidate(result, out string error))
                result.Errors.Add(error);
        }

        return result;
    }

    private void PrepareHouse(PoiPresetSettings settings)
    {
        if (!PoiHouseLayout.Fits(settings.House))
        {
            Errors.Add(PoiBuilderText.Format("invalidSize", PoiHouseLayout.MIN_SIZE, PoiHouseLayout.MAX_SIZE));
            return;
        }

        if (!Enum.IsDefined(typeof(PoiSide), settings.House.Entry))
        {
            Errors.Add(PoiBuilderText.Get("invalidSide"));
            return;
        }

        if (settings.House.SecondFloor && settings.House.Depth < PoiStairwellLayout.MIN_DEPTH)
        {
            Errors.Add(PoiBuilderText.Format("secondFloorDepth", PoiStairwellLayout.MIN_DEPTH));
            return;
        }

        bool pitched = PrepareRoof(settings);
        int ceilingEdgeSide = 1;

        if (pitched || settings.House.SecondFloor)
        {
            Meshes.Add(PoiPart.SlabHalf, default);
            PrepareModel(settings, PoiPart.SlabHalf);
            ceilingEdgeSide = Meshes[PoiPart.SlabHalf].Center.x > 0f ? -1 : 1;
        }

        Plan = PoiHouseLayout.Create(settings.House, pitched, ceilingEdgeSide);
    }

    private bool PrepareRoof(PoiPresetSettings settings)
    {
        if (!settings.House.Pitched)
            return false;

        if (!PoiHouseLayout.FitsPitchedRoof(settings.House))
        {
            RoofNote = PoiBuilderText.Get("oddRoof");
            return false;
        }

        GameObject slope = settings.Slope;
        GameObject gable = settings.Gable;

        if (slope == null || gable == null)
            return true;

        bool hasSlope = PoiMesh.TryRead(slope, PoiPart.Slope, out PoiMesh slopeMesh, out string slopeTransformError);
        bool hasGable = PoiMesh.TryRead(gable, PoiPart.Gable, out PoiMesh gableMesh, out string gableTransformError);

        if (slopeTransformError.Length > 0)
            Errors.Add(PoiBuilderText.Format(slopeTransformError, slope.name));

        if (gableTransformError.Length > 0)
            Errors.Add(PoiBuilderText.Format(gableTransformError, gable.name));

        if (!hasSlope || !hasGable)
        {
            if (slopeTransformError.Length == 0 && gableTransformError.Length == 0)
                RoofNote = PoiBuilderText.Get("incompatibleRoof");

            return false;
        }

        Meshes.Add(PoiPart.Slope, slopeMesh);
        Meshes.Add(PoiPart.Gable, gableMesh);
        return true;
    }

    private void PrepareModel(PoiPresetSettings settings, PoiPart part)
    {
        string modelName = part == PoiPart.InteriorStairSection ? "InteriorStairs" : part.ToString();
        GameObject source = part is PoiPart.Cube or PoiPart.Wall or PoiPart.Doorway or PoiPart.Window or PoiPart.Slab
            or PoiPart.Slope or PoiPart.Gable ? settings.GetModel(part) : PoiBuilderAssets.LoadModel(modelName);

        if (source == null)
        {
            Errors.Add(PoiBuilderText.Format("missingModel", PoiBuilderText.Part(part)));
            return;
        }

        if (PoiMesh.TryRead(source, part, out PoiMesh mesh, out string transformError))
            Meshes[part] = mesh;
        else if (transformError.Length > 0)
            Errors.Add(PoiBuilderText.Format(transformError, source.name));
        else
            Errors.Add(PoiBuilderText.Format("incompatibleModel", PoiBuilderText.Part(part), source.name));
    }

    private void PrepareMaterial(PoiPresetSettings settings, PoiSection section)
    {
        Material material = settings.GetMaterial(section);

        if (material == null && (section is PoiSection.Ceiling or PoiSection.WallInteriors or PoiSection.Partitions or PoiSection.Trim
            or PoiSection.Wainscot or PoiSection.Porch or PoiSection.PorchTimber or PoiSection.Glazing or PoiSection.StairwellGuard or PoiSection.Foundation))
            material = PoiBuilderAssets.LoadFinishMaterial(section);

        Materials.Add(section, material);
        string label = PoiBuilderText.Section(section);

        if (material == null || !EditorUtility.IsPersistent(material))
        {
            Errors.Add(PoiBuilderText.Format("missingMaterial", label));
            return;
        }

        if (!HasDefaultTiling(material, "_BaseMap") || !HasDefaultTiling(material, "_BumpMap"))
            Errors.Add(PoiBuilderText.Format("materialTiling", label, material.name));
    }

    private void PrepareFurniture(PoiPart part)
    {
        if (Furniture.ContainsKey(part))
            return;

        GameObject prefab = PoiBuilderAssets.LoadFurniture(part);

        if (prefab == null)
            Errors.Add(PoiBuilderText.Format("missingModel", PoiBuilderText.Part(part)));
        else
            Furniture.Add(part, prefab);
    }

    private static bool HasDefaultTiling(Material material, string property) => !material.HasProperty(property) ||
        (material.GetTextureScale(property) == Vector2.one && material.GetTextureOffset(property) == Vector2.zero);
}

internal readonly struct PoiMesh
{
    private const float TOLERANCE = 0.001f;
    private const float ROTATION_TOLERANCE = 0.00001f;

    private PoiMesh(Mesh mesh, Matrix4x4 transform, Bounds bounds, int alignment)
    {
        Source = mesh;
        Offset = transform.GetColumn(3);
        Bounds = bounds;
        Alignment = alignment;
    }

    public Mesh Source { get; }
    public Vector3 Offset { get; }
    public Bounds Bounds { get; }
    public Vector3 Center => Bounds.center;
    public int Alignment { get; }

    public static bool TryRead(GameObject source, PoiPart part, out PoiMesh result, out string transformError)
    {
        result = default;
        transformError = string.Empty;

        if (PrefabUtility.GetPrefabAssetType(source) != PrefabAssetType.Model ||
            !AssetDatabase.IsMainAsset(source))
            return false;

        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);

        if (filters.Length != 1 || !filters[0].TryGetComponent(out MeshRenderer renderer))
            return false;

        Mesh mesh = filters[0].sharedMesh;

        if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1 ||
            !mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
            return false;

        Transform meshTransform = filters[0].transform;
        Matrix4x4 transform = meshTransform.localToWorldMatrix;
        transformError = GetTransformError(meshTransform, transform);

        if (transformError.Length > 0 || transform.determinant <= 0f || !IsOrthogonal(transform))
            return false;

        using Mesh.MeshDataArray meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
        Mesh.MeshData data = meshData[0];
        using var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
        data.GetVertices(vertices);
        Vector3 first = transform.MultiplyPoint3x4(vertices[0]);
        var bounds = new Bounds(first, Vector3.zero);

        for (int i = 0; i < vertices.Length; i++)
            bounds.Encapsulate(transform.MultiplyPoint3x4(vertices[i]));

        if (!MatchesBounds(part, bounds))
            return false;

        if ((part is PoiPart.Wall or PoiPart.Doorway or PoiPart.Window or PoiPart.OpeningJamb or PoiPart.OpeningJambSkin) &&
            !MatchesWall(part, data, vertices, transform))
            return false;

        int alignment = 0;

        if (part is PoiPart.Slope or PoiPart.Gable or PoiPart.RoofEave or PoiPart.RoofRake or PoiPart.RoofEaveCorner)
        {
            if (MatchesRamp(part, vertices, transform, 1))
                alignment = 0;
            else if (MatchesRamp(part, vertices, transform, -1))
                alignment = 180;
            else
                return false;
        }

        if (part == PoiPart.ExteriorCorner)
        {
            alignment = (bounds.center.x > 0f, bounds.center.z > 0f) switch
            {
                (true, true) => 180,
                (true, false) => 90,
                (false, true) => -90,
                _ => 0
            };
        }

        if (part is PoiPart.PorchStairSection or PoiPart.PorchStairEndSection or PoiPart.PorchLowStairSection or PoiPart.PorchStairGuard or PoiPart.PorchCanopy
            or PoiPart.InteriorStairSection or PoiPart.InteriorStairRamp or PoiPart.InteriorStairRail or PoiPart.GableFascia or PoiPart.GableFasciaEnd)
        {
            bool alongX = part is PoiPart.GableFascia or PoiPart.GableFasciaEnd;
            float highEnd = 0f;

            foreach (Vector3 point in vertices)
            {
                Vector3 vertex = transform.MultiplyPoint3x4(point);

                if (Mathf.Abs(vertex.y - bounds.max.y) < TOLERANCE)
                    highEnd += alongX ? vertex.x : vertex.z;
            }

            if (Mathf.Abs(highEnd) < TOLERANCE)
                return false;

            alignment = (alongX ? highEnd < 0f : highEnd > 0f) ? 180 : 0;
        }

        if (part == PoiPart.InteriorStairRamp && !MatchesStairRamp(vertices, transform, alignment))
            return false;

        result = new PoiMesh(mesh, transform, bounds, alignment);
        return true;
    }

    private static string GetTransformError(Transform meshTransform, Matrix4x4 matrix)
    {
        for (Transform node = meshTransform; node != null; node = node.parent)
        {
            if ((node.localScale - Vector3.one).sqrMagnitude > TOLERANCE * TOLERANCE)
                return "invalidModelScale";

            Quaternion rotation = node.localRotation;
            var imaginary = new Vector3(rotation.x, rotation.y, rotation.z);

            if (imaginary.sqrMagnitude > ROTATION_TOLERANCE * ROTATION_TOLERANCE)
                return "invalidModelRotation";
        }

        return (matrix.lossyScale - Vector3.one).sqrMagnitude > TOLERANCE * TOLERANCE ? "invalidModelScale" : string.Empty;
    }

    internal static bool IsOrthogonal(Matrix4x4 matrix)
    {
        Vector3 x = matrix.GetColumn(0);
        Vector3 y = matrix.GetColumn(1);
        Vector3 z = matrix.GetColumn(2);
        return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) < TOLERANCE &&
            Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) < TOLERANCE &&
            Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) < TOLERANCE;
    }

    private static bool MatchesBounds(PoiPart part, Bounds bounds)
    {
        Vector3 size = part switch
        {
            PoiPart.Cube => Vector3.one,
            PoiPart.Slab => new Vector3(1f, PoiHouseLayout.SLAB_HEIGHT, 1f),
            PoiPart.SlabHalf => new Vector3(PoiHouseLayout.HALF_SLAB_WIDTH, PoiHouseLayout.SLAB_HEIGHT, 1f),
            PoiPart.Slope => new Vector3(1f, 1f + PoiHouseLayout.SLOPE_THICKNESS, 1f),
            PoiPart.RoofEave => new Vector3(1f, PoiRoofOverhangLayout.SIDE_RUN + PoiHouseLayout.SLOPE_THICKNESS, PoiRoofOverhangLayout.SIDE_RUN),
            PoiPart.RoofRake => new Vector3(PoiRoofOverhangLayout.GABLE_EXTENSION, 1f + PoiHouseLayout.SLOPE_THICKNESS, 1f),
            PoiPart.RoofEaveCorner => new Vector3(PoiRoofOverhangLayout.GABLE_EXTENSION,
                PoiRoofOverhangLayout.SIDE_RUN + PoiHouseLayout.SLOPE_THICKNESS, PoiRoofOverhangLayout.SIDE_RUN),
            PoiPart.Gable or PoiPart.WallPanel or PoiPart.OpeningJamb => new Vector3(1f, 1f, PoiHouseLayout.WALL_THICKNESS),
            PoiPart.PartitionTopPanel => new Vector3(1f, PoiWallLayout.PARTITION_TOP_HEIGHT, PoiHouseLayout.WALL_THICKNESS),
            PoiPart.WallSkin or PoiPart.OpeningJambSkin => new Vector3(1f, 1f, PoiWallLayout.SKIN_DEPTH),
            PoiPart.WallSkinCorner => new Vector3(PoiWallLayout.CORNER_SKIN_WIDTH, 1f, PoiWallLayout.SKIN_DEPTH),
            PoiPart.OpeningTrimSides => new Vector3(PoiWallLayout.CASING_WIDTH, 1f, PoiWallLayout.CASING_DEPTH),
            PoiPart.OpeningTrimHead => new Vector3(PoiWallLayout.CASING_WIDTH, PoiWallLayout.CASING_HEIGHT, PoiWallLayout.CASING_DEPTH),
            PoiPart.OpeningRevealSides => new Vector3(PoiWallLayout.OPENING_WIDTH, 1f, PoiWallLayout.REVEAL_DEPTH),
            PoiPart.OpeningRevealHead => new Vector3(PoiWallLayout.REVEAL_CLEAR_WIDTH, PoiWallLayout.REVEAL_THICKNESS, PoiWallLayout.REVEAL_DEPTH),
            PoiPart.WindowSill => new Vector3(PoiWallLayout.SILL_WIDTH, PoiWallLayout.SILL_HEIGHT, PoiWallLayout.SILL_DEPTH),
            PoiPart.OpeningSill => new Vector3(PoiWallLayout.SILL_WIDTH, PoiWallLayout.SILL_HEIGHT, PoiWallLayout.OPENING_SILL_DEPTH),
            PoiPart.WindowSash => new Vector3(PoiWallLayout.SASH_WIDTH, PoiWallLayout.SASH_HEIGHT, PoiWallLayout.SASH_DEPTH),
            PoiPart.WindowGlass => new Vector3(PoiWallLayout.GLAZING_WIDTH, PoiWallLayout.GLAZING_HEIGHT, PoiWallLayout.GLAZING_DEPTH),
            PoiPart.Baseboard or PoiPart.OpeningBaseboard => new Vector3(1f, PoiWallLayout.BASEBOARD_HEIGHT, PoiWallLayout.BASEBOARD_DEPTH),
            PoiPart.BaseboardCorner => new Vector3(PoiWallLayout.CORNER_BASEBOARD_WIDTH, PoiWallLayout.BASEBOARD_HEIGHT, PoiWallLayout.BASEBOARD_DEPTH),
            PoiPart.ExteriorCorner => new Vector3(PoiWallLayout.CORNER_BOARD_WIDTH + PoiWallLayout.CORNER_BOARD_DEPTH,
                PoiHouseLayout.WALL_HEIGHT, PoiWallLayout.CORNER_BOARD_WIDTH + PoiWallLayout.CORNER_BOARD_DEPTH),
            PoiPart.PorchDeck => new Vector3(PoiPorchLayout.WIDTH, PoiPorchLayout.DECK_HEIGHT, PoiPorchLayout.DEPTH),
            PoiPart.PorchStairSection => new Vector3(PoiPorchLayout.STAIR_SECTION_WIDTH, PoiPorchLayout.STAIR_SECTION_HEIGHT,
                PoiPorchLayout.STAIR_SECTION_DEPTH),
            PoiPart.PorchStairEndSection => new Vector3(PoiPorchLayout.STAIR_SECTION_WIDTH, PoiPorchLayout.STAIRS_HEIGHT, PoiPorchLayout.STAIR_END_DEPTH),
            PoiPart.PorchGuard => new Vector3(PoiPorchLayout.GUARD_WIDTH, PoiPorchLayout.GUARD_HEIGHT, PoiPorchLayout.DEPTH),
            PoiPart.PorchGuardToWall => new Vector3(PoiPorchLayout.GUARD_WIDTH, PoiPorchLayout.GUARD_HEIGHT, PoiPorchLayout.GUARD_TO_WALL_DEPTH),
            PoiPart.PorchStairGuard => new Vector3(PoiPorchLayout.GUARD_WIDTH, PoiPorchLayout.STAIR_GUARD_HEIGHT, PoiPorchLayout.STAIRS_DEPTH),
            PoiPart.InteriorStairSection or PoiPart.InteriorStairRamp => new Vector3(PoiStairwellLayout.WIDTH, PoiStairwellLayout.SECTION_RISE, 1f),
            PoiPart.InteriorStairRail => new Vector3(PoiStairwellLayout.GUARD_THICKNESS,
                PoiStairwellLayout.SECTION_RISE + PoiStairwellLayout.GUARD_HEIGHT - PoiStairwellLayout.RAIL_TREAD_Y, 1f),
            PoiPart.StairNewel => new Vector3(PoiStairwellLayout.NEWEL_WIDTH, PoiStairwellLayout.NEWEL_HEIGHT, PoiStairwellLayout.NEWEL_WIDTH),
            PoiPart.PorchFoundation => new Vector3(1f, PoiPorchLayout.STAIRS_HEIGHT, PoiPorchLayout.DEPTH),
            PoiPart.Plinth => new Vector3(1f, PoiHouseLayout.SLAB_HEIGHT, PoiHouseLayout.PLINTH_THICKNESS),
            PoiPart.PorchLowStairSection => new Vector3(PoiPorchLayout.STAIR_SECTION_WIDTH, PoiPorchLayout.DECK_HEIGHT, PoiPorchLayout.LOW_STAIRS_DEPTH),
            PoiPart.PorchPost => new Vector3(PoiPorchLayout.SUPPORT_WIDTH, PoiPorchLayout.POST_HEIGHT, PoiPorchLayout.SUPPORT_WIDTH),
            PoiPart.PorchCanopy => new Vector3(1f, PoiPorchLayout.CANOPY_RISE + PoiPorchLayout.CANOPY_THICKNESS, PoiPorchLayout.CANOPY_DEPTH),
            PoiPart.RoofFascia => new Vector3(1f, PoiRoofOverhangLayout.FASCIA_HEIGHT, PoiRoofOverhangLayout.FASCIA_DEPTH),
            PoiPart.RoofFasciaEnd => new Vector3(PoiRoofOverhangLayout.EAVE_FASCIA_END_WIDTH, PoiRoofOverhangLayout.FASCIA_HEIGHT,
                PoiRoofOverhangLayout.FASCIA_DEPTH),
            PoiPart.GableFascia => new Vector3(1f, 1f + PoiRoofOverhangLayout.FASCIA_HEIGHT, PoiRoofOverhangLayout.FASCIA_DEPTH),
            PoiPart.GableFasciaEnd => new Vector3(PoiRoofOverhangLayout.GABLE_FASCIA_END_WIDTH,
                PoiRoofOverhangLayout.GABLE_FASCIA_END_WIDTH + PoiRoofOverhangLayout.FASCIA_HEIGHT, PoiRoofOverhangLayout.FASCIA_DEPTH),
            PoiPart.StairwellSideGuard => new Vector3(PoiStairwellLayout.GUARD_THICKNESS, PoiStairwellLayout.GUARD_HEIGHT, PoiStairwellLayout.LENGTH),
            PoiPart.StairwellEndGuard => new Vector3(PoiStairwellLayout.WIDTH, PoiStairwellLayout.GUARD_HEIGHT, PoiStairwellLayout.GUARD_THICKNESS),
            _ => new Vector3(1f, PoiHouseLayout.WALL_HEIGHT, PoiHouseLayout.WALL_THICKNESS)
        };
        float bottom = part switch
        {
            PoiPart.Cube => -0.5f,
            PoiPart.WindowSill or PoiPart.OpeningSill => PoiWallLayout.SILL_BOTTOM,
            PoiPart.PorchStairGuard => PoiPorchLayout.STAIR_GUARD_BOTTOM,
            PoiPart.InteriorStairRail => PoiStairwellLayout.RAIL_TREAD_Y,
            PoiPart.WindowGlass => PoiWallLayout.GLAZING_BOTTOM,
            _ => 0f
        };
        float cornerCenter = (PoiWallLayout.CORNER_BOARD_WIDTH - PoiWallLayout.CORNER_BOARD_DEPTH) * 0.5f;
        float centerX = part == PoiPart.ExteriorCorner ? cornerCenter : part == PoiPart.SlabHalf ? PoiHouseLayout.HALF_SLAB_WIDTH * 0.5f : 0f;
        float centerZ = part == PoiPart.ExteriorCorner ? cornerCenter : 0f;
        return (bounds.size - size).sqrMagnitude < TOLERANCE * TOLERANCE &&
            Mathf.Abs(bounds.min.y - bottom) < TOLERANCE &&
            Mathf.Abs(Mathf.Abs(bounds.center.x) - centerX) < TOLERANCE &&
            Mathf.Abs(Mathf.Abs(bounds.center.z) - centerZ) < TOLERANCE;
    }

    private static bool MatchesRamp(PoiPart part, NativeArray<Vector3> vertices, Matrix4x4 transform, int direction)
    {
        bool slope = part != PoiPart.Gable;
        float slopeRun = part is PoiPart.RoofEave or PoiPart.RoofEaveCorner ? PoiRoofOverhangLayout.SIDE_RUN : 1f;

        foreach (Vector3 point in vertices)
        {
            Vector3 vertex = transform.MultiplyPoint3x4(point);
            float run = (slope ? vertex.z : vertex.x) * direction + slopeRun * 0.5f;
            float minimum = slope ? run : 0f;
            float maximum = slope ? run + PoiHouseLayout.SLOPE_THICKNESS : run;

            if (vertex.y < minimum - TOLERANCE || vertex.y > maximum + TOLERANCE)
                return false;
        }

        return true;
    }

    private static bool MatchesStairRamp(NativeArray<Vector3> vertices, Matrix4x4 transform, int alignment)
    {
        float direction = alignment == 0 ? 1f : -1f;

        foreach (Vector3 point in vertices)
        {
            Vector3 vertex = transform.MultiplyPoint3x4(point);
            float expectedY = (0.5f - direction * vertex.z) * PoiStairwellLayout.SECTION_RISE;

            if (Mathf.Abs(vertex.y - expectedY) > TOLERANCE)
                return false;
        }

        return true;
    }

    private static bool MatchesWall(PoiPart part, Mesh.MeshData data, NativeArray<Vector3> vertices, Matrix4x4 transform)
    {
        UnityEngine.Rendering.SubMeshDescriptor subMesh = data.GetSubMesh(0);

        if (subMesh.topology != MeshTopology.Triangles)
            return false;

        using var indices = new NativeArray<int>(subMesh.indexCount, Allocator.Temp);
        data.GetIndices(indices, 0);

        if (part is PoiPart.OpeningJamb or PoiPart.OpeningJambSkin)
            return !CoversPoint(vertices, indices, transform, new Vector2(0f, 0.5f)) &&
                CoversPoint(vertices, indices, transform, new Vector2(-0.46f, 0.5f)) &&
                CoversPoint(vertices, indices, transform, new Vector2(0.46f, 0.5f));

        bool openAtBase = part == PoiPart.Doorway;
        bool openAtMiddle = part is PoiPart.Doorway or PoiPart.Window;
        return CoversPoint(vertices, indices, transform, new Vector2(0f, 0.1f)) != openAtBase &&
            CoversPoint(vertices, indices, transform, new Vector2(0f, 1.5f)) != openAtMiddle &&
            CoversPoint(vertices, indices, transform, new Vector2(0f, 2.6f));
    }

    private static bool CoversPoint(NativeArray<Vector3> vertices, NativeArray<int> indices, Matrix4x4 transform, Vector2 point)
    {
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector2 a = transform.MultiplyPoint3x4(vertices[indices[i]]);
            Vector2 b = transform.MultiplyPoint3x4(vertices[indices[i + 1]]);
            Vector2 c = transform.MultiplyPoint3x4(vertices[indices[i + 2]]);

            if (Mathf.Abs(Cross(b - a, c - a)) < TOLERANCE)
                continue;

            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float ca = Cross(a - c, point - c);

            if ((ab >= -TOLERANCE && bc >= -TOLERANCE && ca >= -TOLERANCE) ||
                (ab <= TOLERANCE && bc <= TOLERANCE && ca <= TOLERANCE))
                return true;
        }

        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
