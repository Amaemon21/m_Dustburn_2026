using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class InventorySetting : ScriptableObject
{
    [field: SerializeField, BoxGroup("Owner"), Label("Owner ID"), HorizontalLine(2f, EColor.Blue)] public string OwnerId { get; private set; } = "Player";
    [field: SerializeField, BoxGroup("Backpack"), Label("Grid Size"), HorizontalLine(2f, EColor.Blue)]
    [field: FormerlySerializedAs("Size")]
    public Vector2Int Size { get; private set; } = new(6, 4);
}
