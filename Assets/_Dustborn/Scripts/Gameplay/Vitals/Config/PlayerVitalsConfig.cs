using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerVitalsConfig", menuName = "Dustborn/Player/Vitals Config")]
public sealed class PlayerVitalsConfig : ScriptableObject
{
    [field: SerializeField, HorizontalLine(2f, EColor.Red)] public HealthSettings Health { get; private set; } = new();
    [field: SerializeField, HorizontalLine(2f, EColor.Yellow)] public StaminaSettings Stamina { get; private set; } = new();
    [field: SerializeField, HorizontalLine(2f, EColor.Orange)] public MetabolismSettings Metabolism { get; private set; } = new();
    [field: SerializeField, BoxGroup("Infection"), HorizontalLine(2f, EColor.Green), Min(1f)]
    public float MaxInfection { get; private set; } = 100f;
}
