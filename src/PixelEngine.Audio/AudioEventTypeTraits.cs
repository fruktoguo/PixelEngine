using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

internal static class AudioEventTypeTraits
{
    public const int TypeCount = 6;

    public static bool TryGetIndex(AudioEventType type, out int index)
    {
        index = type switch
        {
            AudioEventType.ParticleImpact => 0,
            AudioEventType.FireCrackle => 1,
            AudioEventType.LiquidSplash => 2,
            AudioEventType.Explosion => 3,
            AudioEventType.RigidbodyShatter => 4,
            AudioEventType.AmbientRegion => 5,
            _ => -1,
        };

        return index >= 0;
    }

    public static int GetPerFrameCap(AudioSettings settings, AudioEventType type)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return type switch
        {
            AudioEventType.ParticleImpact => settings.MaxParticleImpactEventsPerFrame,
            AudioEventType.FireCrackle => settings.MaxFireCrackleEventsPerFrame,
            AudioEventType.LiquidSplash => settings.MaxLiquidSplashEventsPerFrame,
            AudioEventType.Explosion => settings.MaxExplosionEventsPerFrame,
            AudioEventType.RigidbodyShatter => settings.MaxRigidbodyShatterEventsPerFrame,
            AudioEventType.AmbientRegion => settings.MaxAmbientRegionEventsPerFrame,
            _ => 0,
        };
    }

    public static byte GetPriority(AudioEventType type)
    {
        return type switch
        {
            AudioEventType.Explosion => 220,
            AudioEventType.RigidbodyShatter => 180,
            AudioEventType.LiquidSplash => 130,
            AudioEventType.ParticleImpact => 110,
            AudioEventType.FireCrackle => 80,
            AudioEventType.AmbientRegion => 40,
            _ => 0,
        };
    }
}
