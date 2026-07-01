using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Simulation;

namespace PixelEngine.Content;

/// <summary>
/// 材质与反应 JSON 加载结果。
/// </summary>
public sealed class MaterialContentLoadResult(MaterialTable materials, ReactionTable reactions)
{
    /// <summary>
    /// 运行时材质表。
    /// </summary>
    public MaterialTable Materials { get; } = materials;

    /// <summary>
    /// packed 反应表。
    /// </summary>
    public ReactionTable Reactions { get; } = reactions;
}

/// <summary>
/// 将 materials.json / reactions.json 转换为 Simulation 运行时表。
/// </summary>
public static class MaterialContentLoader
{
    /// <summary>
    /// 从 JSON 文本加载材质表与反应表。
    /// </summary>
    public static MaterialContentLoadResult Load(string materialsJson, string reactionsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactionsJson);

        MaterialDocumentJson materialsDocument = DeserializeMaterialDocument(materialsJson);
        ReactionDocumentJson reactionsDocument = DeserializeReactionDocument(reactionsJson);
        return Build(materialsDocument, reactionsDocument);
    }

    /// <summary>
    /// 从 DTO 构建材质表与反应表。
    /// </summary>
    public static MaterialContentLoadResult Build(MaterialDocumentJson materialsDocument, ReactionDocumentJson reactionsDocument)
    {
        ArgumentNullException.ThrowIfNull(materialsDocument);
        ArgumentNullException.ThrowIfNull(reactionsDocument);

        MaterialJson[] materialJson = RequireNonEmpty(materialsDocument.Materials, "materials");
        Dictionary<string, ushort> nameToId = BuildNameIndex(materialJson);
        MaterialDef[] baseDefinitions = BuildBaseDefinitions(materialJson, nameToId);
        MaterialTagRepresentative[] representatives = BuildRepresentatives(materialsDocument.TagRepresentatives, nameToId);
        Reaction[] packed = BuildPackedReactions(reactionsDocument.Reactions ?? [], baseDefinitions, nameToId, representatives, out MaterialDef[] finalDefinitions);

        MaterialTable materials = new(finalDefinitions);
        ReactionTable reactions = new(packed, finalDefinitions);
        return new MaterialContentLoadResult(materials, reactions);
    }

    private static MaterialDocumentJson DeserializeMaterialDocument(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
            // 兼容根节点直接为 MaterialJson[] 的紧凑 schema。
        }

        MaterialJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialJsonArray);
        return array is null
            ? throw new JsonException("materials.json 为空或格式无效。")
            : new MaterialDocumentJson { Materials = array };
    }

    private static ReactionDocumentJson DeserializeReactionDocument(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
            // 兼容根节点直接为 ReactionJson[] 的紧凑 schema。
        }

        ReactionJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionJsonArray);
        return array is null
            ? throw new JsonException("reactions.json 为空或格式无效。")
            : new ReactionDocumentJson { Reactions = array };
    }

    private static MaterialJson[] RequireNonEmpty(MaterialJson[]? materials, string propertyName)
    {
        return materials is { Length: > 0 }
            ? materials
            : throw new ArgumentException($"{propertyName} 至少需要一个材质。");
    }

    private static Dictionary<string, ushort> BuildNameIndex(MaterialJson[] materials)
    {
        if (materials.Length > ushort.MaxValue)
        {
            throw new ArgumentException("材质数量超过 ushort id 上限。");
        }

        Dictionary<string, ushort> nameToId = new(materials.Length, StringComparer.Ordinal);
        for (int i = 0; i < materials.Length; i++)
        {
            string name = RequiredName(materials[i].Name, i);
            if (!nameToId.TryAdd(name, checked((ushort)i)))
            {
                throw new ArgumentException($"重复材质 name：{name}。");
            }
        }

        return nameToId;
    }

    private static MaterialDef[] BuildBaseDefinitions(MaterialJson[] materials, Dictionary<string, ushort> nameToId)
    {
        MaterialDef[] definitions = new MaterialDef[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            MaterialJson source = materials[i];
            string name = RequiredName(source.Name, i);
            definitions[i] = new MaterialDef
            {
                Id = checked((ushort)i),
                Name = name,
                Type = ParseEnum<CellType>(source.Type, $"materials[{i}].type"),
                Density = source.Density,
                Dispersion = source.Dispersion,
                LiquidStatic = source.LiquidStatic,
                LiquidSand = source.LiquidSand,
                Flammability = source.Flammability,
                AutoIgnitionTemp = source.AutoIgnitionTemp,
                FireHp = source.FireHp,
                TemperatureOfFire = source.TemperatureOfFire,
                GeneratesSmoke = source.GeneratesSmoke,
                MeltPoint = source.MeltPoint ?? float.NaN,
                MeltTarget = ResolveOptionalMaterial(source.MeltTarget, nameToId),
                FreezePoint = source.FreezePoint ?? float.NaN,
                FreezeTarget = ResolveOptionalMaterial(source.FreezeTarget, nameToId),
                BoilPoint = source.BoilPoint ?? float.NaN,
                BoilTarget = ResolveOptionalMaterial(source.BoilTarget, nameToId),
                HeatConduct = source.HeatConduct,
                HeatCapacity = source.HeatCapacity ?? 1f,
                DefaultLifetime = source.DefaultLifetime,
                Durability = source.Durability,
                TextureId = source.TextureId ?? -1,
                BaseColorBGRA = source.BaseColor,
                ColorNoise = source.ColorNoise,
                PropertyFlags = ParseMaterialProperties(source.Tags),
                AudioCues = BuildAudioCues(source.AudioCues),
            };
        }

        return definitions;
    }

    private static MaterialTagRepresentative[] BuildRepresentatives(TagRepresentativeJson[]? json, Dictionary<string, ushort> nameToId)
    {
        if (json is null || json.Length == 0)
        {
            return [];
        }

        MaterialTagRepresentative[] representatives = new MaterialTagRepresentative[json.Length];
        HashSet<MaterialTag> seen = [];
        for (int i = 0; i < json.Length; i++)
        {
            MaterialTag tag = ParseMaterialTag(json[i].Tag, $"tagRepresentatives[{i}].tag");
            if (!seen.Add(tag))
            {
                throw new ArgumentException($"重复 tag representative：{tag}。");
            }

            representatives[i] = new MaterialTagRepresentative(
                tag,
                ResolveRequiredMaterial(json[i].Material, nameToId, $"tagRepresentatives[{i}].material"));
        }

        return representatives;
    }

    private static Reaction[] BuildPackedReactions(
        ReactionJson[] reactions,
        MaterialDef[] definitions,
        Dictionary<string, ushort> nameToId,
        ReadOnlySpan<MaterialTagRepresentative> representatives,
        out MaterialDef[] finalDefinitions)
    {
        List<Reaction>[] byOwner = new List<Reaction>[definitions.Length];
        MaterialProperty[] propertyFlags = new MaterialProperty[definitions.Length];
        for (int i = 0; i < definitions.Length; i++)
        {
            byOwner[i] = [];
            propertyFlags[i] = definitions[i].PropertyFlags;
        }

        for (int i = 0; i < reactions.Length; i++)
        {
            AppendReaction(reactions[i], i, propertyFlags, nameToId, representatives, byOwner);
        }

        List<Reaction> packed = [];
        finalDefinitions = new MaterialDef[definitions.Length];
        for (int i = 0; i < definitions.Length; i++)
        {
            List<Reaction> owner = byOwner[i];
            owner.Sort(static (a, b) => a.InputB.CompareTo(b.InputB));
            if (owner.Count > byte.MaxValue)
            {
                throw new ArgumentException($"材质 {definitions[i].Name} 的 reaction 数量超过 byte 上限。");
            }

            int start = packed.Count;
            packed.AddRange(owner);
            finalDefinitions[i] = definitions[i] with
            {
                ReactionStart = start,
                ReactionCount = checked((byte)owner.Count),
            };
        }

        return [.. packed];
    }

    private static void AppendReaction(
        ReactionJson source,
        int index,
        ReadOnlySpan<MaterialProperty> propertyFlags,
        Dictionary<string, ushort> nameToId,
        ReadOnlySpan<MaterialTagRepresentative> representatives,
        List<Reaction>[] byOwner)
    {
        MaterialReference inputA = ParseMaterialReference(source.InputA, $"reactions[{index}].inputA");
        MaterialReference inputB = ParseMaterialReference(source.InputB, $"reactions[{index}].inputB");
        ushort outputA = ResolveOutput(source.OutputA, nameToId, representatives, $"reactions[{index}].outputA");
        ushort outputB = ResolveOutput(source.OutputB, nameToId, representatives, $"reactions[{index}].outputB");
        byte probability = ReactionExpansionRules.RateToProbabilityByte(source.Probability);
        ReactionFlags flags = ParseReactionFlags(source.Flags);
        ushort[] membersA = ResolveInputMembers(inputA, propertyFlags, nameToId, $"reactions[{index}].inputA");
        ushort[] membersB = ResolveInputMembers(inputB, propertyFlags, nameToId, $"reactions[{index}].inputB");

        for (int a = 0; a < membersA.Length; a++)
        {
            for (int b = 0; b < membersB.Length; b++)
            {
                ushort materialA = membersA[a];
                ushort materialB = membersB[b];
                AddOwnerReaction(byOwner[materialA], new Reaction
                {
                    InputA = materialA,
                    InputB = materialB,
                    OutputA = outputA,
                    OutputB = outputB,
                    Probability = probability,
                    Flags = flags,
                });

                if ((flags & ReactionFlags.Directional) == 0 && materialA != materialB)
                {
                    AddOwnerReaction(byOwner[materialB], new Reaction
                    {
                        InputA = materialB,
                        InputB = materialA,
                        OutputA = outputB,
                        OutputB = outputA,
                        Probability = probability,
                        Flags = flags,
                    });
                }
            }
        }
    }

    private static void AddOwnerReaction(List<Reaction> owner, Reaction reaction)
    {
        for (int i = 0; i < owner.Count; i++)
        {
            if (owner[i].InputB == reaction.InputB)
            {
                throw new ArgumentException($"材质 {reaction.InputA} 对邻居 {reaction.InputB} 定义了重复 reaction。");
            }
        }

        owner.Add(reaction);
    }

    private static ushort[] ResolveInputMembers(
        MaterialReference reference,
        ReadOnlySpan<MaterialProperty> propertyFlags,
        Dictionary<string, ushort> nameToId,
        string path)
    {
        if (reference.Tag is null)
        {
            return [ResolveRequiredMaterial(reference.Name, nameToId, path)];
        }

        int count = ReactionExpansionRules.CountTagMembers(propertyFlags, reference.Tag.Value);
        if (count == 0)
        {
            throw new ArgumentException($"{path} tag {reference.Tag.Value} 没有任何材质成员。");
        }

        ushort[] members = new ushort[count];
        _ = ReactionExpansionRules.WriteTagMembers(propertyFlags, reference.Tag.Value, members);
        return members;
    }

    private static ushort ResolveOutput(
        string? value,
        Dictionary<string, ushort> nameToId,
        ReadOnlySpan<MaterialTagRepresentative> representatives,
        string path)
    {
        MaterialReference reference = ParseMaterialReference(value, path);
        return reference.Tag is null
            ? ResolveRequiredMaterial(reference.Name, nameToId, path)
            : ReactionExpansionRules.RepresentativeOf(reference.Tag.Value, representatives);
    }

    private static MaterialReference ParseMaterialReference(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{path} 不能为空。");
        }

        string trimmed = value.Trim();
        return trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']'
            ? new MaterialReference(null, ParseMaterialTag(trimmed[1..^1], path))
            : new MaterialReference(trimmed, null);
    }

    private static MaterialProperty ParseMaterialProperties(string[]? tags)
    {
        if (tags is null || tags.Length == 0)
        {
            return MaterialProperty.None;
        }

        MaterialProperty flags = MaterialProperty.None;
        for (int i = 0; i < tags.Length; i++)
        {
            string normalized = NormalizeName(tags[i]);
            if (Enum.TryParse(normalized, ignoreCase: true, out MaterialTag tag))
            {
                flags |= MaterialTagMap.ToProperty(tag);
                continue;
            }

            if (Enum.TryParse(normalized, ignoreCase: true, out MaterialProperty property))
            {
                if (property == MaterialProperty.HasCustomUpdate)
                {
                    throw new ArgumentException("HasCustomUpdate 是运行时注册位，不能在 materials.json 中声明。");
                }

                flags |= property;
                continue;
            }

            throw new ArgumentException($"未知材质 tag/property：{tags[i]}。");
        }

        return flags;
    }

    private static ReactionFlags ParseReactionFlags(string[]? flags)
    {
        if (flags is null || flags.Length == 0)
        {
            return ReactionFlags.None;
        }

        ReactionFlags parsed = ReactionFlags.None;
        for (int i = 0; i < flags.Length; i++)
        {
            string normalized = NormalizeName(flags[i]);
            if (!Enum.TryParse(normalized, ignoreCase: true, out ReactionFlags flag))
            {
                throw new ArgumentException($"未知 reaction flag：{flags[i]}。");
            }

            parsed |= flag;
        }

        return parsed;
    }

    private static MaterialTag ParseMaterialTag(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{path} tag 不能为空。");
        }

        string normalized = NormalizeName(value);
        return Enum.TryParse(normalized, ignoreCase: true, out MaterialTag tag)
            ? tag
            : throw new ArgumentException($"{path} 包含未知 tag：{value}。");
    }

    private static TEnum ParseEnum<TEnum>(string? value, string path)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{path} 不能为空。");
        }

        string normalized = NormalizeName(value);
        return Enum.TryParse(normalized, ignoreCase: true, out TEnum parsed)
            ? parsed
            : throw new ArgumentException($"{path} 包含未知 {typeof(TEnum).Name}：{value}。");
    }

    private static ushort ResolveOptionalMaterial(string? name, Dictionary<string, ushort> nameToId)
    {
        return string.IsNullOrWhiteSpace(name) ? (ushort)0 : ResolveRequiredMaterial(name, nameToId, name);
    }

    private static ushort ResolveRequiredMaterial(string? name, Dictionary<string, ushort> nameToId, string path)
    {
        string materialName = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException($"{path} 材质引用不能为空。")
            : name.Trim();

        return nameToId.TryGetValue(materialName, out ushort id)
            ? id
            : throw new ArgumentException($"{path} 引用了未知材质：{name}。");
    }

    private static string RequiredName(string? name, int index)
    {
        return string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException($"materials[{index}].name 不能为空。")
            : name.Trim();
    }

    private static AudioCueSet BuildAudioCues(AudioCueSetJson? json)
    {
        return json is null
            ? default
            : new AudioCueSet
            {
                ImpactCue = json.Impact,
                FireCue = json.Fire,
                SplashCue = json.Splash,
                ExplosionCue = json.Explosion,
                ShatterCue = json.Shatter,
                AmbientCue = json.Ambient,
            };
    }

    private static string NormalizeName(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int write = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '_' || c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            buffer[write++] = c;
        }

        return new string(buffer[..write]);
    }

    private readonly record struct MaterialReference(string? Name, MaterialTag? Tag);
}

/// <summary>
/// materials.json 根对象。
/// </summary>
public sealed class MaterialDocumentJson
{
    /// <summary>
    /// 材质定义数组；数组顺序决定初始 runtime id。
    /// </summary>
    public MaterialJson[]? Materials { get; init; }

    /// <summary>
    /// 输出端 tag 的代表材质映射。
    /// </summary>
    public TagRepresentativeJson[]? TagRepresentatives { get; init; }
}

/// <summary>
/// 单个材质 JSON DTO。
/// </summary>
public sealed class MaterialJson
{
    /// <summary>稳定材质 name。</summary>
    public string? Name { get; init; }

    /// <summary>CellType 名称。</summary>
    public string? Type { get; init; }

    /// <summary>密度。</summary>
    public byte Density { get; init; }

    /// <summary>横向扩散距离。</summary>
    public byte Dispersion { get; init; }

    /// <summary>液体静态标记。</summary>
    public bool LiquidStatic { get; init; }

    /// <summary>液体按粉末式下落标记。</summary>
    public bool LiquidSand { get; init; }

    /// <summary>接触点燃概率权重。</summary>
    public byte Flammability { get; init; }

    /// <summary>自燃温度阈值。</summary>
    public ushort AutoIgnitionTemp { get; init; }

    /// <summary>燃烧耐久。</summary>
    public int FireHp { get; init; }

    /// <summary>燃烧注热基准。</summary>
    public byte TemperatureOfFire { get; init; }

    /// <summary>产烟倾向。</summary>
    public byte GeneratesSmoke { get; init; }

    /// <summary>熔化阈值。</summary>
    public float? MeltPoint { get; init; }

    /// <summary>熔化目标材质 name。</summary>
    public string? MeltTarget { get; init; }

    /// <summary>凝固阈值。</summary>
    public float? FreezePoint { get; init; }

    /// <summary>凝固目标材质 name。</summary>
    public string? FreezeTarget { get; init; }

    /// <summary>沸腾阈值。</summary>
    public float? BoilPoint { get; init; }

    /// <summary>沸腾目标材质 name。</summary>
    public string? BoilTarget { get; init; }

    /// <summary>每帧热传导概率权重。</summary>
    public byte HeatConduct { get; init; }

    /// <summary>热容量。</summary>
    public float? HeatCapacity { get; init; }

    /// <summary>默认 lifetime。</summary>
    public ushort DefaultLifetime { get; init; }

    /// <summary>耐久。</summary>
    public byte Durability { get; init; }

    /// <summary>纹理索引。</summary>
    public int? TextureId { get; init; }

    /// <summary>BGRA8 基色。</summary>
    [JsonPropertyName("baseColor")]
    public uint BaseColor { get; init; }

    /// <summary>颜色噪声幅度。</summary>
    public byte ColorNoise { get; init; }

    /// <summary>tag / property 名称数组。</summary>
    public string[]? Tags { get; init; }

    /// <summary>音频 cue 配置。</summary>
    public AudioCueSetJson? AudioCues { get; init; }
}

/// <summary>
/// tag 输出代表材质 DTO。
/// </summary>
public sealed class TagRepresentativeJson
{
    /// <summary>tag 名。</summary>
    public string? Tag { get; init; }

    /// <summary>代表材质 name。</summary>
    public string? Material { get; init; }
}

/// <summary>
/// 材质音频 cue DTO。
/// </summary>
public sealed class AudioCueSetJson
{
    /// <summary>撞击 cue。</summary>
    public int Impact { get; init; }

    /// <summary>燃烧 cue。</summary>
    public int Fire { get; init; }

    /// <summary>飞溅 cue。</summary>
    public int Splash { get; init; }

    /// <summary>爆炸 cue。</summary>
    public int Explosion { get; init; }

    /// <summary>破碎 cue。</summary>
    public int Shatter { get; init; }

    /// <summary>环境 cue。</summary>
    public int Ambient { get; init; }
}

/// <summary>
/// reactions.json 根对象。
/// </summary>
public sealed class ReactionDocumentJson
{
    /// <summary>
    /// 反应定义数组。
    /// </summary>
    public ReactionJson[]? Reactions { get; init; }
}

/// <summary>
/// 单条 reaction JSON DTO。
/// </summary>
public sealed class ReactionJson
{
    /// <summary>输入 A，支持具体材质 name 或 [tag]。</summary>
    public string? InputA { get; init; }

    /// <summary>输入 B，支持具体材质 name 或 [tag]。</summary>
    public string? InputB { get; init; }

    /// <summary>输出 A，支持具体材质 name 或 [tag] representative。</summary>
    public string? OutputA { get; init; }

    /// <summary>输出 B，支持具体材质 name 或 [tag] representative。</summary>
    public string? OutputB { get; init; }

    /// <summary>概率，范围 0-100。</summary>
    public int Probability { get; init; } = 100;

    /// <summary>ReactionFlags 名称数组。</summary>
    public string[]? Flags { get; init; }
}

/// <summary>
/// System.Text.Json source-generation 上下文。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(MaterialDocumentJson))]
[JsonSerializable(typeof(MaterialJson[]))]
[JsonSerializable(typeof(ReactionDocumentJson))]
[JsonSerializable(typeof(ReactionJson[]))]
public sealed partial class MaterialContentJsonContext : JsonSerializerContext
{
}
