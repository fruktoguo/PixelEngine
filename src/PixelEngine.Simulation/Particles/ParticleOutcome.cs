namespace PixelEngine.Simulation.Particles;

internal struct ParticleOutcome
{
    public ParticleOutcomeKind Kind;
    public int X;
    public int Y;

    public static ParticleOutcome Flying => default;

    public static ParticleOutcome WantsDeposit(int x, int y)
    {
        return new ParticleOutcome
        {
            Kind = ParticleOutcomeKind.WantsDeposit,
            X = x,
            Y = y,
        };
    }

    public static ParticleOutcome Dead => new()
    {
        Kind = ParticleOutcomeKind.Dead,
    };
}
