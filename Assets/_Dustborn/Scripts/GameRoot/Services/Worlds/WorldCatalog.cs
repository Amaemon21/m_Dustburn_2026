using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class WorldCatalog
{
    public const string FOLDER = "Worlds";

    private const string SCENE_WORLD_NAME = "Мир из сцены (редактор)";

    public static string BuiltInRoot => Path.Combine(Application.streamingAssetsPath, FOLDER);
    public static string UserRoot => Path.Combine(Application.persistentDataPath, FOLDER);

    public async UniTask<IReadOnlyList<WorldInfo>> ListAsync(CancellationToken token)
    {
        string builtIn = BuiltInRoot;
        string user = UserRoot;

        List<WorldInfo> worlds = await UniTask.RunOnThreadPool(() =>
        {
            var found = new List<WorldInfo>();
            Scan(builtIn, true, found);
            Scan(user, false, found);
            return found;
        }, cancellationToken: token);

#if UNITY_EDITOR
        worlds.Add(WorldInfo.SceneWorld(SCENE_WORLD_NAME));
#endif
        return worlds;
    }

    public async UniTask<WorldInfo> FindAsync(string id, CancellationToken token)
    {
        foreach (WorldInfo world in await ListAsync(token))
        {
            if (world.Id == id)
                return world;
        }

        return null;
    }

    public UniTask DeleteAsync(WorldInfo world, CancellationToken token)
    {
        if (world.BuiltIn || world.IsSceneWorld)
            throw new InvalidOperationException($"World '{world.Id}' ships with the game and cannot be deleted");

        string root = Path.GetFullPath(UserRoot);
        string directory = Path.GetFullPath(world.Directory);
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"World '{world.Id}' is not inside the generated worlds folder");

        return UniTask.RunOnThreadPool(() =>
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }, cancellationToken: token);
    }

    private static void Scan(string root, bool builtIn, List<WorldInfo> found)
    {
        if (!Directory.Exists(root))
            return;

        string[] directories = Directory.GetDirectories(root);
        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

        foreach (string directory in directories)
        {
            if (directory.EndsWith(WorldDiskFormat.PARTIAL_SUFFIX, StringComparison.Ordinal))
                continue;

            try
            {
                WorldManifest manifest = WorldDiskFormat.ReadManifest(directory);
                if (manifest != null)
                    found.Add(WorldInfo.FromManifest(manifest, builtIn, directory));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"World folder '{directory}' is unreadable and is skipped: {exception.Message}");
            }
        }
    }
}
