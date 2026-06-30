using System.Reflection;
using System.Runtime.InteropServices;

Assembly assembly = Assembly.GetExecutingAssembly();
AssemblyName name = assembly.GetName();
string version = name.Version?.ToString() ?? "0.0.0.0";

Console.WriteLine($"{name.Name} {version}");
Console.WriteLine($"RID: {RuntimeInformation.RuntimeIdentifier}");

return 0;
