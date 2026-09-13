public class MainMenuEnterParams : SceneEnterParams
{
    public string Result { get; }

    public MainMenuEnterParams(string result) : base(Scenes.MAINMENU)
    {
        Result = result;
    }
}
