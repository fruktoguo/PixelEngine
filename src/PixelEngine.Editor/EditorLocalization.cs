using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 外置语言包信息。
/// </summary>
public sealed record EditorLanguageInfo(string Locale, string DisplayName, string SourcePath);

internal sealed record EditorLanguagePackDocument
{
    public int FormatVersion { get; init; } = 1;

    public string Locale { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public Dictionary<string, string> Strings { get; init; } = new(StringComparer.Ordinal);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EditorLanguagePackDocument))]
internal sealed partial class EditorLocalizationJsonContext : JsonSerializerContext;

/// <summary>
/// Editor 外置 JSON 语言包加载与当前语言解析入口。
/// </summary>
public static class EditorLocalization
{
    private readonly record struct WindowTitleCacheKey(
        string Locale,
        string Key,
        string Fallback,
        string CanonicalId);

    private sealed record LoadedLanguagePack(EditorLanguageInfo Info, IReadOnlyDictionary<string, string> Strings);

    private static readonly Lock Gate = new();
    private static IReadOnlyDictionary<string, LoadedLanguagePack> _packs =
        new ReadOnlyDictionary<string, LoadedLanguagePack>(new Dictionary<string, LoadedLanguagePack>(StringComparer.OrdinalIgnoreCase));
    private static readonly Dictionary<WindowTitleCacheKey, string> WindowTitleCache = [];
    private static string _locale = "en-US";

    /// <summary>当前 locale。</summary>
    public static string CurrentLocale
    {
        get
        {
            lock (Gate)
            {
                return _locale;
            }
        }
    }

    /// <summary>当前已发现语言。</summary>
    public static IReadOnlyList<EditorLanguageInfo> AvailableLanguages
    {
        get
        {
            lock (Gate)
            {
                return [.. _packs.Values.Select(static pack => pack.Info).OrderBy(static info => info.DisplayName, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// 从按优先级排列的目录加载语言包；后面的目录覆盖同 locale 的内置包。
    /// </summary>
    public static void Configure(IEnumerable<string> directories, string locale)
    {
        ArgumentNullException.ThrowIfNull(directories);
        Dictionary<string, LoadedLanguagePack> packs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                TryLoad(path, packs);
            }
        }

        lock (Gate)
        {
            _packs = new ReadOnlyDictionary<string, LoadedLanguagePack>(packs);
            _locale = ResolveLocale(locale, packs);
            WindowTitleCache.Clear();
        }
    }

    /// <summary>切换到已加载的 locale。</summary>
    /// <returns>locale 存在时返回 true。</returns>
    public static bool TrySetLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        lock (Gate)
        {
            if (!_packs.ContainsKey(locale))
            {
                return false;
            }

            _locale = locale;
            return true;
        }
    }

    /// <summary>解析语言 key；缺失时回退 English，再回退调用方文本。</summary>
    public static string Get(string key, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (Gate)
        {
            return ResolveTextLocked(key, fallback);
        }
    }

    /// <summary>
    /// 返回可本地化且跨语言保持稳定 dock ID 的窗口标题，并缓存组合结果以避免每帧字符串分配。
    /// </summary>
    /// <param name="key">语言包 key。</param>
    /// <param name="fallback">语言包缺失时的可见标题。</param>
    /// <param name="canonicalId">不随语言变化的 canonical 窗口 ID。</param>
    /// <returns>供 ImGui Begin 使用的可见标题与隐藏 ID。</returns>
    public static string GetWindowTitle(string key, string fallback, string canonicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);
        lock (Gate)
        {
            WindowTitleCacheKey cacheKey = new(_locale, key, fallback, canonicalId);
            if (WindowTitleCache.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }

            string title = EditorDockSpace.CreatePersistentWindowTitle(
                ResolveTextLocked(key, fallback),
                canonicalId);
            WindowTitleCache.Add(cacheKey, title);
            return title;
        }
    }

    /// <summary>按当前语言格式化带参数文本。</summary>
    public static string Format(string key, string fallback, params object?[] arguments)
    {
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key, fallback), arguments);
    }

    private static string ResolveTextLocked(string key, string fallback)
    {
        return _packs.TryGetValue(_locale, out LoadedLanguagePack? current) &&
            current.Strings.TryGetValue(key, out string? localized) &&
            !string.IsNullOrWhiteSpace(localized)
            ? localized
            : _packs.TryGetValue("en-US", out LoadedLanguagePack? english) &&
            english.Strings.TryGetValue(key, out string? englishText) &&
            !string.IsNullOrWhiteSpace(englishText)
            ? englishText
            : fallback;
    }

    private static void TryLoad(string path, Dictionary<string, LoadedLanguagePack> packs)
    {
        try
        {
            EditorLanguagePackDocument? document = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                EditorLocalizationJsonContext.Default.EditorLanguagePackDocument);
            if (document is null ||
                document.FormatVersion != 1 ||
                string.IsNullOrWhiteSpace(document.Locale) ||
                string.IsNullOrWhiteSpace(document.DisplayName))
            {
                return;
            }

            Dictionary<string, string> strings = new(StringComparer.Ordinal);
            foreach ((string key, string value) in document.Strings)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    strings[key.Trim()] = value;
                }
            }

            EditorLanguageInfo info = new(document.Locale.Trim(), document.DisplayName.Trim(), Path.GetFullPath(path));
            packs[info.Locale] = new LoadedLanguagePack(info, new ReadOnlyDictionary<string, string>(strings));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // 单个扩展语言包损坏不得阻止 Editor；该 locale 不进入可用列表。
        }
    }

    private static string ResolveLocale(string locale, IReadOnlyDictionary<string, LoadedLanguagePack> packs)
    {
        string culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return !string.IsNullOrWhiteSpace(locale) && packs.ContainsKey(locale)
            ? locale
            : packs.ContainsKey(culture)
            ? culture
            : culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && packs.ContainsKey("zh-CN")
            ? "zh-CN"
            : packs.ContainsKey("en-US") ? "en-US" : packs.Keys.FirstOrDefault() ?? "en-US";
    }
}
