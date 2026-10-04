using System.Collections.Generic;
using UnityEngine;

public static class DistanceUtility
{
    public static float SqrDistance(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaY = first.y - second.y;
        float deltaZ = first.z - second.z;

        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
    }

    public static float SqrDistance(Vector2 first, Vector2 second)
    {
        float deltaX = first.x - second.x;
        float deltaY = first.y - second.y;

        return deltaX * deltaX + deltaY * deltaY;
    }

    public static bool WithinRadius(Vector3 first, Vector3 second, float radius)
    {
        return SqrDistance(first, second) < radius * radius;
    }

    public static bool WithinRadius(Vector2 first, Vector2 second, float radius)
    {
        return SqrDistance(first, second) < radius * radius;
    }

    public static bool WithinRadius(Vector2 first, Vector2 second, float radius, out float sqrDistance)
    {
        sqrDistance = SqrDistance(first, second);

        return sqrDistance < radius * radius;
    }

    public static T GetClosest<T>(this IEnumerable<T> components, Vector3 position) where T : Component
    {
        T closest = null;
        float minSqrDistance = float.MaxValue;

        foreach (T component in components)
        {
            if (component == null)
                continue;

            float sqrDistance = SqrDistance(component.transform.position, position);

            if (sqrDistance >= minSqrDistance)
                continue;

            closest = component;
            minSqrDistance = sqrDistance;
        }

        return closest;
    }
}
