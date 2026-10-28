using UnityEngine;

public sealed class GameplayAssets
{
    public InventoryItemDatabase InventoryItems { get; }
    public PlayerInventorySettings InventorySettings { get; }
    public UnitDatabaseConfig UnitDatabase { get; }
    public PlayerInputSettings PlayerInput { get; }
    public UIInputSettings UIInput { get; }
    public InteractSettings InteractionSettings { get; }
    public PlayerVitalsConfig PlayerVitals { get; }
    public PlayerProgressionConfig PlayerProgression { get; }

    public GameObject PlayerPrefab { get; }
    public UIGameplayRootBinder UIPrefab { get; }

    public GameplayAssets(
        InventoryItemDatabase inventoryItems, PlayerInventorySettings inventorySettings,
        UnitDatabaseConfig unitDatabase, PlayerInputSettings playerInput,
        UIInputSettings uiInput, InteractSettings interactionSettings,
        PlayerVitalsConfig playerVitals, PlayerProgressionConfig playerProgression,
        GameObject playerPrefab, UIGameplayRootBinder uiPrefab)
    {
        InventoryItems = inventoryItems;
        InventorySettings = inventorySettings;
        UnitDatabase = unitDatabase;
        PlayerInput = playerInput;
        UIInput = uiInput;
        InteractionSettings = interactionSettings;
        PlayerVitals = playerVitals;
        PlayerProgression = playerProgression;

        PlayerPrefab = playerPrefab;
        UIPrefab = uiPrefab;
    }
}
