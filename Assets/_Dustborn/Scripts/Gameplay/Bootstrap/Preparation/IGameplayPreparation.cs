using System;
using System.Threading;
using Cysharp.Threading.Tasks;

public interface IGameplayPreparation
{
    UniTask Prepare(IProgress<SceneLoadStatus> progress, CancellationToken token);
}
