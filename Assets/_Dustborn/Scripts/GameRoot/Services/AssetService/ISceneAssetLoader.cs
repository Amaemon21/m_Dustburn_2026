using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;

public interface ISceneAssetLoader
{
    UniTask LoadAsync(CancellationToken token);
    void Register(IContainerBuilder builder);
    void Unload();
}
