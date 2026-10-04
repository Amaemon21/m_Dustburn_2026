public abstract class SceneEnterParams
{
    public Scenes Scene { get; }
    public string SceneName => Scene.ToString();

    public SceneEnterParams(Scenes scene)
    {
        Scene = scene;
    }

    public T As<T>() where T : SceneEnterParams
    {
        return (T)this;
    }
}
