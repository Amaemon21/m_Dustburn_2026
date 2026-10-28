using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class WorldPreviewCache : IDisposable
{
    private readonly Dictionary<string, Texture2D> _textures = new();

    public async UniTask<Texture2D> LoadAsync(WorldInfo world, CancellationToken token)
    {
        string path = world?.PreviewPath;
        if (path == null)
            return null;

        if (_textures.TryGetValue(world.Id, out Texture2D cached))
            return cached;

        byte[] png = await UniTask.RunOnThreadPool(() => File.Exists(path) ? File.ReadAllBytes(path) : null, cancellationToken: token);
        if (png == null)
            return null;

        if (_textures.TryGetValue(world.Id, out cached))
            return cached;

        Texture2D texture = WorldPreviewImage.Load(png);
        if (texture != null)
            _textures[world.Id] = texture;

        return texture;
    }

    public void Forget(string worldId)
    {
        if (!_textures.Remove(worldId, out Texture2D texture))
            return;

        WorldData.Release(texture);
    }

    public void Dispose()
    {
        foreach (Texture2D texture in _textures.Values)
            WorldData.Release(texture);

        _textures.Clear();
    }
}
