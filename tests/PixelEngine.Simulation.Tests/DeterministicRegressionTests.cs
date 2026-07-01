using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 确定性模式下固定初态到 golden 终态的回归测试。
/// </summary>
public sealed class DeterministicRegressionTests
{
    /// <summary>
    /// 验证固定 sand/solid 初态的 movement golden 终态稳定。
    /// </summary>
    [Fact]
    public void MovementGoldenMatchesFixedSeedSnapshot()
    {
        DeterministicSimFixture fixture = new();
        fixture.Set(fixture.Center, 10, 10, DeterministicSimFixture.Sand);
        fixture.Set(fixture.Center, 10, 43, DeterministicSimFixture.Solid);
        fixture.Center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = fixture.CreateKernel();

        kernel.StepCa();
        kernel.SwapDirtyRects();

        AssertGolden("movement.txt", fixture.ExportNormalizedSnapshot());
    }

    /// <summary>
    /// 验证固定 lava+water 反应初态的 golden 终态稳定。
    /// </summary>
    [Fact]
    public void ReactionGoldenMatchesFixedSeedSnapshot()
    {
        DeterministicSimFixture fixture = new();
        fixture.Set(fixture.Center, 63, 10, DeterministicSimFixture.Lava);
        fixture.Set(fixture.East, 0, 10, DeterministicSimFixture.Water);
        NeighborWindow window = new(fixture.Source, fixture.Center.Coord);
        ReactionEngine reactions = fixture.CreateLavaWaterReactionEngine();

        bool reacted = reactions.TryReact(
            ref window,
            63,
            10,
            DeterministicSimFixture.Lava,
            64,
            10,
            DeterministicSimFixture.Water,
            CellFlags.Parity,
            randomByte: 0);

        Assert.True(reacted);
        AssertGolden("reaction.txt", fixture.ExportNormalizedSnapshot());
    }

    /// <summary>
    /// 验证固定 ice 温度相变初态的 golden 终态稳定。
    /// </summary>
    [Fact]
    public void TemperatureGoldenMatchesFixedSeedSnapshot()
    {
        DeterministicSimFixture fixture = new();
        fixture.Set(fixture.Center, 12, 12, DeterministicSimFixture.Ice);
        fixture.Center.SetCurrentDirty(new DirtyRect(12, 12, 12, 12));
        TemperatureField temperature = new();
        temperature.AddHeat(12, 12, 20);

        temperature.ApplyPhaseTransitions(fixture.Source, fixture.Materials, CellFlags.Parity);

        AssertGolden("temperature.txt", fixture.ExportNormalizedSnapshot());
    }

    private static void AssertGolden(string fileName, string actual)
    {
        string normalizedActual = DeterministicSimFixture.Normalize(actual);
        string expected = DeterministicSimFixture.ReadGolden(fileName);
        Assert.True(
            string.Equals(expected, normalizedActual, StringComparison.Ordinal),
            $"Golden {fileName} 不匹配。实际快照：\n{normalizedActual}");
    }
}
