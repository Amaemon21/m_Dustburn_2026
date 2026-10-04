using UnityEngine;

public sealed class GameplayAssets
{
    public InventoryItemDatabase InventoryItems { get; }
    public PlayerInventorySettings InventorySettings { get; }
    public UnitDatabaseConfig UnitDatabase { get; }
    public PlayerInputSettings PlayerInput { get; }
    public UIInputSettings UIInput { get; }
    public InteractSettings InteractionSettings { get; }

    public GameObject PlayerPrefab { get; }
    public UIGameplayRootBinder UIPrefab { get; }

    public GameplayAssets(
        InventoryItemDatabase inventoryItems, PlayerInventorySettings inventorySettings,
        UnitDatabaseConfig unitDatabase, PlayerInputSettings playerInput,
        UIInputSettings uiInput, InteractSettings interactionSettings,
        GameObject playerPrefab, UIGameplayRootBinder uiPrefab)
    {
        InventoryItems = inventoryItems;
        InventorySettings = inventorySettings;
        UnitDatabase = unitDatabase;
        PlayerInput = playerInput;
        UIInput = uiInput;
        InteractionSettings = interactionSettings;

        PlayerPrefab = playerPrefab;
        UIPrefab = uiPrefab;
    }
}
