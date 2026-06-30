using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Content.Tests;

/// <summary>
/// Plan 04 materials/reactions JSON 加载测试。
/// </summary>
public sealed class MaterialContentLoaderTests
{
    /// <summary>
    /// 验证 materials.json 全字段映射、target name 解析与 source-gen 反序列化。
    /// </summary>
    [Fact]
    public void LoadMapsMaterialSchemaToRuntimeDefinitions()
    {
        MaterialContentLoadResult result = MaterialContentLoader.Load(MaterialsJson, EmptyReactionsJson);
        MaterialTable table = result.Materials;

        Assert.True(table.TryGetId("water", out ushort waterId));
        ref readonly MaterialDef water = ref table.Get(waterId);
        Assert.Equal(CellType.Liquid, water.Type);
        Assert.Equal(90, water.Density);
        Assert.Equal(4, water.Dispersion);
        Assert.True(water.LiquidStatic);
        Assert.False(water.LiquidSand);
        Assert.Equal(8, water.Flammability);
        Assert.Equal(451, water.AutoIgnitionTemp);
        Assert.Equal(12, water.FireHp);
        Assert.Equal(2, water.TemperatureOfFire);
        Assert.Equal(3, water.GeneratesSmoke);
        Assert.Equal(0xFF336699u, water.BaseColorBGRA);
        Assert.Equal(7, water.TextureId);
        Assert.Equal(6, water.ColorNoise);
        Assert.Equal(1, water.AudioCues.FireCue);
        Assert.True((water.PropertyFlags & MaterialProperty.BurnableFast) != 0);
        Assert.Equal(table.GetIdOrFallback("ice", 0), water.FreezeTarget);
        Assert.Equal(table.GetIdOrFallback("steam", 0), water.BoilTarget);
    }

    /// <summary>
    /// 验证 reaction JSON 的 tag 输入展开、representative 输出、rate 和 flags 映射。
    /// </summary>
    [Fact]
    public void LoadExpandsTagReactionsIntoPackedReactionTable()
    {
        MaterialContentLoadResult result = MaterialContentLoader.Load(MaterialsJson, ReactionsJson);
        MaterialTable materials = result.Materials;
        ReactionTable reactions = result.Reactions;
        ushort fire = materials.GetIdOrFallback("fire", 0);
        ushort wood = materials.GetIdOrFallback("wood", 0);
        ushort ash = materials.GetIdOrFallback("ash", 0);
        ushort smoke = materials.GetIdOrFallback("smoke", 0);

        ref readonly MaterialDef fireDef = ref materials.Get(fire);
        int index = reactions.Find(fire, wood, in fireDef);

        Assert.True(index >= 0);
        ref readonly Reaction reaction = ref reactions.At(index);
        Assert.Equal(ash, reaction.OutputA);
        Assert.Equal(smoke, reaction.OutputB);
        Assert.Equal(128, reaction.Probability);
        Assert.True((reaction.Flags & ReactionFlags.EmitHeat) != 0);

        ref readonly MaterialDef woodDef = ref materials.Get(wood);
        int mirror = reactions.Find(wood, fire, in woodDef);
        Assert.True(mirror >= 0);
        Assert.Equal(smoke, reactions.At(mirror).OutputA);
        Assert.Equal(ash, reactions.At(mirror).OutputB);
    }

    /// <summary>
    /// 验证输出端 tag 缺少 representative 时拒绝加载。
    /// </summary>
    [Fact]
    public void LoadRejectsOutputTagWithoutRepresentative()
    {
        const string materialsJson = """
        {
          "materials": [
            { "name": "empty", "type": "Empty", "heatCapacity": 1 },
            { "name": "fire", "type": "Fire", "heatCapacity": 1, "tags": [ "fire" ] },
            { "name": "wood", "type": "Solid", "heatCapacity": 1, "tags": [ "corrodible" ] }
          ]
        }
        """;
        const string reactionsJson = """
        {
          "reactions": [
            { "inputA": "[fire]", "inputB": "[corrodible]", "outputA": "[fire]", "outputB": "empty" }
          ]
        }
        """;

        ArgumentException exception = Assert.Throws<ArgumentException>(() => MaterialContentLoader.Load(materialsJson, reactionsJson));

        Assert.Contains("代表材质", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证非法概率由 schema 加载层拒绝。
    /// </summary>
    [Fact]
    public void LoadRejectsProbabilityOutsideSchemaRange()
    {
        const string reactionsJson = """
        { "reactions": [
          { "inputA": "fire", "inputB": "wood", "outputA": "ash", "outputB": "smoke", "probability": 101 }
        ] }
        """;

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => MaterialContentLoader.Load(MaterialsJson, reactionsJson));

        Assert.Equal("rate", exception.ParamName);
    }

    /// <summary>
    /// 验证 materials.json 不能声明运行时 custom-update 门控位。
    /// </summary>
    [Fact]
    public void LoadRejectsRuntimeOnlyCustomUpdateProperty()
    {
        const string materialsJson = """
        { "materials": [
          { "name": "empty", "type": "Empty", "heatCapacity": 1, "tags": [ "has_custom_update" ] }
        ] }
        """;

        ArgumentException exception = Assert.Throws<ArgumentException>(() => MaterialContentLoader.Load(materialsJson, EmptyReactionsJson));

        Assert.Contains("HasCustomUpdate", exception.Message, StringComparison.Ordinal);
    }

    private const string EmptyReactionsJson = """{ "reactions": [] }""";

    private const string ReactionsJson = """
    {
      "reactions": [
        {
          "inputA": "[fire]",
          "inputB": "[corrodible]",
          "outputA": "[corrodible]",
          "outputB": "[fire]",
          "probability": 50,
          "flags": [ "emit_heat" ]
        }
      ]
    }
    """;

    private const string MaterialsJson = """
    {
      "tagRepresentatives": [
        { "tag": "fire", "material": "smoke" },
        { "tag": "corrodible", "material": "ash" }
      ],
      "materials": [
        { "name": "empty", "type": "Empty", "heatCapacity": 1 },
        { "name": "ice", "type": "Solid", "density": 100, "meltPoint": 5, "meltTarget": "water", "heatCapacity": 1 },
        {
          "name": "water",
          "type": "Liquid",
          "density": 90,
          "dispersion": 4,
          "liquidStatic": true,
          "flammability": 8,
          "autoIgnitionTemp": 451,
          "fireHp": 12,
          "temperatureOfFire": 2,
          "generatesSmoke": 3,
          "freezePoint": 0,
          "freezeTarget": "ice",
          "boilPoint": 100,
          "boilTarget": "steam",
          "heatConduct": 200,
          "heatCapacity": 2.5,
          "defaultLifetime": 9,
          "durability": 10,
          "textureId": 7,
          "baseColor": 4281558681,
          "colorNoise": 6,
          "tags": [ "burnable_fast" ],
          "audioCues": { "fire": 1, "splash": 2, "impact": 3, "explosion": 4, "ambient": 5 }
        },
        { "name": "steam", "type": "Gas", "density": 1, "heatCapacity": 1 },
        { "name": "fire", "type": "Fire", "density": 1, "heatCapacity": 1, "tags": [ "fire", "emissive" ] },
        { "name": "wood", "type": "Solid", "density": 120, "heatCapacity": 1, "tags": [ "corrodible" ] },
        { "name": "ash", "type": "Powder", "density": 40, "heatCapacity": 1 },
        { "name": "smoke", "type": "Gas", "density": 1, "heatCapacity": 1 }
      ]
    }
    """;
}
