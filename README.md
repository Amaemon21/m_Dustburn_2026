# Dustborn

A first-person survival game with procedural voxel terrain, exploration, loot, a grid inventory, building and persistent playthroughs.

## Requirements

- Unity 6000.3.20f1 with URP 17.3.0. See `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` and `Packages/packages-lock.json`.
- A* Pathfinding Project Pro and Repetitionless are embedded under `Packages/`. R3 uses an integration package through UPM and the core package through NuGetForUnity. See `Assets/packages.config`.

## Running

| Scene | Purpose |
|---|---|
| `Boot` | Application startup and loading screen. |
| `MainMenu` | Create or select a reusable map, start or load a playthrough, or enter the test scene. |
| `Gameplay` | Load the playthrough's world and saved state. |
| `Gameplay_Test` | Small hand-built gameplay scene with a dedicated save slot. |

Play mode uses Boot for the four project scenes. The main menu can generate and store a reusable map before gameplay starts. Direct entry into the authored gameplay scene needs its baked world. See the [world authoring workflow](Tools/WorldGeneration.md). The generated assets under `Assets/_Dustborn/Generated/` are local.

## Controls

`Assets/Settings/GameInputs.inputactions` defines WASD and mouse look, Shift sprint, Space jump, C crouch, F interact, I or Tab inventory, Escape close, 1–7 hotbar and mouse buttons for primary and secondary actions. F5 requests a save through `GameplaySaveInputService`. In inventory and loot UI, Shift+click transfers, Shift+drag splits, the wheel adjusts the dragged amount, R takes all, E uses the selected item, and G drops it.

## Checks without Unity

Run from the project root unless noted. Some compile projects need installed Unity DLLs and cached assemblies from an earlier editor import. See the [compile tools](Tools/UnityCompileCheck/README.md) for reference paths.

```powershell
dotnet run --project Tools/InventoryCheck/InventoryCheck.csproj
dotnet run --project Tools/InventoryCheck/InteractionCheck.csproj
dotnet run --project Tools/InventoryCheck/AssetCheck.csproj
dotnet build Tools/UnityCompileCheck/UnityCompileCheck.Runtime.csproj
dotnet build Tools/InventoryCheck/GameplayCompile.csproj -p:UseAddressablesStubs=true
Push-Location Tools/HeadlessCheck
dotnet run -c Release -- --pipeline-smoke
Pop-Location
```

[HeadlessCheck](Tools/HeadlessCheck/README.md) lists the world checks and their inputs. The project owner verifies scene wiring, rendering and runtime timings in Unity.
