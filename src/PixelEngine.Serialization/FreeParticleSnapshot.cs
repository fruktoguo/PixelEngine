using System.Runtime.InteropServices;

namespace PixelEngine.Serialization;

/// <summary>
/// 自由粒子的存档 DTO。只保存运行时可重建的弹道状态，不保存渲染 RGBA。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct FreeParticleSnapshot(
    float X,
    float Y,
    float Vx,
    float Vy,
    ushort Material,
    byte ColorVariant,
    byte Life);
