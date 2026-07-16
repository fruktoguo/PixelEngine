using System.Reflection;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 从 production UI 方法上的稳定声明生成 command catalog；不存在手写 capability 映射副本。
/// scheduler 会把该 catalog 与真实 semantic registrations 双向联结并 fail closed。
/// </summary>
internal static class EditorUiCommandCatalog
{
    public static AutomationUiCommandRegistration[] CreateRegistrations()
    {
        Assembly[] assemblies =
        [
            typeof(EditorUiCommandCatalog).Assembly,
            typeof(EditorApp).Assembly,
        ];
        List<AutomationUiCommandRegistration> registrations = [];
        for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            Type[] types = assemblies[assemblyIndex].GetTypes();
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type type = types[typeIndex];
                EditorUiSurfaceAttribute? surface = type.GetCustomAttribute<EditorUiSurfaceAttribute>(inherit: false);
                MethodInfo[] methods = type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
                {
                    MethodInfo method = methods[methodIndex];
                    EditorUiCommandsAttribute[] commandGroups =
                    [
                        .. method.GetCustomAttributes<EditorUiCommandsAttribute>(inherit: false),
                    ];
                    if (commandGroups.Length == 0)
                    {
                        continue;
                    }

                    if (surface is null)
                    {
                        throw new InvalidOperationException(
                            $"UI command handler '{type.FullName}.{method.Name}' 缺少 EditorUiSurfaceAttribute。");
                    }

                    for (int groupIndex = 0; groupIndex < commandGroups.Length; groupIndex++)
                    {
                        string[] commandIds = commandGroups[groupIndex].CommandIds;
                        for (int commandIndex = 0; commandIndex < commandIds.Length; commandIndex++)
                        {
                            registrations.Add(new AutomationUiCommandRegistration
                            {
                                Id = commandIds[commandIndex],
                                SurfaceId = surface.Id,
                                Handler = method,
                            });
                        }
                    }
                }
            }
        }

        return [.. registrations.OrderBy(static item => item.Id, StringComparer.Ordinal)];
    }
}
