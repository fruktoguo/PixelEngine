using System.Reflection;
using System.Runtime.InteropServices;
using PixelEngine.Hosting;

Assembly assembly = Assembly.GetExecutingAssembly();
AssemblyName name = assembly.GetName();
string version = name.Version?.ToString() ?? "0.0.0.0";

Console.WriteLine($"{name.Name} {version}");
Console.WriteLine($"RID: {RuntimeInformation.RuntimeIdentifier}");

EngineProject project = new(
    "content",
    "demo",
    [new SceneDescriptor("demo")]);
using Engine engine = new EngineBuilder()
    .UseHeadless()
    .UseDeterministicMode()
    .WithProject(project)
    .Build();

engine.RunHeadlessTicks(1);
Console.WriteLine($"Engine frame: {engine.Context.Clock.FrameIndex}, scene: {engine.Context.GetService<ISceneService>().Current?.Name}");

return 0;
