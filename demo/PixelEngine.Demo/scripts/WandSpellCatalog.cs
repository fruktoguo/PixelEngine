using System.Text.Json;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 的 Wand / Spell 严格数据目录。参考文件只记录结构身份与统计，正式玩法使用原创 spell 定义。
/// </summary>
internal sealed class WandSpellCatalog
{
    internal const int CurrentSchemaVersion = 1;

    private static readonly string[] RootProperties =
        ["schemaVersion", "reference", "limits", "spells", "wands"];
    private static readonly string[] ReferenceProperties =
        ["buildId", "versionHash", "sourceFiles", "actionInventory"];
    private static readonly string[] SourceFileProperties = ["path", "sha256"];
    private static readonly string[] InventoryProperties =
        ["total", "projectile", "staticProjectile", "modifier", "drawMany", "material", "other", "utility", "passive"];
    private static readonly string[] LimitProperties =
        ["maxDrawsPerCast", "maxRecursionDepth", "maxProjectilesPerCast", "maxWandCapacity", "maxAlwaysCast"];
    private static readonly string[] SpellProperties =
        ["id", "displayName", "description", "category", "manaCost", "maxUses", "castDelaySeconds", "rechargeSeconds", "effect"];
    private static readonly string[] EffectProperties =
    [
        "kind", "projectile", "trigger", "triggerDraw", "triggerDelaySeconds", "drawCount",
        "damage", "terrainDamage", "speed", "lifetimeSeconds", "gravity", "bounces", "explosionRadius",
        "spreadDegrees", "damageAdd", "terrainDamageAdd", "speedMultiplier", "lifetimeMultiplier",
        "gravityAdd", "bouncesAdd", "material", "materialRadius", "lightRadius", "lightIntensity",
        "manaChargeMultiplier", "repeatCount"
    ];
    private static readonly string[] WandProperties =
    [
        "id", "displayName", "shuffle", "spellsPerCast", "castDelaySeconds", "rechargeSeconds",
        "manaMax", "manaChargePerSecond", "capacity", "spreadDegrees", "speedMultiplier", "alwaysCast", "deck"
    ];

    internal int SchemaVersion { get; private init; }

    internal WandReferenceDefinition Reference { get; private init; } = null!;

    internal WandEvaluationLimits Limits { get; private init; } = null!;

    internal WandSpellDefinition[] Spells { get; private init; } = [];

    internal WandDefinition[] Wands { get; private init; } = [];

    internal static WandSpellCatalog Load(IConfigApi config, string path = "wand-spells.json")
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(config.ReadText(path));
    }

    internal static WandSpellCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        JsonElement root = RequireObject(document.RootElement, "root");
        RequireOnlyProperties(root, RootProperties, "root");

        WandSpellCatalog catalog = new()
        {
            SchemaVersion = ReadRequiredInt32(root, "schemaVersion", "root"),
            Reference = ParseReference(ReadRequiredObject(root, "reference", "root")),
            Limits = ParseLimits(ReadRequiredObject(root, "limits", "root")),
            Spells = ParseSpells(ReadRequiredArray(root, "spells", "root")),
            Wands = ParseWands(ReadRequiredArray(root, "wands", "root")),
        };
        catalog.ValidateAndCompile();
        return catalog;
    }

    internal int FindSpellIndex(string spellId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellId);
        for (int i = 0; i < Spells.Length; i++)
        {
            if (string.Equals(Spells[i].Id, spellId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw Invalid($"未知 spell id：{spellId}。");
    }

    internal int FindWandIndex(string wandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wandId);
        for (int i = 0; i < Wands.Length; i++)
        {
            if (string.Equals(Wands[i].Id, wandId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw Invalid($"未知 wand id：{wandId}。");
    }

    private void ValidateAndCompile()
    {
        Require(SchemaVersion == CurrentSchemaVersion, $"schemaVersion 必须为 {CurrentSchemaVersion}。");
        Reference.Validate();
        Limits.Validate();
        Require(Spells.Length is >= 9 and <= 256, "spells 数量必须位于 [9,256]。");
        Require(Wands.Length == 4, "Demo 必须定义四把可装备 Wand。");

        HashSet<string> spellIds = new(StringComparer.Ordinal);
        for (int i = 0; i < Spells.Length; i++)
        {
            WandSpellDefinition spell = Spells[i] ?? throw Invalid($"spells[{i}] 不能为空。");
            spell.Validate($"spells[{i}]");
            Require(spellIds.Add(spell.Id), $"spell id 重复：{spell.Id}。");
            spell.Index = i;
        }

        HashSet<string> wandIds = new(StringComparer.Ordinal);
        bool[] referencedSpells = new bool[Spells.Length];
        for (int i = 0; i < Wands.Length; i++)
        {
            WandDefinition wand = Wands[i] ?? throw Invalid($"wands[{i}] 不能为空。");
            wand.Validate(Limits, $"wands[{i}]");
            Require(wandIds.Add(wand.Id), $"wand id 重复：{wand.Id}。");
            wand.Index = i;
            wand.DeckSpellIndices = CompileSpellReferences(wand.Deck, referencedSpells);
            wand.AlwaysCastSpellIndices = CompileSpellReferences(wand.AlwaysCast, referencedSpells);
        }

        for (int i = 0; i < referencedSpells.Length; i++)
        {
            Require(referencedSpells[i], $"spell {Spells[i].Id} 未被任何 Wand 使用。");
        }

        foreach (WandSpellCategory category in Enum.GetValues<WandSpellCategory>())
        {
            bool found = false;
            for (int i = 0; i < Spells.Length; i++)
            {
                found |= Spells[i].Category == category;
            }

            Require(found, $"目录缺少 {category} 类 spell。");
        }
    }

    private int[] CompileSpellReferences(string[] references, bool[] referencedSpells)
    {
        int[] indices = new int[references.Length];
        for (int i = 0; i < references.Length; i++)
        {
            int spellIndex = FindSpellIndex(references[i]);
            indices[i] = spellIndex;
            referencedSpells[spellIndex] = true;
        }

        return indices;
    }

    private static WandReferenceDefinition ParseReference(JsonElement element)
    {
        RequireOnlyProperties(element, ReferenceProperties, "reference");
        JsonElement sourceFilesElement = ReadRequiredArray(element, "sourceFiles", "reference");
        WandReferenceFile[] sourceFiles = new WandReferenceFile[sourceFilesElement.GetArrayLength()];
        int sourceIndex = 0;
        foreach (JsonElement sourceElement in sourceFilesElement.EnumerateArray())
        {
            JsonElement source = RequireObject(sourceElement, $"reference.sourceFiles[{sourceIndex}]");
            RequireOnlyProperties(source, SourceFileProperties, $"reference.sourceFiles[{sourceIndex}]");
            sourceFiles[sourceIndex] = new WandReferenceFile(
                ReadRequiredString(source, "path", $"reference.sourceFiles[{sourceIndex}]"),
                ReadRequiredString(source, "sha256", $"reference.sourceFiles[{sourceIndex}]"));
            sourceIndex++;
        }

        JsonElement inventory = ReadRequiredObject(element, "actionInventory", "reference");
        RequireOnlyProperties(inventory, InventoryProperties, "reference.actionInventory");
        return new WandReferenceDefinition
        {
            BuildId = ReadRequiredString(element, "buildId", "reference"),
            VersionHash = ReadRequiredString(element, "versionHash", "reference"),
            SourceFiles = sourceFiles,
            ActionInventory = new WandReferenceActionInventory(
                ReadRequiredInt32(inventory, "total", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "projectile", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "staticProjectile", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "modifier", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "drawMany", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "material", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "other", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "utility", "reference.actionInventory"),
                ReadRequiredInt32(inventory, "passive", "reference.actionInventory")),
        };
    }

    private static WandEvaluationLimits ParseLimits(JsonElement element)
    {
        RequireOnlyProperties(element, LimitProperties, "limits");
        return new WandEvaluationLimits
        {
            MaxDrawsPerCast = ReadRequiredInt32(element, "maxDrawsPerCast", "limits"),
            MaxRecursionDepth = ReadRequiredInt32(element, "maxRecursionDepth", "limits"),
            MaxProjectilesPerCast = ReadRequiredInt32(element, "maxProjectilesPerCast", "limits"),
            MaxWandCapacity = ReadRequiredInt32(element, "maxWandCapacity", "limits"),
            MaxAlwaysCast = ReadRequiredInt32(element, "maxAlwaysCast", "limits"),
        };
    }

    private static WandSpellDefinition[] ParseSpells(JsonElement array)
    {
        WandSpellDefinition[] spells = new WandSpellDefinition[array.GetArrayLength()];
        int index = 0;
        foreach (JsonElement itemElement in array.EnumerateArray())
        {
            string label = $"spells[{index}]";
            JsonElement item = RequireObject(itemElement, label);
            RequireOnlyProperties(item, SpellProperties, label);
            spells[index] = new WandSpellDefinition
            {
                Id = ReadRequiredString(item, "id", label),
                DisplayName = ReadRequiredString(item, "displayName", label),
                Description = ReadRequiredString(item, "description", label),
                Category = ReadRequiredEnum<WandSpellCategory>(item, "category", label),
                ManaCost = ReadRequiredSingle(item, "manaCost", label),
                MaxUses = ReadRequiredInt32(item, "maxUses", label),
                CastDelaySeconds = ReadRequiredSingle(item, "castDelaySeconds", label),
                RechargeSeconds = ReadRequiredSingle(item, "rechargeSeconds", label),
                Effect = ParseEffect(ReadRequiredObject(item, "effect", label), $"{label}.effect"),
            };
            index++;
        }

        return spells;
    }

    private static WandSpellEffectDefinition ParseEffect(JsonElement element, string label)
    {
        RequireOnlyProperties(element, EffectProperties, label);
        return new WandSpellEffectDefinition
        {
            Kind = ReadRequiredEnum<WandSpellEffectKind>(element, "kind", label),
            Projectile = ReadOptionalEnum(element, "projectile", WandProjectileKind.None, label),
            Trigger = ReadOptionalEnum(element, "trigger", WandTriggerKind.None, label),
            TriggerDraw = ReadOptionalInt32(element, "triggerDraw", 0, label),
            TriggerDelaySeconds = ReadOptionalSingle(element, "triggerDelaySeconds", 0f, label),
            DrawCount = ReadOptionalInt32(element, "drawCount", 0, label),
            Damage = ReadOptionalSingle(element, "damage", 0f, label),
            TerrainDamage = ReadOptionalSingle(element, "terrainDamage", 0f, label),
            Speed = ReadOptionalSingle(element, "speed", 0f, label),
            LifetimeSeconds = ReadOptionalSingle(element, "lifetimeSeconds", 0f, label),
            Gravity = ReadOptionalSingle(element, "gravity", 0f, label),
            Bounces = ReadOptionalInt32(element, "bounces", 0, label),
            ExplosionRadius = ReadOptionalInt32(element, "explosionRadius", 0, label),
            SpreadDegrees = ReadOptionalSingle(element, "spreadDegrees", 0f, label),
            DamageAdd = ReadOptionalSingle(element, "damageAdd", 0f, label),
            TerrainDamageAdd = ReadOptionalSingle(element, "terrainDamageAdd", 0f, label),
            SpeedMultiplier = ReadOptionalSingle(element, "speedMultiplier", 1f, label),
            LifetimeMultiplier = ReadOptionalSingle(element, "lifetimeMultiplier", 1f, label),
            GravityAdd = ReadOptionalSingle(element, "gravityAdd", 0f, label),
            BouncesAdd = ReadOptionalInt32(element, "bouncesAdd", 0, label),
            Material = ReadOptionalString(element, "material", string.Empty, label),
            MaterialRadius = ReadOptionalInt32(element, "materialRadius", 0, label),
            LightRadius = ReadOptionalSingle(element, "lightRadius", 0f, label),
            LightIntensity = ReadOptionalSingle(element, "lightIntensity", 0f, label),
            ManaChargeMultiplier = ReadOptionalSingle(element, "manaChargeMultiplier", 1f, label),
            RepeatCount = ReadOptionalInt32(element, "repeatCount", 0, label),
        };
    }

    private static WandDefinition[] ParseWands(JsonElement array)
    {
        WandDefinition[] wands = new WandDefinition[array.GetArrayLength()];
        int index = 0;
        foreach (JsonElement itemElement in array.EnumerateArray())
        {
            string label = $"wands[{index}]";
            JsonElement item = RequireObject(itemElement, label);
            RequireOnlyProperties(item, WandProperties, label);
            wands[index] = new WandDefinition
            {
                Id = ReadRequiredString(item, "id", label),
                DisplayName = ReadRequiredString(item, "displayName", label),
                Shuffle = ReadRequiredBoolean(item, "shuffle", label),
                SpellsPerCast = ReadRequiredInt32(item, "spellsPerCast", label),
                CastDelaySeconds = ReadRequiredSingle(item, "castDelaySeconds", label),
                RechargeSeconds = ReadRequiredSingle(item, "rechargeSeconds", label),
                ManaMax = ReadRequiredSingle(item, "manaMax", label),
                ManaChargePerSecond = ReadRequiredSingle(item, "manaChargePerSecond", label),
                Capacity = ReadRequiredInt32(item, "capacity", label),
                SpreadDegrees = ReadRequiredSingle(item, "spreadDegrees", label),
                SpeedMultiplier = ReadRequiredSingle(item, "speedMultiplier", label),
                AlwaysCast = ReadStringArray(ReadRequiredArray(item, "alwaysCast", label), $"{label}.alwaysCast"),
                Deck = ReadStringArray(ReadRequiredArray(item, "deck", label), $"{label}.deck"),
            };
            index++;
        }

        return wands;
    }

    private static string[] ReadStringArray(JsonElement array, string label)
    {
        string[] values = new string[array.GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"{label}[{index}] 必须是字符串。");
            }

            values[index++] = item.GetString() ?? string.Empty;
        }

        return values;
    }

    private static JsonElement ReadRequiredObject(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value)
            ? RequireObject(value, $"{label}.{name}")
            : throw Invalid($"{label}.{name} 缺失。");
    }

    private static JsonElement ReadRequiredArray(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw Invalid($"{label}.{name} 必须是数组。");
    }

    private static JsonElement RequireObject(JsonElement value, string label)
    {
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw Invalid($"{label} 必须是对象。");
    }

    private static string ReadRequiredString(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw Invalid($"{label}.{name} 必须是字符串。");
    }

    private static string ReadOptionalString(JsonElement parent, string name, string fallback, string label)
    {
        return !parent.TryGetProperty(name, out JsonElement value)
            ? fallback
            : value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : throw Invalid($"{label}.{name} 必须是字符串。");
    }

    private static int ReadRequiredInt32(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : throw Invalid($"{label}.{name} 必须是整数。");
    }

    private static int ReadOptionalInt32(JsonElement parent, string name, int fallback, string label)
    {
        return !parent.TryGetProperty(name, out JsonElement value)
            ? fallback
            : value.TryGetInt32(out int result)
                ? result
                : throw Invalid($"{label}.{name} 必须是整数。");
    }

    private static float ReadRequiredSingle(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value) &&
            value.TryGetSingle(out float result) &&
            float.IsFinite(result)
            ? result
            : throw Invalid($"{label}.{name} 必须是有限数值。");
    }

    private static float ReadOptionalSingle(JsonElement parent, string name, float fallback, string label)
    {
        return !parent.TryGetProperty(name, out JsonElement value)
            ? fallback
            : value.TryGetSingle(out float result) && float.IsFinite(result)
                ? result
                : throw Invalid($"{label}.{name} 必须是有限数值。");
    }

    private static bool ReadRequiredBoolean(JsonElement parent, string name, string label)
    {
        return parent.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Invalid($"{label}.{name} 必须是布尔值。");
    }

    private static TEnum ReadRequiredEnum<TEnum>(JsonElement parent, string name, string label)
        where TEnum : struct, Enum
    {
        string value = ReadRequiredString(parent, name, label);
        return Enum.TryParse(value, ignoreCase: true, out TEnum result) && Enum.IsDefined(result)
            ? result
            : throw Invalid($"{label}.{name} 枚举值无效：{value}。");
    }

    private static TEnum ReadOptionalEnum<TEnum>(JsonElement parent, string name, TEnum fallback, string label)
        where TEnum : struct, Enum
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{label}.{name} 必须是字符串。");
        }

        string text = value.GetString() ?? string.Empty;
        return Enum.TryParse(text, ignoreCase: true, out TEnum result) && Enum.IsDefined(result)
            ? result
            : throw Invalid($"{label}.{name} 枚举值无效：{text}。");
    }

    private static void RequireOnlyProperties(JsonElement element, string[] allowed, string label)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            bool found = false;
            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(property.Name, allowed[i], StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw Invalid($"{label} 含未知字段 {property.Name}。");
            }
        }
    }

    internal static bool IsStableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsSha256(string value)
    {
        return IsLowerHex(value, 64);
    }

    internal static bool IsCommitHash(string value)
    {
        return IsLowerHex(value, 40);
    }

    internal static bool IsBuildId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerHex(string value, int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Invalid(message);
        }
    }

    internal static InvalidDataException Invalid(string message)
    {
        return new InvalidDataException($"wand-spells.json 配置无效：{message}");
    }
}

internal sealed class WandReferenceDefinition
{
    internal string BuildId { get; init; } = string.Empty;

    internal string VersionHash { get; init; } = string.Empty;

    internal WandReferenceFile[] SourceFiles { get; init; } = [];

    internal WandReferenceActionInventory ActionInventory { get; init; }

    internal void Validate()
    {
        WandSpellCatalog.Require(WandSpellCatalog.IsBuildId(BuildId), "reference.buildId 必须为十进制 build id。");
        WandSpellCatalog.Require(WandSpellCatalog.IsCommitHash(VersionHash), "reference.versionHash 必须为 40 位 lowercase commit hash。");
        WandSpellCatalog.Require(SourceFiles.Length is >= 3 and <= 16, "reference.sourceFiles 数量必须位于 [3,16]。");
        HashSet<string> paths = new(StringComparer.Ordinal);
        for (int i = 0; i < SourceFiles.Length; i++)
        {
            WandReferenceFile source = SourceFiles[i];
            WandSpellCatalog.Require(
                source.Path.StartsWith("data/scripts/gun/", StringComparison.Ordinal) && source.Path.EndsWith(".lua", StringComparison.Ordinal),
                $"reference.sourceFiles[{i}].path 必须位于 data/scripts/gun/。");
            WandSpellCatalog.Require(paths.Add(source.Path), $"reference source path 重复：{source.Path}。");
            WandSpellCatalog.Require(WandSpellCatalog.IsSha256(source.Sha256), $"reference.sourceFiles[{i}].sha256 无效。");
        }

        ActionInventory.Validate();
    }
}

internal readonly record struct WandReferenceFile(string Path, string Sha256);

internal readonly record struct WandReferenceActionInventory(
    int Total,
    int Projectile,
    int StaticProjectile,
    int Modifier,
    int DrawMany,
    int Material,
    int Other,
    int Utility,
    int Passive)
{
    internal void Validate()
    {
        int sum = checked(Projectile + StaticProjectile + Modifier + DrawMany + Material + Other + Utility + Passive);
        WandSpellCatalog.Require(Total > 0 && Total == sum, "reference.actionInventory.total 与分类合计不一致。");
        WandSpellCatalog.Require(
            Projectile > 0 && StaticProjectile > 0 && Modifier > 0 && DrawMany > 0 &&
            Material > 0 && Other > 0 && Utility > 0 && Passive > 0,
            "reference.actionInventory 每个类别都必须为正数。");
    }
}

internal sealed class WandEvaluationLimits
{
    internal int MaxDrawsPerCast { get; init; }

    internal int MaxRecursionDepth { get; init; }

    internal int MaxProjectilesPerCast { get; init; }

    internal int MaxWandCapacity { get; init; }

    internal int MaxAlwaysCast { get; init; }

    internal void Validate()
    {
        WandSpellCatalog.Require(MaxDrawsPerCast is >= 16 and <= 256, "limits.maxDrawsPerCast 必须位于 [16,256]。");
        WandSpellCatalog.Require(MaxRecursionDepth is >= 2 and <= 16, "limits.maxRecursionDepth 必须位于 [2,16]。");
        WandSpellCatalog.Require(MaxProjectilesPerCast is >= 8 and <= 128, "limits.maxProjectilesPerCast 必须位于 [8,128]。");
        WandSpellCatalog.Require(MaxWandCapacity is >= 8 and <= 64, "limits.maxWandCapacity 必须位于 [8,64]。");
        WandSpellCatalog.Require(MaxAlwaysCast is >= 1 and <= 8, "limits.maxAlwaysCast 必须位于 [1,8]。");
    }
}

internal sealed class WandSpellDefinition
{
    internal int Index { get; set; }

    internal string Id { get; init; } = string.Empty;

    internal string DisplayName { get; init; } = string.Empty;

    internal string Description { get; init; } = string.Empty;

    internal WandSpellCategory Category { get; init; }

    internal float ManaCost { get; init; }

    internal int MaxUses { get; init; }

    internal float CastDelaySeconds { get; init; }

    internal float RechargeSeconds { get; init; }

    internal WandSpellEffectDefinition Effect { get; init; } = null!;

    internal void Validate(string label)
    {
        WandSpellCatalog.Require(WandSpellCatalog.IsStableId(Id), $"{label}.id 不是稳定键。");
        WandSpellCatalog.Require(!string.IsNullOrWhiteSpace(DisplayName), $"{label}.displayName 不能为空。");
        WandSpellCatalog.Require(!string.IsNullOrWhiteSpace(Description), $"{label}.description 不能为空。");
        WandSpellCatalog.Require(Enum.IsDefined(Category), $"{label}.category 无效。");
        WandSpellCatalog.Require(float.IsFinite(ManaCost) && ManaCost is >= -100f and <= 1000f, $"{label}.manaCost 超界。");
        WandSpellCatalog.Require(MaxUses is -1 or (>= 1 and <= 999), $"{label}.maxUses 必须为 -1 或 [1,999]。");
        WandSpellCatalog.Require(float.IsFinite(CastDelaySeconds) && CastDelaySeconds is >= -2f and <= 10f, $"{label}.castDelaySeconds 超界。");
        WandSpellCatalog.Require(float.IsFinite(RechargeSeconds) && RechargeSeconds is >= -2f and <= 10f, $"{label}.rechargeSeconds 超界。");
        WandSpellEffectDefinition effect = Effect ?? throw WandSpellCatalog.Invalid($"{label}.effect 不能为空。");

        effect.Validate(Category, label);
        if (Category == WandSpellCategory.LimitedUse)
        {
            WandSpellCatalog.Require(MaxUses > 0, $"{label} limited-use spell 必须有有限 maxUses。");
        }
    }
}

internal sealed class WandSpellEffectDefinition
{
    internal WandSpellEffectKind Kind { get; init; }

    internal WandProjectileKind Projectile { get; init; }

    internal WandTriggerKind Trigger { get; init; }

    internal int TriggerDraw { get; init; }

    internal float TriggerDelaySeconds { get; init; }

    internal int DrawCount { get; init; }

    internal float Damage { get; init; }

    internal float TerrainDamage { get; init; }

    internal float Speed { get; init; }

    internal float LifetimeSeconds { get; init; }

    internal float Gravity { get; init; }

    internal int Bounces { get; init; }

    internal int ExplosionRadius { get; init; }

    internal float SpreadDegrees { get; init; }

    internal float DamageAdd { get; init; }

    internal float TerrainDamageAdd { get; init; }

    internal float SpeedMultiplier { get; init; } = 1f;

    internal float LifetimeMultiplier { get; init; } = 1f;

    internal float GravityAdd { get; init; }

    internal int BouncesAdd { get; init; }

    internal string Material { get; init; } = string.Empty;

    internal int MaterialRadius { get; init; }

    internal float LightRadius { get; init; }

    internal float LightIntensity { get; init; }

    internal float ManaChargeMultiplier { get; init; } = 1f;

    internal int RepeatCount { get; init; }

    internal void Validate(WandSpellCategory category, string spellLabel)
    {
        string label = $"{spellLabel}.effect";
        WandSpellCatalog.Require(Enum.IsDefined(Kind), $"{label}.kind 无效。");
        WandSpellCatalog.Require(Enum.IsDefined(Projectile), $"{label}.projectile 无效。");
        WandSpellCatalog.Require(Enum.IsDefined(Trigger), $"{label}.trigger 无效。");
        RequireFiniteRanges(label);

        switch (category)
        {
            case WandSpellCategory.Projectile:
            case WandSpellCategory.LimitedUse:
                RequireLaunch(label, allowTrigger: false);
                break;
            case WandSpellCategory.Trigger:
                RequireLaunch(label, allowTrigger: true);
                WandSpellCatalog.Require(Trigger != WandTriggerKind.None && TriggerDraw is >= 1 and <= 8, $"{label} trigger 配置无效。");
                if (Trigger == WandTriggerKind.Timer)
                {
                    WandSpellCatalog.Require(TriggerDelaySeconds is > 0f and <= 8f, $"{label}.triggerDelaySeconds 无效。");
                }

                break;
            case WandSpellCategory.Modifier:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Modifier, $"{label}.kind 必须为 modifier。");
                bool modifies = DamageAdd != 0f || TerrainDamageAdd != 0f || SpeedMultiplier != 1f ||
                    LifetimeMultiplier != 1f || GravityAdd != 0f || BouncesAdd != 0 || SpreadDegrees != 0f;
                WandSpellCatalog.Require(modifies, $"{label} modifier 没有任何效果。");
                break;
            case WandSpellCategory.Draw:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Draw && DrawCount is >= 2 and <= 8, $"{label} draw 配置无效。");
                break;
            case WandSpellCategory.Material:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Material, $"{label}.kind 必须为 material。");
                WandSpellCatalog.Require(Projectile == WandProjectileKind.Material && Speed > 0f && LifetimeSeconds > 0f, $"{label} material projectile 无效。");
                WandSpellCatalog.Require(WandSpellCatalog.IsStableId(Material) && MaterialRadius is >= 1 and <= 32, $"{label} material 配置无效。");
                break;
            case WandSpellCategory.Utility:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Launch, $"{label}.kind 必须为 launch。");
                WandSpellCatalog.Require(Projectile is WandProjectileKind.Dig or WandProjectileKind.Teleport, $"{label}.projectile 必须为 dig 或 teleport。");
                WandSpellCatalog.Require(Speed > 0f && LifetimeSeconds > 0f, $"{label} utility projectile 无效。");
                break;
            case WandSpellCategory.Passive:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Passive, $"{label}.kind 必须为 passive。");
                WandSpellCatalog.Require(ManaChargeMultiplier >= 1f || LightRadius > 0f, $"{label} passive 没有任何效果。");
                break;
            case WandSpellCategory.Special:
                WandSpellCatalog.Require(Kind == WandSpellEffectKind.Repeat && RepeatCount is >= 1 and <= 4, $"{label} repeat 配置无效。");
                break;
            default:
                throw WandSpellCatalog.Invalid($"{spellLabel}.category 无效。");
        }
    }

    private void RequireLaunch(string label, bool allowTrigger)
    {
        WandSpellCatalog.Require(Kind == WandSpellEffectKind.Launch, $"{label}.kind 必须为 launch。");
        WandSpellCatalog.Require(
            Projectile is WandProjectileKind.Bolt or WandProjectileKind.Orb or WandProjectileKind.Grenade,
            $"{label}.projectile 必须为 bolt、orb 或 grenade。");
        WandSpellCatalog.Require(Speed > 0f && LifetimeSeconds > 0f, $"{label} projectile 速度与寿命必须为正。");
        WandSpellCatalog.Require(Damage > 0f || TerrainDamage > 0f || ExplosionRadius > 0, $"{label} projectile 没有命中效果。");
        WandSpellCatalog.Require(allowTrigger || Trigger == WandTriggerKind.None, $"{label} 非 trigger spell 不得声明 trigger。");
    }

    private void RequireFiniteRanges(string label)
    {
        Span<float> values =
        [
            TriggerDelaySeconds, Damage, TerrainDamage, Speed, LifetimeSeconds, Gravity, SpreadDegrees,
            DamageAdd, TerrainDamageAdd, SpeedMultiplier, LifetimeMultiplier, GravityAdd,
            LightRadius, LightIntensity, ManaChargeMultiplier
        ];
        for (int i = 0; i < values.Length; i++)
        {
            WandSpellCatalog.Require(float.IsFinite(values[i]), $"{label} 含非有限数值。");
        }

        WandSpellCatalog.Require(TriggerDraw is >= 0 and <= 8, $"{label}.triggerDraw 超界。");
        WandSpellCatalog.Require(DrawCount is >= 0 and <= 8, $"{label}.drawCount 超界。");
        WandSpellCatalog.Require(Damage is >= 0f and <= 10000f, $"{label}.damage 超界。");
        WandSpellCatalog.Require(TerrainDamage is >= 0f and <= 50000f, $"{label}.terrainDamage 超界。");
        WandSpellCatalog.Require(Speed is >= 0f and <= 2000f, $"{label}.speed 超界。");
        WandSpellCatalog.Require(LifetimeSeconds is >= 0f and <= 30f, $"{label}.lifetimeSeconds 超界。");
        WandSpellCatalog.Require(Gravity is >= -2000f and <= 2000f, $"{label}.gravity 超界。");
        WandSpellCatalog.Require(Bounces is >= 0 and <= 32, $"{label}.bounces 超界。");
        WandSpellCatalog.Require(ExplosionRadius is >= 0 and <= 128, $"{label}.explosionRadius 超界。");
        WandSpellCatalog.Require(SpreadDegrees is >= -180f and <= 180f, $"{label}.spreadDegrees 超界。");
        WandSpellCatalog.Require(DamageAdd is >= -1000f and <= 1000f, $"{label}.damageAdd 超界。");
        WandSpellCatalog.Require(TerrainDamageAdd is >= -5000f and <= 5000f, $"{label}.terrainDamageAdd 超界。");
        WandSpellCatalog.Require(SpeedMultiplier is > 0f and <= 8f, $"{label}.speedMultiplier 超界。");
        WandSpellCatalog.Require(LifetimeMultiplier is > 0f and <= 8f, $"{label}.lifetimeMultiplier 超界。");
        WandSpellCatalog.Require(BouncesAdd is >= -32 and <= 32, $"{label}.bouncesAdd 超界。");
        WandSpellCatalog.Require(MaterialRadius is >= 0 and <= 32, $"{label}.materialRadius 超界。");
        WandSpellCatalog.Require(LightRadius is >= 0f and <= 512f, $"{label}.lightRadius 超界。");
        WandSpellCatalog.Require(LightIntensity is >= 0f and <= 4f, $"{label}.lightIntensity 超界。");
        WandSpellCatalog.Require(ManaChargeMultiplier is > 0f and <= 8f, $"{label}.manaChargeMultiplier 超界。");
    }
}

internal sealed class WandDefinition
{
    internal int Index { get; set; }

    internal string Id { get; init; } = string.Empty;

    internal string DisplayName { get; init; } = string.Empty;

    internal bool Shuffle { get; init; }

    internal int SpellsPerCast { get; init; }

    internal float CastDelaySeconds { get; init; }

    internal float RechargeSeconds { get; init; }

    internal float ManaMax { get; init; }

    internal float ManaChargePerSecond { get; init; }

    internal int Capacity { get; init; }

    internal float SpreadDegrees { get; init; }

    internal float SpeedMultiplier { get; init; }

    internal string[] AlwaysCast { get; init; } = [];

    internal string[] Deck { get; init; } = [];

    internal int[] AlwaysCastSpellIndices { get; set; } = [];

    internal int[] DeckSpellIndices { get; set; } = [];

    internal void Validate(WandEvaluationLimits limits, string label)
    {
        WandSpellCatalog.Require(WandSpellCatalog.IsStableId(Id), $"{label}.id 不是稳定键。");
        WandSpellCatalog.Require(!string.IsNullOrWhiteSpace(DisplayName), $"{label}.displayName 不能为空。");
        WandSpellCatalog.Require(SpellsPerCast is >= 1 and <= 8, $"{label}.spellsPerCast 超界。");
        WandSpellCatalog.Require(float.IsFinite(CastDelaySeconds) && CastDelaySeconds is >= 0.01f and <= 10f, $"{label}.castDelaySeconds 超界。");
        WandSpellCatalog.Require(float.IsFinite(RechargeSeconds) && RechargeSeconds is >= 0.01f and <= 20f, $"{label}.rechargeSeconds 超界。");
        WandSpellCatalog.Require(float.IsFinite(ManaMax) && ManaMax is >= 10f and <= 10000f, $"{label}.manaMax 超界。");
        WandSpellCatalog.Require(float.IsFinite(ManaChargePerSecond) && ManaChargePerSecond is >= 1f and <= 10000f, $"{label}.manaChargePerSecond 超界。");
        WandSpellCatalog.Require(Capacity is >= 1 && Capacity <= limits.MaxWandCapacity, $"{label}.capacity 超界。");
        WandSpellCatalog.Require(float.IsFinite(SpreadDegrees) && SpreadDegrees is >= -45f and <= 180f, $"{label}.spreadDegrees 超界。");
        WandSpellCatalog.Require(float.IsFinite(SpeedMultiplier) && SpeedMultiplier is > 0f and <= 8f, $"{label}.speedMultiplier 超界。");
        WandSpellCatalog.Require(Deck.Length is >= 1 && Deck.Length <= Capacity, $"{label}.deck 必须非空且不超过 capacity。");
        WandSpellCatalog.Require(AlwaysCast.Length <= limits.MaxAlwaysCast, $"{label}.alwaysCast 超界。");
        ValidateReferences(Deck, $"{label}.deck");
        ValidateReferences(AlwaysCast, $"{label}.alwaysCast");
    }

    private static void ValidateReferences(string[] references, string label)
    {
        HashSet<string> local = new(StringComparer.Ordinal);
        for (int i = 0; i < references.Length; i++)
        {
            WandSpellCatalog.Require(WandSpellCatalog.IsStableId(references[i]), $"{label}[{i}] 不是稳定键。");
            WandSpellCatalog.Require(local.Add(references[i]), $"{label} 重复引用 {references[i]}。");
        }
    }
}

internal enum WandSpellCategory : byte
{
    Projectile,
    Modifier,
    Draw,
    Trigger,
    Material,
    Utility,
    Passive,
    LimitedUse,
    Special,
}

internal enum WandSpellEffectKind : byte
{
    Launch,
    Modifier,
    Draw,
    Material,
    Passive,
    Repeat,
}

internal enum WandProjectileKind : byte
{
    None,
    Bolt,
    Orb,
    Grenade,
    Material,
    Dig,
    Teleport,
}

internal enum WandTriggerKind : byte
{
    None,
    Hit,
    Timer,
    Death,
}
