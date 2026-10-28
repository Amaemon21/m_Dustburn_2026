public class GameplayEnterParams : SceneEnterParams
{
    public const string TEST_SLOT = "slot_test";

    public string SlotId { get; }
    public string NewGameName { get; }
    public string NewGameWorldId { get; }
    public bool IsNewGame => NewGameName != null;

    public GameplayEnterParams(string slotId, Scenes scene = Scenes.Gameplay, string newGameName = null, string newGameWorldId = null)
        : base(scene)
    {
        if (scene != Scenes.Gameplay && scene != Scenes.Gameplay_Test)
            throw new System.ArgumentOutOfRangeException(nameof(scene));

        SlotId = slotId;
        NewGameName = newGameName;
        NewGameWorldId = newGameWorldId;
    }

    public static GameplayEnterParams ForScene(Scenes scene)
        => new(scene == Scenes.Gameplay_Test ? TEST_SLOT : SaveService.DEFAULT_SLOT, scene);

    public static GameplayEnterParams NewGame(string slotId, string name, string worldId) => new(slotId, Scenes.Gameplay, name, worldId);
}
