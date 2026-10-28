public readonly struct VitalsEffect
{
    public float Health { get; }
    public float Stamina { get; }
    public float Food { get; }
    public float Water { get; }
    public float Infection { get; }

    public VitalsEffect(float health, float stamina, float food, float water, float infection)
    {
        Health = health;
        Stamina = stamina;
        Food = food;
        Water = water;
        Infection = infection;
    }
}
