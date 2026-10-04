using NaughtyAttributes;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

[Serializable]
public sealed class ProjectAssetReferences
{
    [field: SerializeField, Header("Prefabs"), HorizontalLine(2f, EColor.Green)] public AssetReferenceGameObject UIRootPrefab { get; private set; }
    [field: SerializeField] public AssetReferenceGameObject MainMenuUIPrefab { get; private set; }
}
