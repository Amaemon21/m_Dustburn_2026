using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class MenuButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
    IPointerUpHandler, ISelectHandler, IDeselectHandler, IMoveHandler
{
    [Header("References")] 
    [SerializeField] private RectTransform _content;

    [SerializeField] private TMP_Text _label;
    [SerializeField] private Image _hoverFill;
    [SerializeField] private Image _accent;
    [SerializeField] private Image _chevron;
    [SerializeField] private Image _rightArrow;
    [SerializeField] private Image _underline;

    [Header("Motion (unscaled seconds)")] 
    [SerializeField, Min(0f)] private float _hoverIn = 0.20f;

    [SerializeField, Min(0f)] private float _hoverOut = 0.16f;
    [SerializeField, Min(0f)] private float _clickFlash = 0.08f;
    [SerializeField, Min(0f)] private float _textShift = 8f;

    [Header("Colors")] 
    [SerializeField] private Color _idleText = new Color(0.851f, 0.843f, 0.812f, 1f);
    [SerializeField] private Color _focusedText = new Color(0.941f, 0.918f, 0.867f, 1f);
    [SerializeField] private Color _disabledText = new Color(0.45f, 0.48f, 0.47f, 1f);
    [SerializeField] private Color _highlight = new Color(0.773f, 0.647f, 0.282f, 0.20f);
    [SerializeField] private Color _pressedHighlight = new Color(0.773f, 0.647f, 0.282f, 0.32f);
    [SerializeField] private Color _accentColor = new Color(0.773f, 0.647f, 0.282f, 1f);
    [SerializeField] private Color _idleLine = new Color(0.42f, 0.43f, 0.39f, 0.40f);
    [SerializeField] private Color _focusedLine = new Color(0.773f, 0.647f, 0.282f, 0.60f);

    private Button button;
    private Vector2 homePosition;
    private bool cached, pointerInside, pointerDown, keyboardFocus;
    private bool activeLastFrame, interactableLastFrame, clickFlashing;
    private float progress;
    private Tween hoverTween, clickTimer;

    private void Awake()
    {
        Cache();
    }

    private void OnEnable()
    {
        Cache();
        button.onClick.AddListener(OnClicked);
        pointerInside = pointerDown = false;
        keyboardFocus = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        progress = 0f;
        clickFlashing = false;
        activeLastFrame = false;
        interactableLastFrame = button.IsInteractable();
        ApplyCurrent();
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(OnClicked);
        KillTweens();
        pointerInside = pointerDown = keyboardFocus = activeLastFrame = false;
        progress = 0f;
        clickFlashing = false;
        if (cached) ApplyCurrent();
    }

    private void OnDestroy()
    {
        KillTweens();
    }

    private void Cache()
    {
        if (button == null) button = GetComponent<Button>();
        if (cached) return;
        if (_content != null) homePosition = _content.anchoredPosition;
        if (_hoverFill != null)
        {
            _hoverFill.type = Image.Type.Filled;
            _hoverFill.fillMethod = Image.FillMethod.Horizontal;
            _hoverFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _hoverFill.preserveAspect = false;
        }

        cached = true;
    }

    private void LateUpdate()
    {
        RefreshState();
    }

    private void RefreshState()
    {
        if (!isActiveAndEnabled || !cached) return;
        bool interactable = button != null && button.IsInteractable();
        bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        bool active = interactable && selected && (pointerInside || keyboardFocus);
        bool changed = active != activeLastFrame;
        if (!interactable && (pointerDown || clickFlashing))
        {
            pointerDown = clickFlashing = false;
            KillClickTimer();
        }

        if (changed)
        {
            activeLastFrame = active;
            AnimateHover(active);
        }

        if (interactable != interactableLastFrame)
        {
            interactableLastFrame = interactable;
            ApplyCurrent();
        }
    }

    private void AnimateHover(bool active)
    {
        KillHoverTween();
        float target = active ? 1f : 0f;
        float duration = (active ? _hoverIn : _hoverOut) * Mathf.Abs(target - progress);
        if (duration <= 0f)
        {
            progress = target;
            ApplyCurrent();
            return;
        }

        hoverTween = DOTween.To(() => progress, value =>
            {
                progress = value;
                ApplyCurrent();
            }, target, duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => hoverTween = null);
    }

    private void ApplyCurrent()
    {
        bool interactable = button != null && button.IsInteractable();
        Apply(progress, interactable && ((pointerInside && pointerDown) || clickFlashing), interactable);
    }

    private void KillHoverTween()
    {
        if (hoverTween != null && hoverTween.IsActive()) hoverTween.Kill(false);
        hoverTween = null;
    }

    private void KillClickTimer()
    {
        if (clickTimer != null && clickTimer.IsActive()) clickTimer.Kill(false);
        clickTimer = null;
    }

    private void KillTweens()
    {
        KillHoverTween();
        KillClickTimer();
    }

    private void Apply(float amount, bool pressed, bool interactable)
    {
        if (_content != null) _content.anchoredPosition = homePosition + Vector2.right * (_textShift * amount);

        if (_label != null) _label.color = !interactable ? _disabledText : Color.Lerp(_idleText, _focusedText, amount);

        if (_hoverFill != null)
        {
            _hoverFill.fillAmount = amount;
            _hoverFill.color = pressed ? _pressedHighlight : _highlight;
        }

        SetAlpha(_accent, amount);
        SetAlpha(_chevron, amount);
        if (_underline != null) _underline.color = Color.Lerp(_idleLine, _focusedLine, amount);
    }

    private void SetAlpha(Image image, float amount)
    {
        if (image == null) return;
        Color color = _accentColor;
        color.a *= amount;
        image.color = color;
    }

    private void OnClicked()
    {
        if (!isActiveAndEnabled || !button.IsInteractable()) return;
        if (!pointerInside) keyboardFocus = true;
        RefreshState();
        KillClickTimer();
        clickFlashing = _clickFlash > 0f;
        ApplyCurrent();
        if (!clickFlashing) return;

        clickTimer = DOVirtual.DelayedCall(_clickFlash, () =>
            {
                clickFlashing = false;
                ApplyCurrent();
            }, true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => clickTimer = null);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!button.IsInteractable()) return;
        pointerInside = true;
        keyboardFocus = false;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
            EventSystem.current.SetSelectedGameObject(gameObject, eventData);
        RefreshState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = pointerDown = false;
        RefreshState();
        ApplyCurrent();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && button.IsInteractable())
        {
            pointerDown = true;
            ApplyCurrent();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            pointerDown = false;
            ApplyCurrent();
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        keyboardFocus = !(eventData is PointerEventData);
        RefreshState();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        keyboardFocus = pointerDown = false;
        RefreshState();
        ApplyCurrent();
    }

    public void OnMove(AxisEventData eventData)
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
        {
            keyboardFocus = true;
            RefreshState();
        }
    }

    public void Configure(RectTransform contentRect, TMP_Text text, Image fill, Image leftAccent, Image leftChevron,
        Image arrow, Image rule)
    {
        KillTweens();
        if (cached) Apply(0f, false, true);
        _content = contentRect;
        _label = text;
        _hoverFill = fill;
        _accent = leftAccent;
        _chevron = leftChevron;
        _underline = rule;
        cached = false;
        progress = 0f;
        Cache();
        Apply(0f, false, true);
    }
}