using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class FirstPersonHandsPresenter : IHeldItemPresenter
{
    private const string GRAPH_NAME = "FirstPersonHands";
    private const string ITEM_GRAPH_NAME = "FirstPersonItem";
    private const int VOICES = 4;

    private readonly Animator _animator;
    private readonly Transform _socket;
    private readonly Transform _viewBone;
    private readonly Renderer[] _renderers;
    private readonly AnimationRig _rig;
    private readonly ClipLayer _layer;
    private readonly HandsMotionPlayer _motions;
    private readonly IViewImpact _impact;
    private readonly Dictionary<HeldItemConfig, ItemView> _views = new();
    private ItemView _shown;
    private bool _visible;

    public FirstPersonHandsPresenter(PlayerView view, IViewImpact impact)
    {
        _impact = impact;
        _animator = view.HandsAnimator;
        _animator.avatar = null;
        _socket = view.HandsSocket;
        _viewBone = view.HandsViewBone;
        _renderers = view.HandsRenderers ?? Array.Empty<Renderer>();
        _rig = new AnimationRig(GRAPH_NAME, _animator);
        _layer = _rig.AddLayer(VOICES);
        _motions = new HandsMotionPlayer(_layer);
        Hide();
    }

    public bool Show(HeldItemConfig config)
    {
        if (config == null || config.Animations == null)
            return false;

        if (_motions.Set != config.Animations)
            _motions.Use(config.Animations);

        _animator.enabled = true;
        _visible = true;
        SetRenderersVisible(true);
        ShowView(ViewOf(config));
        return true;
    }

    public void Hide()
    {
        _visible = false;
        _motions.Stop();
        ShowView(null);
        SetRenderersVisible(false);
        _animator.enabled = false;
    }

    public bool Play(HandsMotion motion) => _visible && _motions.Play(motion);

    public void Impact(float shake)
    {
        if (_visible)
            _impact?.Impact(shake);
    }

    public void Tick(float deltaTime)
    {
        if (!_visible)
            return;

        _layer.Advance(deltaTime);
        _shown?.Layer?.Advance(deltaTime);
        _motions.Update();
        _rig.Evaluate();
        PinViewBone();
        _shown?.Rig?.Evaluate();
    }

    private void PinViewBone()
    {
        if (_viewBone == null)
            return;

        Transform model = _animator.transform;
        Quaternion boneRotation = Quaternion.Inverse(model.rotation) * _viewBone.rotation;
        Vector3 bonePosition = model.InverseTransformPoint(_viewBone.position);
        Quaternion rotation = Quaternion.Inverse(boneRotation);
        model.SetLocalPositionAndRotation(-(rotation * bonePosition), rotation);
    }

    private ItemView ViewOf(HeldItemConfig config)
    {
        if (config.ViewPrefab == null)
            return null;

        if (_views.TryGetValue(config, out ItemView view))
            return view;

        GameObject root = Object.Instantiate(config.ViewPrefab, _socket, false);
        root.name = config.ViewPrefab.name;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        root.SetActive(false);
        view = new ItemView(root);
        _views.Add(config, view);
        return view;
    }

    private void ShowView(ItemView view)
    {
        if (_shown == view)
            return;

        if (_shown != null)
            _shown.Root.SetActive(false);

        _shown = view;

        if (_shown != null)
        {
            _shown.Root.SetActive(true);
            _shown.Animate();
        }

        _motions.Attach(_shown?.Layer);
    }

    private void SetRenderersVisible(bool visible)
    {
        foreach (Renderer renderer in _renderers)
            if (renderer != null)
                renderer.enabled = visible;
    }

    public void Dispose()
    {
        _motions.Attach(null);

        foreach (ItemView view in _views.Values)
            view.Dispose();

        _views.Clear();
        _shown = null;
        _rig.Dispose();
    }

    private sealed class ItemView : IDisposable
    {
        private readonly Animator _animator;

        public ItemView(GameObject root)
        {
            Root = root;
            _animator = root.GetComponentInChildren<Animator>(true);
        }

        public GameObject Root { get; }
        public AnimationRig Rig { get; private set; }
        public ClipLayer Layer { get; private set; }

        public void Animate()
        {
            if (Rig != null || _animator == null)
                return;

            Rig = new AnimationRig(ITEM_GRAPH_NAME, _animator);
            Layer = Rig.AddLayer(VOICES);
        }

        public void Dispose()
        {
            Rig?.Dispose();
            Rig = null;
            Layer = null;

            if (Root != null)
                Object.Destroy(Root);
        }
    }
}
