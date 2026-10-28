public class MainMenuEnterParams : SceneEnterParams
{
    public string Result { get; }
    public string Notice { get; }

    public MainMenuEnterParams(string result, string notice = null) : base(Scenes.MainMenu)
    {
        Result = result;
        Notice = notice ?? string.Empty;
    }
}
