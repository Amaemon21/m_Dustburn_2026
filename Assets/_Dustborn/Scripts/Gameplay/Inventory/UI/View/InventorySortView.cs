using System;
using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySortView : View<InventorySortViewModel>, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly TimeSpan CLOSE_DELAY = TimeSpan.FromSeconds(0.25);

    [SerializeField, Required] private RectTransform _expand;
    [SerializeField, Required] private RectTransform _list;
    [SerializeField, Required] private Button _optionTemplate;

    [Space(10)]
    [SerializeField] private Image _expandIcon;
    [SerializeField] private Sprite _closedIcon;
    [SerializeField] private Sprite _openIcon;

    private readonly List<Button> _options = new();
    private readonly SerialDisposable _closing = new();
    private Canvas _listCanvas;
    private Image _arrow;
    private Sprite _closedSprite;

    private bool IsOpen => _list.gameObject.activeSelf;

    protected override void BindCore(InventorySortViewModel viewModel, CompositeDisposable bindings)
    {
        Close();
        for (int i = 0; i < viewModel.Modes.Count; i++)
        {
            InventorySortMode mode = viewModel.Modes[i];
            Button option = Option(i);
            option.gameObject.SetActive(true);
            option.GetComponentInChildren<TMP_Text>(true).text = InventorySortText.Label(mode);
            bindings.Add(option.OnClickAsObservable().Subscribe(_ => Choose(mode)));
        }

        for (int i = viewModel.Modes.Count; i < _options.Count; i++)
            _options[i].gameObject.SetActive(false);
    }

    private void Awake() => Close();

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (ViewModel == null)
        {
            Debug.LogWarning($"{nameof(InventorySortView)} on '{name}' is not bound: assign it to the _sort field of the panel's {nameof(InventoryView)}", this);
            return;
        }

        GameObject pressed = eventData.pointerPressRaycast.gameObject;
        Transform target = pressed != null ? pressed.transform : null;
        if (target != null && target.IsChildOf(_list))
            return;

        if (target != null && target.IsChildOf(_expand))
        {
            Toggle();
            return;
        }

        Choose(ViewModel.DefaultMode);
    }

    public void OnPointerEnter(PointerEventData eventData) => _closing.Disposable = Disposable.Empty;

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsOpen)
            _closing.Disposable = Observable.Timer(CLOSE_DELAY).Subscribe(_ => Close());
    }

    private void Choose(InventorySortMode mode)
    {
        Close();
        ViewModel?.Sort.Execute(mode);
    }

    private void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    private void Open()
    {
        _closing.Disposable = Disposable.Empty;
        _list.gameObject.SetActive(true);
        RaiseList();
        ShowArrow(true);
    }

    private void Close()
    {
        _closing.Disposable = Disposable.Empty;
        _list.gameObject.SetActive(false);
        ShowArrow(false);
    }

    private void ShowArrow(bool open)
    {
        if (!TryGetArrow(out Image arrow))
            return;

        Sprite sprite = open ? _openIcon : _closedSprite;
        if (sprite != null)
            arrow.sprite = sprite;
    }

    private bool TryGetArrow(out Image arrow)
    {
        if (_arrow == null)
        {
            if (_expandIcon != null)
                _arrow = _expandIcon;
            else if (_expand == null || !_expand.TryGetComponent(out _arrow))
            {
                arrow = null;
                return false;
            }
            _closedSprite = _closedIcon != null ? _closedIcon : _arrow.sprite;
        }

        arrow = _arrow;
        return arrow != null;
    }

    private void RaiseList()
    {
        if (_listCanvas == null)
        {
            if (!_list.TryGetComponent(out _listCanvas))
                _listCanvas = _list.gameObject.AddComponent<Canvas>();
            if (!_list.TryGetComponent(out GraphicRaycaster _))
                _list.gameObject.AddComponent<GraphicRaycaster>();
        }

        Canvas root = GetComponentInParent<Canvas>(true).rootCanvas;
        _listCanvas.overrideSorting = true;
        _listCanvas.sortingLayerID = root.sortingLayerID;
        _listCanvas.sortingOrder = root.sortingOrder + 1;
    }

    private Button Option(int index)
    {
        if (_options.Count == 0)
            _options.Add(_optionTemplate);
        while (_options.Count <= index)
            _options.Add(Instantiate(_optionTemplate, _optionTemplate.transform.parent));
        return _options[index];
    }

    protected override void OnUnbound()
    {
        if (_list != null)
            Close();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _closing.Dispose();
    }
}
