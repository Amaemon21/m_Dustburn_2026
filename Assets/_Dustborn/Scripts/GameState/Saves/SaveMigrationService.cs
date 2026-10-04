using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class SaveMigrationService
{
    private readonly Dictionary<int, ISaveMigration> _migrations = new();

    public SaveMigrationService(IReadOnlyList<ISaveMigration> migrations)
    {
        foreach (ISaveMigration migration in migrations)
        {
            if (migration.ToVersion <= migration.FromVersion)
                throw new ArgumentException("A migration must advance the save version", nameof(migrations));
            _migrations.Add(migration.FromVersion, migration);
        }
    }

    public void Upgrade(JObject document)
    {
        int version = document["version"]?.Value<int>() ?? 1;
        if (version > SaveDocument.CURRENT_VERSION)
            throw new UnsupportedSaveVersionException(version);
        while (version < SaveDocument.CURRENT_VERSION)
        {
            if (!_migrations.TryGetValue(version, out ISaveMigration migration) ||
                migration.ToVersion > SaveDocument.CURRENT_VERSION)
                throw new UnsupportedSaveVersionException(version);
            migration.Migrate(document);
            version = migration.ToVersion;
        }
        document["version"] = version;
    }
}
