using VContainer;
using VContainer.Unity;

public sealed class PlayerStatsInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register(CreateSheet, Lifetime.Singleton);
    }

    private static PlayerStatSheet CreateSheet(IObjectResolver resolver)
    {
        PlayerVitalsConfig vitals = resolver.Resolve<PlayerVitalsConfig>();
        PlayerInventorySettings inventory = resolver.Resolve<PlayerInventorySettings>();

        return new PlayerStatSheet()
            .Add(PlayerStatType.MaxHealth, vitals.Health.Max, 1f)
            .Add(PlayerStatType.HealthRegen, vitals.Health.RegenPerSecond)
            .Add(PlayerStatType.MaxStamina, vitals.Stamina.Max, 1f)
            .Add(PlayerStatType.StaminaRegen, vitals.Stamina.RegenPerSecond)
            .Add(PlayerStatType.StaminaCost, 1f)
            .Add(PlayerStatType.MaxFood, vitals.Metabolism.MaxFood, 1f)
            .Add(PlayerStatType.FoodDecay, vitals.Metabolism.FoodDecayPerMinute)
            .Add(PlayerStatType.MaxWater, vitals.Metabolism.MaxWater, 1f)
            .Add(PlayerStatType.WaterDecay, vitals.Metabolism.WaterDecayPerMinute)
            .Add(PlayerStatType.MaxInfection, vitals.MaxInfection, 1f)
            .Add(PlayerStatType.CarryCapacity, inventory.MaxCarryWeight)
            .Add(PlayerStatType.ExperienceGain, 1f);
    }
}
