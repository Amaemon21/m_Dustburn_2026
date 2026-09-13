using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

public class SceneLoader
{
    public UniTask LoadAsync(string sceneName, IProgress<float> progress, CancellationToken token)
    {
        return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single)
            .ToUniTask(progress, PlayerLoopTiming.Update, token);
    }
}
