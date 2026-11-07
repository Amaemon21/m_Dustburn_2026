using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class GameplaySaveScreenshotService : IGameplayActivatable, IDisposable
{
    private const int WIDTH = 640;
    private const int HEIGHT = 360;
    private readonly ISaveService _saves;
    private readonly GameSlotImageStore _images;
    private readonly ICameraService _camera;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public GameplaySaveScreenshotService(ISaveService saves, GameSlotImageStore images, ICameraService camera)
    {
        _saves = saves;
        _images = images;
        _camera = camera;
    }

    public void Activate()
    {
        _subscriptions.Clear();
        _subscriptions.Add(_saves.SubscribeSlotSaved(CaptureAsync));
    }

    private async UniTask CaptureAsync(string slotId, CancellationToken saveToken)
    {
        if (_disposed)
            return;
        using var capture = CancellationTokenSource.CreateLinkedTokenSource(saveToken, _lifetime.Token);
        try
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, capture.Token);
            if (_saves.ActiveSlot != slotId || _camera.MainCamera == null)
                return;
            byte[] png = Capture(_camera.MainCamera);
            if (png != null)
                await _images.WriteAsync(slotId, png, capture.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogWarning($"The game was saved, but its preview could not be updated: {exception.Message}");
        }
    }

    private static byte[] Capture(Camera camera)
    {
        RenderTexture target = RenderTexture.GetTemporary(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                return null;
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, WIDTH, HEIGHT), 0, 0, false);
            image.Apply(false, false);
            return image.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (image != null)
                UnityEngine.Object.Destroy(image);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _subscriptions.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
