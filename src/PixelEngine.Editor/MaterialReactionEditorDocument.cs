using PixelEngine.Content;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 材质与反应编辑器使用的可变文档模型；运行时 id 只读展示，不会写入 JSON。
/// </summary>
public sealed class MaterialReactionEditorDocument
{
    /// <summary>
    /// 材质行。
    /// </summary>
    public List<MaterialEditorRow> Materials { get; } = [];

    /// <summary>
    /// tag 输出代表材质行。
    /// </summary>
    public List<TagRepresentativeEditorRow> TagRepresentatives { get; } = [];

    /// <summary>
    /// 反应规则行。
    /// </summary>
    public List<ReactionEditorRow> Reactions { get; } = [];

    /// <summary>
    /// 从 Content DTO 构建可变编辑文档。
    /// </summary>
    public static MaterialReactionEditorDocument FromContent(
        MaterialDocumentJson materials,
        ReactionDocumentJson reactions,
        MaterialTable? runtimeMaterials = null)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(reactions);
        MaterialReactionEditorDocument document = new();
        if (materials.TagRepresentatives is not null)
        {
            for (int i = 0; i < materials.TagRepresentatives.Length; i++)
            {
                TagRepresentativeJson source = materials.TagRepresentatives[i];
                document.TagRepresentatives.Add(new TagRepresentativeEditorRow
                {
                    Tag = source.Tag ?? string.Empty,
                    Material = source.Material ?? string.Empty,
                });
            }
        }

        if (materials.Materials is not null)
        {
            for (int i = 0; i < materials.Materials.Length; i++)
            {
                MaterialJson source = materials.Materials[i];
                ushort? runtimeId = null;
                if (!string.IsNullOrWhiteSpace(source.Name) &&
                    runtimeMaterials is not null &&
                    runtimeMaterials.TryGetId(source.Name, out ushort id))
                {
                    runtimeId = id;
                }

                document.Materials.Add(MaterialEditorRow.FromContent(source, runtimeId));
            }
        }

        if (reactions.Reactions is not null)
        {
            for (int i = 0; i < reactions.Reactions.Length; i++)
            {
                document.Reactions.Add(ReactionEditorRow.FromContent(reactions.Reactions[i]));
            }
        }

        return document;
    }

    /// <summary>
    /// 转成 Content materials.json DTO。
    /// </summary>
    public MaterialDocumentJson ToMaterialDocument()
    {
        return new MaterialDocumentJson
        {
            TagRepresentatives = [.. TagRepresentatives.Select(static row => new TagRepresentativeJson
            {
                Tag = NullIfWhiteSpace(row.Tag),
                Material = NullIfWhiteSpace(row.Material),
            })],
            Materials = [.. Materials.Select(static row => row.ToContent())],
        };
    }

    /// <summary>
    /// 转成 Content reactions.json DTO。
    /// </summary>
    public ReactionDocumentJson ToReactionDocument()
    {
        return new ReactionDocumentJson
        {
            Reactions = [.. Reactions.Select(static row => row.ToContent())],
        };
    }

    internal static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static string[]? SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    internal static string JoinCsv(string[]? values)
    {
        return values is null || values.Length == 0 ? string.Empty : string.Join(", ", values);
    }
}

/// <summary>
/// 可编辑材质行。
/// </summary>
public sealed class MaterialEditorRow
{
    /// <summary>运行时 id，只读展示。</summary>
    public ushort? RuntimeId { get; set; }

    /// <summary>稳定材质 name。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CellType 名称。</summary>
    public string Type { get; set; } = nameof(CellType.Empty);

    /// <summary>材质密度，用于 CA 移动排序。</summary>
    public int Density { get; set; }

    /// <summary>粉体/液体横向扩散范围。</summary>
    public int Dispersion { get; set; }

    /// <summary>液体静止后是否允许转入 static 态。</summary>
    public bool LiquidStatic { get; set; }

    /// <summary>是否按 liquid-sand 混合规则处理。</summary>
    public bool LiquidSand { get; set; }

    /// <summary>可燃性参数。</summary>
    public int Flammability { get; set; }

    /// <summary>自燃温度阈值。</summary>
    public int AutoIgnitionTemp { get; set; }

    /// <summary>燃烧生命值。</summary>
    public int FireHp { get; set; }

    /// <summary>燃烧时释放的温度。</summary>
    public int TemperatureOfFire { get; set; }

    /// <summary>燃烧时生成烟雾的概率或强度。</summary>
    public int GeneratesSmoke { get; set; }

    /// <summary>熔化温度阈值。</summary>
    public float? MeltPoint { get; set; }

    /// <summary>熔化后的目标材质 name。</summary>
    public string MeltTarget { get; set; } = string.Empty;

    /// <summary>凝固温度阈值。</summary>
    public float? FreezePoint { get; set; }

    /// <summary>凝固后的目标材质 name。</summary>
    public string FreezeTarget { get; set; } = string.Empty;

    /// <summary>沸腾温度阈值。</summary>
    public float? BoilPoint { get; set; }

    /// <summary>沸腾后的目标材质 name。</summary>
    public string BoilTarget { get; set; } = string.Empty;

    /// <summary>热传导系数。</summary>
    public int HeatConduct { get; set; }

    /// <summary>热容量。</summary>
    public float HeatCapacity { get; set; } = 1f;

    /// <summary>默认生命周期 tick 数。</summary>
    public int DefaultLifetime { get; set; }

    /// <summary>刚体/破坏耐久度。</summary>
    public int Durability { get; set; }

    /// <summary>结构完整度阈值；0 表示有效伤害即时破坏。</summary>
    public int Integrity { get; set; }

    /// <summary>破坏后的目标材质 name。</summary>
    public string DestroyedTarget { get; set; } = string.Empty;

    /// <summary>破坏时请求抛射的碎屑数量。</summary>
    public int DebrisCount { get; set; }

    /// <summary>可采集材质破坏时产生的采集计数。</summary>
    public int MineYield { get; set; }

    /// <summary>材质纹理 id；负数表示未指定。</summary>
    public int TextureId { get; set; } = -1;

    /// <summary>基础显示色，不写入 sim cell。</summary>
    public uint BaseColor { get; set; }

    /// <summary>渲染色噪声强度。</summary>
    public int ColorNoise { get; set; }

    /// <summary>材质着色风格。</summary>
    public string RenderStyle { get; set; } = nameof(MaterialRenderStyle.Ground);

    /// <summary>材质图例分类。</summary>
    public string LegendCategory { get; set; } = nameof(MaterialLegendCategory.Terrain);

    /// <summary>描边或裂纹叠色 BGRA8。</summary>
    public uint EdgeColorBGRA { get; set; }

    /// <summary>渲染 alpha。</summary>
    public int Opacity { get; set; } = byte.MaxValue;

    /// <summary>高亮或 emissive 叠色 BGRA8。</summary>
    public uint HighlightColorBGRA { get; set; }

    /// <summary>编辑器 / HUD 展示名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>是否在图例里默认展示。</summary>
    public bool LegendVisible { get; set; } = true;

    /// <summary>逗号分隔的 tag 列表。</summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>撞击音效 cue id。</summary>
    public int ImpactCue { get; set; }

    /// <summary>燃烧音效 cue id。</summary>
    public int FireCue { get; set; }

    /// <summary>飞溅音效 cue id。</summary>
    public int SplashCue { get; set; }

    /// <summary>爆炸音效 cue id。</summary>
    public int ExplosionCue { get; set; }

    /// <summary>破碎音效 cue id。</summary>
    public int ShatterCue { get; set; }

    /// <summary>环境循环音效 cue id。</summary>
    public int AmbientCue { get; set; }

    /// <summary>
    /// 从 Content DTO 构建材质行。
    /// </summary>
    public static MaterialEditorRow FromContent(MaterialJson source, ushort? runtimeId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MaterialEditorRow
        {
            RuntimeId = runtimeId,
            Name = source.Name ?? string.Empty,
            Type = source.Type ?? nameof(CellType.Empty),
            Density = source.Density,
            Dispersion = source.Dispersion,
            LiquidStatic = source.LiquidStatic,
            LiquidSand = source.LiquidSand,
            Flammability = source.Flammability,
            AutoIgnitionTemp = source.AutoIgnitionTemp,
            FireHp = source.FireHp,
            TemperatureOfFire = source.TemperatureOfFire,
            GeneratesSmoke = source.GeneratesSmoke,
            MeltPoint = source.MeltPoint,
            MeltTarget = source.MeltTarget ?? string.Empty,
            FreezePoint = source.FreezePoint,
            FreezeTarget = source.FreezeTarget ?? string.Empty,
            BoilPoint = source.BoilPoint,
            BoilTarget = source.BoilTarget ?? string.Empty,
            HeatConduct = source.HeatConduct,
            HeatCapacity = source.HeatCapacity ?? 1f,
            DefaultLifetime = source.DefaultLifetime,
            Durability = source.Durability,
            Integrity = source.Integrity,
            DestroyedTarget = source.DestroyedTarget ?? string.Empty,
            DebrisCount = source.DebrisCount,
            MineYield = source.MineYield,
            TextureId = source.TextureId ?? -1,
            BaseColor = source.BaseColor,
            ColorNoise = source.ColorNoise,
            RenderStyle = source.RenderStyle ?? nameof(MaterialRenderStyle.Ground),
            LegendCategory = source.LegendCategory ?? nameof(MaterialLegendCategory.Terrain),
            EdgeColorBGRA = source.EdgeColorBGRA,
            Opacity = source.Opacity ?? byte.MaxValue,
            HighlightColorBGRA = source.HighlightColorBGRA,
            DisplayName = source.DisplayName ?? string.Empty,
            LegendVisible = source.LegendVisible ?? true,
            Tags = MaterialReactionEditorDocument.JoinCsv(source.Tags),
            ImpactCue = source.AudioCues?.Impact ?? 0,
            FireCue = source.AudioCues?.Fire ?? 0,
            SplashCue = source.AudioCues?.Splash ?? 0,
            ExplosionCue = source.AudioCues?.Explosion ?? 0,
            ShatterCue = source.AudioCues?.Shatter ?? 0,
            AmbientCue = source.AudioCues?.Ambient ?? 0,
        };
    }

    /// <summary>
    /// 转成 Content DTO；不写运行时 id。
    /// </summary>
    public MaterialJson ToContent()
    {
        return new MaterialJson
        {
            Name = MaterialReactionEditorDocument.NullIfWhiteSpace(Name),
            Type = MaterialReactionEditorDocument.NullIfWhiteSpace(Type),
            Density = ClampByte(Density),
            Dispersion = ClampByte(Dispersion),
            LiquidStatic = LiquidStatic,
            LiquidSand = LiquidSand,
            Flammability = ClampByte(Flammability),
            AutoIgnitionTemp = ClampUshort(AutoIgnitionTemp),
            FireHp = FireHp,
            TemperatureOfFire = ClampByte(TemperatureOfFire),
            GeneratesSmoke = ClampByte(GeneratesSmoke),
            MeltPoint = MeltPoint,
            MeltTarget = MaterialReactionEditorDocument.NullIfWhiteSpace(MeltTarget),
            FreezePoint = FreezePoint,
            FreezeTarget = MaterialReactionEditorDocument.NullIfWhiteSpace(FreezeTarget),
            BoilPoint = BoilPoint,
            BoilTarget = MaterialReactionEditorDocument.NullIfWhiteSpace(BoilTarget),
            HeatConduct = ClampByte(HeatConduct),
            HeatCapacity = HeatCapacity,
            DefaultLifetime = ClampUshort(DefaultLifetime),
            Durability = ClampByte(Durability),
            Integrity = ClampUshort(Integrity),
            DestroyedTarget = MaterialReactionEditorDocument.NullIfWhiteSpace(DestroyedTarget),
            DebrisCount = ClampByte(DebrisCount),
            MineYield = ClampByte(MineYield),
            TextureId = TextureId < 0 ? null : TextureId,
            BaseColor = BaseColor,
            ColorNoise = ClampByte(ColorNoise),
            RenderStyle = MaterialReactionEditorDocument.NullIfWhiteSpace(RenderStyle),
            LegendCategory = MaterialReactionEditorDocument.NullIfWhiteSpace(LegendCategory),
            EdgeColorBGRA = EdgeColorBGRA,
            Opacity = ClampByte(Opacity),
            HighlightColorBGRA = HighlightColorBGRA,
            DisplayName = MaterialReactionEditorDocument.NullIfWhiteSpace(DisplayName),
            LegendVisible = LegendVisible,
            Tags = MaterialReactionEditorDocument.SplitCsv(Tags),
            AudioCues = new AudioCueSetJson
            {
                Impact = ImpactCue,
                Fire = FireCue,
                Splash = SplashCue,
                Explosion = ExplosionCue,
                Shatter = ShatterCue,
                Ambient = AmbientCue,
            },
        };
    }

    private static byte ClampByte(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
    }

    private static ushort ClampUshort(int value)
    {
        return (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
    }
}

/// <summary>
/// tag 输出代表材质编辑行。
/// </summary>
public sealed class TagRepresentativeEditorRow
{
    /// <summary>tag 名称。</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>该 tag 展开时使用的代表材质 name。</summary>
    public string Material { get; set; } = string.Empty;
}

/// <summary>
/// 可编辑反应规则行。
/// </summary>
public sealed class ReactionEditorRow
{
    /// <summary>输入材质 A，支持材质 name 或 tag。</summary>
    public string InputA { get; set; } = string.Empty;

    /// <summary>输入材质 B，支持材质 name 或 tag。</summary>
    public string InputB { get; set; } = string.Empty;

    /// <summary>输出材质 A。</summary>
    public string OutputA { get; set; } = string.Empty;

    /// <summary>输出材质 B。</summary>
    public string OutputB { get; set; } = string.Empty;

    /// <summary>反应概率，范围 0 到 100。</summary>
    public int Probability { get; set; } = 100;

    /// <summary>逗号分隔的反应 flags。</summary>
    public string Flags { get; set; } = string.Empty;

    /// <summary>
    /// 从 Content DTO 构建反应编辑行。
    /// </summary>
    public static ReactionEditorRow FromContent(ReactionJson source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ReactionEditorRow
        {
            InputA = source.InputA ?? string.Empty,
            InputB = source.InputB ?? string.Empty,
            OutputA = source.OutputA ?? string.Empty,
            OutputB = source.OutputB ?? string.Empty,
            Probability = source.Probability,
            Flags = MaterialReactionEditorDocument.JoinCsv(source.Flags),
        };
    }

    /// <summary>
    /// 转成 Content DTO。
    /// </summary>
    public ReactionJson ToContent()
    {
        return new ReactionJson
        {
            InputA = MaterialReactionEditorDocument.NullIfWhiteSpace(InputA),
            InputB = MaterialReactionEditorDocument.NullIfWhiteSpace(InputB),
            OutputA = MaterialReactionEditorDocument.NullIfWhiteSpace(OutputA),
            OutputB = MaterialReactionEditorDocument.NullIfWhiteSpace(OutputB),
            Probability = Math.Clamp(Probability, 0, 100),
            Flags = MaterialReactionEditorDocument.SplitCsv(Flags),
        };
    }
}
