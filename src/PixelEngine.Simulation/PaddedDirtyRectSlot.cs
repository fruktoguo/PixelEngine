using System.Runtime.InteropServices;

namespace PixelEngine.Simulation;

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct PaddedDirtyRectSlot
{
    [FieldOffset(0)]
    public DirtyRect Rect;
}
