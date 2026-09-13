public class UniqueId
{
    private readonly string _id;

    public string Id => _id;

    public UniqueId(string id)
    {
        _id = id;
    }
}