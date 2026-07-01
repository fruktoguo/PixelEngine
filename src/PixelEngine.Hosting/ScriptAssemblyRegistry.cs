using System.Reflection;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 装配期记录可供脚本宿主发现 Behaviour 的程序集。
/// </summary>
public sealed class ScriptAssemblyRegistry
{
    private readonly List<Assembly> _assemblies = [];

    /// <summary>
    /// 已注册脚本程序集。
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>
    /// 注册一个脚本程序集；重复注册同一程序集会被忽略。
    /// </summary>
    /// <param name="assembly">包含 Behaviour 类型的程序集。</param>
    public void Register(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }
    }
}
