using System;
using R3;

public sealed class PlayerLevelViewModel : ViewModel
{
    private readonly PlayerProgression _progression;

    public ReadOnlyReactiveProperty<int> Level => _progression.Level;
    public ReadOnlyReactiveProperty<float> Experience => _progression.Experience;
    public ReadOnlyReactiveProperty<int> RequiredExperience => _progression.RequiredExperience;

    public PlayerLevelViewModel(PlayerProgression progression)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }
}
