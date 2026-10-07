using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using R3;
using UnityEngine;
using Random = System.Random;

internal static partial class Harness
{
    private static StrikeInfo Hit(int damage, int wear, params MaterialBonus[] bonuses)
        => new(damage, wear, bonuses, Vector3.zero, Vector3.back);

    private static T FakeAsset<T>() where T : ScriptableObject
    {
        T asset = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        FieldInfo pointer = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("UnityEngine.Object.m_CachedPtr not found; the fake asset needs another liveness marker");
        pointer.SetValue(asset, new IntPtr(1));
        return asset;
    }

    private static ResourceInventoryItem FakeItem(string itemId)
    {
        ResourceInventoryItem item = FakeAsset<ResourceInventoryItem>();
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.ItemId), itemId);
        return item;
    }

    private static void SetDeclaredField<TOwner>(object target, string property, object value)
        => (typeof(TOwner).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{typeof(TOwner).Name}.{property} has no backing field")).SetValue(target, value);

    private static BlockConfig FakeBlock(BlockMaterial material, int maxHealth, HarvestYield[] harvest, DestroyDrop[] drops)
    {
        BlockConfig config = FakeAsset<BlockConfig>();
        SetBackingField(config, nameof(BlockConfig.Material), material);
        SetBackingField(config, nameof(BlockConfig.MaxHealth), maxHealth);
        SetBackingField(config, nameof(BlockConfig.Harvest), harvest);
        SetBackingField(config, nameof(BlockConfig.DestroyDrops), drops);
        return config;
    }

    private static int Total(IEnumerable<InventorySlotState> stacks, string itemId)
        => stacks.Where(stack => stack.ItemId == itemId).Sum(stack => stack.Amount);

    private static void VerifyBlockHarvest()
    {
        BlockMaterial wood = FakeAsset<BlockMaterial>();
        BlockConfig pine = FakeBlock(wood, 100,
            new[] { new HarvestYield(FakeItem("wood"), 10) },
            new[] { new DestroyDrop(FakeItem("seed"), 2, 2, 1f) });

        using Block tree = new("tree", pine, 0, new Random(1));
        List<InventorySlotState> yields = new();
        foreach (int damage in new[] { 25, 25, 30, 40 })
        {
            DamageResult result = tree.TakeDamage(damage);
            tree.Harvest(result, 1f, yields);
        }

        Check(tree.IsDestroyed && tree.Health.CurrentValue == 0, "a block is destroyed once its health is spent");
        Check(Total(yields, "wood") == 10 && yields.Count(stack => stack.ItemId == "wood") == 4,
            "harvest arrives with every hit and adds up to the full count");
        Check(Total(yields, "seed") == 2, "destroy drops come with the final hit");
        Check(tree.TakeDamage(10).Dealt == 0, "a destroyed block takes no more damage");

        using Block doubled = new("doubled", pine, 0, new Random(1));
        List<InventorySlotState> rich = new();
        doubled.Harvest(doubled.TakeDamage(100), 2f, rich);
        Check(Total(rich, "wood") == 20, "the harvest multiplier scales the yield");

        using Block saved = new("saved", pine, 60, new Random(1));
        Check(saved.Health.CurrentValue == 40 && saved.Damage == 60, "saved damage is restored");
        using Block overkill = new("overkill", pine, 500, new Random(1));
        Check(overkill.Health.CurrentValue == 1, "saved damage never destroys a block on load");
    }

    private static void VerifyStrikeHarvest()
    {
        BlockMaterial wood = FakeAsset<BlockMaterial>();
        BlockMaterial stone = FakeAsset<BlockMaterial>();
        BlockConfig pine = FakeBlock(wood, 100, new[] { new HarvestYield(FakeItem("wood"), 10) }, Array.Empty<DestroyDrop>());
        ItemCatalog catalog = WearCatalog();
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        inventory.RegisterInventory(new InventoryGridData { OwnerId = "chop", Size = new Vector2Int(1, 1) });
        inventory.AddItemsToInventory("chop", Vector2Int.zero, "axe", 1);
        TestRewards rewards = new();
        using DamageFeed feed = new();
        List<DamageReport> reports = new();
        using IDisposable listening = feed.Reports.Subscribe(reports.Add);
        LocalHeldItemEffects effects = new(inventory, catalog, rewards, feed);
        HeldItem axe = new("chop", Vector2Int.zero, "axe", null);
        MaterialBonus[] axeBonuses = { new(stone, 0.5f, 1f), new(wood, 2f, 1.5f) };

        using Block tree = new("tree", pine, 0, new Random(1));
        effects.Strike(axe, tree, Hit(25, 1, axeBonuses));
        Check(tree.Health.CurrentValue == 50 && Total(rewards.Given, "wood") == 7,
            "the material bonus scales damage and harvest of a hit");
        Check(reports.Count == 1 && ReferenceEquals(reports[0].Target, tree) && reports[0].Result.Dealt == 50,
            "every hit is reported to the damage feed");
        effects.Strike(axe, tree, Hit(25, 1, axeBonuses));
        Check(tree.IsDestroyed && Total(rewards.Given, "wood") == 15 && reports[^1].Result.Destroyed,
            "the last hit hands over the rest of the harvest");

        using Block rock = new("rock", pine, 0, new Random(1));
        effects.Strike(axe, rock, Hit(25, 1));
        Check(rock.Health.CurrentValue == 75, "a tool without a bonus for the material hits at base damage");
    }

    private static async Task VerifyBlockSave()
    {
        MemoryStorage storage = new();
        SaveRegistry registry = new SaveRegistry().Register<BlocksData>(SaveInstaller.BLOCKS_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE);
        SaveService saves = new(registry, storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository repository = new(saves);
        repository.SetDamage("decor/3/10/-4", 35);
        repository.MarkDestroyed("scene/abc");
        Check(saves.IsDirty(SaveInstaller.WORLD_FILE), "block damage marks the world file dirty");
        await saves.SaveDirtyAsync(CancellationToken.None);

        SaveService reloaded = new(registry, storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository restored = new(reloaded);
        Check(restored.DamageOf("decor/3/10/-4") == 35 && !restored.IsDestroyed("decor/3/10/-4"), "block damage survives a save");
        Check(restored.IsDestroyed("scene/abc") && restored.DamageOf("unknown") == 0, "destroyed blocks survive a save");
        Check(new DecorKey(3, 10, -4).ToString() == "decor/3/10/-4", "a decor key names its layer and cell");
    }

    private static void VerifyTargetHealthBar()
    {
        using DamageFeed feed = new();
        using TargetHealthService bars = new(feed);
        TestDamageable first = new();
        TestDamageable second = new();
        TargetHealthViewModel blocks = bars.For(DamageTargetKind.Block);
        TargetHealthViewModel entities = bars.For(DamageTargetKind.Entity);

        first.TakeDamage(30);
        feed.Publish(new DamageReport(first, new DamageResult(30, false)));
        HealthBarFrame hit = entities.Frame.CurrentValue;
        Check(entities.Visible.CurrentValue && !blocks.Visible.CurrentValue, "a hit shows the bar of its target kind only");
        Check(hit.Health == 70 && hit.MaxHealth == 100 && Mathf.Approximately(hit.Fill, 0.7f), "the bar shows health over max health");

        first.TakeDamage(20);
        feed.Publish(new DamageReport(first, new DamageResult(20, false)));
        Check(Mathf.Approximately(entities.Frame.CurrentValue.Fill, 0.5f) && entities.Frame.CurrentValue.Target == hit.Target,
            "a further hit on the same target keeps its frame serial, so the trail animates");

        second.TakeDamage(10);
        feed.Publish(new DamageReport(second, new DamageResult(10, false)));
        Check(entities.Frame.CurrentValue.Target != hit.Target, "a new target gets a new serial, so the bar snaps");
        entities.Hide();
        Check(!entities.Visible.CurrentValue && entities.Target == null, "hiding releases the target");
    }

    private sealed class TestRewards : IItemRewards
    {
        public readonly List<InventorySlotState> Given = new();

        public void Give(InventorySlotState stack, Vector3 dropOrigin) => Given.Add(stack);
    }
}
