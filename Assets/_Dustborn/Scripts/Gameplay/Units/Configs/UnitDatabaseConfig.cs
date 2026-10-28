using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDatabaseConfig", menuName = "Dustborn/Units/Unit Database Config")]
public class UnitDatabaseConfig : ScriptableObject
{
    [SerializeField, BoxGroup("Unit Catalog"), Label("Units"), HorizontalLine(2f, EColor.Blue)] private List<UnitConfig> _enemyDatabase = new();

    [field: SerializeField, Foldout("Navigation")] public NavigationSettings Navigation { get; private set; } = new();

    public UnitConfig GetUnitConfigByType(UnitType enemyType)
    {
        return _enemyDatabase.Find(config => config.Type == enemyType);
    }

    private void OnValidate()
    {
        _enemyDatabase ??= new List<UnitConfig>();
        ListDuplicates.Clear(_enemyDatabase, config => config, index => _enemyDatabase[index] = null, this, "Units");
        ListDuplicates.Warn(_enemyDatabase, config => config.Type, this, "Units (by Type)");
    }
}
