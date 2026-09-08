using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class WorldGenerator : MonoBehaviour
{
    [SerializeField] private WorldBuildSettings _settings;
    [SerializeField] private BakedWorld _world;
    [SerializeField] private Transform _viewer;
    [SerializeField] private bool _loadOnStart = true;
    [SerializeField] private Behaviour[] _pauseWhileLoading = Array.Empty<Behaviour>();
    [SerializeField, HideInInspector] private GameObject _previewRoot;
    [SerializeField, HideInInspector] private WorldGenerationConfig _config;
    [SerializeField, HideInInspector] private BiomeDatabase _biomes;
    [SerializeField, HideInInspector] private PoiDatabase _pois;

    private WorldRuntimeLoader _loader;
    private bool[] _previousEnabled;
    private string _error;
    private bool _completed;

    public WorldBuildSettings Settings => _settings;
    public BakedWorld World => _world;
    public Transform Viewer => _viewer;
    public GameObject PreviewRoot => _previewRoot;
    public bool Ready => _loader != null && _loader.Ready;
    public float Progress => _loader == null ? 0f : _loader.Progress;
    public string Status => _error ?? (Ready ? "Мир готов" : _loader?.Stage ?? "Ожидание загрузки");

    public event Action WorldReady;

    private void OnEnable()
    {
        if (!Application.isPlaying || !_loadOnStart || _settings == null)
            return;

        LoadWorld();
    }

    public void LoadWorld()
    {
        if (!Application.isPlaying || _loader != null)
            return;

        if (_previewRoot != null)
            _previewRoot.SetActive(false);

        PauseGameplay();
        _error = null;
        _completed = false;

        try
        {
            _loader = new WorldRuntimeLoader(_settings, _world, _viewer, transform);
        }
        catch (Exception exception)
        {
            _error = "Мир не загружен. Проверьте сохранённую генерацию и игрока.";
            Debug.LogException(exception, this);
        }
    }

    private void Update()
    {
        if (_loader == null || _completed || _error != null)
            return;

        try
        {
            _loader.Tick();

            if (!Ready)
                return;

            _completed = true;
            ResumeGameplay();
            WorldReady?.Invoke();
        }
        catch (Exception exception)
        {
            _error = "Ошибка загрузки мира. Подробности в консоли.";
            Debug.LogException(exception, this);
        }
    }

    private void OnDisable()
    {
        _loader?.Dispose();
        _loader = null;
        ResumeGameplay();
    }

    private void PauseGameplay()
    {
        if (_previousEnabled != null)
            return;

        _previousEnabled = new bool[_pauseWhileLoading.Length];

        for (int index = 0; index < _pauseWhileLoading.Length; index++)
        {
            Behaviour behaviour = _pauseWhileLoading[index];

            if (behaviour == null || behaviour == this)
                continue;

            _previousEnabled[index] = behaviour.enabled;
            behaviour.enabled = false;
        }
    }

    private void ResumeGameplay()
    {
        if (_previousEnabled == null)
            return;

        for (int index = 0; index < _previousEnabled.Length; index++)
        {
            if (_pauseWhileLoading[index] != null && _previousEnabled[index])
                _pauseWhileLoading[index].enabled = true;
        }

        _previousEnabled = null;
    }

    private void OnGUI()
    {
        if (_settings == null || !_settings.ShowLoadingScreen || Ready || (_loader == null && _error == null))
            return;

        Color previous = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.07f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float width = Mathf.Min(440f, Screen.width - 32f);
        var area = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f - 35f, width, 80f);
        GUI.Label(area, $"{Status}\n{Progress:P0}");
        area.y += 50f;
        area.height = 12f;
        GUI.Box(area, GUIContent.none);
        area.width *= Progress;
        GUI.color = new Color(0.35f, 0.7f, 0.4f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
