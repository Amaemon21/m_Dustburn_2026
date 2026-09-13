using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIRootView : MonoBehaviour
{
    public const string RESOURCES_PATH = "UIRoot";

    [SerializeField] private GameObject _loadingScreen;
    [SerializeField] private Transform _uiSceneContainer;
    [SerializeField] private Image _progressFill;
    [SerializeField] private TMP_Text _statusLabel;

    [SerializeField, Min(0f)]
    [Tooltip("Loading screen stays up at least this long, so a fast transition does not flash it")]
    private float _minLoadingScreenTime = 1f;

    public float MinLoadingScreenTime => _minLoadingScreenTime;

    private void Awake()
    {
        HideLoadingScreen();
    }

    public void ShowLoadingScreen()
    {
        SetProgress(0f);
        SetStatus(string.Empty);
        _loadingScreen.SetActive(true);
    }

    public void HideLoadingScreen()
    {
        _loadingScreen.SetActive(false);
    }

    public void SetProgress(float value)
    {
        if (_progressFill == null)
            return;

        _progressFill.fillAmount = Mathf.Clamp01(value);
    }

    public void SetStatus(string status)
    {
        if (_statusLabel == null)
            return;

        _statusLabel.text = status;
    }

    public void AttachSceneUI(GameObject sceneUi)
    {
        ClearSceneUI();

        sceneUi.transform.SetParent(_uiSceneContainer, false);
    }

    public void ClearSceneUI()
    {
        for (int i = _uiSceneContainer.childCount - 1; i >= 0; i--)
            Destroy(_uiSceneContainer.GetChild(i).gameObject);
    }
}
