# Decor streaming checks

Run from the repository root:

```powershell
dotnet run --project Tools/DecorStreamingCheck/DecorStreamingCheck.csproj
```

The executable links the production spawner, pool, demolition cursor, material preparation, block view and diagnostic scan. A fake engine supplies object hierarchies, delayed destruction and renderer data. Unity is never loaded or launched.

Checks cover layer boundaries, removed copies, incremental mesh and instance batches, gameplay rebinding before activation, tint and bounds stability, pool limits, destroyed pool entries, demolition across frames, block state reset, the diagnostic report limit, deferred grass jobs, cancellation and deduplicated prop regrounding.

From `Tools/HeadlessCheck`, run `dotnet run -- --runtime-streaming` to compare grass job vertices, normals, tangents, UVs, colors and triangle winding against the existing geometry, and to verify coverage after every incremental terrain swap. Run `dotnet run -- --voxel-recenter` to check swap groups on the saved world.

Use `Tools/UnityCompileCheck/UnityCompileCheck.BenchmarksEditor.csproj` and `Tools/InventoryCheck/GameplayCompile.csproj` for compilation against real Unity assemblies. These checks do not measure native activation, rendering or frame times; compare a new Profiler capture in Unity to validate performance.
