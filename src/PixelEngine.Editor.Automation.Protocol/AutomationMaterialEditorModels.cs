namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>材质/反应面板的一份完整可编辑草稿。</summary>
public sealed record AutomationMaterialEditorDocument
{
    /// <summary>全部材质行；顺序与面板及 JSON 一致。</summary>
    public required AutomationMaterialEditorRow[] Materials { get; init; }

    /// <summary>全部 tag 输出代表行。</summary>
    public required AutomationMaterialTagRepresentative[] TagRepresentatives { get; init; }

    /// <summary>全部源反应规则行。</summary>
    public required AutomationMaterialReactionRow[] Reactions { get; init; }
}

/// <summary>材质/反应面板中的一个完整可编辑材质行。</summary>
public sealed record AutomationMaterialEditorRow
{
    /// <summary>稳定材质 name。</summary>
    public required string Name { get; init; }

    /// <summary>CellType 名称。</summary>
    public required string Type { get; init; }

    /// <summary>材质密度。</summary>
    public int Density { get; init; }

    /// <summary>粉体/液体横向扩散范围；加载时 clamp 到引擎移动上限。</summary>
    public int FlowRate { get; init; }

    /// <summary>液体静止后是否允许转入 static 态。</summary>
    public bool LiquidStatic { get; init; }

    /// <summary>是否按 liquid-sand 混合规则处理。</summary>
    public bool LiquidSand { get; init; }

    /// <summary>可燃性参数。</summary>
    public int Flammability { get; init; }

    /// <summary>自燃温度阈值。</summary>
    public int AutoIgnitionTemp { get; init; }

    /// <summary>燃烧生命值。</summary>
    public int FireHp { get; init; }

    /// <summary>燃烧时释放温度。</summary>
    public int TemperatureOfFire { get; init; }

    /// <summary>燃烧产烟概率或强度。</summary>
    public int GeneratesSmoke { get; init; }

    /// <summary>熔化温度；无相变时为 null。</summary>
    public float? MeltPoint { get; init; }

    /// <summary>熔化目标稳定 name。</summary>
    public required string MeltTarget { get; init; }

    /// <summary>凝固温度；无相变时为 null。</summary>
    public float? FreezePoint { get; init; }

    /// <summary>凝固目标稳定 name。</summary>
    public required string FreezeTarget { get; init; }

    /// <summary>沸腾温度；无相变时为 null。</summary>
    public float? BoilPoint { get; init; }

    /// <summary>沸腾目标稳定 name。</summary>
    public required string BoilTarget { get; init; }

    /// <summary>热传导系数。</summary>
    public int HeatConduct { get; init; }

    /// <summary>热容量。</summary>
    public float HeatCapacity { get; init; }

    /// <summary>默认生命周期 ticks。</summary>
    public int DefaultLifetime { get; init; }

    /// <summary>刚体/破坏耐久度。</summary>
    public int Durability { get; init; }

    /// <summary>结构完整度阈值。</summary>
    public int MaxIntegrity { get; init; }

    /// <summary>破坏后的目标稳定材质 name。</summary>
    public required string RubbleTarget { get; init; }

    /// <summary>破坏时碎屑数量。</summary>
    public int DebrisCount { get; init; }

    /// <summary>采集产出数量。</summary>
    public int MineYield { get; init; }

    /// <summary>材质纹理索引；负数表示未指定。</summary>
    public int TextureId { get; init; }

    /// <summary>BGRA8 基础显示色。</summary>
    public uint BaseColorBgra { get; init; }

    /// <summary>渲染色噪声强度。</summary>
    public int ColorNoise { get; init; }

    /// <summary>材质渲染风格。</summary>
    public required string RenderStyle { get; init; }

    /// <summary>材质图例分类。</summary>
    public required string LegendCategory { get; init; }

    /// <summary>BGRA8 描边色。</summary>
    public uint OutlineColorBgra { get; init; }

    /// <summary>渲染 alpha。</summary>
    public int Alpha { get; init; }

    /// <summary>BGRA8 流动/高亮提示色。</summary>
    public uint FlowTintBgra { get; init; }

    /// <summary>编辑器与 HUD 显示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>是否默认显示在图例中。</summary>
    public bool LegendVisible { get; init; }

    /// <summary>逗号分隔的 tags，保留面板草稿文本。</summary>
    public required string Tags { get; init; }

    /// <summary>撞击 audio cue id。</summary>
    public int ImpactCue { get; init; }

    /// <summary>燃烧 audio cue id。</summary>
    public int FireCue { get; init; }

    /// <summary>飞溅 audio cue id。</summary>
    public int SplashCue { get; init; }

    /// <summary>爆炸 audio cue id。</summary>
    public int ExplosionCue { get; init; }

    /// <summary>破碎 audio cue id。</summary>
    public int ShatterCue { get; init; }

    /// <summary>环境循环 audio cue id。</summary>
    public int AmbientCue { get; init; }
}

/// <summary>tag 展开输出使用的代表材质。</summary>
public sealed record AutomationMaterialTagRepresentative
{
    /// <summary>tag 名称。</summary>
    public required string Tag { get; init; }

    /// <summary>代表材质稳定 name。</summary>
    public required string Material { get; init; }
}

/// <summary>材质/反应面板中的一个源反应规则。</summary>
public sealed record AutomationMaterialReactionRow
{
    /// <summary>输入 A；支持稳定材质 name 或 tag。</summary>
    public required string InputA { get; init; }

    /// <summary>输入 B；支持稳定材质 name 或 tag。</summary>
    public required string InputB { get; init; }

    /// <summary>输出 A。</summary>
    public required string OutputA { get; init; }

    /// <summary>输出 B。</summary>
    public required string OutputB { get; init; }

    /// <summary>反应概率，范围 0 到 100。</summary>
    public int Probability { get; init; }

    /// <summary>逗号分隔的 reaction flags，保留面板草稿文本。</summary>
    public required string Flags { get; init; }
}

/// <summary>稳定材质 name 到本进程 runtime 数值 ID 的只读诊断绑定。</summary>
public sealed record AutomationMaterialRuntimeBinding
{
    /// <summary>稳定材质 name。</summary>
    public required string Name { get; init; }

    /// <summary>当前进程 runtime ID；不能作为写入身份。</summary>
    public int RuntimeId { get; init; }
}

/// <summary>材质/反应面板的完整可观察快照。</summary>
public sealed record AutomationMaterialEditorSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前完整草稿。</summary>
    public required AutomationMaterialEditorDocument Document { get; init; }

    /// <summary>当前 live runtime bindings；新建或缺失材质不在此数组中。</summary>
    public required AutomationMaterialRuntimeBinding[] RuntimeBindings { get; init; }

    /// <summary>面板最近一次状态文本。</summary>
    public required string Status { get; init; }
}

/// <summary>原子替换材质/反应面板完整草稿。</summary>
public sealed record AutomationMaterialEditorSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标完整草稿；未出现的行会被删除。</summary>
    public required AutomationMaterialEditorDocument Document { get; init; }
}

/// <summary>tag 展开与 packed reaction 预览结果。</summary>
public sealed record AutomationMaterialEditorPreviewResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>材质数量。</summary>
    public int MaterialCount { get; init; }

    /// <summary>源反应规则数量。</summary>
    public int SourceReactionCount { get; init; }

    /// <summary>展开后的 packed reaction 数量。</summary>
    public int PackedReactionCount { get; init; }

    /// <summary>与人工面板一致的状态文本。</summary>
    public required string Message { get; init; }
}

/// <summary>热重载后需要刷新的材质关联资产。</summary>
public sealed record AutomationMaterialEditorAssetReload
{
    /// <summary>稳定材质 name。</summary>
    public required string MaterialName { get; init; }

    /// <summary>当前进程 runtime ID；只用于诊断。</summary>
    public int RuntimeId { get; init; }

    /// <summary>纹理绑定是否变化。</summary>
    public bool TextureChanged { get; init; }

    /// <summary>audio cues 是否变化。</summary>
    public bool AudioChanged { get; init; }
}

/// <summary>双文件持久化与运行时热重载的完整结果。</summary>
public sealed record AutomationMaterialEditorApplyResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>本次转为 tombstone 的稳定材质 names。</summary>
    public required string[] TombstonedMaterialNames { get; init; }

    /// <summary>新增稳定材质数量。</summary>
    public int AddedCount { get; init; }

    /// <summary>保留稳定 runtime ID 的材质数量。</summary>
    public int PreservedCount { get; init; }

    /// <summary>live grid 中实际替换到 fallback 的 cell 数量。</summary>
    public int LiveGridFallbackReplacementCount { get; init; }

    /// <summary>展开后的 packed reaction 数量。</summary>
    public int PackedReactionCount { get; init; }

    /// <summary>需要刷新的关联资产。</summary>
    public required AutomationMaterialEditorAssetReload[] AssetReloads { get; init; }

    /// <summary>是否因清理失败保留恢复 journal。</summary>
    public bool CleanupPending { get; init; }

    /// <summary>保留 journal 的绝对路径；正常完成时为 null。</summary>
    public string? RetainedJournalPath { get; init; }

    /// <summary>journal 清理错误；正常完成时为 null。</summary>
    public string? CleanupError { get; init; }

    /// <summary>与人工面板一致的状态文本。</summary>
    public required string Status { get; init; }
}
