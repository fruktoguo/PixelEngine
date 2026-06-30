using System.Text;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// 存档版本迁移链测试。
/// </summary>
public sealed class MigrationChainTests
{
    /// <summary>
    /// 验证迁移链按版本逐级升级 byte payload。
    /// </summary>
    [Fact]
    public void MigrationChainUpgradesBytesStepByStep()
    {
        MigrationChain chain = new(3, [new AppendMigrator(1, "->v2"), new AppendMigrator(2, "->v3")]);

        byte[] upgraded = chain.Upgrade(Encoding.UTF8.GetBytes("v1"), fromVersion: 1);

        Assert.Equal("v1->v2->v3", Encoding.UTF8.GetString(upgraded));
    }

    /// <summary>
    /// 验证源版本等于目标版本时只复制 payload，不要求迁移器。
    /// </summary>
    [Fact]
    public void MigrationChainCopiesCurrentVersionPayload()
    {
        MigrationChain chain = new(1, []);
        byte[] source = [1, 2, 3];

        byte[] upgraded = chain.Upgrade(source, fromVersion: 1);
        source[0] = 9;

        Assert.Equal([1, 2, 3], upgraded);
    }

    /// <summary>
    /// 验证 stream 入口读取并写出升级后的 payload。
    /// </summary>
    [Fact]
    public void MigrationChainUpgradesStreams()
    {
        MigrationChain chain = new(2, [new AppendMigrator(1, "-new")]);
        using MemoryStream input = new(Encoding.UTF8.GetBytes("old"));
        using MemoryStream output = new();

        chain.Upgrade(input, output, fromVersion: 1);

        Assert.Equal("old-new", Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// 验证缺失迁移器会明确失败。
    /// </summary>
    [Fact]
    public void MigrationChainRejectsMissingStep()
    {
        MigrationChain chain = new(3, [new AppendMigrator(1, "->v2")]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            chain.Upgrade([1], fromVersion: 1));

        Assert.Contains("缺少", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证迁移器必须严格推进一个版本。
    /// </summary>
    [Fact]
    public void MigrationChainRejectsMigratorThatDoesNotAdvanceOneVersion()
    {
        MigrationChain chain = new(3, [new BadMigrator(1, nextVersion: 3)]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            chain.Upgrade([1], fromVersion: 1));

        Assert.Contains("逐级", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证重复迁移源版本在构造时失败。
    /// </summary>
    [Fact]
    public void MigrationChainRejectsDuplicateMigrators()
    {
        _ = Assert.Throws<ArgumentException>(() =>
            new MigrationChain(3, [new AppendMigrator(1, "a"), new AppendMigrator(1, "b")]));
    }

    /// <summary>
    /// 验证高于当前目标版本的存档不会被降级或静默接受。
    /// </summary>
    [Fact]
    public void MigrationChainRejectsFutureVersion()
    {
        MigrationChain chain = new(2, [new AppendMigrator(1, "->v2")]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            chain.Upgrade([1], fromVersion: 3));

        Assert.Contains("高于", exception.Message, StringComparison.Ordinal);
    }

    private sealed class AppendMigrator(int fromVersion, string suffix) : ISaveMigrator
    {
        public int FromVersion { get; } = fromVersion;

        public void Migrate(MigrationContext context)
        {
            string text = Encoding.UTF8.GetString(context.Payload) + suffix;
            context.ReplacePayload(Encoding.UTF8.GetBytes(text), FromVersion + 1);
        }
    }

    private sealed class BadMigrator(int fromVersion, int nextVersion) : ISaveMigrator
    {
        public int FromVersion { get; } = fromVersion;

        public void Migrate(MigrationContext context)
        {
            context.ReplacePayload(context.Payload, nextVersion);
        }
    }
}
