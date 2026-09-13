public readonly struct SceneLoadStatus
{
    public float Progress { get; }
    public string Status { get; }

    public SceneLoadStatus(float progress, string status)
    {
        Progress = progress;
        Status = status;
    }
}
