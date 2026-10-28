public sealed class WorldSelection
{
    public string PreferredId { get; private set; }

    public void Prefer(string worldId) => PreferredId = worldId;

    public string Take()
    {
        string preferred = PreferredId;
        PreferredId = null;
        return preferred;
    }
}
