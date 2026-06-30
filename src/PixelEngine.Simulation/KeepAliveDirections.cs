namespace PixelEngine.Simulation;

internal static class KeepAliveDirections
{
    public const int SlotNorthWest = 0;
    public const int SlotNorth = 1;
    public const int SlotNorthEast = 2;
    public const int SlotWest = 3;
    public const int SlotEast = 4;
    public const int SlotSouthWest = 5;
    public const int SlotSouth = 6;
    public const int SlotSouthEast = 7;

    public static int IncomingSlotForTouchedNeighborSlot(int neighborSlot)
    {
        return neighborSlot switch
        {
            0 => SlotSouthEast,
            1 => SlotSouth,
            2 => SlotSouthWest,
            3 => SlotEast,
            5 => SlotWest,
            6 => SlotNorthEast,
            7 => SlotNorth,
            8 => SlotNorthWest,
            _ => throw new ArgumentOutOfRangeException(nameof(neighborSlot), neighborSlot, "slot 必须是 3x3 邻域中的非中心邻居。"),
        };
    }
}
