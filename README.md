# m_Dustburn_2026

A first-person survival game in the spirit of 7 Days to Die: an 8×8 km procedural world with a diggable voxel ground, biomes, rivers, roads and settlements, a grid inventory, containers and loot.

## Requirements

- Unity 6000.3.20f1 (URP)
- A* Pathfinding Project Pro and Repetitionless are embedded under `Packages/`; R3 and its dependencies come through NuGetForUnity (`Assets/packages.config`)

## Running

Open the project and press Play in any scene: play mode always starts in `Boot` and enters the open scene through the normal flow.

| Scene | Purpose |
|---|---|
| `Boot` | entry, loading screen |
| `MainMenu` | menu; buttons for the full game and for the test scene |
| `Gameplay` | the baked procedural world |
| `Gameplay_Test` | a small hand-built scene for testing gameplay without waiting for the world |

`Gameplay` needs a baked world: `Мир → Генерация мира` → **Сгенерировать мир**. The output in `Assets/_Dustborn/Generated/` is not committed.

## Controls

Bound in `Assets/Settings/GameInputs.inputactions`: WASD move, mouse look, Shift sprint, Space jump, C crouch, F interact, I inventory, Escape close, 1–7 hotbar; F5 saves (hard-wired in `GameplaySaveInputService`). In an open inventory: Shift+click quick transfer, drag to move, Shift+drag to split, wheel to change the dragged amount, R take all from a container, E use and G drop the selected item.

## Checks without Unity

```powershell
dotnet run --project Tools/InventoryCheck/InventoryCheck.csproj --no-restore
dotnet run --project Tools/InventoryCheck/InteractionCheck.csproj --no-restore
dotnet run --project Tools/InventoryCheck/AssetCheck.csproj --no-restore
dotnet build Tools/InventoryCheck/GameplayCompile.csproj --no-restore -p:UseAddressablesStubs=true
dotnet run --project Tools/HeadlessCheck -- --pipeline-smoke
```

The last one runs the world generation pipeline headless; `Tools/HeadlessCheck` has many more modes.
