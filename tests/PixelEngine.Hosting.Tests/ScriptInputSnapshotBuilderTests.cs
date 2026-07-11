using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 输入快照构建器测试。
/// 不变式：输入快照与仲裁后状态一致、键鼠轮询无丢帧合并。
/// </summary>
public sealed class ScriptInputSnapshotBuilderTests
{
    /// <summary>
    /// 验证旧版五参数输入驱动构造器仍存在，避免已编译宿主扩展在升级后缺失方法。
    /// </summary>
    [Fact]
    public void SilkInputPhaseDriverRetainsLegacyFiveParameterConstructor()
    {
        Assert.Contains(
            typeof(SilkInputPhaseDriver).GetConstructors(),
            constructor => constructor.GetParameters().Length == 5);
    }

    /// <summary>
    /// 验证键盘/鼠标通道可独立门控，避免 Editor/ImGui capture 时脚本误收输入。
    /// </summary>
    [Fact]
    public void BuilderAppliesKeyboardAndMouseRoutesIndependently()
    {
        ScriptInputApi input = new();
        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.A],
            [MouseButton.Left],
            mouseX: 10,
            mouseY: 20,
            wheelY: 1);

        Assert.True(input.IsDown(Key.A));
        Assert.True(input.WasPressed(Key.A));
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Left));
        Assert.Equal(1f, input.MouseWheelY);

        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.A],
            [MouseButton.Left],
            mouseX: 11,
            mouseY: 21,
            wheelY: 2,
            allowKeyboard: false,
            allowMouse: true);

        Assert.False(input.IsDown(Key.A));
        Assert.True(input.WasReleased(Key.A));
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.False(input.WasMousePressed(MouseButton.Left));
        Assert.Equal(2f, input.MouseWheelY);

        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.D],
            [MouseButton.Left],
            mouseX: 12,
            mouseY: 22,
            wheelY: 3,
            allowKeyboard: true,
            allowMouse: false);

        Assert.True(input.IsDown(Key.D));
        Assert.True(input.WasPressed(Key.D));
        Assert.False(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMouseReleased(MouseButton.Left));
        Assert.Equal(0f, input.MouseWheelY);
    }

    /// <summary>
    /// 验证嵌入式宿主的 Game View 指针坐标可直接进入 gameplay，且拒绝非有限坐标。
    /// </summary>
    [Fact]
    public void SilkInputUsesValidatedGameplayViewportMapperCoordinates()
    {
        FixedGameplayViewportMapper mapper = new(123.5f, 67.25f, succeeds: true);

        Assert.True(SilkInputPhaseDriver.TryMapGameplayViewportPointer(mapper, out float x, out float y));
        Assert.Equal(123.5f, x);
        Assert.Equal(67.25f, y);

        RecordingGameplayViewportMapper exactMapper = new();
        Assert.True(SilkInputPhaseDriver.TryMapGameplayViewportPointer(
            exactMapper,
            framebufferX: 800f,
            framebufferY: 600f,
            out x,
            out y));
        Assert.Equal((800f, 600f), (exactMapper.LastFramebufferX, exactMapper.LastFramebufferY));
        Assert.Equal((320f, 180f), (x, y));

        mapper = new(float.NaN, 12f, succeeds: true);
        Assert.False(SilkInputPhaseDriver.TryMapGameplayViewportPointer(mapper, out x, out y));
        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    /// <summary>
    /// 验证 runtime ImGui 适配器转发原始 framebuffer 坐标和独立键盘焦点。
    /// </summary>
    [Fact]
    public void GameplayViewportGuiInputRouteDelegatesFramebufferMappingAndKeyboardFocus()
    {
        RecordingGameplayViewportMapper mapper = new();
        GameplayViewportGuiInputRoute route = new(mapper);

        Assert.False(route.AllowsKeyboardInput);
        Assert.True(route.TryMapPointer(840f, 420f, out float x, out float y));
        Assert.Equal((840f, 420f), (mapper.LastFramebufferX, mapper.LastFramebufferY));
        Assert.Equal((320f, 180f), (x, y));

        IGameplayViewportInputMapper legacy = new FixedGameplayViewportMapper(12f, 34f, succeeds: true);
        Assert.True(legacy.AllowsRuntimeGuiKeyboardInput);
        Assert.True(legacy.TryMapFramebufferPointerToViewport(999f, 777f, out x, out y));
        Assert.Equal((12f, 34f), (x, y));
    }

    private sealed class FixedGameplayViewportMapper(float x, float y, bool succeeds) : IGameplayViewportInputMapper
    {
        public bool TryMapPointerToViewport(out float viewportX, out float viewportY)
        {
            viewportX = x;
            viewportY = y;
            return succeeds;
        }
    }

    private sealed class RecordingGameplayViewportMapper : IGameplayViewportInputMapper
    {
        public bool AllowsRuntimeGuiKeyboardInput => false;

        public float LastFramebufferX { get; private set; }

        public float LastFramebufferY { get; private set; }

        public bool TryMapPointerToViewport(out float viewportX, out float viewportY)
        {
            viewportX = 0f;
            viewportY = 0f;
            return false;
        }

        public bool TryMapFramebufferPointerToViewport(
            float framebufferX,
            float framebufferY,
            out float viewportX,
            out float viewportY)
        {
            LastFramebufferX = framebufferX;
            LastFramebufferY = framebufferY;
            viewportX = 320f;
            viewportY = 180f;
            return true;
        }
    }
}
