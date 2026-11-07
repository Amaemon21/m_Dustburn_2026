using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class WorldPreviewCache : IDisposable
{
    public const int THUMBNAIL_SIZE = 320;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorldInfo> _worlds = new();
    private bool _disposed;

    public UniTask<Texture2D> LoadAsync(WorldInfo world, CancellationToken token)
    {
        if (world != null)
            _worlds[world.Id] = world;
        return LoadPathAsync(world?.PreviewPath, token);
    }

    public async UniTask<Texture2D> LoadThumbnailAsync(WorldInfo world, CancellationToken token)
    {
        if (world == null)
            return null;
        _worlds[world.Id] = world;
        Texture2D thumbnail = await LoadPathAsync(world.ThumbnailPath, token, THUMBNAIL_SIZE);
        return thumbnail != null ? thumbnail : await LoadPathAsync(world.PreviewPath, token, THUMBNAIL_SIZE);
    }

    public async UniTask<Texture2D> LoadPathAsync(string path, CancellationToken token, int maxSize = 0)
    {
        if (_disposed || path == null)
            return null;
        string key = maxSize > 0 ? path + "|" + maxSize : path;
        if (_textures.TryGetValue(key, out Texture2D cached))
            return cached;
        try
        {
            byte[] png = await UniTask.RunOnThreadPool(() => File.Exists(path) ? File.ReadAllBytes(path) : null, cancellationToken: token);
            if (_disposed || png == null)
                return null;
            if (_textures.TryGetValue(key, out cached))
                return cached;
            Texture2D texture = WorldPreviewImage.Load(png);
            if (texture != null)
            {
                if (maxSize > 0)
                    texture = Downsample(texture, maxSize);
                texture.wrapMode = TextureWrapMode.Clamp;
                _textures[key] = texture;
            }
            return texture;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    private static Texture2D Downsample(Texture2D source, int maxSize)
    {
        int longest = Mathf.Max(source.width, source.height);
        if (longest <= maxSize)
            return source;
        int width = Mathf.Max(1, Mathf.RoundToInt((float)source.width * maxSize / longest));
        int height = Mathf.Max(1, Mathf.RoundToInt((float)source.height * maxSize / longest));
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D result = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            result.Apply(false, true);
            result.filterMode = FilterMode.Bilinear;
            return result;
        }
        catch
        {
            WorldData.Release(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            WorldData.Release(source);
        }
    }

    public void Forget(string worldId)
    {
        if (!_worlds.Remove(worldId, out WorldInfo world))
            return;
        ForgetPath(world.PreviewPath);
        ForgetPath(world.ThumbnailPath);
    }

    public void ForgetPath(string path)
    {
        if (path == null)
            return;
        foreach (string key in new List<string>(_textures.Keys))
        {
            if (key != path && !key.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_textures.Remove(key, out Texture2D texture))
                WorldData.Release(texture);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (Texture2D texture in _textures.Values)
            WorldData.Release(texture);
        _textures.Clear();
        _worlds.Clear();
    }
}
