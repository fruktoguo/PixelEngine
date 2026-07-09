using Xunit;

namespace PixelEngine.Testing;

/// <summary>
/// xUnit v2 兼容的 native smoke 条件事实：在 discovery 阶段把未启用环境报告为 skipped。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NativeSmokeFactAttribute : FactAttribute
{
    /// <summary>
    /// 默认要求桌面 GL smoke 环境。
    /// </summary>
    public NativeSmokeFactAttribute()
        : this("PIXELENGINE_RENDERING_GL_SMOKE")
    {
    }

    /// <summary>
    /// 要求指定的 native smoke 环境变量为 1。
    /// </summary>
    /// <param name="requiredEnvironmentVariable">必需的环境变量名。</param>
    public NativeSmokeFactAttribute(string requiredEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredEnvironmentVariable);
        if (!string.Equals(
                Environment.GetEnvironmentVariable(requiredEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            Skip = $"需要 {requiredEnvironmentVariable}=1 才能执行真实 native smoke。";
        }
    }
}
