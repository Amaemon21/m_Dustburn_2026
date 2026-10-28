public readonly struct WorldGenerationProgress
{
    public readonly string Status;
    public readonly float Fraction;

    public WorldGenerationProgress(string status, float fraction)
    {
        Status = status;
        Fraction = fraction;
    }
}
