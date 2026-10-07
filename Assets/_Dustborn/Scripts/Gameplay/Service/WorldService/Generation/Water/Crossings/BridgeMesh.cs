using System.Collections.Generic;
using UnityEngine;

public sealed class BridgeMesh
{
    public const float DECK_OVERLAP = 4f;
    public const float DECK_LIFT = -0.15f;
    public const float DECK_THICKNESS = 0.8f;
    public const float KERB = 2.5f;
    public const float PARAPET_HEIGHT = 0.9f;
    public const float PARAPET_WIDTH = 0.3f;
    public const float PIER_SPACING = 10f;
    public const float PIER_WIDTH = 0.8f;
    public const float PIER_FOOTING = 3f;

    private static readonly Vector3 Right = new(1f, 0f, 0f);
    private static readonly Vector3 Up = new(0f, 1f, 0f);
    private static readonly Vector3 Forward = new(0f, 0f, 1f);

    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector2> Uv = new();
    public readonly List<int> Triangles = new();

    public int Piers { get; private set; }

    public static BridgeMesh Build(WaterCrossing crossing)
    {
        var mesh = new BridgeMesh();
        float length = crossing.Span + 2f * DECK_OVERLAP;
        float width = crossing.RoadWidth + 2f * KERB;
        float top = DECK_LIFT;
        float rail = (width - PARAPET_WIDTH) * 0.5f;

        mesh.Box(new Vector3(0f, top - DECK_THICKNESS * 0.5f, 0f), new Vector3(width, DECK_THICKNESS, length));
        mesh.Box(new Vector3(-rail, top + PARAPET_HEIGHT * 0.5f, 0f), new Vector3(PARAPET_WIDTH, PARAPET_HEIGHT, length));
        mesh.Box(new Vector3(rail, top + PARAPET_HEIGHT * 0.5f, 0f), new Vector3(PARAPET_WIDTH, PARAPET_HEIGHT, length));

        float bottom = crossing.WaterHeight - PIER_FOOTING - crossing.DeckHeight;
        float underside = top - DECK_THICKNESS;
        mesh.Piers = Mathf.FloorToInt(crossing.Span / PIER_SPACING);

        for (int i = 1; i <= mesh.Piers; i++)
        {
            float along = crossing.Span * (i / (float)(mesh.Piers + 1) - 0.5f);
            mesh.Box(new Vector3(0f, 0.5f * (underside + bottom), along), new Vector3(width - 2f * KERB, underside - bottom, PIER_WIDTH));
        }

        return mesh;
    }

    private void Box(Vector3 center, Vector3 size)
    {
        Vector3 half = size * 0.5f;

        Face(center, Up, Right * half.x, Forward * half.z, half.y);
        Face(center, -Up, Right * half.x, -Forward * half.z, half.y);
        Face(center, Right, Forward * half.z, Up * half.y, half.x);
        Face(center, -Right, -Forward * half.z, Up * half.y, half.x);
        Face(center, Forward, -Right * half.x, Up * half.y, half.z);
        Face(center, -Forward, Right * half.x, Up * half.y, half.z);
    }

    private void Face(Vector3 center, Vector3 normal, Vector3 u, Vector3 v, float offset)
    {
        Vector3 middle = center + normal * offset;
        int start = Vertices.Count;

        Vertices.Add(middle - u - v);
        Vertices.Add(middle - u + v);
        Vertices.Add(middle + u + v);
        Vertices.Add(middle + u - v);

        float width = 2f * u.magnitude, height = 2f * v.magnitude;

        Uv.Add(new Vector2(0f, 0f));
        Uv.Add(new Vector2(0f, height));
        Uv.Add(new Vector2(width, height));
        Uv.Add(new Vector2(width, 0f));

        for (int i = 0; i < 4; i++)
            Normals.Add(normal);

        Triangles.Add(start);
        Triangles.Add(start + 1);
        Triangles.Add(start + 2);
        Triangles.Add(start);
        Triangles.Add(start + 2);
        Triangles.Add(start + 3);
    }
}
