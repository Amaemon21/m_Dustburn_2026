using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyItemWear()
    {
        ItemCatalog catalog = WearCatalog();
        Expect<InvalidOperationException>(() => catalog.SetMaxStack("axe", 5), "a wearing item cannot stack");
        Expect<InvalidOperationException>(() => new ItemCatalog().SetDurability("arrow", 3), "durability needs a single stack");
        Expect<ArgumentOutOfRangeException>(() => catalog.SetDurability("axe", -1), "negative durability rejected");

        using InventoryGrid grid = new(new InventoryGridData { OwnerId = "wear", Size = new Vector2Int(4, 1) }, catalog);
        Vector2Int first = new(0, 0);
        Vector2Int second = new(1, 0);
        Vector2Int third = new(2, 0);
        Vector2Int fourth = new(3, 0);
        grid.AddItems("axe", 2);
        grid.AddItems("wood", 5);
        Check(grid.AddWear(first, "axe", 3) && grid.GetSlot(first).Wear == 3 && grid.GetSlot(second).Wear == 0,
            "wear lands on one instance");
        Check(grid.AddWear(first, "axe", 50) && grid.GetSlot(first).Wear == 10 && catalog.IsWornOut(grid.GetSlot(first).State.CurrentValue),
            "wear stops at durability");
        Check(!grid.AddWear(first, "wood", 1) && !grid.AddWear(fourth, "axe", 1), "wear needs that item in the slot");
        Check(!grid.AddWear(third, "wood", 1) && grid.GetSlot(third).Wear == 0, "items without durability do not wear");
        Check(grid.MoveTo(first, grid, fourth, 1) && grid.GetSlot(fourth).Wear == 10 && grid.GetSlot(first).IsEmpty &&
            grid.GetSlot(first).Wear == 0, "a move carries wear and clears the source");
        grid.SwitchSlots(second, fourth);
        Check(grid.GetSlot(second).Wear == 10 && grid.GetSlot(fourth).Wear == 0, "a swap carries wear");
        Check(grid.RemoveItems(second, "axe").Success && grid.GetSlot(second).Wear == 0, "an emptied slot forgets wear");

        InventorySlotProxy slot = new(new InventorySlotData());
        slot.Set("arrow", 5, 2);
        Check(slot.Origin.Wear == 2 && slot.State.CurrentValue.Wear == 2, "slot proxy writes wear to its origin");
        Expect<ArgumentException>(() => slot.Set("arrow", 5, -1), "negative wear rejected");
        Check(JsonConvert.DeserializeObject<InventorySlotData>("{\"ItemId\":\"axe\",\"Amount\":1}").Wear == 0,
            "a save written before wear reads as a new item");
    }

    private static async Task VerifyWearSave()
    {
        ItemCatalog catalog = WearCatalog();
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using (InventoryRepository repository = new(saves))
        using (InventoryService inventory = new(repository, catalog))
        {
            inventory.RegisterInventory(new InventoryGridData { OwnerId = "wear", Size = new Vector2Int(2, 1) });
            inventory.AddItemsToInventory("wear", "axe", 1);
            inventory.AddWear("wear", Vector2Int.zero, "axe", 4);
            await repository.SaveAsync(CancellationToken.None);
        }
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using (InventoryRepository repository = new(saves))
        using (InventoryService restored = new(repository, catalog))
            Check(restored.GetInventory("wear").GetSlot(Vector2Int.zero).Wear == 4, "saved wear restored");
    }

    private static void VerifyHeldItemSelection()
    {
        ItemCatalog catalog = WearCatalog();
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("player", new Vector2Int(2, 2), 3);
        using HeldItemSelection selection = new(player, catalog);
        Vector2Int axeSlot = new(1, 0);

        Check(selection.Held.CurrentValue.IsEmpty, "an empty hotbar holds nothing");
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, axeSlot, "axe", 1);
        Check(selection.Held.CurrentValue.IsEmpty, "an item outside the selected slot is not held");
        players.SelectHotbarSlot(player.OwnerId, 1);
        HeldItem held = selection.Held.CurrentValue;
        Check(held.ItemId == "axe" && held.Coordinates == axeSlot && held.OwnerId == player.Hotbar.OwnerId,
            "selecting a slot holds its item");

        int changes = 0;
        using IDisposable counter = selection.Held.Skip(1).Subscribe(_ => changes++);
        inventory.AddWear(player.Hotbar.OwnerId, axeSlot, "axe", 2);
        Check(changes == 0, "wear does not draw the held item again");
        inventory.MoveItems(player.Hotbar.OwnerId, axeSlot, player.Backpack.OwnerId, Vector2Int.zero);
        Check(selection.Held.CurrentValue.IsEmpty && changes == 1, "moving the item away empties the hands");
        players.SelectHotbarSlot(player.OwnerId, 2);
        Check(changes == 1, "switching between empty slots keeps the hands as they are");
    }

    private static void VerifyHeldItemEffects()
    {
        ItemCatalog catalog = WearCatalog();
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        inventory.RegisterInventory(new InventoryGridData { OwnerId = "hands", Size = new Vector2Int(2, 1) });
        inventory.AddItemsToInventory("hands", Vector2Int.zero, "axe", 1);
        using DamageFeed feed = new();
        LocalHeldItemEffects effects = new(inventory, catalog, new TestRewards(), feed);
        HeldItem axe = new("hands", Vector2Int.zero, "axe", null);
        TestDamageable target = new();

        effects.Strike(axe, target, Hit(7, 4));
        Check(target.Taken == 7 && inventory.GetInventory("hands").GetSlot(Vector2Int.zero).Wear == 4, "a strike damages and wears");
        effects.Strike(axe, null, Hit(7, 6));
        Check(!effects.IsUsable(axe) && inventory.GetInventory("hands").GetSlot(Vector2Int.zero).Wear == 10,
            "a strike on scenery wears the item out");
        effects.Strike(axe, target, Hit(7, 1));
        Check(target.Taken == 7, "a worn-out item deals no damage");
        Check(effects.IsUsable(new HeldItem("hands", new Vector2Int(1, 0), null, null)), "bare hands are always usable");
        Check(!effects.IsUsable(new HeldItem("hands", new Vector2Int(1, 0), "axe", null)), "an item that left its slot is not usable");
    }

    private static void VerifyPlayerHands()
    {
        ItemActionFactory actions = new ItemActionFactory().Register<TestActionConfig>((config, context) => new TestAction(config, context));
        Expect<InvalidOperationException>(() => actions.Register<TestActionConfig>((config, context) => null), "an action type registers once");
        Expect<InvalidOperationException>(() => actions.Create(new UnregisteredActionConfig(), null), "an unregistered action is refused by name");

        TestPresenter presenter = new();
        HeldItemConfig axeConfig = TestHeldItem(0.5f, 0.25f, new TestActionConfig(0.4f));
        HeldItemConfig pickConfig = TestHeldItem(0.5f, 0.25f, new TestActionConfig(0.4f));
        HeldItemConfig clubConfig = TestHeldItem(0.5f, 0.25f, new TestActionConfig(0.4f));
        HeldItem axe = new("hotbar", new Vector2Int(0, 0), "axe", axeConfig);
        HeldItem pick = new("hotbar", new Vector2Int(1, 0), "pick", pickConfig);
        HeldItem club = new("hotbar", new Vector2Int(2, 0), "club", clubConfig);
        HandsRequest attack = new() { Primary = true };
        using PlayerHands hands = new(presenter, new TestAim(), actions, null);

        hands.Tick(default, 0.1f);
        Check(presenter.Hidden == 1 && presenter.Shown.Count == 0, "nothing held hides the arms");
        hands.Hold(axe);
        hands.Tick(attack, 0.1f);
        Check(presenter.Shown.Count == 1 && ReferenceEquals(presenter.Shown[0], axeConfig) && presenter.Played[^1] == HandsMotion.Equip,
            "holding an item draws it");
        hands.Tick(attack, 0.2f);
        Check(TestAction.Started == 0, "no use while drawing");
        hands.Tick(attack, 0.3f);
        hands.Tick(attack, 0.1f);
        Check(TestAction.Started == 1, "use starts once drawn");
        hands.Tick(attack, 0.1f);
        Check(TestAction.Started == 1, "a running use is not restarted");

        hands.Hold(pick);
        hands.Tick(default, 0.1f);
        Check(TestAction.Cancelled == 1 && presenter.Played[^1] == HandsMotion.Unequip, "switching mid-use cancels and holsters");
        hands.Hold(club);
        hands.Tick(default, 0.1f);
        hands.Tick(default, 0.2f);
        Check(presenter.Shown.Count == 2 && ReferenceEquals(presenter.Shown[1], clubConfig), "the latest selection wins after holstering");

        TestAction.Blocked = true;
        hands.Tick(attack, 0.6f);
        hands.Tick(attack, 0.1f);
        Check(TestAction.Started == 1, "an unusable item does not start");
        TestAction.Blocked = false;
        hands.Tick(attack, 0.1f);
        Check(TestAction.Started == 2, "a usable item starts again");

        HandsRequest walking = new() { Moving = true };
        hands.Tick(default, 0.5f);
        hands.Tick(walking, 0.1f);
        Check(presenter.Played[^1] == HandsMotion.Walk, "moving while ready walks");
        int played = presenter.Played.Count;
        hands.Tick(walking, 0.1f);
        Check(presenter.Played.Count == played, "a held pose is not replayed");
        hands.Tick(new HandsRequest { Moving = true, Running = true }, 0.1f);
        Check(presenter.Played[^1] == HandsMotion.Run, "sprinting runs");
        hands.Tick(default, 0.1f);
        Check(presenter.Played[^1] == HandsMotion.Idle, "stopping returns to idle");
        hands.Tick(new HandsRequest { Primary = true, Moving = true }, 0.1f);
        Check(TestAction.Started == 3 && presenter.Played[^1] == HandsMotion.Idle, "a use takes over from the pose");
        hands.Tick(walking, 0.5f);
        hands.Tick(walking, 0.1f);
        Check(presenter.Played[^1] == HandsMotion.Walk, "walking resumes after a use");

        hands.Hold(default);
        hands.Tick(default, 0.1f);
        hands.Tick(default, 0.3f);
        Check(presenter.Hidden == 2, "an empty slot holsters into hidden arms");
    }

    private static ItemCatalog WearCatalog()
    {
        ItemCatalog catalog = new();
        catalog.SetMaxStack("axe", 1);
        catalog.SetDurability("axe", 10);
        return catalog;
    }

    private static HeldItemConfig TestHeldItem(float drawTime, float holsterTime, ItemActionConfig primary)
    {
        HeldItemConfig config = (HeldItemConfig)RuntimeHelpers.GetUninitializedObject(typeof(HeldItemConfig));
        FieldInfo pointer = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("UnityEngine.Object.m_CachedPtr not found; the fake asset needs another liveness marker");
        pointer.SetValue(config, new IntPtr(1));
        SetBackingField(config, nameof(HeldItemConfig.DrawTime), drawTime);
        SetBackingField(config, nameof(HeldItemConfig.HolsterTime), holsterTime);
        SetBackingField(config, nameof(HeldItemConfig.Primary), primary);
        return config;
    }

    private static void SetBackingField(object target, string property, object value)
        => (target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{property} has no backing field")).SetValue(target, value);

    [Serializable]
    private sealed class TestActionConfig : ItemActionConfig
    {
        public float Duration { get; }
        public TestActionConfig(float duration) => Duration = duration;
    }

    [Serializable]
    private sealed class UnregisteredActionConfig : ItemActionConfig
    {
    }

    private sealed class TestAction : IItemAction
    {
        public static int Started;
        public static int Cancelled;
        public static bool Blocked;
        private readonly TestActionConfig _config;
        private float _elapsed;

        public TestAction(TestActionConfig config, ItemActionContext context) => _config = config;

        public bool CanStart => !Blocked;
        public void Start() { _elapsed = 0f; Started++; }
        public bool Tick(float deltaTime) => (_elapsed += deltaTime) < _config.Duration;
        public void Cancel() => Cancelled++;
    }

    private sealed class TestPresenter : IHeldItemPresenter
    {
        public readonly List<HeldItemConfig> Shown = new();
        public readonly List<HandsMotion> Played = new();
        public int Hidden;

        public bool Show(HeldItemConfig config)
        {
            if (config == null)
                return false;
            Shown.Add(config);
            return true;
        }

        public void Hide() => Hidden++;
        public void Play(HandsMotion motion) => Played.Add(motion);
        public void Dispose() { }
    }

    private sealed class TestAim : IAimSource
    {
        public Ray Aim => default;
        public Transform Body => null;
    }

    private sealed class TestDamageable : IDamageable
    {
        private readonly ReactiveProperty<int> _health = new(100);
        public int Taken;
        public DamageTargetKind Kind => DamageTargetKind.Entity;
        public BlockMaterial Material => null;
        public int MaxHealth => 100;
        public ReadOnlyReactiveProperty<int> Health => _health;
        public bool IsDestroyed => _health.Value <= 0;

        public DamageResult TakeDamage(int damage)
        {
            Taken += damage;
            int dealt = Math.Min(damage, _health.Value);
            _health.Value -= dealt;
            return new DamageResult(dealt, IsDestroyed);
        }
    }
}
