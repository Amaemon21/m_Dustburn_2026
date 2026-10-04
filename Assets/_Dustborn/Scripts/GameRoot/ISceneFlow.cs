using System.Threading;
using Cysharp.Threading.Tasks;

public interface ISceneFlow
{
    bool Handles(Scenes scene);
    SceneEnterParams DirectEnter(Scenes scene);
    UniTask<SceneEnterParams> Run(SceneEnterParams enterParams, CancellationToken token);
}
