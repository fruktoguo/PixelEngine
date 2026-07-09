using System.Runtime.InteropServices;

namespace PixelEngine.Simulation;

/// <summary>
/// 固定 64 字节的 dirty 矩形槽；避免伪共享并满足 SIMD/缓存行对齐布局。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct PaddedDirtyRectSlot
{
    [FieldOffset(0)]
    public DirtyRect Rect;
}
