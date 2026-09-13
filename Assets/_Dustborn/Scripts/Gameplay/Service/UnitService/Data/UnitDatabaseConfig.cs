using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDatabaseConfig", menuName = "Gameplay/Units/UnitDatabaseConfig")]
public class UnitDatabaseConfig : ScriptableObject
{
    public const string RESOURCES_PATH = "UnitDatabaseConfig";

    [SerializeField, BoxGroup("EnemyDatabase"), HorizontalLine] private List<UnitConfig> _enemyDatabase = new();

    public UnitConfig GetUnitConfigByType(UnitType enemyType)
    {
        return _enemyDatabase.Find(config => config.Type == enemyType);
    } 
}