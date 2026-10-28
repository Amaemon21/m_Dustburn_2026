using System;

public sealed class PlayerStatusViewModel : ViewModel
{
    public VitalViewModel Health { get; }
    public VitalViewModel Stamina { get; }
    public VitalViewModel Food { get; }
    public VitalViewModel Water { get; }
    public VitalViewModel Infection { get; }
    public PlayerLevelViewModel Level { get; }

    public PlayerStatusViewModel(PlayerVitals vitals, PlayerProgression progression)
    {
        if (vitals == null)
            throw new ArgumentNullException(nameof(vitals));
        if (progression == null)
            throw new ArgumentNullException(nameof(progression));

        Health = new VitalViewModel(vitals.Health);
        Stamina = new VitalViewModel(vitals.Stamina);
        Food = new VitalViewModel(vitals.Food);
        Water = new VitalViewModel(vitals.Water);
        Infection = new VitalViewModel(vitals.Infection);
        Level = new PlayerLevelViewModel(progression);
    }

    protected override void OnDisposed()
    {
        Health.Dispose();
        Stamina.Dispose();
        Food.Dispose();
        Water.Dispose();
        Infection.Dispose();
        Level.Dispose();
        base.OnDisposed();
    }
}
