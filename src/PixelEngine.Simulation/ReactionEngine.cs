using PixelEngine.Simulation.Particles;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 基于 packed 反应表的 CA 接触反应执行器。
/// </summary>
public sealed class ReactionEngine(
    MaterialTable materials,
    ReactionTable reactions,
    IReactionSideEffectSink? sideEffects = null,
    ICellDestructionSink? cellDestructionSink = null) : IReactionExecutor
{
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly IReactionSideEffectSink? _sideEffects = sideEffects;
    private readonly ICellDestructionSink _cellDestructionSink = cellDestructionSink ?? ICellDestructionSink.Null;
    private ReactionTable _reactions = reactions ?? throw new ArgumentNullException(nameof(reactions));

    /// <summary>
    /// 替换 packed reaction table；由内容热重载在帧边界调用。
    /// </summary>
    public void ReloadReactions(ReactionTable reactions)
    {
        _reactions = reactions ?? throw new ArgumentNullException(nameof(reactions));
    }

    /// <inheritdoc />
    public bool TryReact(
        ref NeighborWindow window,
        int wx1,
        int wy1,
        ushort materialA,
        int wx2,
        int wy2,
        ushort materialB,
        byte parityBit,
        byte randomByte)
    {
        if (CellFlags.MatchesFrame(window.GetFlags(wx1, wy1), parityBit) ||
            CellFlags.MatchesFrame(window.GetFlags(wx2, wy2), parityBit))
        {
            return false;
        }

        ref readonly MaterialDef def = ref _materials.Get(materialA);
        int reactionIndex = _reactions.Find(materialA, materialB, in def);
        if (reactionIndex < 0)
        {
            return false;
        }

        ref readonly Reaction reaction = ref _reactions.At(reactionIndex);
        if (!PassesProbability(reaction.Probability, randomByte))
        {
            return false;
        }

        CorrosionReactionResult corrosion = TryApplyCorrosion(
            ref window,
            wx1,
            wy1,
            materialA,
            wx2,
            wy2,
            materialB,
            in reaction,
            parityBit);
        if (corrosion == CorrosionReactionResult.Applied)
        {
            EmitSideEffects(reaction, wx1, wy1, wx2, wy2);
            return true;
        }

        if (corrosion == CorrosionReactionResult.NoEffect)
        {
            return false;
        }

        ApplyOutput(ref window, wx1, wy1, reaction.OutputA, parityBit);
        ApplyOutput(ref window, wx2, wy2, reaction.OutputB, parityBit);
        EmitSideEffects(reaction, wx1, wy1, wx2, wy2);
        return true;
    }

    private static bool PassesProbability(byte probability, byte randomByte)
    {
        return probability == byte.MaxValue || (probability != 0 && randomByte < probability);
    }

    private CorrosionReactionResult TryApplyCorrosion(
        ref NeighborWindow window,
        int wx1,
        int wy1,
        ushort materialA,
        int wx2,
        int wy2,
        ushort materialB,
        in Reaction reaction,
        byte parityBit)
    {
        bool acidA = IsAcid(materialA);
        bool acidB = IsAcid(materialB);
        return acidA == acidB
            ? CorrosionReactionResult.NotCorrosion
            : acidA && IsCorrodible(materialB)
            ? ApplyCorrosionPair(ref window, wx1, wy1, reaction.OutputA, wx2, wy2, materialB, reaction.OutputB, parityBit)
                ? CorrosionReactionResult.Applied
                : CorrosionReactionResult.NoEffect
            : acidB && IsCorrodible(materialA)
            ? ApplyCorrosionPair(ref window, wx2, wy2, reaction.OutputB, wx1, wy1, materialA, reaction.OutputA, parityBit)
                ? CorrosionReactionResult.Applied
                : CorrosionReactionResult.NoEffect
            : CorrosionReactionResult.NotCorrosion;
    }

    private bool ApplyCorrosionPair(
        ref NeighborWindow window,
        int acidX,
        int acidY,
        ushort acidOutput,
        int targetX,
        int targetY,
        ushort targetMaterial,
        ushort rigidOwnedOutput,
        byte parityBit)
    {
        if (CellFlags.Has(window.GetFlags(targetX, targetY), CellFlags.RigidOwned))
        {
            ApplyOutput(ref window, acidX, acidY, acidOutput, parityBit);
            ApplyOutput(ref window, targetX, targetY, rigidOwnedOutput, parityBit);
            return true;
        }

        CorrosionDamageResult result = ApplyCorrosionDamage(ref window, targetX, targetY, targetMaterial, parityBit);
        if (result == CorrosionDamageResult.None)
        {
            return false;
        }

        if (result == CorrosionDamageResult.Destroyed)
        {
            ApplyOutput(ref window, acidX, acidY, acidOutput, parityBit);
        }
        else
        {
            window.SetFlags(acidX, acidY, CellFlags.SetParity(window.GetFlags(acidX, acidY), parityBit));
        }

        return true;
    }

    private CorrosionDamageResult ApplyCorrosionDamage(
        ref NeighborWindow window,
        int wx,
        int wy,
        ushort material,
        byte parityBit)
    {
        if (material == 0 ||
            _materials.Hot.Type[material] is not (CellType.Solid or CellType.Powder) ||
            (_materials.Hot.PropertyFlags[material] & MaterialProperty.Indestructible) != 0)
        {
            window.SetDamage(wx, wy, 0);
            return CorrosionDamageResult.None;
        }

        int effectiveDamage = EngineConstants.CorrosionReactionDamage -
            (_materials.Hot.Hardness[material] * EngineConstants.DamageHardnessAbsorb);
        if (effectiveDamage <= 0)
        {
            return CorrosionDamageResult.None;
        }

        ushort maxIntegrity = _materials.Hot.MaxIntegrity[material];
        if (maxIntegrity != 0)
        {
            int accumulated = Math.Min(byte.MaxValue, window.GetDamage(wx, wy) + effectiveDamage);
            if (accumulated * EngineConstants.DamageIntegrityScale < maxIntegrity)
            {
                window.SetDamage(wx, wy, (byte)accumulated);
                window.SetFlags(wx, wy, CellFlags.SetParity(window.GetFlags(wx, wy), parityBit));
                return CorrosionDamageResult.Accumulated;
            }
        }

        ushort rubbleTarget = _materials.Hot.RubbleTarget[material];
        window.SetMaterial(wx, wy, rubbleTarget);
        window.SetLifetime(wx, wy, DefaultLifetimeByte(rubbleTarget));
        window.SetFlags(wx, wy, rubbleTarget == 0 ? (byte)0 : CellFlags.SetParity(0, parityBit));
        window.SetDamage(wx, wy, 0);
        NotifyCellDestroyed(wx, wy, material, rubbleTarget);
        return CorrosionDamageResult.Destroyed;
    }

    private void ApplyOutput(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit)
    {
        window.SetMaterial(wx, wy, material);
        window.SetLifetime(wx, wy, DefaultLifetimeByte(material));
        byte flags = window.GetFlags(wx, wy);
        flags = IsFireMaterial(material) ? CellFlags.Set(flags, CellFlags.Burning) : CellFlags.Clear(flags, CellFlags.Burning);
        window.SetFlags(wx, wy, CellFlags.SetParity(flags, parityBit));
    }

    private bool IsAcid(ushort material)
    {
        return (_materials.Hot.PropertyFlags[material] & MaterialProperty.Acid) != 0;
    }

    private bool IsCorrodible(ushort material)
    {
        return (_materials.Hot.PropertyFlags[material] & MaterialProperty.Corrodible) != 0;
    }

    private bool IsFireMaterial(ushort material)
    {
        return _materials.Hot.Type[material] == CellType.Fire ||
            (_materials.Hot.PropertyFlags[material] & MaterialProperty.Fire) != 0;
    }

    private void NotifyCellDestroyed(int wx, int wy, ushort sourceMaterial, ushort targetMaterial)
    {
        MaterialProperty flags = _materials.Hot.PropertyFlags[sourceMaterial];
        byte mineYield = (flags & MaterialProperty.Diggable) != 0
            ? _materials.Hot.MineYield[sourceMaterial]
            : (byte)0;
        CellDestructionEvent item = new(
            wx,
            wy,
            sourceMaterial,
            targetMaterial,
            targetMaterial == 0 ? sourceMaterial : targetMaterial,
            _materials.Hot.DebrisCount[sourceMaterial],
            mineYield);
        _cellDestructionSink.OnCellDestroyed(in item);
    }

    private void EmitSideEffects(in Reaction reaction, int wx1, int wy1, int wx2, int wy2)
    {
        byte smokeA = _materials.Hot.GeneratesSmoke[reaction.OutputA];
        byte smokeB = _materials.Hot.GeneratesSmoke[reaction.OutputB];
        bool needsSink =
            (reaction.Flags & (ReactionFlags.EmitHeat | ReactionFlags.SpawnParticle)) != 0 ||
            smokeA != 0 ||
            smokeB != 0;
        if (!needsSink)
        {
            return;
        }

        IReactionSideEffectSink sink = _sideEffects ??
            throw new InvalidOperationException("反应产生副作用，但 ReactionEngine 未配置 IReactionSideEffectSink。");
        if ((reaction.Flags & ReactionFlags.EmitHeat) != 0)
        {
            byte heat = Math.Max(
                _materials.Hot.TemperatureOfFire[reaction.OutputA],
                _materials.Hot.TemperatureOfFire[reaction.OutputB]);
            sink.AddHeat(wx1, wy1, reaction.OutputA, heat);
            sink.AddHeat(wx2, wy2, reaction.OutputB, heat);
        }

        if ((reaction.Flags & ReactionFlags.SpawnParticle) != 0)
        {
            RequestParticleEjection(sink, wx1, wy1, reaction.OutputA);
            RequestParticleEjection(sink, wx2, wy2, reaction.OutputB);
        }

        if (smokeA != 0)
        {
            sink.EmitSmoke(wx1, wy1, reaction.OutputA, smokeA);
        }

        if (smokeB != 0)
        {
            sink.EmitSmoke(wx2, wy2, reaction.OutputB, smokeB);
        }
    }

    private void RequestParticleEjection(IReactionSideEffectSink sink, int wx, int wy, ushort material)
    {
        if (material == 0)
        {
            return;
        }

        EjectMask mask = MaskFor(_materials.Hot.Type[material]);
        if (mask == EjectMask.None)
        {
            return;
        }

        EjectionRequest request = new(wx, wy, radius: 0, impulseSpeed: 0, impulseJitter: 0, mask);
        if (!sink.RequestParticleEjection(in request))
        {
            throw new InvalidOperationException("反应副作用请求自由粒子抛射失败。");
        }
    }

    private static EjectMask MaskFor(CellType type)
    {
        return type switch
        {
            CellType.Empty => EjectMask.None,
            CellType.Solid => EjectMask.Solid,
            CellType.Powder => EjectMask.Powder,
            CellType.Liquid => EjectMask.Liquid,
            CellType.Gas => EjectMask.Gas,
            CellType.Fire => EjectMask.Fire,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知 cell 类型。"),
        };
    }

    private byte DefaultLifetimeByte(ushort material)
    {
        ushort lifetime = _materials.Hot.DefaultLifetime[material];
        return lifetime > byte.MaxValue
            ? throw new InvalidOperationException($"材质 {material} 的默认 lifetime 超过 byte 存储上限。")
            : (byte)lifetime;
    }

    private enum CorrosionDamageResult : byte
    {
        None,
        Accumulated,
        Destroyed,
    }

    private enum CorrosionReactionResult : byte
    {
        NotCorrosion,
        NoEffect,
        Applied,
    }
}
