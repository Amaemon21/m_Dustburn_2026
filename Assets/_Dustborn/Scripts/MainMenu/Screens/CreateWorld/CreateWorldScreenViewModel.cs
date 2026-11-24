using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Debug = UnityEngine.Debug;

public sealed class CreateWorldScreenViewModel : MainMenuScreenViewModel
{
    private const int MAX_NAME_LENGTH = 40;

    private readonly WorldSettingsProvider _settings;
    private readonly WorldGenerationService _generator;
    private readonly WorldSelection _selection;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<bool> _isGenerating = new();
    private readonly ReactiveProperty<bool> _hasResult = new();
    private readonly ReactiveProperty<bool> _isStale = new();
    private readonly ReactiveProperty<string> _status = new(string.Empty);
    private readonly ReactiveProperty<string> _confirmLabel = new(LocalizationService.Text("menu.createWorld.save"));
    private readonly ReactiveProperty<float> _progress = new();
    private readonly Stopwatch _generationClock = new();
    private readonly ShareGroup _relief;
    private readonly ShareGroup _biomes;
    private CancellationTokenSource _generation;
    private WorldGenerationService.Draft _draft;
    private string _suggestedName;

    public ReactiveProperty<string> Name { get; } = new(string.Empty);
    public ReactiveProperty<string> Seed { get; } = new(string.Empty);
    public ReadOnlyReactiveProperty<string> SeedNumber { get; }
    public OptionFieldViewModel Size { get; } = new(SizeLabels(), (WorldVariation.REFERENCE_SIZE - WorldVariation.MIN_SIZE) / WorldVariation.SIZE_STEP);
    public OptionFieldViewModel PreviewQuality { get; } = new(MainMenuText.PreviewQualityOptions, (int)WorldPreviewQuality.Medium);
    public OptionFieldViewModel Towns { get; }
    public OptionFieldViewModel Wilderness { get; }
    public OptionFieldViewModel Rivers { get; }
    public OptionFieldViewModel Lakes { get; }
    public OptionFieldViewModel Craters { get; }
    public OptionFieldViewModel Rifts { get; }
    public OptionFieldViewModel Canyons { get; }
    public OptionFieldViewModel Plains => _relief.Fields[0];
    public OptionFieldViewModel Hills => _relief.Fields[1];
    public OptionFieldViewModel Mountains => _relief.Fields[2];
    public OptionFieldViewModel PineForest => _biomes.Fields[0];
    public OptionFieldViewModel BurntForest => _biomes.Fields[1];
    public OptionFieldViewModel Desert => _biomes.Fields[2];
    public OptionFieldViewModel Snow => _biomes.Fields[3];
    public OptionFieldViewModel Wasteland => _biomes.Fields[4];
    public OptionFieldViewModel Layout { get; }
    public WorldMapPreviewViewModel Preview { get; } = new();
    public ReadOnlyReactiveProperty<bool> IsGenerating => _isGenerating;
    public ReadOnlyReactiveProperty<bool> HasResult => _hasResult;
    public ReadOnlyReactiveProperty<bool> IsStale => _isStale;
    public ReadOnlyReactiveProperty<string> Status => _status;
    public ReadOnlyReactiveProperty<string> ConfirmLabel => _confirmLabel;
    public ReadOnlyReactiveProperty<float> Progress => _progress;
    public long ElapsedSeconds => _generationClock.ElapsedMilliseconds / 1000;
    public ReadOnlyReactiveProperty<string> GenerateLabel { get; }
    public ReactiveCommand<Unit> Generate { get; }
    public ReactiveCommand<Unit> RandomSeed { get; }
    public ReactiveCommand<Unit> Back { get; }
    public ReactiveCommand<Unit> NewGame { get; }

    public CreateWorldScreenViewModel(MainMenuNavigation navigation, WorldSettingsProvider settings, WorldGenerationService generator,
        WorldSelection selection, ConfirmDialogViewModel dialog) : base(navigation, MainMenuScreen.CreateWorld)
    {
        _settings = settings;
        _generator = generator;
        _selection = selection;
        _dialog = dialog;

        var defaults = new WorldVariation();
        Towns = Amount(defaults.Towns);
        Wilderness = Amount(defaults.Wilderness);
        Rivers = Amount(defaults.Rivers);
        Lakes = Amount(defaults.Lakes);
        Craters = Amount(defaults.Craters);
        Rifts = Amount(defaults.Rifts);
        Canyons = Amount(defaults.Canyons);
        Layout = new OptionFieldViewModel(MainMenuText.LayoutOptions, (int)defaults.Layout);
        _relief = new ShareGroup(new[] { defaults.Plains, defaults.Hills, defaults.Mountains }, new[] { 100, 100, WorldVariation.MAX_MOUNTAINS });
        _biomes = new ShareGroup(new[] { defaults.PineForest, defaults.BurntForest, defaults.Desert, defaults.Snow, defaults.Wasteland });

        SeedNumber = Seed.Select(text => LocalizationService.Format("menu.createWorld.seedNumber", WorldVariation.SeedOf(text)))
            .ToReadOnlyReactiveProperty();
        GenerateLabel = _hasResult.Select(has => LocalizationService.Text(has ? "menu.createWorld.regenerate" : "menu.createWorld.generate"))
            .ToReadOnlyReactiveProperty();

        Generate = Command();
        RandomSeed = Command();
        Back = Command();
        NewGame = Command();

        foreach (OptionFieldViewModel field in WorldFields())
            Disposables.Add(field.Index.Subscribe(_ => RefreshStale()));

        Disposables.Add(Seed.Subscribe(_ => RefreshStale()));
        Disposables.Add(Seed.Subscribe(SuggestName));
        Disposables.Add(PreviewQuality.Index.Skip(1).Subscribe(index => Preview.RepaintAsync((WorldPreviewQuality)index).Forget()));
        Disposables.Add(_isStale.Subscribe(Preview.SetStale));
        Disposables.Add(_isGenerating.CombineLatest(_hasResult, _isStale, (generating, has, stale) => (generating, has && !stale))
            .Subscribe(RefreshCommands));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(RandomSeed.Executed.Subscribe(_ => Seed.Value = NewSeed()));
        Disposables.Add(Generate.Executed.Subscribe(_ => GenerateAsync().Forget()));
        Disposables.Add(Back.Executed.Subscribe(_ => GoBack()));
        Disposables.Add(NewGame.Executed.Subscribe(_ => SaveAsync().Forget()));
    }

    public override void GoBack()
    {
        if (_isGenerating.Value)
        {
            _generation?.Cancel();
            return;
        }

        ClearDraft();
        Navigation.Show(MainMenuScreen.NewGame);
    }

    public WorldVariation Variation()
    {
        return new WorldVariation
        {
            Seed = WorldVariation.SeedOf(Seed.Value),
            WorldSize = WorldVariation.MIN_SIZE + Size.Index.Value * WorldVariation.SIZE_STEP,
            Towns = (WorldAmount)Towns.Index.Value,
            Wilderness = (WorldAmount)Wilderness.Index.Value,
            Rivers = (WorldAmount)Rivers.Index.Value,
            Lakes = (WorldAmount)Lakes.Index.Value,
            Craters = (WorldAmount)Craters.Index.Value,
            Rifts = (WorldAmount)Rifts.Index.Value,
            Canyons = (WorldAmount)Canyons.Index.Value,
            Plains = _relief.Percent(0),
            Hills = _relief.Percent(1),
            Mountains = _relief.Percent(2),
            PineForest = _biomes.Percent(0),
            BurntForest = _biomes.Percent(1),
            Desert = _biomes.Percent(2),
            Snow = _biomes.Percent(3),
            Wasteland = _biomes.Percent(4),
            Layout = (BiomeLayout)Layout.Index.Value
        };
    }

    private async UniTask SaveAsync()
    {
        WorldGenerationService.Draft draft = _draft;
        if (draft == null || _isGenerating.Value || _isStale.Value)
            return;

        string name = CleanName(Name.Value, draft.Preview.World.Seed);
        StartOperation(WorldGenerationService.WriteStatus);

        try
        {
            WorldInfo world = await _generator.SaveAsync(draft, name, _lifetime.Token);
            if (IsDisposed)
                return;

            ClearDraft();
            _selection.SelectCreatedMap(world.Id);
            Navigation.Show(MainMenuScreen.NewGame);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!IsDisposed)
                _dialog.ShowMessage(LocalizationService.Text("menu.createWorld.saveFailed"));
        }
        finally
        {
            FinishOperation();
        }
    }

    private void OnShown()
    {
        if (_isGenerating.Value)
            return;

        if (!_hasResult.Value)
        {
            _status.Value = string.Empty;
            _progress.Value = 0f;
        }

        if (string.IsNullOrWhiteSpace(Seed.Value))
            Seed.Value = NewSeed();
    }

    private async UniTask GenerateAsync()
    {
        if (_isGenerating.Value)
            return;

        _generation = new CancellationTokenSource();
        CancellationToken token = _generation.Token;
        WorldVariation variation = Variation();
        string name = CleanName(Name.Value, variation.Seed);
        var quality = (WorldPreviewQuality)PreviewQuality.Index.Value;
        StartOperation(LocalizationService.Text("menu.createWorld.loadingSettings"));
        ClearDraft();

        try
        {
            using AddressableAsset<WorldBuildSettings> settings = await _settings.LoadAsync(token);
            IProgress<WorldGenerationProgress> progress = Cysharp.Threading.Tasks.Progress.Create<WorldGenerationProgress>(Report);
            WorldGenerationService.Draft draft = await _generator.PrepareAsync(settings.Asset, variation, name, quality, progress, token);
            if (IsDisposed)
                return;

            WorldGenerationResult result = draft.Preview;
            Preview.Show(result, Seed.Value, quality);
            _draft = draft;
            _hasResult.Value = true;
            _progress.Value = 1f;
            _status.Value = LocalizationService.Format("menu.createWorld.done", result.World.Name, result.Stats.Seconds);
            RefreshStale();
        }
        catch (OperationCanceledException)
        {
            SetStatus(LocalizationService.Text("menu.createWorld.cancelled"));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus(LocalizationService.Format("menu.createWorld.failed", exception.Message));
        }
        finally
        {
            _generation.Dispose();
            _generation = null;
            FinishOperation();
        }
    }

    private void StartOperation(string status)
    {
        _progress.Value = 0f;
        _status.Value = status;
        _generationClock.Restart();
        _isGenerating.Value = true;
    }

    private void FinishOperation()
    {
        _generationClock.Stop();
        if (!IsDisposed)
            _isGenerating.Value = false;
    }

    private void ClearDraft()
    {
        if (_draft == null)
            return;

        _draft = null;
        _hasResult.Value = false;
        _isStale.Value = false;
        Preview.Clear();
    }

    private void Report(WorldGenerationProgress progress)
    {
        if (IsDisposed)
            return;

        _status.Value = progress.Status;
        _progress.Value = progress.Fraction;
    }

    private void RefreshStale()
    {
        if (IsDisposed)
            return;

        _isStale.Value = _draft != null && !_draft.Preview.World.Variation.SameWorld(Variation());
    }

    private void SetStatus(string status)
    {
        if (!IsDisposed)
            _status.Value = status;
    }

    private void RefreshCommands((bool Generating, bool CanSave) state)
    {
        Generate.SetCanExecute(!state.Generating);
        RandomSeed.SetCanExecute(!state.Generating);
        Back.SetCanExecute(!state.Generating);
        NewGame.SetCanExecute(!state.Generating && state.CanSave);

        foreach (OptionFieldViewModel field in WorldFields())
            field.SetInteractable(!state.Generating);

        PreviewQuality.SetInteractable(!state.Generating);
    }

    private IEnumerable<OptionFieldViewModel> WorldFields()
    {
        yield return Size;
        yield return Towns;
        yield return Wilderness;
        yield return Rivers;
        yield return Lakes;
        yield return Craters;
        yield return Rifts;
        yield return Canyons;
        yield return Layout;

        foreach (OptionFieldViewModel field in _relief.Fields)
            yield return field;

        foreach (OptionFieldViewModel field in _biomes.Fields)
            yield return field;
    }

    private OptionFieldViewModel Amount(WorldAmount value)
    {
        var field = new OptionFieldViewModel(MainMenuText.AmountOptions, (int)value);
        return field;
    }

    private void SuggestName(string seed)
    {
        if (!string.IsNullOrWhiteSpace(Name.Value) && Name.Value != _suggestedName)
            return;

        _suggestedName = WorldNames.Of(WorldVariation.SeedOf(seed));
        Name.Value = _suggestedName;
    }

    private static string CleanName(string name, int seed)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return WorldNames.Of(seed);

        return trimmed.Length > MAX_NAME_LENGTH ? trimmed.Substring(0, MAX_NAME_LENGTH) : trimmed;
    }

    private static string NewSeed() => UnityEngine.Random.Range(1, int.MaxValue).ToString();

    private static string[] SizeLabels()
    {
        int count = (WorldVariation.MAX_SIZE - WorldVariation.MIN_SIZE) / WorldVariation.SIZE_STEP + 1;
        var labels = new string[count];

        for (int i = 0; i < count; i++)
            labels[i] = MainMenuText.Size(WorldVariation.MIN_SIZE + i * WorldVariation.SIZE_STEP);

        return labels;
    }

    protected override void OnDisposed()
    {
        base.OnDisposed();
        _generation?.Cancel();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _draft = null;
        Name.Dispose();
        Seed.Dispose();
        SeedNumber.Dispose();
        GenerateLabel.Dispose();
        Size.Dispose();
        PreviewQuality.Dispose();
        Layout.Dispose();
        _relief.Dispose();
        _biomes.Dispose();
        Preview.Dispose();
        Towns.Dispose();
        Wilderness.Dispose();
        Rivers.Dispose();
        Lakes.Dispose();
        Craters.Dispose();
        Rifts.Dispose();
        Canyons.Dispose();
        _isGenerating.Dispose();
        _hasResult.Dispose();
        _isStale.Dispose();
        _status.Dispose();
        _confirmLabel.Dispose();
        _progress.Dispose();
    }
}
