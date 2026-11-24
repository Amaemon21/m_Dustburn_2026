using UnityEditor;
using UnityEngine;

internal static class PoiStairClearance
{
    private const string PLAYER = "Assets/_Dustborn/Content/Prefabs/Player.prefab";
    private const string MOVEMENT = "Assets/_Dustborn/Content/Configs/Player/PlayerMovementConfig.asset";
    private const float LANDING_REACH = 0.5f;
    private const float TOLERANCE = 0.001f;

    public static bool TryValidate(PoiBuildPreparation preparation, out string error)
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER);
        ScriptableObject movement = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MOVEMENT);
        CharacterController controller = player != null ? player.GetComponentInChildren<CharacterController>(true) : default;

        if (controller == null || movement == null)
        {
            error = PoiBuilderText.Get("stairPlayerMissing");
            return false;
        }

        using var config = new SerializedObject(movement);
        SerializedProperty standingHeight = config.FindProperty("<StandingHeight>k__BackingField");

        if (standingHeight == null)
        {
            error = PoiBuilderText.Get("stairPlayerMissing");
            return false;
        }

        float radius = controller.radius + controller.skinWidth;
        float height = Mathf.Max(controller.height, standingHeight.floatValue) + controller.skinWidth;
        float slope = Mathf.Atan(PoiStairwellLayout.SECTION_RISE) * Mathf.Rad2Deg;
        float crestRise = Mathf.Max(PoiStairwellLayout.RISE, radius * PoiStairwellLayout.SECTION_RISE);

        if (radius * 2f >= PoiStairwellLayout.WALK_WIDTH || radius >= LANDING_REACH ||
            slope >= controller.slopeLimit || crestRise + controller.skinWidth >= controller.stepOffset)
        {
            error = PoiBuilderText.Get("stairPlayerDoesNotFit");
            return false;
        }

        return TryValidateRoute(preparation, radius, height, out error);
    }

    internal static bool TryValidateRoute(PoiBuildPreparation preparation, float radius, float height, out string error)
    {
        PoiBuildPlan plan = preparation.Plan;
        PoiStairwell stairwell = plan.Stairwell;
        float lower = plan.WallColumns[0].Y;
        float upper = plan.UpperWallColumns[0].Y;
        bool lowerLanding = false;
        bool upperLanding = false;
        int sections = 0;

        foreach (PoiPlacement part in plan.Parts)
        {
            if (part.Part == PoiPart.InteriorStairSection)
            {
                float expectedZ = stairwell.LowEnd - sections - 0.5f;
                float expectedY = lower + sections * PoiStairwellLayout.SECTION_RISE;

                if (part.Yaw != 0 || !Near(part.X, stairwell.X) || !Near(part.Y, expectedY) || !Near(part.Z, expectedZ))
                {
                    error = PoiBuilderText.Get("stairSectionsMisaligned");
                    return false;
                }

                sections++;
                continue;
            }

            if (part.Section == PoiSection.Furniture)
                continue;

            PoiMesh mesh = preparation.Meshes[part.Part];
            Bounds bounds = GetBounds(part, mesh);

            if (part.Section == PoiSection.Floor && Near(bounds.max.y, lower))
                lowerLanding |= Covers(bounds, stairwell.X, stairwell.LowEnd + LANDING_REACH);

            if (part.Section == PoiSection.UpperFloor && Near(bounds.max.y, upper) && bounds.max.z <= stairwell.HighEnd + TOLERANCE)
            {
                upperLanding |= Covers(bounds, stairwell.X, stairwell.HighEnd - LANDING_REACH);
                continue;
            }

            if (BlocksRoute(stairwell, bounds, lower, upper, radius, height))
            {
                error = PoiBuilderText.Format("stairRouteBlocked", PoiBuilderText.Part(part.Part), part.X, part.Y, part.Z);
                return false;
            }
        }

        if (!lowerLanding || !upperLanding || sections != PoiStairwellLayout.LENGTH ||
            !Near(lower + sections * PoiStairwellLayout.SECTION_RISE, upper))
        {
            error = PoiBuilderText.Get("stairSectionsMisaligned");
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool BlocksRoute(PoiStairwell stairwell, Bounds bounds, float lower, float upper, float radius, float height)
    {
        if (bounds.max.x <= stairwell.X - radius || bounds.min.x >= stairwell.X + radius)
            return false;

        float minZ = Mathf.Max(bounds.min.z - radius, stairwell.HighEnd - LANDING_REACH);
        float maxZ = Mathf.Min(bounds.max.z + radius, stairwell.LowEnd + LANDING_REACH);

        if (minZ >= maxZ)
            return false;

        float lowestFeet = FootHeight(stairwell, lower, upper, maxZ);
        float highestHead = FootHeight(stairwell, lower, upper, minZ) + height;
        return bounds.max.y > lowestFeet + TOLERANCE && bounds.min.y < highestHead - TOLERANCE;
    }

    private static float FootHeight(PoiStairwell stairwell, float lower, float upper, float z) =>
        Mathf.Clamp(lower + (stairwell.LowEnd - z) * PoiStairwellLayout.SECTION_RISE, lower, upper);

    private static bool Covers(Bounds bounds, float x, float z) =>
        x >= bounds.min.x - TOLERANCE && x <= bounds.max.x + TOLERANCE &&
        z >= bounds.min.z - TOLERANCE && z <= bounds.max.z + TOLERANCE;

    private static Bounds GetBounds(PoiPlacement part, PoiMesh model)
    {
        int yaw = ((part.Yaw + model.Alignment) % 360 + 360) % 360;
        Vector3 center = model.Center;
        Vector3 size = model.Bounds.size;
        Vector3 rotatedCenter = yaw switch
        {
            90 => new Vector3(center.z, center.y, -center.x),
            180 => new Vector3(-center.x, center.y, -center.z),
            270 => new Vector3(-center.z, center.y, center.x),
            _ => center
        };

        if (yaw is 90 or 270)
            size = new Vector3(size.z, size.y, size.x);

        return new Bounds(new Vector3(part.X, part.Y, part.Z) + rotatedCenter, size);
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < TOLERANCE;
}
