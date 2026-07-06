using System.Globalization;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL 上下文能力快照。
/// </summary>
public sealed class GlCapabilities
{
    private GlCapabilities(
        string version,
        string renderer,
        string vendor,
        int majorVersion,
        int minorVersion,
        bool isGles,
        bool isAngle,
        bool hasComputeShader,
        bool hasBufferStorage,
        string[] extensions)
    {
        Version = version;
        Renderer = renderer;
        Vendor = vendor;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        IsGles = isGles;
        IsAngle = isAngle;
        HasComputeShader = hasComputeShader;
        HasBufferStorage = hasBufferStorage;
        Extensions = extensions;
    }

    /// <summary>
    /// GL_VERSION 字符串。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// GL_RENDERER 字符串。
    /// </summary>
    public string Renderer { get; }

    /// <summary>
    /// GL_VENDOR 字符串。
    /// </summary>
    public string Vendor { get; }

    /// <summary>
    /// 主版本号。
    /// </summary>
    public int MajorVersion { get; }

    /// <summary>
    /// 次版本号。
    /// </summary>
    public int MinorVersion { get; }

    /// <summary>
    /// 当前上下文是否为 OpenGL ES。
    /// </summary>
    public bool IsGles { get; }

    /// <summary>
    /// 当前上下文是否疑似由 ANGLE 提供。
    /// </summary>
    public bool IsAngle { get; }

    /// <summary>
    /// 是否具备 compute shader 能力。桌面 GL 4.3+ 或 ES 3.1+ 视为可用。
    /// </summary>
    public bool HasComputeShader { get; }

    /// <summary>
    /// 是否具备 buffer storage / persistent mapping 能力。
    /// </summary>
    public bool HasBufferStorage { get; }

    /// <summary>
    /// 上下文扩展列表。
    /// </summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// 查询当前 OpenGL 上下文能力。
    /// </summary>
    /// <param name="gl">Silk.NET OpenGL 入口。</param>
    /// <returns>能力快照。</returns>
    public static GlCapabilities Query(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        string version = gl.GetStringS(GLEnum.Version) ?? string.Empty;
        string renderer = gl.GetStringS(GLEnum.Renderer) ?? string.Empty;
        string vendor = gl.GetStringS(GLEnum.Vendor) ?? string.Empty;
        bool isGles = ParseVersion(version).IsGles;

        List<string> extensions = [];
        try
        {
            gl.GetInteger(GLEnum.NumExtensions, out int extensionCount);
            for (uint i = 0; i < (uint)extensionCount; i++)
            {
                string? extension = gl.GetStringS(GLEnum.Extensions, i);
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    extensions.Add(extension);
                }
            }
        }
        catch (Exception) when (!isGles)
        {
            string extensionString = gl.GetStringS(GLEnum.Extensions) ?? string.Empty;
            AddSplitExtensions(extensionString, extensions);
        }

        return FromRaw(version, renderer, vendor, extensions);
    }

    /// <summary>
    /// 从原始 GL 字符串构造能力快照，供测试和无窗口探针使用。
    /// </summary>
    /// <param name="version">GL_VERSION 字符串。</param>
    /// <param name="renderer">GL_RENDERER 字符串。</param>
    /// <param name="vendor">GL_VENDOR 字符串。</param>
    /// <param name="extensions">扩展名集合。</param>
    /// <returns>能力快照。</returns>
    public static GlCapabilities FromRaw(
        string version,
        string renderer,
        string vendor,
        IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(extensions);

        (int major, int minor, bool isGles) = ParseVersion(version);
        string[] extensionArray =
        [
            .. extensions
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal),
        ];
        bool isAngle = IsAngleContext(version, renderer, vendor);
        bool hasCompute = isGles
            ? IsAtLeast(major, minor, 3, 1)
            : IsAtLeast(major, minor, 4, 3);
        bool hasBufferStorage = !isGles &&
            (IsAtLeast(major, minor, 4, 4) ||
             extensionArray.Contains("GL_ARB_buffer_storage", StringComparer.Ordinal));
        return new GlCapabilities(
            version,
            renderer,
            vendor,
            major,
            minor,
            isGles,
            isAngle,
            hasCompute,
            hasBufferStorage,
            extensionArray);
    }

    private static bool IsAngleContext(string version, string renderer, string vendor)
    {
        return version.Contains("ANGLE", StringComparison.OrdinalIgnoreCase) ||
            renderer.Contains("ANGLE", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("ANGLE", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Major, int Minor, bool IsGles) ParseVersion(string version)
    {
        bool isGles = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<char> span = version.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsDigit(span[i]))
            {
                continue;
            }

            int start = i;
            while (i < span.Length && char.IsDigit(span[i]))
            {
                i++;
            }

            if (i >= span.Length || span[i] != '.')
            {
                continue;
            }

            ReadOnlySpan<char> majorSpan = span[start..i];
            i++;
            int minorStart = i;
            while (i < span.Length && char.IsDigit(span[i]))
            {
                i++;
            }

            ReadOnlySpan<char> minorSpan = span[minorStart..i];
            if (int.TryParse(majorSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int major) &&
                int.TryParse(minorSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
            {
                return (major, minor, isGles);
            }
        }

        return (0, 0, isGles);
    }

    private static bool IsAtLeast(int major, int minor, int requiredMajor, int requiredMinor)
    {
        return major > requiredMajor || (major == requiredMajor && minor >= requiredMinor);
    }

    private static void AddSplitExtensions(string extensionString, List<string> extensions)
    {
        foreach (string extension in extensionString.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            extensions.Add(extension);
        }
    }
}
