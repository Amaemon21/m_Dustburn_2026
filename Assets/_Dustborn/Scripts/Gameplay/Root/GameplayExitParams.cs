public class GameplayExitParams
{
    public SceneEnterParams TargetSceneEnterParams { get; }

    public GameplayExitParams(SceneEnterParams targetSceneEnterParams)
    {
        TargetSceneEnterParams = targetSceneEnterParams;
    }
}
