using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameSlotThumbnailCache
{
    private readonly WorldPreviewCache _previews;
    private readonly WorldCatalog _worlds;
    private readonly GameSlotImageStore _images;

    public GameSlotThumbnailCache(WorldPreviewCache previews, WorldCatalog worlds, GameSlotImageStore images)
    {
        _previews = previews;
        _worlds = worlds;
        _images = images;
    }

    public async UniTask<Texture2D> LoadAsync(GameSlotInfo slot, CancellationToken token)
    {
        try
        {
            Texture2D screenshot = await _previews.LoadPathAsync(_images.PathFor(slot.Id), token, WorldPreviewCache.THUMBNAIL_SIZE);
            if (screenshot != null)
                return screenshot;
            WorldInfo world = await _worlds.FindAsync(slot.WorldId, token);
            return await _previews.LoadThumbnailAsync(world, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
}
