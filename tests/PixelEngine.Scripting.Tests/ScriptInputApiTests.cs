using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本输入快照 API 测试。
/// </summary>
public sealed class ScriptInputApiTests
{
    /// <summary>
    /// 验证键盘、鼠标、滚轮与边沿状态来自逐帧输入快照。
    /// </summary>
    [Fact]
    public void UpdateComputesKeyMouseEdgesAxesAndWheel()
    {
        ScriptInputApi input = new();

        input.Update([Key.D, Key.Space], [MouseButton.Left], mouseX: 12, mouseY: 34, wheelY: 2);

        Assert.True(input.IsDown(Key.D));
        Assert.True(input.WasPressed(Key.Space));
        Assert.False(input.WasReleased(Key.Space));
        Assert.Equal(1f, input.Axis(Axis.Horizontal));
        Assert.Equal((12f, 34f), input.MousePixel);
        Assert.Equal(2f, input.MouseWheelY);
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Left));

        input.Update([Key.A], [MouseButton.Right], mouseX: 20, mouseY: 40, wheelY: -1);

        Assert.False(input.IsDown(Key.D));
        Assert.True(input.WasReleased(Key.D));
        Assert.True(input.WasReleased(Key.Space));
        Assert.True(input.WasMouseReleased(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Right));
        Assert.Equal(-1f, input.Axis(Axis.Horizontal));
        Assert.Equal(-1f, input.MouseWheelY);
    }

    /// <summary>
    /// 验证输入枚举保持从 0 开始的连续布局，使脚本输入 API 可用 AOT-safe 数组索引替代运行时反射。
    /// </summary>
    [Fact]
    public void InputEnumsRemainDenseForAotSafeIndexing()
    {
        AssertDenseEnum<Key>();
        AssertDenseEnum<MouseButton>();
    }

    /// <summary>
    /// 验证未知输入值被显式拒绝，而不是越界访问输入快照数组。
    /// </summary>
    [Fact]
    public void UnknownInputValuesAreRejected()
    {
        ScriptInputApi input = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.IsDown((Key)(-1)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.WasPressed((Key)999));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.IsMouseDown((MouseButton)(-1)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.WasMousePressed((MouseButton)999));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.Update([(Key)999], [], 0, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => input.Update([], [(MouseButton)999], 0, 0, 0));
    }

    /// <summary>
    /// 验证运行时代码不再使用 NativeAOT 会警告的枚举反射来初始化脚本输入快照。
    /// </summary>
    [Fact]
    public void ScriptInputApiAvoidsRuntimeEnumReflection()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PixelEngine.Scripting", "ScriptInputApi.cs"));

        Assert.DoesNotContain("Enum.GetValues", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.IsDefined", source, StringComparison.Ordinal);
    }

    private static void AssertDenseEnum<T>()
        where T : struct, Enum
    {
        int[] values =
        [
            .. Enum.GetValues<T>()
            .Select(static value => Convert.ToInt32(value))
            .Order(),
        ];

        Assert.NotEmpty(values);
        Assert.Equal(Enumerable.Range(0, values.Length), values);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }
}
