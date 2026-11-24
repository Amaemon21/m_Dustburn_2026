using System;
using UnityEngine;

[Serializable]
public sealed class MeleePresentationConfig
{
    [field: SerializeField] public HandsMotion Motion { get; private set; } = HandsMotion.Attack;
    [field: SerializeField, Range(0f, 1f)] public float ImpactShake { get; private set; }
    [field: SerializeField] public Material ImpactParticleMaterial { get; private set; }
}
