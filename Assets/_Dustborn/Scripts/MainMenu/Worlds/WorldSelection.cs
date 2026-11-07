public sealed class WorldSelection
{
    private string _createdMapId;

    public void SelectCreatedMap(string worldId) => _createdMapId = worldId;

    public string TakeCreatedMap()
    {
        string worldId = _createdMapId;
        _createdMapId = null;
        return worldId;
    }
}
