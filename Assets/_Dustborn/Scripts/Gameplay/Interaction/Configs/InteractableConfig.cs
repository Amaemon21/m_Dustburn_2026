using NaughtyAttributes;

using UnityEngine;

public abstract class InteractableConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Identity"), Label("Display Name"), HorizontalLine(2f, EColor.Blue)] public string InteractableName { get; private set; }
}
