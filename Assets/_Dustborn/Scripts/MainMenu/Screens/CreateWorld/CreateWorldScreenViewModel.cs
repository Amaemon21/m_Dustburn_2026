using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class CreateWorldScreenViewModel : MainMenuScreenViewModel
{
    private const string DEFAULT_NAME = "Новый мир";
    private const string CANCELLED = "Генерация отменена";
    private const string FAILED = "Не удалось создать мир: {0}";
    private const string LOADING_SETTINGS = "Загрузка настроек генератора";
    private const int DEFAULT_SIZE = WorldVariation.REFERENCE_SIZE;
    private static readonly string[] AMOUNTS = { "Нет", "Мало", "Норма", "Много" };
    private static readonly string[] RELIEFS = { "Равнины", "Пологий", "Холмистый" };

    private readonly WorldSettingsProvider _settings;
    private readonly WorldGenerationService _generator;
    private readonly WorldSelection _selection;
    private readonly ReactiveProperty<bool> _isGenerating = new();
    private readonly ReactiveProperty<string> _status = new(string.Empty);
    private readonly ReactiveProperty<float> _progress = new();
    private CancellationTokenSource _generation;

    public ReactiveProperty<string> Name { get; } = new(DEFAULT_NAME);
    public ReactiveProperty<string> Seed { get; } = new(string.Empty);
    public OptionFieldViewModel Size { get; } = new(SizeLabels(), (DEFAULT_SIZE - WorldVariation.MIN_SIZE) / WorldVariation.SIZE_STEP);
    public OptionFieldViewModel Towns { get; } = new(AMOUNTS, (int)WorldAmount.Normal);
    public OptionFieldViewModel Farmsteads { get; } = new(AMOUNTS, (int)WorldAmount.Normal);
    public OptionFieldViewModel Rivers { get; } = new(AMOUNTS, (int)WorldAmount.Normal);
    public OptionFieldViewModel Lakes { get; } = new(AMOUNTS, (int)WorldAmount.Normal);
    public OptionFieldViewModel Mountains { get; } = new(AMOUNTS, (int)WorldAmount.Normal);
    public OptionFieldViewModel Relief { get; } = new(RELIEFS, (int)WorldRelief.Hills);
    public ReadOnlyReactiveProperty<bool> IsGenerating => _isGenerating;
    public ReadOnlyReactiveProperty<string> Status => _status;
    public ReadOnlyReactiveProperty<float> Progress => _progress;
    public ReactiveCommand<Unit> Generate { get; }
    public ReactiveCommand<Unit> Cancel { get; }
    public ReactiveCommand<Unit> RandomSeed { get; }
    public ReactiveCommand<Unit> Back { get; }

    public CreateWorldScreenViewModel(MainMenuNavigation navigation, WorldSettingsProvider settings, WorldGenerationService generator,
        WorldSelection selection) : base(navigation, MainMenuScreen.CreateWorld)
    {
        _settings = settings;
        _generator = generator;
        _selection = selection;

        Generate = Command();
        Cancel = Command();
        RandomSeed = Command();
        Back = NavigationCommand(MainMenuScreen.NewGame);

        Disposables.Add(Name);
        Disposables.Add(Seed);
        Disposables.Add(Size);
        Disposables.Add(Towns);
        Disposables.Add(Farmsteads);
        Disposables.Add(Rivers);
        Disposables.Add(Lakes);
        Disposables.Add(Mountains);
        Disposables.Add(Relief);
        Disposables.Add(_isGenerating.Subscribe(RefreshCommands));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(RandomSeed.Executed.Subscribe(_ => Seed.Value = NewSeed()));
        Disposables.Add(Generate.Executed.Subscribe(_ => GenerateAsync().Forget()));
        Disposables.Add(Cancel.Executed.Subscribe(_ => _generation?.Cancel()));
    }

    public override void GoBack()
    {
        if (_isGenerating.Value)
        {
            _generation?.Cancel();
            return;
        }

        Navigation.Show(MainMenuScreen.NewGame);
    }

    private void OnShown()
    {
        if (_isGenerating.Value)
            return;

        _status.Value = string.Empty;
        _progress.Value = 0f;

        if (string.IsNullOrWhiteSpace(Seed.Value))
            Seed.Value = NewSeed();
    }

    private async UniTask GenerateAsync()
    {
        _generation = new CancellationTokenSource();
        CancellationToken token = _generation.Token;
        _isGenerating.Value = true;
        _progress.Value = 0f;
        _status.Value = LOADING_SETTINGS;

        try
        {
            using AddressableAsset<WorldBuildSettings> settings = await _settings.LoadAsync(token);
            IProgress<WorldGenerationProgress> progress = Cysharp.Threading.Tasks.Progress.Create<WorldGenerationProgress>(Report);
            WorldInfo world = await _generator.GenerateAsync(settings.Asset, Variation(), Name.Value, progress, token);

            _selection.Prefer(world.Id);
            _status.Value = string.Empty;
            Navigation.Show(MainMenuScreen.NewGame);
        }
        catch (OperationCanceledException)
        {
            SetStatus(CANCELLED);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus(string.Format(FAILED, exception.Message));
        }
        finally
        {
            _generation.Dispose();
            _generation = null;

            if (!IsDisposed)
                _isGenerating.Value = false;
        }
    }

    private WorldVariation Variation()
    {
        return new WorldVariation
        {
            Seed = WorldVariation.SeedOf(Seed.Value),
            WorldSize = WorldVariation.MIN_SIZE + Size.Index.Value * WorldVariation.SIZE_STEP,
            Towns = (WorldAmount)Towns.Index.Value,
            Farmsteads = (WorldAmount)Farmsteads.Index.Value,
            Rivers = (WorldAmount)Rivers.Index.Value,
            Lakes = (WorldAmount)Lakes.Index.Value,
            Mountains = (WorldAmount)Mountains.Index.Value,
            Relief = (WorldRelief)Relief.Index.Value
        };
    }

    private void Report(WorldGenerationProgress progress)
    {
        if (IsDisposed)
            return;

        _status.Value = progress.Status;
        _progress.Value = progress.Fraction;
    }

    private void SetStatus(string status)
    {
        if (!IsDisposed)
            _status.Value = status;
    }

    private void RefreshCommands(bool generating)
    {
        Generate.SetCanExecute(!generating);
        RandomSeed.SetCanExecute(!generating);
        Back.SetCanExecute(!generating);
        Cancel.SetCanExecute(generating);
    }

    private static string NewSeed() => UnityEngine.Random.Range(1, int.MaxValue).ToString();

    private static string[] SizeLabels()
    {
        int count = (WorldVariation.MAX_SIZE - WorldVariation.MIN_SIZE) / WorldVariation.SIZE_STEP + 1;
        var labels = new string[count];

        for (int i = 0; i < count; i++)
            labels[i] = $"{(WorldVariation.MIN_SIZE + i * WorldVariation.SIZE_STEP) / WorldVariation.SIZE_STEP} км";

        return labels;
    }

    protected override void OnDisposed()
    {
        _generation?.Cancel();
        _isGenerating.Dispose();
        _status.Dispose();
        _progress.Dispose();
    }
}
