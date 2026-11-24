using System;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;
using VContainer.Unity;

public sealed class BlockPlacementPreview : IDisposable
{
    private readonly MaterialPropertyBlock _properties = new();
    private readonly IObjectResolver _resolver;
    private BlockInventoryItem _item;
    private GameObject _root;
    private Renderer[] _renderers;

    public BlockPlacementPreview(IObjectResolver resolver) => _resolver = resolver;

    public void Show(BlockInventoryItem item, Vector3 position, Quaternion rotation, bool allowed)
    {
        if (_item != item)
        {
            Dispose();
            _item = item;
            _root = _resolver.Instantiate(item.Prefab.gameObject, position, rotation);
            _root.name = "BlockPlacementPreview";
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2;
            foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (BlockView view in _root.GetComponentsInChildren<BlockView>(true))
                view.enabled = false;
            _renderers = _root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in _renderers)
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        _root.SetActive(true);
        _root.transform.SetPositionAndRotation(position, rotation);
        Color color = allowed ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.15f, 0.15f);
        _properties.SetColor("_BaseColor", color);
        _properties.SetColor("_Color", color);
        foreach (Renderer renderer in _renderers)
            renderer.SetPropertyBlock(_properties);
    }

    public void Hide()
    {
        if (_root != null)
            _root.SetActive(false);
    }

    public void Dispose()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _item = null;
        _renderers = null;
    }
}
