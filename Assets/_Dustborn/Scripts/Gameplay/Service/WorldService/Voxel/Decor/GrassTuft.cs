using UnityEngine;

public static class GrassTuft
{
    public const int PLANES = 3;
    public const int CORNERS = 4;
    public const int VERTICES = PLANES * CORNERS;

    public const float PLANE_STEP = 180f / PLANES;
    public const float SPLAY = 14f;

    public const float EDGE_HIDE = 0.04f;
    public const float EDGE_SHOW = 0.18f;

    public static readonly Vector2[] Corners = { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) };
    public static readonly int[] Quad = { 0, 2, 1, 0, 3, 2 };

    public static Vector3 Offset(float yaw, int plane, Vector2 corner, Vector2 size, Vector2 slope)
    {
        Frame(yaw, plane, out float sinYaw, out float cosYaw, out float sinSplay, out float cosSplay);

        float across = (corner.x - 0.5f) * size.x;
        float up = corner.y * size.y;

        var offset = new Vector3(across * cosYaw + up * sinSplay * sinYaw, up * cosSplay, -across * sinYaw + up * sinSplay * cosYaw);

        offset.y += slope.x * offset.x + slope.y * offset.z;

        return offset;
    }

    public static Vector3 Normal(float yaw, int plane)
    {
        Frame(yaw, plane, out float sinYaw, out float cosYaw, out float sinSplay, out float cosSplay);

        return new Vector3(-cosSplay * sinYaw, sinSplay, -cosSplay * cosYaw);
    }

    public static Vector3 Tangent(float yaw, int plane)
    {
        Frame(yaw, plane, out float sinYaw, out float cosYaw, out _, out _);

        return new Vector3(cosYaw, 0f, -sinYaw);
    }

    public static float Visibility(float facing)
    {
        float t = Mathf.Clamp01((Mathf.Abs(facing) - EDGE_HIDE) / (EDGE_SHOW - EDGE_HIDE));

        return t * t * (3f - 2f * t);
    }

    private static void Frame(float yaw, int plane, out float sinYaw, out float cosYaw, out float sinSplay, out float cosSplay)
    {
        float turn = (yaw + plane * PLANE_STEP) * Mathf.Deg2Rad;
        float splay = (plane % 2 == 0 ? SPLAY : -SPLAY) * Mathf.Deg2Rad;

        sinYaw = Mathf.Sin(turn);
        cosYaw = Mathf.Cos(turn);
        sinSplay = Mathf.Sin(splay);
        cosSplay = Mathf.Cos(splay);
    }
}
