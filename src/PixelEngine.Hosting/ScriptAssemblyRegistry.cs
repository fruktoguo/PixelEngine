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

    /// <summary>
    /// 按程序集简单名替换脚本程序集；用于热重载后让 Behaviour 发现路径指向最新动态程序集。
    /// </summary>
    /// <param name="assembly">包含 Behaviour 类型的最新程序集。</param>
    public void RegisterOrReplaceByName(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? assemblyName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            Register(assembly);
            return;
        }

        for (int i = _assemblies.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_assemblies[i].GetName().Name, assemblyName, StringComparison.Ordinal))
            {
                _assemblies.RemoveAt(i);
            }
        }

        _assemblies.Add(assembly);
    }
}
