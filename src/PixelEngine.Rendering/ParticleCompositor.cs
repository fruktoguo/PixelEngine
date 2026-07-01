using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 9 自由粒子 CPU stamp 合成器。粒子自身不存 RGBA，颜色由材质与 colorVariant 在渲染相位生成。
/// </summary>
public sealed class ParticleCompositor(IMaterialTextureProvider? textures = null)
{
    private readonly IMaterialTextureProvider? _textures = textures;

    /// <summary>
    /// 把活跃自由粒子 stamp 到 render buffer，并把发光粒子写入 emissive 副输出。
    /// </summary>
    /// <param name="particles">活跃粒子连续前缀。</param>
    /// <param name="materials">材质表。</param>
    /// <param name="camera">相机快照。</param>
    /// <param name="target">目标 render buffer。</param>
    /// <param name="aux">副输出 buffer。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void Stamp(
        ReadOnlySpan<Particle> particles,
        MaterialTable materials,
        CameraState camera,
        RenderBuffer target,
        RenderAuxBuffers aux,
        FrameProfiler? profiler = null)
    {
        long started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(aux);
        if (target.Width != aux.Width || target.Height != aux.Height)
        {
            throw new ArgumentException("render buffer 与副输出尺寸必须一致。", nameof(aux));
        }

        Span<uint> pixels = target.Pixels;
        Span<uint> emissive = aux.Emissive;
        for (int i = 0; i < particles.Length; i++)
        {
            Particle particle = particles[i];
            int sx = WorldToScreen(particle.X, camera.OriginWorldX, camera.CellsPerPixel);
            int sy = WorldToScreen(particle.Y, camera.OriginWorldY, camera.CellsPerPixel);
            if ((uint)sx >= (uint)target.Width || (uint)sy >= (uint)target.Height)
            {
                continue;
            }

            ref readonly MaterialDef material = ref materials.Get(particle.Material);
            uint color = SampleParticleColor(in material, particle);
            int index = (sy * target.Width) + sx;
            pixels[index] = color;
            if (IsEmissiveParticle(in material))
            {
                emissive[index] = color;
            }
        }

        RecordSub(profiler, started);
    }

    private static int WorldToScreen(float world, float origin, float cellsPerPixel)
    {
        return (int)MathF.Round((world - origin) / cellsPerPixel, MidpointRounding.AwayFromZero);
    }

    private uint SampleParticleColor(in MaterialDef material, in Particle particle)
    {
        uint color = material.BaseColorBGRA;
        if (material.TextureId >= 0 && _textures is not null &&
            _textures.TrySample(in material, (int)MathF.Floor(particle.X), (int)MathF.Floor(particle.Y), out uint textureColor))
        {
            color = textureColor;
        }

        return ApplyVariant(color, particle.ColorVariant, material.ColorNoise);
    }

    private static bool IsEmissiveParticle(in MaterialDef material)
    {
        return (material.PropertyFlags & MaterialProperty.Emissive) != 0 || material.Type == CellType.Fire;
    }

    private static uint ApplyVariant(uint bgra, byte variant, byte materialNoise)
    {
        if (variant == 0 && materialNoise == 0)
        {
            return bgra;
        }

        int delta = (variant - 128) * Math.Max(1, (int)materialNoise) / 255;
        byte b = Adjust((byte)(bgra & 0xFF), delta);
        byte g = Adjust((byte)((bgra >> 8) & 0xFF), delta);
        byte r = Adjust((byte)((bgra >> 16) & 0xFF), delta);
        byte a = (byte)((bgra >> 24) & 0xFF);
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static byte Adjust(byte value, int delta)
    {
        return (byte)Math.Clamp(value + delta, 0, 255);
    }

    private static void RecordSub(FrameProfiler? profiler, long started)
    {
        if (profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(FrameSubPhase.ParticleStamp, elapsed * 1000.0 / Stopwatch.Frequency);
    }
}
