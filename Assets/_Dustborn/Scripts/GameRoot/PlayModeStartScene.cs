using System;

public static class PlayModeStartScene
{
    public const string SESSION_KEY = "Dustborn.PlayModeStartScene";

    public static bool TryGet(out Scenes scene)
    {
#if UNITY_EDITOR
        return Enum.TryParse(UnityEditor.SessionState.GetString(SESSION_KEY, string.Empty), out scene);
#else
        scene = default;
        return false;
#endif
    }
}
