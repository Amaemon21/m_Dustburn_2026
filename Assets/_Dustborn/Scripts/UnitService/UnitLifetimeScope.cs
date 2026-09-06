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
        builder.RegisterInstance(_playerTransform);
        
        UnitDatabaseConfig unitDatabase = Resources.Load<UnitDatabaseConfig>(UnitDatabaseConfig.RESOURCES_PATH);
        builder.RegisterInstance(unitDatabase);
        
        builder.RegisterInstance(_spawnPoints);
        
        builder.Register<UnitFactory>(Lifetime.Singleton);
        builder.RegisterEntryPoint<UnitSpawnService>();
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
