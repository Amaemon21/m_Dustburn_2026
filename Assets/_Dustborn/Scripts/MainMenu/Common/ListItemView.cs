using DG.Tweening;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class ListItemView : View<ListItemViewModel>, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler, IMoveHandler
{
    [SerializeField] private TMP_Text _name;
    [SerializeField] private TMP_Text _details;
    [SerializeField, Required] private Button _button;
    [SerializeField] private RawImage _thumbnail;
    [SerializeField] private Image _selectionAccent;

    [SerializeField, BoxGroup("Highlight"), Tooltip("Shared hover and selection fill. Selected rows keep Fill Amount at 1.")]
    private Image _hoverFill;
    [SerializeField, BoxGroup("Highlight")] private Image _hoverAccent;

    [SerializeField, Min(0f), BoxGroup("Motion")] private float _hoverIn = 0.20f;
    [SerializeField, Min(0f), BoxGroup("Motion")] private float _hoverOut = 0.16f;

    [SerializeField, Foldout("Colors")] private Color _titleHoverColor = new(0.941f, 0.918f, 0.867f, 1f);
    [SerializeField, Foldout("Colors")] private Color _detailsHoverColor = new(0.851f, 0.843f, 0.812f, 1f);
    [SerializeField, Foldout("Colors")] private Color _fillColor = new(0.773f, 0.647f, 0.282f, 0.16f);
    [SerializeField, Foldout("Colors")] private Color _accentColor = new(0.773f, 0.647f, 0.282f, 1f);

    private Color _titleIdleColor;
    private Color _detailsIdleColor;
    private Tween _tween;
    private float _progress;
    private bool _cached;
    private bool _pointerInside;
    private bool _highlighted;
    private bool _selected;
    private bool _keyboardFocus;

    protected override void BindCore(ListItemViewModel viewModel, CompositeDisposable bindings)
    {
        Cache();
        _selected = viewModel.IsSelected.CurrentValue;
        ResetHighlight(_selected);
        if (_name != null)
            _name.text = viewModel.Title;
        if (_details != null)
            _details.text = viewModel.Details;
        if (_thumbnail != null)
            bindings.Add(viewModel.Thumbnail.Subscribe(ShowThumbnail));

        _button.BindCommand(viewModel.Select, bindings);
        bindings.Add(viewModel.IsSelected.Subscribe(ShowSelection));
        FocusSelection();
    }

    protected override void OnUnbound()
    {
        _selected = false;
        ResetHighlight();
        if (_thumbnail == null)
            return;

        _thumbnail.texture = null;
        _thumbnail.enabled = false;
    }

    private void ShowSelection(bool selected)
    {
        _selected = selected;
        if (!isActiveAndEnabled)
            return;

        RefreshHighlight();
        Apply();
        FocusSelection();
    }

    private void ShowThumbnail(Texture2D texture)
    {
        _thumbnail.texture = texture;
        _thumbnail.enabled = texture != null;
        if (texture == null)
            return;

        Rect frame = _thumbnail.rectTransform.rect;
        float imageAspect = (float)texture.width / texture.height;
        float frameAspect = frame.height > 0f ? frame.width / frame.height : 16f / 9f;
        float width = Mathf.Min(1f, frameAspect / imageAspect);
        float height = Mathf.Min(1f, imageAspect / frameAspect);
        _thumbnail.uvRect = new Rect((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
    }

    private void OnEnable()
    {
        Cache();
        ResetHighlight(_selected);
        FocusSelection();
    }

    private void OnDisable()
    {
        ResetHighlight();
    }

    protected override void OnDestroy()
    {
        KillTween();
        base.OnDestroy();
    }

    private void LateUpdate()
    {
        RefreshHighlight();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
        _keyboardFocus = false;
        RefreshHighlight();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
        RefreshHighlight();
    }

    public void OnSelect(BaseEventData eventData)
    {
        _keyboardFocus = !(eventData is PointerEventData);
        if (!_selected && ViewModel != null && _button != null && _button.IsInteractable())
            ViewModel.Select.Execute(Unit.Default);
        RefreshHighlight();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _keyboardFocus = false;
        RefreshHighlight();
    }

    public void OnMove(AxisEventData eventData)
    {
        _keyboardFocus = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        RefreshHighlight();
    }

    private void FocusSelection()
    {
        EventSystem events = EventSystem.current;
        if (!_selected || !isActiveAndEnabled || _button == null || !_button.IsInteractable() || events == null || events.alreadySelecting)
            return;

        GameObject current = events.currentSelectedGameObject;
        if (current == gameObject)
            return;
        if (current != null && current.activeInHierarchy &&
            (transform.parent == null || !current.transform.IsChildOf(transform.parent)))
            return;

        events.SetSelectedGameObject(gameObject);
    }

    private void Cache()
    {
        if (_cached)
            return;

        if (_button == null)
            _button = GetComponent<Button>();
        if (_name != null)
            _titleIdleColor = _name.color;
        if (_details != null)
            _detailsIdleColor = _details.color;
        if (_hoverFill != null)
        {
            _hoverFill.type = Image.Type.Filled;
            _hoverFill.fillMethod = Image.FillMethod.Horizontal;
            _hoverFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _hoverFill.preserveAspect = false;
            _hoverFill.raycastTarget = false;
            _hoverFill.enabled = true;
        }
        if (_hoverAccent != null)
            _hoverAccent.raycastTarget = false;

        _cached = true;
    }

    private void RefreshHighlight()
    {
        if (!isActiveAndEnabled || !_cached)
            return;

        bool highlighted = _selected || ((_pointerInside || _keyboardFocus) && _button != null && _button.IsInteractable());
        if (highlighted == _highlighted)
            return;

        _highlighted = highlighted;
        KillTween();
        float target = highlighted ? 1f : 0f;
        float duration = (highlighted ? _hoverIn : _hoverOut) * Mathf.Abs(target - _progress);
        if (duration <= 0f)
        {
            _progress = target;
            Apply();
            return;
        }

        _tween = DOTween.To(() => _progress, value =>
            {
                _progress = value;
                Apply();
            }, target, duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .OnKill(() => _tween = null);
    }

    private void Apply()
    {
        if (_name != null)
            _name.color = Color.Lerp(_titleIdleColor, _titleHoverColor, _progress);
        if (_details != null)
            _details.color = Color.Lerp(_detailsIdleColor, _detailsHoverColor, _progress);
        if (_hoverFill != null)
        {
            _hoverFill.color = _fillColor;
            _hoverFill.fillAmount = _progress;
        }
        if (_hoverAccent != null)
        {
            Color color = _accentColor;
            color.a *= _progress;
            _hoverAccent.color = color;
            _hoverAccent.enabled = _progress > 0f;
        }
        if (_selectionAccent != null && _selectionAccent != _hoverAccent)
            _selectionAccent.enabled = _selected;
    }

    private void ResetHighlight(bool selected = false)
    {
        KillTween();
        _pointerInside = false;
        _keyboardFocus = false;
        _highlighted = selected;
        _progress = selected ? 1f : 0f;
        if (_cached)
            Apply();
    }

    private void KillTween()
    {
        if (_tween != null && _tween.IsActive())
            _tween.Kill(false);
        _tween = null;
    }
}
