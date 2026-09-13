public class GameplayEnterParams : SceneEnterParams
{
    public string SlotId { get; }

    public GameplayEnterParams(string slotId) : base(Scenes.GAMEPLAY)
    {
        SlotId = slotId;
    }
}
