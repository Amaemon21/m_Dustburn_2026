using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class SlotViewPool<T> where T : Component
{
    private readonly T _prefab;
    private readonly Transform _content;
    private readonly IObjectResolver _resolver;
    private readonly List<T> _views = new();

    public IReadOnlyList<T> Views => _views;

    public SlotViewPool(T prefab, Transform content, IObjectResolver resolver)
    {
        _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab), "Assign the slot prefab");
        _content = content != null ? content : throw new ArgumentNullException(nameof(content), "Assign the slot content");
        _resolver = resolver;
    }

    public IReadOnlyList<T> Show(int count)
    {
        while (_views.Count < count)
            _views.Add(_resolver.Instantiate(_prefab, _content));

        for (int i = 0; i < _views.Count; i++)
            _views[i].gameObject.SetActive(i < count);
        return _views;
    }
}
