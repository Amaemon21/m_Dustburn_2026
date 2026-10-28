using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerProgressionConfig", menuName = "Dustborn/Player/Progression Config")]
public sealed class PlayerProgressionConfig : ScriptableObject
{
    [field: SerializeField, HorizontalLine(2f, EColor.Violet)] public LevelSettings Levels { get; private set; } = new();
    [field: SerializeField, HorizontalLine(2f, EColor.Green)] public HarvestExperienceSettings Harvest { get; private set; } = new();
}
