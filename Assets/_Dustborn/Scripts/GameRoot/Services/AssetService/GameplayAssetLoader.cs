using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameplayAssetLoader : SceneAssetLoader<GameplayAssets>
{
    private readonly GameplayAssetReferences _references;

    public GameplayAssetLoader(IAssetService assets, GameplayAssetReferences references) : base(assets)
        => _references = references;

    protected override async UniTask<GameplayAssets> LoadAssetsAsync(AssetScope scope, CancellationToken token)
    {
        InventoryItemDatabase items = await scope.LoadAsync<InventoryItemDatabase>(_references.InventoryItems, token);
        PlayerInventorySettings inventory = await scope.LoadAsync<PlayerInventorySettings>(_references.InventorySettings, token);
        UnitDatabaseConfig units = await scope.LoadAsync<UnitDatabaseConfig>(_references.UnitDatabase, token);
        PlayerInputSettings playerInput = await scope.LoadAsync<PlayerInputSettings>(_references.PlayerInput, token);
        UIInputSettings input = await scope.LoadAsync<UIInputSettings>(_references.UIInput, token);
        InteractSettings interaction = await scope.LoadAsync<InteractSettings>(_references.InteractionSettings, token);

        GameObject player = await scope.LoadAsync<GameObject>(_references.PlayerPrefab, token);
        GameObject ui = await scope.LoadAsync<GameObject>(_references.GameplayUIPrefab, token);

        return new GameplayAssets(items, inventory, units, playerInput, input, interaction,
            player, ui.GetComponent<UIGameplayRootBinder>());
    }
}

