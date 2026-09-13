using NaughtyAttributes;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class UnitLifetimeScope : LifetimeScope
{
    [SerializeField] private Transform _playerTransform;
    [SerializeField] private UnitSpawnPoint[] _spawnPoints;

    protected override void Configure(IContainerBuilder builder)
    {
        new UnitInstaller(_playerTransform, _spawnPoints).Install(builder);
    }

#if UNITY_EDITOR
    [Button("Collect Spawn Points")]
    private void CollectSpawnPoints()
    {
        _spawnPoints = FindObjectsByType<UnitSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
