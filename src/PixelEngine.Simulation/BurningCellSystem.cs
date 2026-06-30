namespace PixelEngine.Simulation;

/// <summary>
/// burning cell 的每 tick 副作用与燃尽处理。
/// </summary>
public sealed class BurningCellSystem(MaterialTable materials, ushort burnoutMaterial, IReactionSideEffectSink? sideEffects = null) : ILifetimeSink
{
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly IReactionSideEffectSink? _sideEffects = sideEffects;

    /// <summary>
    /// 注册到 fire/burning 材质上的 custom-update 委托。
    /// </summary>
    public void UpdateBurning(ref CellCursor cell, ref NeighborWindow window, ref ChunkWorkContext context)
    {
        if (!CellFlags.Has(cell.Flags, CellFlags.Burning))
        {
            return;
        }

        byte heat = _materials.Hot.TemperatureOfFire[cell.Material];
        byte smoke = _materials.Hot.GeneratesSmoke[cell.Material];
        if (heat != 0 || smoke != 0)
        {
            IReactionSideEffectSink sink = _sideEffects ??
                throw new InvalidOperationException("burning cell 产生副作用，但未配置 IReactionSideEffectSink。");
            if (heat != 0)
            {
                sink.AddHeat(cell.X, cell.Y, cell.Material, heat);
            }

            if (smoke != 0)
            {
                sink.EmitSmoke(cell.X, cell.Y, cell.Material, smoke);
            }
        }

        context.SetCell(ref window, cell.X, cell.Y, cell.Material, cell.Flags, cell.Lifetime);
    }

    /// <inheritdoc />
    public void OnExpired(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit)
    {
        byte flags = window.GetFlags(wx, wy);
        if (!CellFlags.Has(flags, CellFlags.Burning) && _materials.Hot.Type[material] != CellType.Fire)
        {
            return;
        }

        window.SetMaterial(wx, wy, burnoutMaterial);
        window.SetLifetime(wx, wy, DefaultLifetimeByte(burnoutMaterial));
        window.SetFlags(wx, wy, CellFlags.SetParity(CellFlags.Clear(flags, CellFlags.Burning), parityBit));
    }

    private byte DefaultLifetimeByte(ushort material)
    {
        ushort lifetime = _materials.Hot.DefaultLifetime[material];
        return lifetime > byte.MaxValue
            ? throw new InvalidOperationException($"材质 {material} 的默认 lifetime 超过 byte 存储上限。")
            : (byte)lifetime;
    }
}
