using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本 GUI 生命周期派发测试。
/// </summary>
public sealed class ScriptGuiDispatchTests
{
    /// <summary>
    /// 验证 ScriptRuntime.DrawGui 会按 OnUpdate 相同 bucket 顺序调用启用 Behaviour，并跳过禁用脚本。
    /// </summary>
    [Fact]
    public void DrawGuiDispatchesEnabledBehavioursInUpdateOrder()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        ScriptRuntime runtime = new();
        runtime.Initialize(context);
        List<string> events = [];

        GuiBehaviour first = scene.CreateEntity().AddComponent<GuiBehaviour>();
        GuiBehaviour disabled = scene.CreateEntity().AddComponent<GuiBehaviour>();
        GuiBehaviour second = scene.CreateEntity().AddComponent<GuiBehaviour>();
        first.Name = "first";
        disabled.Name = "disabled";
        second.Name = "second";
        first.Events = events;
        disabled.Events = events;
        second.Events = events;
        disabled.Enabled = false;

        scene.DispatchUpdate(context, 0.016f);
        runtime.DrawGui(new FakeGuiContext());

        Assert.Equal(["update:first", "update:second", "gui:first", "gui:second"], events);
    }

    private sealed class GuiBehaviour : Behaviour
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Events { get; set; } = [];

        protected override void OnUpdate(float dt)
        {
            Events.Add($"update:{Name}");
        }

        protected override void OnGui(IGuiContext gui)
        {
            _ = Assert.IsType<FakeGuiContext>(gui);
            Events.Add($"gui:{Name}");
        }
    }

    private sealed class FakeGuiContext : IGuiContext
    {
        public int Width => 320;

        public int Height => 180;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public List<string> Drawn { get; } = [];

        public void SetNextWindow(float x, float y, float width, float height, GuiCondition condition = GuiCondition.Always)
        {
            Drawn.Add($"next:{x},{y},{width},{height},{condition}");
        }

        public bool BeginWindow(string id, string title, GuiWindowFlags flags = GuiWindowFlags.None)
        {
            Drawn.Add($"begin:{id}:{title}:{flags}");
            return true;
        }

        public void EndWindow()
        {
            Drawn.Add("end");
        }

        public void Text(string text)
        {
            Drawn.Add($"text:{text}");
        }

        public void TextColored(string text, uint colorBgra)
        {
            Drawn.Add($"text-colored:{text}:{colorBgra:X8}");
        }

        public void SameLine()
        {
            Drawn.Add("same-line");
        }

        public void Separator()
        {
            Drawn.Add("separator");
        }

        public bool Button(string label)
        {
            Drawn.Add($"button:{label}");
            return false;
        }

        public bool Checkbox(string label, ref bool value)
        {
            Drawn.Add($"checkbox:{label}:{value}");
            return false;
        }

        public void ProgressBar(float value01, string? label = null)
        {
            Drawn.Add($"progress:{value01}:{label}");
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16)
        {
            Drawn.Add($"swatch:{id}:{colorBgra:X8}:{size}");
        }
    }

    private sealed class FakeScriptContext(Scene scene) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IWorldEffects World => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IEventBus Events => throw new NotSupportedException();

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }
}
