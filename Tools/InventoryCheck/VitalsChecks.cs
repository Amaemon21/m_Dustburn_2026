using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static SaveRegistry PlayerRegistry() => new SaveRegistry()
        .Register<PlayerVitalsData>(SaveInstaller.VITALS_KEY, SaveScope.Slot, SaveInstaller.PLAYER_FILE)
        .Register<PlayerProgressionData>(SaveInstaller.PROGRESSION_KEY, SaveScope.Slot, SaveInstaller.PLAYER_FILE);

    private static PlayerStatSheet DefaultSheet()
    {
        HealthSettings health = new();
        StaminaSettings stamina = new();
        MetabolismSettings metabolism = new();
        return new PlayerStatSheet()
            .Add(PlayerStatType.MaxHealth, health.Max, 1f)
            .Add(PlayerStatType.HealthRegen, health.RegenPerSecond)
            .Add(PlayerStatType.MaxStamina, stamina.Max, 1f)
            .Add(PlayerStatType.StaminaRegen, stamina.RegenPerSecond)
            .Add(PlayerStatType.StaminaCost, 1f)
            .Add(PlayerStatType.MaxFood, metabolism.MaxFood, 1f)
            .Add(PlayerStatType.FoodDecay, metabolism.FoodDecayPerMinute)
            .Add(PlayerStatType.MaxWater, metabolism.MaxWater, 1f)
            .Add(PlayerStatType.WaterDecay, metabolism.WaterDecayPerMinute)
            .Add(PlayerStatType.MaxInfection, 100f, 1f)
            .Add(PlayerStatType.ExperienceGain, 1f);
    }

    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.01f;

    private static async Task<SaveService> LoadedPlayerSaves(MemoryStorage storage)
    {
        SaveService saves = new(PlayerRegistry(), storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        return saves;
    }

    private static async Task VerifyVitals()
    {
        MemoryStorage storage = new();
        SaveService saves = await LoadedPlayerSaves(storage);
        using PlayerStatSheet stats = DefaultSheet();
        using (PlayerVitals vitals = new(stats, new PlayerVitalsRepository(saves)))
        {
            Check(vitals.Health.Current.CurrentValue == 100f && vitals.Stamina.IsFull && vitals.Food.IsFull &&
                  vitals.Water.IsFull && vitals.Infection.IsEmpty, "a new character starts full and uninfected");
            Check(saves.IsDirty(SaveInstaller.PLAYER_FILE), "a new character marks the player file dirty");

            object fortitude = new();
            stats.Get(PlayerStatType.MaxHealth).AddModifier(new StatModifier(StatModifierType.Flat, 50f, fortitude));
            Check(vitals.Health.Max.CurrentValue == 150f && vitals.Health.Current.CurrentValue == 100f,
                "a max health buff raises the ceiling and keeps current health");
            stats.Get(PlayerStatType.MaxHealth).RemoveModifiersFrom(fortitude);
            stats.Get(PlayerStatType.MaxHealth).AddModifier(new StatModifier(StatModifierType.Multiplier, 0.6f, fortitude));
            Check(Near(vitals.Health.Current.CurrentValue, 60f), "a max health debuff clamps current health");
            stats.Get(PlayerStatType.MaxHealth).RemoveModifiersFrom(fortitude);
            Check(Near(vitals.Health.Current.CurrentValue, 60f) && vitals.Health.Max.CurrentValue == 100f,
                "lifting the debuff does not refill health");

            vitals.Apply(new VitalsEffect(80f, 0f, -30f, 5f, 25f));
            Check(vitals.Health.IsFull && Near(vitals.Food.Current.CurrentValue, 70f) && vitals.Water.IsFull &&
                  Near(vitals.Infection.Current.CurrentValue, 25f), "a consumable effect is clamped per vital");
            Check(Near(vitals.Infection.Fraction, 0.25f), "infection reads as a share of its maximum");

            vitals.Stamina.Set(42.5f);
            await saves.SaveDirtyAsync(CancellationToken.None);
        }

        SaveService reloaded = await LoadedPlayerSaves(storage);
        using PlayerStatSheet freshStats = DefaultSheet();
        using PlayerVitals restored = new(freshStats, new PlayerVitalsRepository(reloaded));
        Check(Near(restored.Stamina.Current.CurrentValue, 42.5f) && Near(restored.Food.Current.CurrentValue, 70f) &&
              Near(restored.Infection.Current.CurrentValue, 25f) && restored.Health.IsFull, "vitals survive a save");
    }

    private static async Task VerifyStamina()
    {
        SaveService saves = await LoadedPlayerSaves(new MemoryStorage());
        using PlayerStatSheet stats = DefaultSheet();
        using PlayerVitals vitals = new(stats, new PlayerVitalsRepository(saves));
        using StaminaSystem stamina = new(new StaminaSettings(), vitals, stats);
        float spentTotal = 0f;
        using IDisposable spending = stamina.Spent.Subscribe(spent => spentTotal += spent);

        PlayerMovementResult sprinting = new(new Vector2(0f, 1f), true, true, false, false, false, 8f);
        for (int i = 0; i < 10; i++)
            stamina.Apply(sprinting, 0.1f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 88f) && Near(spentTotal, 12f), "sprinting drains stamina per second");

        stamina.Tick(0.5f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 88f), "stamina waits before regenerating");
        stamina.Tick(0.6f);
        Check(vitals.Stamina.IsFull, "stamina regenerates after the delay");

        stamina.Spend(500f);
        Check(vitals.Stamina.IsEmpty && stamina.IsExhausted.CurrentValue, "running dry exhausts");
        PlayerMovementRequest sprint = PlayerMovementRequest.Idle;
        sprint.Sprint = true;
        sprint.Jump = true;
        stamina.Modify(ref sprint);
        Check(!sprint.Sprint && !sprint.Jump, "exhaustion blocks sprint and an unaffordable jump");
        stamina.Tick(1f);
        Check(vitals.Stamina.IsEmpty, "no regeneration in the tick that follows spending");
        stamina.Tick(1f);
        Check(stamina.IsExhausted.CurrentValue, "exhaustion holds below the recovery share");
        stamina.Tick(0.5f);
        Check(!stamina.IsExhausted.CurrentValue, "exhaustion lifts at the recovery share");

        vitals.Stamina.Set(50f);
        stamina.Apply(new PlayerMovementResult(Vector2.zero, false, false, false, true, false, 0f), 0.02f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 40f), "a jump costs stamina");
        stamina.Apply(new PlayerMovementResult(Vector2.zero, true, true, false, false, false, 0f), 1f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 40f), "sprinting in place costs nothing");

        object heavyArmor = new();
        stats.Get(PlayerStatType.StaminaCost).AddModifier(new StatModifier(StatModifierType.Percent, 1f, heavyArmor));
        Check(stamina.CanSpend(20f) && !stamina.CanSpend(21f), "a stamina cost debuff scales affordability");
        stamina.Spend(10f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 20f), "a stamina cost debuff scales spending");

        LocalHeldItemEffects effects = new(null, null, null, stamina);
        Check(effects.CanExert(10f) && !effects.CanExert(11f), "held item effects ask the player's stamina");
    }

    private static async Task VerifyStaminaWithoutPauses()
    {
        SaveService saves = await LoadedPlayerSaves(new MemoryStorage());
        using PlayerStatSheet stats = DefaultSheet();
        using PlayerVitals vitals = new(stats, new PlayerVitalsRepository(saves));
        StaminaSettings settings = new();
        SetBackingField(settings, nameof(StaminaSettings.RegenDelay), 0f);
        SetBackingField(settings, nameof(StaminaSettings.ExhaustionRecovery), 0f);
        using StaminaSystem stamina = new(settings, vitals, stats);

        const float FRAME = 1f / 60f;
        PlayerMovementResult sprinting = new(new Vector2(0f, 1f), true, true, false, false, false, 8f);
        PlayerMovementResult walking = new(new Vector2(0f, 1f), true, false, false, false, false, 4f);

        vitals.Stamina.Set(50f);
        stamina.Apply(sprinting, 0.1f);
        stamina.Tick(0.1f);
        Check(Near(vitals.Stamina.Current.CurrentValue, 48.8f), "a zero regen delay does not regenerate while sprinting");

        vitals.Stamina.Set(0.1f);
        int sprintFrames = 0;
        int firstBlocked = -1;

        for (int frame = 0; frame < 20; frame++)
        {
            PlayerMovementRequest request = PlayerMovementRequest.Idle;
            request.Sprint = true;
            stamina.Modify(ref request);
            if (request.Sprint)
                sprintFrames++;
            else if (firstBlocked < 0)
                firstBlocked = frame;
            stamina.Apply(request.Sprint ? sprinting : walking, FRAME);
            stamina.Tick(FRAME);
        }

        Check(firstBlocked == 1 && sprintFrames == 1, $"a zero recovery share still stops the sprint: {sprintFrames} sprint frames");
        Check(stamina.IsExhausted.CurrentValue && vitals.Stamina.Fraction < 0.1f, "a zero recovery share still holds exhaustion");

        stamina.Tick(5f);
        Check(!stamina.IsExhausted.CurrentValue, "exhaustion lifts once the minimum share is back");
    }

    private static async Task VerifyMetabolism()
    {
        SaveService saves = await LoadedPlayerSaves(new MemoryStorage());
        using PlayerStatSheet stats = DefaultSheet();
        using PlayerVitals vitals = new(stats, new PlayerVitalsRepository(saves));
        using StaminaSystem stamina = new(new StaminaSettings(), vitals, stats);
        using MetabolismSystem metabolism = new(new MetabolismSettings(), vitals, stats, stamina);
        HealthRegenSystem regen = new(new HealthSettings(), vitals, stats);
        Stat staminaRegen = stats.Get(PlayerStatType.StaminaRegen);

        metabolism.Tick(60f);
        Check(Near(vitals.Food.Current.CurrentValue, 98.5f) && Near(vitals.Water.Current.CurrentValue, 97.5f),
            "food and water decay per minute");

        stamina.Spend(100f);
        Check(Near(vitals.Food.Current.CurrentValue, 97.5f) && Near(vitals.Water.Current.CurrentValue, 96f),
            "spent stamina burns food and water");

        vitals.Food.Set(20f);
        metabolism.Tick(0f);
        Check(metabolism.IsHungry && Near(staminaRegen.Value.CurrentValue, 10f), "hunger slows stamina regeneration");
        vitals.Water.Set(20f);
        metabolism.Tick(0f);
        Check(metabolism.IsThirsty && Near(staminaRegen.Value.CurrentValue, 5f), "thirst stacks with hunger");
        vitals.Food.Set(100f);
        vitals.Water.Set(100f);
        metabolism.Tick(0f);
        Check(!metabolism.IsHungry && !metabolism.IsThirsty && Near(staminaRegen.Value.CurrentValue, 20f),
            "eating and drinking lift the debuffs");

        vitals.Health.Set(50f);
        regen.Tick(10f);
        Check(Near(vitals.Health.Current.CurrentValue, 55f), "a fed character regenerates health");
        vitals.Food.Set(40f);
        regen.Tick(10f);
        Check(Near(vitals.Health.Current.CurrentValue, 55f), "a hungry character does not regenerate");

        vitals.Food.Set(0f);
        metabolism.Tick(10f);
        Check(Near(vitals.Health.Current.CurrentValue, 53f), "starvation drains health");
        vitals.Water.Set(0f);
        metabolism.Tick(10f);
        Check(Near(vitals.Health.Current.CurrentValue, 48f), "dehydration adds to starvation");
    }

    private static async Task VerifyProgression()
    {
        ExperienceCurve curve = new LevelSettings().CreateCurve();
        Check(curve.RequiredFor(1) == 100 && curve.RequiredFor(2) == 115 && curve.RequiredFor(3) == 132 &&
              curve.RequiredFor(curve.MaxLevel) == 0, "the experience curve grows per level");

        MemoryStorage storage = new();
        SaveService saves = await LoadedPlayerSaves(storage);
        using PlayerStatSheet stats = DefaultSheet();
        List<int> reached = new();
        using (PlayerProgression progression = new(curve, stats, new PlayerProgressionRepository(saves)))
        using (progression.LeveledUp.Subscribe(reached.Add))
        {
            Check(progression.Level.CurrentValue == 1 && progression.RequiredExperience.CurrentValue == 100,
                "a new character starts at level one");
            progression.AddExperience(99f);
            Check(progression.Level.CurrentValue == 1 && Near(progression.Experience.CurrentValue, 99f), "experience accumulates");
            progression.AddExperience(1f);
            Check(progression.Level.CurrentValue == 2 && progression.Experience.CurrentValue == 0f &&
                  progression.RequiredExperience.CurrentValue == 115, "reaching the requirement levels up");
            progression.AddExperience(252f);
            Check(progression.Level.CurrentValue == 4 && Near(progression.Experience.CurrentValue, 5f) &&
                  string.Join(",", reached) == "2,3,4", "a large gain levels up several times");

            object focus = new();
            stats.Get(PlayerStatType.ExperienceGain).AddModifier(new StatModifier(StatModifierType.Multiplier, 2f, focus));
            Check(Near(progression.AddExperience(10f), 20f) && Near(progression.Experience.CurrentValue, 25f),
                "an experience buff multiplies gains");
            stats.Get(PlayerStatType.ExperienceGain).RemoveModifiersFrom(focus);

            using DamageFeed feed = new();
            using HarvestExperienceSystem harvest = new(feed, progression, new HarvestExperienceSettings());
            harvest.Activate();
            TestTree tree = new();
            feed.Publish(new DamageReport(tree, new DamageResult(25, false)));
            Check(Near(progression.Experience.CurrentValue, 30f), "chopping earns experience per damage");
            feed.Publish(new DamageReport(tree, new DamageResult(25, true)));
            Check(Near(progression.Experience.CurrentValue, 55f), "felling earns a bonus by the target's health");
            feed.Publish(new DamageReport(tree, new DamageResult(0, true)));
            feed.Publish(new DamageReport(new TestDamageable(), new DamageResult(25, false)));
            Check(Near(progression.Experience.CurrentValue, 55f), "no experience for empty hits or non-blocks");
            await saves.SaveDirtyAsync(CancellationToken.None);
        }

        SaveService reloaded = await LoadedPlayerSaves(storage);
        using PlayerProgression restored = new(curve, stats, new PlayerProgressionRepository(reloaded));
        Check(restored.Level.CurrentValue == 4 && Near(restored.Experience.CurrentValue, 55f), "level and experience survive a save");

        using PlayerProgression capped = new(new ExperienceCurve(10, 1f, 3), stats,
            new PlayerProgressionRepository(await LoadedPlayerSaves(new MemoryStorage())));
        capped.AddExperience(1000f);
        Check(capped.IsMaxLevel && capped.Level.CurrentValue == 3 && capped.RequiredExperience.CurrentValue == 0 &&
              capped.AddExperience(10f) == 0f, "the last level stops experience");
    }

    private static async Task VerifyExperienceNotifications()
    {
        SaveService inventorySaves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        ItemCatalog catalog = new();
        using InventoryRepository repository = new(inventorySaves);
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("xp", new Vector2Int(2, 1), 1);
        using ItemNotificationsViewModel notes = new(player, players, catalog);

        using PlayerStatSheet stats = DefaultSheet();
        using PlayerProgression progression = new(new LevelSettings().CreateCurve(), stats,
            new PlayerProgressionRepository(await LoadedPlayerSaves(new MemoryStorage())));
        using ExperienceNotifier notifier = new(progression, notes);
        progression.AddExperience(3f);
        Check(notes.Active.Count == 0, "experience before activation is not announced");

        notifier.Activate();
        progression.AddExperience(0.4f);
        progression.AddExperience(0.4f);
        Check(notes.Active.Count == 0, "fractions of a point wait for a whole point");
        progression.AddExperience(0.4f);
        Check(notes.Active.Count == 1 && notes.Active[0].Kind == NotificationKind.Experience &&
              notes.Active[0].Text.CurrentValue == "+1", "a whole point is announced with the carry");
        progression.AddExperience(5f);
        Check(notes.Active.Count == 1 && notes.Active[0].Text.CurrentValue == "+6", "experience adds to its live line");
        progression.AddExperience(120f);
        Check(notes.Active[0].Text.CurrentValue == "+126" && progression.Level.CurrentValue == 2,
            "a level-up gain is announced in full");
    }

    private static async Task VerifyPlayerStatus()
    {
        SaveService saves = await LoadedPlayerSaves(new MemoryStorage());
        using PlayerStatSheet stats = DefaultSheet();
        using PlayerVitals vitals = new(stats, new PlayerVitalsRepository(saves));
        using PlayerProgression progression = new(new LevelSettings().CreateCurve(), stats, new PlayerProgressionRepository(saves));
        PlayerStatusViewModel status = new(vitals, progression);
        vitals.Water.Set(64f);
        progression.AddExperience(42f);
        Check(status.Water.Current.CurrentValue == 64f && status.Health.Max.CurrentValue == 100f &&
              status.Infection.Type == VitalType.Infection, "the status view model reads live vitals");
        Check(status.Level.Level.CurrentValue == 1 && Near(status.Level.Experience.CurrentValue, 42f) &&
              status.Level.RequiredExperience.CurrentValue == 100, "the status view model reads the level");
        status.Dispose();
        Check(status.Health.IsDisposed && status.Level.IsDisposed, "the status view model disposes its parts");
    }

    private sealed class UnlimitedStamina : IPlayerStamina
    {
        private readonly ReactiveProperty<bool> _exhausted = new(false);

        public ReadOnlyReactiveProperty<bool> IsExhausted => _exhausted;
        public Observable<float> Spent => Observable.Empty<float>();
        public bool CanSpend(float amount) => true;
        public void Spend(float amount) { }
    }

    private sealed class TestTree : IDamageable
    {
        private readonly ReactiveProperty<int> _health = new(200);
        public DamageTargetKind Kind => DamageTargetKind.Block;
        public BlockMaterial Material => null;
        public int MaxHealth => 200;
        public ReadOnlyReactiveProperty<int> Health => _health;
        public bool IsDestroyed => _health.Value <= 0;
        public DamageResult TakeDamage(int damage) => new(0, IsDestroyed);
    }
}
