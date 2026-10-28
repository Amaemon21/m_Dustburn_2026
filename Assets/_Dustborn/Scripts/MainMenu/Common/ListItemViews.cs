using System.Collections.Generic;
using UnityEngine;

public sealed class ListItemViews
{
    private readonly ListItemView _prefab;
    private readonly Transform _content;
    private readonly List<ListItemView> _views = new();

    public ListItemViews(ListItemView prefab, Transform content)
    {
        _prefab = prefab;
        _content = content;
    }

    public void Show(IReadOnlyList<ListItemViewModel> items)
    {
        while (_views.Count < items.Count)
            _views.Add(Object.Instantiate(_prefab, _content));

        for (int i = 0; i < _views.Count; i++)
        {
            bool used = i < items.Count;
            _views[i].gameObject.SetActive(used);

            if (used)
                _views[i].Bind(items[i]);
            else
                _views[i].Unbind();
        }
    }

    public void Unbind()
    {
        foreach (ListItemView view in _views)
        {
            if (view != null)
                view.Unbind();
        }
    }
}
