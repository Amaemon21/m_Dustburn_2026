using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDatabaseConfig", menuName = "Dustborn/Units/Unit Database Config")]
public class UnitDatabaseConfig : ScriptableObject
{
    [SerializeField, BoxGroup("Unit Catalog"), Label("Units"), HorizontalLine(2f, EColor.Blue)] private List<UnitConfig> _enemyDatabase = new();

    public UnitConfig GetUnitConfigByType(UnitType enemyType)
    {
        return _enemyDatabase.Find(config => config.Type == enemyType);
    } 
}
