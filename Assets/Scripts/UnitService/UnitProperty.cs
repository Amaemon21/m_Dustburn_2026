using System;
using Pathfinding;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class UnitProperty
{
    public FollowerEntity FollowerEntity;
    public Transform UnitCenter;
    public Transform WanderCenter;
    public float Radius;
    public float FieldOfViewAngle;
    public float MaxDistance;
    public LayerMask LayerMask;

    public int MaxHealth = 100;
}