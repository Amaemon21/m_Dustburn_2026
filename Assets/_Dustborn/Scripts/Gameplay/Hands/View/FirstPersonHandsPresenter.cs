using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class FirstPersonHandsPresenter : IHeldItemPresenter
{
    private const int BASE_LAYER = 0;
    private const float USE_BLEND = 0.05f;
    private const float HOLSTER_BLEND = 0.1f;
    private const float POSE_BLEND = 0.25f;

    private static readonly int[] MOTION_HASHES = HashMotions();

    private readonly Animator _animator;
    private readonly Transform _socket;
    private readonly Renderer[] _renderers;
    private readonly Dictionary<HeldItemConfig, GameObject> _views = new();
    private readonly HashSet<(RuntimeAnimatorController, HandsMotion)> _reported = new();
    private GameObject _shown;

    public FirstPersonHandsPresenter(PlayerView view)
    {
        _animator = view.HandsAnimator;
        _socket = view.HandsSocket;
        _renderers = view.HandsRenderers ?? Array.Empty<Renderer>();
        Hide();
    }

    public bool Show(HeldItemConfig config)
    {
        if (config == null || config.Animator == null)
            return false;

        _animator.enabled = true;
        if (_animator.runtimeAnimatorController != config.Animator)
            _animator.runtimeAnimatorController = config.Animator;
        if (!_animator.isInitialized)
            _animator.Rebind();
        SetRenderersVisible(true);
        ShowView(ViewOf(config));
        return true;
    }

    public void Hide()
    {
        ShowView(null);
        SetRenderersVisible(false);
        _animator.enabled = false;
    }

    public void Play(HandsMotion motion)
    {
        RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
        
        if (!_animator.enabled || controller == null)
            return;

        int hash = MOTION_HASHES[(int)motion];
        
        if (!_animator.HasState(BASE_LAYER, hash))
        {
            ReportMissing(controller, motion);
            return;
        }

        switch (motion)
        {
            case HandsMotion.Equip:
                _animator.Play(hash, BASE_LAYER, 0f);
                _animator.Update(0f);
                break;
            case HandsMotion.Unequip:
                _animator.CrossFadeInFixedTime(hash, HOLSTER_BLEND, BASE_LAYER, 0f);
                break;
            case HandsMotion.Idle:
            case HandsMotion.Walk:
            case HandsMotion.Run:
                _animator.CrossFadeInFixedTime(hash, POSE_BLEND, BASE_LAYER, 0f);
                break;
            default:
                _animator.CrossFadeInFixedTime(hash, USE_BLEND, BASE_LAYER, 0f);
                break;
        }
    }

    private GameObject ViewOf(HeldItemConfig config)
    {
        if (config.ViewPrefab == null)
            return null;
        
        if (_views.TryGetValue(config, out GameObject view))
            return view;

        view = Object.Instantiate(config.ViewPrefab, _socket, false);
        view.name = config.ViewPrefab.name;
        
        foreach (Collider collider in view.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        
        view.SetActive(false);
        _views.Add(config, view);
        return view;
    }

    private void ShowView(GameObject view)
    {
        if (_shown == view)
            return;
        
        if (_shown != null)
            _shown.SetActive(false);
        
        _shown = view;
        
        if (_shown != null)
            _shown.SetActive(true);
    }

    private void SetRenderersVisible(bool visible)
    {
        foreach (Renderer renderer in _renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }

    private void ReportMissing(RuntimeAnimatorController controller, HandsMotion motion)
    {
        if (_reported.Add((controller, motion)))
            Debug.LogError($"{nameof(FirstPersonHandsPresenter)}: controller '{controller.name}' has no '{motion}' state on its base layer, the hands skip this motion");
    }

    private static int[] HashMotions()
    {
        HandsMotion[] motions = (HandsMotion[])Enum.GetValues(typeof(HandsMotion));
        int[] hashes = new int[motions.Length];
        
        foreach (HandsMotion motion in motions)
            hashes[(int)motion] = Animator.StringToHash(motion.ToString());
        
        return hashes;
    }

    public void Dispose()
    {
        foreach (GameObject view in _views.Values)
        {
            if (view != null)
            {
                Object.Destroy(view);
            }
        }

        _views.Clear();
        _shown = null;
    }
}
