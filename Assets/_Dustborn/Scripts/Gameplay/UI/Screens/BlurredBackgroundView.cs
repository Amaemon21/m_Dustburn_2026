using System;
using R3;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class BlurredBackgroundView : MonoBehaviour
{
    private RawImage _image;
    private IDisposable _binding;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
    }

    private void OnEnable()
    {
        KawaseBlur.Request();
        Show(KawaseBlur.Result);
        _binding = Observable.EveryUpdate()
            .Select(_ => KawaseBlur.Result)
            .DistinctUntilChanged()
            .Subscribe(Show);
    }

    private void OnDisable()
    {
        _binding?.Dispose();
        _binding = null;
        KawaseBlur.Release();
    }

    private void Show(Texture texture)
    {
        _image.texture = texture;
        _image.enabled = texture != null;
    }
}
