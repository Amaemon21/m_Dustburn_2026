public class GameplayEnterParams : SceneEnterParams
{
    public string SlotId { get; }

    public GameplayEnterParams(string slotId, Scenes scene = Scenes.Gameplay) : base(scene)
    {
        if (scene != Scenes.Gameplay && scene != Scenes.Gameplay_Test)
            throw new System.ArgumentOutOfRangeException(nameof(scene));

        SlotId = slotId;
    }
}
