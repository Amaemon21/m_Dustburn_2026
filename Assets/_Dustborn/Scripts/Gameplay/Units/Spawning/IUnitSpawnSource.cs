using System.Collections.Generic;

public interface IUnitSpawnSource
{
    IEnumerable<UnitSpawnRequest> Requests();
}
