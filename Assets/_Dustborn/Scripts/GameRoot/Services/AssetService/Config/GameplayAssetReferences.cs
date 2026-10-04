using NaughtyAttributes;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

[Serializable]
public sealed class GameplayAssetReferences
{
    [field: SerializeField, Header("Configs"), HorizontalLine(2f, EColor.Blue)] public AssetReferenceT<InventoryItemDatabase> InventoryItems { get; private set; }
    [field: SerializeField] public AssetReferenceT<PlayerInventorySettings> InventorySettings { get; private set; }
    [field: SerializeField] public AssetReferenceT<UnitDatabaseConfig> UnitDatabase { get; private set; }
    [field: SerializeField] public AssetReferenceT<PlayerInputSettings> PlayerInput { get; private set; }
    [field: SerializeField] public AssetReferenceT<UIInputSettings> UIInput { get; private set; }
    [field: SerializeField] public AssetReferenceT<InteractSettings> InteractionSettings { get; private set; }

    [field: SerializeField, Header("Prefabs"), HorizontalLine(2f, EColor.Green)] public AssetReferenceGameObject PlayerPrefab { get; private set; }
    [field: SerializeField] public AssetReferenceGameObject GameplayUIPrefab { get; private set; }
}
