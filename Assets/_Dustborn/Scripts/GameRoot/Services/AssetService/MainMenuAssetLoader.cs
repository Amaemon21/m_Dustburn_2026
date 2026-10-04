using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class MainMenuAssetLoader : SceneAssetLoader<MainMenuAssets>
{
    private readonly ProjectAssetReferences _references;

    public MainMenuAssetLoader(IAssetService assets, ProjectAssetReferences references) : base(assets)
        => _references = references;

    protected override async UniTask<MainMenuAssets> LoadAssetsAsync(AssetScope scope, CancellationToken token)
    {
        GameObject ui = await scope.LoadAsync<GameObject>(_references.MainMenuUIPrefab, token);
        return new MainMenuAssets(ui.GetComponent<UIMainMenuRootBinder>());
    }
}
