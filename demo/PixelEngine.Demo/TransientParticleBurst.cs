using PixelEngine.Scripting;

namespace PixelEngine.Demo;

internal static class TransientParticleBurst
{
    private const ushort DefaultVisualLifetime = 60;

    public static void Emit(IScriptContext context, float x, float y, int count, float speed, ushort lifetime = DefaultVisualLifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        count = Math.Clamp(count, 0, 48);
        if (count == 0)
        {
            return;
        }

        MaterialId material = ResolveVisualMaterial(context);
        if (material == MaterialId.Invalid)
        {
            return;
        }

        ushort safeLifetime = lifetime == 0 ? DefaultVisualLifetime : lifetime;
        float angleStep = MathF.Tau / count;
        float safeSpeed = MathF.Max(0f, speed);
        for (int i = 0; i < count; i++)
        {
            float angle = angleStep * i;
            ParticleSpawnDesc spawn = new(
                x,
                y,
                MathF.Cos(angle) * safeSpeed,
                MathF.Sin(angle) * safeSpeed,
                material,
                safeLifetime);
            context.Particles.Spawn(in spawn);
        }
    }

    public static MaterialId ResolveVisualMaterial(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Materials.TryResolve("smoke", out MaterialId smoke)
            ? smoke
            : context.Materials.TryResolve("fire", out MaterialId fire)
                ? fire
                : context.Materials.TryResolve("steam", out MaterialId steam)
                    ? steam
                    : context.Materials.Resolve("sand");
    }
}
