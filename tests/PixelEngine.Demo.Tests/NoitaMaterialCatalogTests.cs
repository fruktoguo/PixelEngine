using System.Text.Json;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita material/reaction reference catalog provenance and completeness tests.
/// </summary>
public sealed class NoitaMaterialCatalogTests
{
    /// <summary>
    /// The committed catalog must preserve every Build 17130612 declaration and reaction.
    /// </summary>
    [Fact]
    public void CatalogPreservesCompleteBuild17130612MaterialAndReactionSource()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ContentRoot(), "noita-material-catalog.json")));
        JsonElement root = document.RootElement;
        JsonElement reference = root.GetProperty("reference");
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Noita", reference.GetProperty("game").GetString());
        Assert.Equal("17130612", reference.GetProperty("buildId").GetString());
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", reference.GetProperty("versionHash").GetString());
        Assert.Equal("data/materials.xml", reference.GetProperty("sourcePath").GetString());
        Assert.Equal(
            "122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04",
            reference.GetProperty("sourceSha256").GetString());

        JsonElement counts = root.GetProperty("counts");
        Assert.Equal(468, counts.GetProperty("declarations").GetInt32());
        Assert.Equal(466, counts.GetProperty("uniqueMaterials").GetInt32());
        Assert.Equal(325, counts.GetProperty("reactions").GetInt32());
        Assert.Equal(5, counts.GetProperty("requiredReactions").GetInt32());

        JsonElement declarations = root.GetProperty("declarations");
        Assert.Equal(468, declarations.GetArrayLength());
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int i = 0; i < declarations.GetArrayLength(); i++)
        {
            JsonElement declaration = declarations[i];
            Assert.Equal(i, declaration.GetProperty("ordinal").GetInt32());
            Assert.True(names.Add(declaration.GetProperty("name").GetString()!) ||
                declaration.GetProperty("name").GetString() is "meat_pumpkin" or "rock_box2d");
            Assert.True(declaration.GetProperty("attributes").TryGetProperty("wang_color", out _));
            Assert.Equal(JsonValueKind.Array, declaration.GetProperty("childXml").ValueKind);
        }

        Assert.Equal(466, names.Count);
        foreach (JsonElement declaration in declarations.EnumerateArray())
        {
            if (declaration.GetProperty("kind").GetString() != "CellDataChild")
            {
                continue;
            }

            string parent = declaration.GetProperty("parent").GetString()!;
            Assert.Contains(parent, names);
        }

        JsonElement duplicates = root.GetProperty("duplicateNames");
        Assert.Equal(2, duplicates.GetArrayLength());
        Assert.Equal(
            ["meat_pumpkin", "rock_box2d"],
            [.. duplicates.EnumerateArray().Select(item => item.GetProperty("name").GetString()!)]);

        AssertOrdinals(root.GetProperty("reactions"), 325);
        AssertOrdinals(root.GetProperty("requiredReactions"), 5);
    }

    private static void AssertOrdinals(JsonElement values, int expectedCount)
    {
        Assert.Equal(expectedCount, values.GetArrayLength());
        for (int i = 0; i < values.GetArrayLength(); i++)
        {
            JsonElement value = values[i];
            Assert.Equal(i, value.GetProperty("ordinal").GetInt32());
            Assert.True(value.GetProperty("attributes").TryGetProperty("probability", out _));
            Assert.Equal(JsonValueKind.Array, value.GetProperty("childXml").ValueKind);
        }
    }

    private static string ContentRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo", "PixelEngine.Demo", "content"));
    }
}
