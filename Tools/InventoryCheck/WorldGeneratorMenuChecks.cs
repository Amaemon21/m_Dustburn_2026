using System.Linq;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyWorldGeneratorMenu()
    {
        var amount = new OptionFieldViewModel(new[] { "Нет", "Мало", "Норма", "Много" }, 2);
        amount.Next.Execute(R3.Unit.Default);
        amount.Next.Execute(R3.Unit.Default);
        Check(amount.Index.Value == 3 && amount.Label.CurrentValue == "Много", "the right arrow steps to the next value and stops at the last");
        Check(!amount.Next.CanExecute.CurrentValue && amount.Previous.CanExecute.CurrentValue, "the right arrow is disabled on the last value");
        amount.SetInteractable(false);
        Check(!amount.Previous.CanExecute.CurrentValue && !amount.Next.CanExecute.CurrentValue, "a locked selector disables both arrows");
        amount.Dispose();

        var relief = new ShareGroup(new[] { 30, 45, 25 }, new[] { 100, 100, WorldVariation.MAX_MOUNTAINS });
        relief.Fields[2].Next.Execute(R3.Unit.Default);
        Check(Sum(relief, 3) == 100 && relief.Percent(2) == 30 && relief.Percent(1) == 40, "raising a share takes the step from the largest other share");

        for (int i = 0; i < 20; i++)
            relief.Fields[2].Next.Execute(R3.Unit.Default);

        Check(relief.Percent(2) == WorldVariation.MAX_MOUNTAINS && Sum(relief, 3) == 100, "mountains stop at their cap and the shares still sum to 100");

        for (int i = 0; i < 30; i++)
            relief.Fields[0].Previous.Execute(R3.Unit.Default);

        Check(relief.Percent(0) == 0 && Sum(relief, 3) == 100, "lowering a share gives the step to the next open share");
        relief.Dispose();

        var biomes = new ShareGroup(new[] { 35, 15, 20, 15, 15 });
        for (int i = 0; i < 25; i++)
            biomes.Fields[0].Next.Execute(R3.Unit.Default);

        Check(biomes.Percent(0) == 100 && Sum(biomes, 5) == 100, "one biome can take the whole map");
        biomes.Fields[0].Next.Execute(R3.Unit.Default);
        Check(biomes.Percent(0) == 100 && !biomes.Fields[0].Next.CanExecute.CurrentValue, "a full share cannot grow further");
        biomes.Dispose();

        var viewport = new MapViewport();
        viewport.Wheel(1f, new Vector2(0.9f, 0.9f));
        viewport.Wheel(1f, new Vector2(0.9f, 0.9f));
        Rect zoomed = viewport.UvRect;
        Check(viewport.Zoom > 1f && zoomed.xMax <= 1f + 1e-5f && zoomed.yMax <= 1f + 1e-5f && zoomed.center.x > 0.5f,
            "zooming toward a corner keeps the view inside the map and moves toward the pivot");
        viewport.Drag(new Vector2(-5f, -5f));
        Check(viewport.UvRect.xMax <= 1f + 1e-5f && viewport.UvRect.xMin >= -1e-5f, "dragging never leaves the map");
        viewport.Reset();
        Check(viewport.UvRect == new Rect(0f, 0f, 1f, 1f), "reset shows the whole map");

        var variation = new WorldVariation { Plains = 0, Hills = 0, Mountains = 100 };
        ReliefShares shares = variation.Relief();
        Check(Mathf.Approximately(shares.Mountains, WorldVariation.MAX_MOUNTAINS / 100f)
              && Mathf.Approximately(shares.Plains + shares.Hills + shares.Mountains, 1f), "relief shares are normalized and mountains are capped");
        Check(Mathf.Approximately(new WorldVariation { PineForest = 50, BurntForest = 50, Desert = 0, Snow = 0, Wasteland = 0 }.BiomeShares().Sum(), 1f),
            "biome shares are normalized");
    }

    private static int Sum(ShareGroup group, int count) => Enumerable.Range(0, count).Sum(group.Percent);
}
