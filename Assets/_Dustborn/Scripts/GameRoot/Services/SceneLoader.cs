using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader
{
    public UniTask LoadAsync(Scenes scene, IProgress<float> progress, CancellationToken token)
        => LoadAsync(scene.ToString(), progress, token);

    public async UniTask LoadAsync(string sceneName, IProgress<float> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await UniTask.SwitchToMainThread(token);
        token.ThrowIfCancellationRequested();

        if (!Application.isPlaying)
            throw new OperationCanceledException("Scene loading requires play mode", token);

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
            throw new InvalidOperationException($"Scene '{sceneName}' is unavailable. Enable it in the active Build Profile scene list");

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            token.ThrowIfCancellationRequested();
            if (!Application.isPlaying)
                throw new OperationCanceledException("Play mode ended while loading a scene", token);
            throw new InvalidOperationException($"Unity did not create a load operation for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'");
        }

        await operation.ToUniTask(progress, PlayerLoopTiming.Update, token);
    }
}
