using Newtonsoft.Json.Linq;

public interface ISaveMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(JObject document);
}
