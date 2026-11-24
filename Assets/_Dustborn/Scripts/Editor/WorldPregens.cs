using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class WorldPregens
{
    private const string MENU = "CONTEXT/WorldBuildSettings/Generate built-in worlds";
    private static string Title => LocalizationService.Text("editor.worldPregens.title");
    private static string Question => LocalizationService.Text("editor.worldPregens.question");
    private static string Cancelled => LocalizationService.Text("editor.worldPregens.cancelled");
    private static string Failed => LocalizationService.Text("editor.worldPregens.failed");
    private static string NoSettings => LocalizationService.Text("editor.worldPregens.missingSettings");

    private static readonly (string Name, int Size)[] Presets =
    {
        ("PREGEN06K01", 6144),
        ("PREGEN06K02", 6144),
        ("PREGEN08K01", 8192),
        ("PREGEN08K02", 8192),
        ("PREGEN10K01", 10240),
        ("PREGEN10K02", 10240)
    };

    private static bool _running;

    private sealed class ProgressBar : IProgress<WorldGenerationProgress>
    {
        private readonly CancellationTokenSource _cancel;

        public int Index;
        public string Name;

        public ProgressBar(CancellationTokenSource cancel)
        {
            _cancel = cancel;
        }

        public void Report(WorldGenerationProgress value)
        {
            string message = $"{Name} ({Index + 1}/{Presets.Length}): {value.Status}";
            float progress = (Index + Mathf.Clamp01(value.Fraction)) / Presets.Length;

            if (EditorUtility.DisplayCancelableProgressBar(Title, message, progress))
                _cancel.Cancel();
        }
    }

    [MenuItem(MENU)]
    private static void Generate(MenuCommand command)
    {
        var settings = command.context as WorldBuildSettings;
        if (settings == null || settings.Config == null || settings.Ground == null || !settings.Ground.IsValid)
        {
            EditorUtility.DisplayDialog(Title, NoSettings, "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(Title, string.Format(Question, Presets.Length), LocalizationService.Text("editor.worldPregens.generate"), LocalizationService.Text("editor.common.cancel")))
            return;

        RunAsync(settings).Forget();
    }

    [MenuItem(MENU, true)]
    private static bool CanGenerate() => !_running && !EditorApplication.isPlayingOrWillChangePlaymode;

    private static string IdOf(string name) => name.ToLowerInvariant();

    private static async UniTask RunAsync(WorldBuildSettings settings)
    {
        _running = true;
        using var cancel = new CancellationTokenSource();
        var bar = new ProgressBar(cancel);
        var generator = new WorldGenerationService();
        EditorApplication.LockReloadAssemblies();

        try
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                (string name, int size) = Presets[i];
                bar.Index = i;
                bar.Name = name;

                var variation = new WorldVariation { Seed = WorldVariation.SeedOf(name), WorldSize = size };
                string title = WorldNames.Of(variation.Seed);
                WorldGenerationResult result = await generator.GenerateAsync(settings, variation, IdOf(name), title, WorldCatalog.BuiltInRoot, true,
                    WorldPreviewQuality.High, bar, cancel.Token);

                Debug.Log($"{nameof(WorldPregens)}: {name} ({result.World.Name}) written to {result.World.Directory}, seed {variation.Seed}, size {size}, "
                    + $"{result.Stats.Buildings} buildings in {result.Stats.Seconds:0} s");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log($"{nameof(WorldPregens)}: generation cancelled");
            EditorUtility.DisplayDialog(Title, Cancelled, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(Title, string.Format(Failed, exception.Message), "OK");
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            _running = false;
        }
    }

}
