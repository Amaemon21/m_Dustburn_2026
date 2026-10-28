using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public struct GrassMeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Color Color;
    public Vector2 Corner;
    public Vector3 Root;
}

[BurstCompile]
public struct GrassMeshJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<DecorInstance> Instances;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<GrassMeshVertex> Vertices;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Indices;
    public Vector3 Origin;
    public Color Tint;

    public void Execute(int index)
    {
        DecorInstance instance = Instances[index];
        Vector3 root = instance.Position - Origin;
        var color = new Color(Tint.r * instance.Shade, Tint.g * instance.Shade, Tint.b * instance.Shade, Tint.a);

        for (int plane = 0; plane < GrassTuft.PLANES; plane++)
        {
            float yaw = math.radians(instance.Rotation + plane * GrassTuft.PLANE_STEP);
            float splay = math.radians(plane % 2 == 0 ? GrassTuft.SPLAY : -GrassTuft.SPLAY);
            math.sincos(yaw, out float sinYaw, out float cosYaw);
            math.sincos(splay, out float sinSplay, out float cosSplay);
            var normal = new Vector3(-cosSplay * sinYaw, sinSplay, -cosSplay * cosYaw);
            var tangent = new Vector4(cosYaw, 0f, -sinYaw, -1f);
            int first = index * GrassTuft.VERTICES + plane * GrassTuft.CORNERS;

            for (int corner = 0; corner < GrassTuft.CORNERS; corner++)
            {
                var uv = new Vector2(corner == 1 || corner == 2 ? 1f : 0f, corner >= 2 ? 1f : 0f);
                float across = (uv.x - 0.5f) * instance.Scale;
                float up = uv.y * instance.Height;
                var offset = new Vector3(across * cosYaw + up * sinSplay * sinYaw, up * cosSplay,
                    -across * sinYaw + up * sinSplay * cosYaw);
                offset.y += instance.Slope.x * offset.x + instance.Slope.y * offset.z;

                Vertices[first + corner] = new GrassMeshVertex
                {
                    Position = root + offset,
                    Normal = normal,
                    Tangent = tangent,
                    Color = color,
                    Corner = uv,
                    Root = -offset
                };
            }

            int triangle = index * GrassTuft.PLANES * 6 + plane * 6;
            Indices[triangle] = first;
            Indices[triangle + 1] = first + 2;
            Indices[triangle + 2] = first + 1;
            Indices[triangle + 3] = first;
            Indices[triangle + 4] = first + 3;
            Indices[triangle + 5] = first + 2;
        }
    }
}
