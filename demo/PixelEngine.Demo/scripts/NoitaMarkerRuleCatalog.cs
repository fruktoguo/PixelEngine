namespace PixelEngine.Demo;

/// <summary>
/// 从 Noita 参考 Lua 注册表预编译得到的纯 C# marker 规则目录。
/// 玩家包不包含 Lua 源码、Lua VM 或运行时脚本解释器。
/// </summary>
internal static partial class NoitaMarkerRuleCatalog
{
    internal static NoitaMarkerRule Resolve(string function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        return TryResolveGenerated(function, out NoitaMarkerRule rule)
            ? rule
            : throw new KeyNotFoundException($"Noita marker function {function} 没有预编译 C# 规则。");
    }

    internal static bool TryResolve(string function, out NoitaMarkerRule rule)
    {
        if (string.IsNullOrWhiteSpace(function))
        {
            rule = default;
            return false;
        }

        return TryResolveGenerated(function, out rule);
    }
}

internal enum NoitaMarkerRuleKind : byte
{
    PixelScene,
    Vegetation,
    Loot,
    Prop,
    Encounter,
    Trigger,
    Effect,
}

internal readonly record struct NoitaMarkerRule(
    string Function,
    NoitaMarkerRuleKind Kind,
    int SourceRegistrationCount,
    int SourceBiomeCount);
