using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>World Inspector 面板的跟随/锁定状态与最近显示结果。</summary>
public sealed record AutomationWorldInspectorSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>是否跟随跨面板 cell selection。</summary>
    public bool FollowSelection { get; init; }

    /// <summary>关闭跟随时使用的锁定世界 X。</summary>
    public int LockedWorldX { get; init; }

    /// <summary>关闭跟随时使用的锁定世界 Y。</summary>
    public int LockedWorldY { get; init; }

    /// <summary>面板最近实际显示的 cell；无选择或坐标不可用时为 null。</summary>
    public AutomationRuntimeCellInspection? Inspection { get; init; }
}

/// <summary>原子设置 World Inspector 的跟随模式与保留锁定坐标。</summary>
public sealed record AutomationWorldInspectorSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>是否跟随跨面板 cell selection。</summary>
    public bool FollowSelection { get; init; }

    /// <summary>锁定模式目标世界 X；跟随模式下作为下次解锁坐标保留。</summary>
    public int LockedWorldX { get; init; }

    /// <summary>锁定模式目标世界 Y；跟随模式下作为下次解锁坐标保留。</summary>
    public int LockedWorldY { get; init; }
}

/// <summary>Editor 世界存档 slot 摘要。</summary>
public sealed record AutomationSaveSlotInfo
{
    /// <summary>canonical slot ID。</summary>
    public required string SlotId { get; init; }

    /// <summary>slot canonical directory path。</summary>
    public required string Path { get; init; }

    /// <summary>manifest 最后写入时间。</summary>
    public DateTimeOffset LastWriteUtc { get; init; }

    /// <summary>world manifest 格式版本。</summary>
    public int FormatVersion { get; init; }

    /// <summary>世界种子。</summary>
    public ulong WorldSeed { get; init; }

    /// <summary>保存时的游戏 tick。</summary>
    public long GameTimeTicks { get; init; }

    /// <summary>存档 chunk 数。</summary>
    public int ChunkCount { get; init; }
}

/// <summary>分页存档 slot 列表。</summary>
public sealed record AutomationSaveSlotListResponse
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前页 slots。</summary>
    public AutomationSaveSlotInfo[] Items { get; init; } = [];

    /// <summary>稳定分页信息。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>保存或加载一个 canonical world save slot。</summary>
public sealed record AutomationSaveSlotRequest
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>canonical slot ID；服务端会执行相同的安全规范化。</summary>
    public required string SlotId { get; init; }
}

/// <summary>world save/load 操作的稳定摘要。</summary>
public sealed record AutomationSaveSlotOperationResult
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>操作对应的 slot。</summary>
    public required AutomationSaveSlotInfo Slot { get; init; }

    /// <summary>快照世界种子。</summary>
    public ulong WorldSeed { get; init; }

    /// <summary>快照游戏 tick。</summary>
    public long GameTimeTicks { get; init; }

    /// <summary>快照 resident chunk 数。</summary>
    public int ChunkCount { get; init; }

    /// <summary>加载时因缺失 material name 使用 fallback 的次数；保存时为 0。</summary>
    public long MaterialFallbackHitCount { get; init; }
}

/// <summary>按稳定名称读取一个 runtime material。</summary>
public sealed record AutomationMaterialRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>稳定材质名称；不接受 runtime 数值 ID 作为身份。</summary>
    public required string Name { get; init; }
}

/// <summary>当前 Engine material table 中一个 live 材质的完整只读定义。</summary>
public sealed record AutomationMaterialDefinition
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>该材质的稳定 revision resource ID。</summary>
    public required string ResourceId { get; init; }

    /// <summary>稳定字符串身份。</summary>
    public required string Name { get; init; }

    /// <summary>当前进程内 runtime 数值 ID；只用于诊断。</summary>
    public int RuntimeId { get; init; }

    /// <summary>面向用户的显示名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>CellType 名称。</summary>
    public required string CellType { get; init; }

    /// <summary>材质密度。</summary>
    public int Density { get; init; }

    /// <summary>液体或气体横向扩散距离。</summary>
    public int Dispersion { get; init; }

    /// <summary>液体是否静态。</summary>
    public bool LiquidStatic { get; init; }

    /// <summary>液体是否按粉末规则下落。</summary>
    public bool LiquidSand { get; init; }

    /// <summary>接触点燃权重。</summary>
    public int Flammability { get; init; }

    /// <summary>自燃温度阈值。</summary>
    public int AutoIgnitionTemp { get; init; }

    /// <summary>燃烧耐久；-1 表示永燃。</summary>
    public int FireHp { get; init; }

    /// <summary>燃烧温度注入基准。</summary>
    public int TemperatureOfFire { get; init; }

    /// <summary>产烟倾向。</summary>
    public int GeneratesSmoke { get; init; }

    /// <summary>熔化阈值；无相变时为 null。</summary>
    public float? MeltPoint { get; init; }

    /// <summary>熔化目标稳定名称；无相变时为 null。</summary>
    public string? MeltTargetName { get; init; }

    /// <summary>凝固阈值；无相变时为 null。</summary>
    public float? FreezePoint { get; init; }

    /// <summary>凝固目标稳定名称；无相变时为 null。</summary>
    public string? FreezeTargetName { get; init; }

    /// <summary>沸腾阈值；无相变时为 null。</summary>
    public float? BoilPoint { get; init; }

    /// <summary>沸腾目标稳定名称；无相变时为 null。</summary>
    public string? BoilTargetName { get; init; }

    /// <summary>热传导概率权重。</summary>
    public int HeatConduct { get; init; }

    /// <summary>热容量。</summary>
    public float HeatCapacity { get; init; }

    /// <summary>默认 lifetime。</summary>
    public int DefaultLifetime { get; init; }

    /// <summary>基础破坏耐久。</summary>
    public int Durability { get; init; }

    /// <summary>结构硬度。</summary>
    public int Hardness { get; init; }

    /// <summary>最大结构完整度。</summary>
    public int MaxIntegrity { get; init; }

    /// <summary>破坏后目标的稳定材质名称。</summary>
    public string? DestroyedTargetName { get; init; }

    /// <summary>破坏时碎屑数量。</summary>
    public int DebrisCount { get; init; }

    /// <summary>采矿收益。</summary>
    public int MineYield { get; init; }

    /// <summary>材质纹理索引；-1 表示纯色。</summary>
    public int TextureId { get; init; }

    /// <summary>BGRA8 基色。</summary>
    public uint BaseColorBgra { get; init; }

    /// <summary>颜色噪声幅度。</summary>
    public int ColorNoise { get; init; }

    /// <summary>渲染样式名称。</summary>
    public required string RenderStyle { get; init; }

    /// <summary>图例分类名称。</summary>
    public required string LegendCategory { get; init; }

    /// <summary>BGRA8 边缘色。</summary>
    public uint EdgeColorBgra { get; init; }

    /// <summary>渲染 alpha。</summary>
    public int Opacity { get; init; }

    /// <summary>BGRA8 高亮色。</summary>
    public uint HighlightColorBgra { get; init; }

    /// <summary>是否默认显示在图例/调色板。</summary>
    public bool LegendVisible { get; init; }

    /// <summary>MaterialProperty flags 的稳定名称组合。</summary>
    public required string PropertyFlags { get; init; }

    /// <summary>是否进入 emissive/bloom 路径。</summary>
    public bool Emissive { get; init; }

    /// <summary>是否可被结构破坏 API 处理。</summary>
    public bool Destructible { get; init; }

    /// <summary>是否阻挡 kinematic character。</summary>
    public bool BlocksCharacter { get; init; }

    /// <summary>packed reaction 数量。</summary>
    public int ReactionCount { get; init; }

    /// <summary>撞击 audio cue handle。</summary>
    public int ImpactCue { get; init; }

    /// <summary>燃烧 audio cue handle。</summary>
    public int FireCue { get; init; }

    /// <summary>飞溅 audio cue handle。</summary>
    public int SplashCue { get; init; }

    /// <summary>爆炸 audio cue handle。</summary>
    public int ExplosionCue { get; init; }

    /// <summary>刚体破碎 audio cue handle。</summary>
    public int ShatterCue { get; init; }

    /// <summary>区域 ambient audio cue handle。</summary>
    public int AmbientCue { get; init; }
}

/// <summary>分页 runtime material catalog。</summary>
public sealed record AutomationMaterialListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前页 live material definitions。</summary>
    public AutomationMaterialDefinition[] Items { get; init; } = [];

    /// <summary>稳定分页信息。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>按世界坐标读取一个 runtime cell。</summary>
public sealed record AutomationRuntimeCellInspectRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>世界 X。</summary>
    public required int WorldX { get; init; }
    /// <summary>世界 Y。</summary>
    public required int WorldY { get; init; }
}

/// <summary>Chunk 本地坐标系下的闭区间 dirty rectangle。</summary>
public sealed record AutomationDirtyRectSnapshot
{
    /// <summary>是否为空。</summary>
    public required bool IsEmpty { get; init; }
    /// <summary>最小本地 X。</summary>
    public required int MinX { get; init; }
    /// <summary>最小本地 Y。</summary>
    public required int MinY { get; init; }
    /// <summary>最大本地 X。</summary>
    public required int MaxX { get; init; }
    /// <summary>最大本地 Y。</summary>
    public required int MaxY { get; init; }
}

/// <summary>World Inspector 的单 cell 完整只读快照。</summary>
public sealed record AutomationRuntimeCellInspection
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>世界 X。</summary>
    public required int WorldX { get; init; }
    /// <summary>世界 Y。</summary>
    public required int WorldY { get; init; }
    /// <summary>Chunk X。</summary>
    public required int ChunkX { get; init; }
    /// <summary>Chunk Y。</summary>
    public required int ChunkY { get; init; }
    /// <summary>Chunk 本地 X。</summary>
    public required int LocalX { get; init; }
    /// <summary>Chunk 本地 Y。</summary>
    public required int LocalY { get; init; }
    /// <summary>Runtime 数值材质 ID；只用于当前进程诊断。</summary>
    public required int MaterialId { get; init; }
    /// <summary>稳定材质名称。</summary>
    public required string MaterialName { get; init; }
    /// <summary>是否有温度场。</summary>
    public required bool TemperatureAvailable { get; init; }
    /// <summary>摄氏温度；无温度场时仍返回 0。</summary>
    public required float TemperatureCelsius { get; init; }
    /// <summary>原始 cell flags。</summary>
    public required int RawFlags { get; init; }
    /// <summary>Parity 位。</summary>
    public required bool Parity { get; init; }
    /// <summary>Settled 位。</summary>
    public required bool Settled { get; init; }
    /// <summary>Burning 位。</summary>
    public required bool Burning { get; init; }
    /// <summary>Free-falling 位。</summary>
    public required bool FreeFalling { get; init; }
    /// <summary>Rigid-owned 位。</summary>
    public required bool RigidOwned { get; init; }
    /// <summary>可选 runtime body key。</summary>
    public int? BodyKey { get; init; }
    /// <summary>本帧读取 dirty rectangle。</summary>
    public required AutomationDirtyRectSnapshot CurrentDirty { get; init; }
    /// <summary>下一帧累计 dirty rectangle。</summary>
    public required AutomationDirtyRectSnapshot WorkingDirty { get; init; }
    /// <summary>稳定 ChunkState 名称。</summary>
    public required string ChunkState { get; init; }
    /// <summary>Chunk 最近处理 parity。</summary>
    public required int ChunkParity { get; init; }
}

/// <summary>Physics 面板运行时调参与统计。</summary>
public sealed record AutomationRuntimePhysicsSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>像素/米固定比例。</summary>
    public required float PixelsPerMeter { get; init; }
    /// <summary>Box2D 内部子步数。</summary>
    public required int SubStepCount { get; init; }
    /// <summary>Box2D task bridge worker 数。</summary>
    public required int WorkerCount { get; init; }
    /// <summary>碎片像素阈值。</summary>
    public required int FragmentPixelThreshold { get; init; }
    /// <summary>形状重建节流 tick。</summary>
    public required int RebuildThrottleTicks { get; init; }
    /// <summary>重力 X。</summary>
    public required float GravityX { get; init; }
    /// <summary>重力 Y。</summary>
    public required float GravityY { get; init; }
    /// <summary>活跃刚体数。</summary>
    public required int ActiveBodyCount { get; init; }
    /// <summary>最近同步的 damage 数。</summary>
    public required int PendingDamageCount { get; init; }
    /// <summary>最近擦除 cell 数。</summary>
    public required int LastErasedCellCount { get; init; }
    /// <summary>最近写回 cell 数。</summary>
    public required int LastStampedCellCount { get; init; }
    /// <summary>最近受损刚体数。</summary>
    public required int DamagedBodyCount { get; init; }
    /// <summary>最近销毁刚体数。</summary>
    public required int DestroyedBodyCount { get; init; }
    /// <summary>最近创建刚体数。</summary>
    public required int CreatedBodyCount { get; init; }
    /// <summary>最近碎片像素数。</summary>
    public required int FragmentPixelCount { get; init; }
    /// <summary>最近因 sleeping 跳过刚体数。</summary>
    public required int SkippedSleepingBodyCount { get; init; }
    /// <summary>实际 task bridge worker 数。</summary>
    public required int TaskBridgeWorkerCount { get; init; }
    /// <summary>Native task callback 捕获异常数。</summary>
    public required int TaskBridgeFaultedCallbackCount { get; init; }
}

/// <summary>设置 Physics 面板可编辑参数。</summary>
public sealed record AutomationRuntimePhysicsSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>Box2D 内部子步数。</summary>
    public required int SubStepCount { get; init; }
    /// <summary>碎片像素阈值。</summary>
    public required int FragmentPixelThreshold { get; init; }
    /// <summary>重力 X。</summary>
    public required float GravityX { get; init; }
    /// <summary>重力 Y。</summary>
    public required float GravityY { get; init; }
}

/// <summary>Particles 面板运行时调参与统计。</summary>
public sealed record AutomationRuntimeParticlesSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>最大活跃粒子数。</summary>
    public required int MaxCount { get; init; }
    /// <summary>每 tick 重力增量。</summary>
    public required float GravityPerTick { get; init; }
    /// <summary>最大寿命 tick。</summary>
    public required int MaxLifetimeTicks { get; init; }
    /// <summary>沉积速度阈值。</summary>
    public required float DepositSpeedEpsilon { get; init; }
    /// <summary>抛射冲量倍率。</summary>
    public required float EjectionImpulseScale { get; init; }
    /// <summary>单 tick 抛射上限。</summary>
    public required int MaxEjectionPerTick { get; init; }
    /// <summary>当前活跃数。</summary>
    public required int ActiveCount { get; init; }
    /// <summary>固定池容量。</summary>
    public required int Capacity { get; init; }
    /// <summary>本 tick 生成数。</summary>
    public required int SpawnedThisTick { get; init; }
    /// <summary>本 tick 沉积数。</summary>
    public required int DepositedThisTick { get; init; }
    /// <summary>本 tick 寿命终止数。</summary>
    public required int KilledByLifetimeThisTick { get; init; }
    /// <summary>本 tick 丢弃数。</summary>
    public required int DroppedThisTick { get; init; }
    /// <summary>本 tick 丢弃音频事件数。</summary>
    public required int AudioEventsDroppedThisTick { get; init; }
    /// <summary>本 tick cell 破坏事件数。</summary>
    public required int CellDestructionEventsThisTick { get; init; }
}

/// <summary>设置 Particles 面板可编辑参数。</summary>
public sealed record AutomationRuntimeParticlesSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>最大活跃粒子数。</summary>
    public required int MaxCount { get; init; }
    /// <summary>每 tick 重力增量。</summary>
    public required float GravityPerTick { get; init; }
    /// <summary>最大寿命 tick。</summary>
    public required int MaxLifetimeTicks { get; init; }
    /// <summary>沉积速度阈值。</summary>
    public required float DepositSpeedEpsilon { get; init; }
    /// <summary>抛射冲量倍率。</summary>
    public required float EjectionImpulseScale { get; init; }
    /// <summary>单 tick 抛射上限。</summary>
    public required int MaxEjectionPerTick { get; init; }
}

/// <summary>Lighting 质量档。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationLightingQuality>))]
public enum AutomationLightingQuality
{
    /// <summary>完整 lighting、bloom 与 post。</summary>
    Full,
    /// <summary>关闭 bloom，保留 lighting 与 post。</summary>
    BloomDisabled,
    /// <summary>仅 fog-of-war、emissive 与基础 post。</summary>
    FogOfWarEmissiveOnly,
}

/// <summary>Lighting 面板运行时调参。</summary>
public sealed record AutomationRuntimeLightingSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>质量档。</summary>
    public required AutomationLightingQuality Quality { get; init; }
    /// <summary>是否启用 bloom。</summary>
    public required bool BloomEnabled { get; init; }
    /// <summary>Bloom bright-pass 阈值。</summary>
    public required float BloomThreshold { get; init; }
    /// <summary>Bloom 合成强度。</summary>
    public required float BloomIntensity { get; init; }
    /// <summary>是否应用 fog-of-war visibility。</summary>
    public required bool FogOfWarEnabled { get; init; }
    /// <summary>是否启用 dither。</summary>
    public required bool DitherEnabled { get; init; }
    /// <summary>Gamma。</summary>
    public required float Gamma { get; init; }
    /// <summary>是否启用 Radiance Cascades。</summary>
    public required bool RadianceCascadesEnabled { get; init; }
}

/// <summary>设置 Lighting 面板可编辑参数。</summary>
public sealed record AutomationRuntimeLightingSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>质量档。</summary>
    public required AutomationLightingQuality Quality { get; init; }
    /// <summary>是否启用 bloom。</summary>
    public required bool BloomEnabled { get; init; }
    /// <summary>Bloom bright-pass 阈值。</summary>
    public required float BloomThreshold { get; init; }
    /// <summary>Bloom 合成强度。</summary>
    public required float BloomIntensity { get; init; }
    /// <summary>是否应用 fog-of-war visibility。</summary>
    public required bool FogOfWarEnabled { get; init; }
    /// <summary>是否启用 dither。</summary>
    public required bool DitherEnabled { get; init; }
    /// <summary>Gamma。</summary>
    public required float Gamma { get; init; }
    /// <summary>是否启用 Radiance Cascades。</summary>
    public required bool RadianceCascadesEnabled { get; init; }
}
