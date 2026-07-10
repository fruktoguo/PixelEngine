using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// 用户级 Editor Preferences 文档。
/// </summary>
internal sealed record EditorPreferencesDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public float UiScale { get; init; } = EditorUiScale.Default;

    public bool SaveLayoutOnExit { get; init; } = true;

    public bool ReopenLastProject { get; init; } = true;

    public bool RestoreLastScene { get; init; } = true;

    public string ExternalScriptEditor { get; init; } = string.Empty;

    public bool TryNormalize(out EditorPreferencesDocument normalized, out string diagnostic)
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            normalized = new EditorPreferencesDocument();
            diagnostic = $"不支持的 Editor Preferences 版本：{FormatVersion}。";
            return false;
        }

        normalized = this with
        {
            UiScale = EditorUiScale.Normalize(UiScale),
            ExternalScriptEditor = ExternalScriptEditor?.Trim() ?? string.Empty,
        };
        diagnostic = string.Empty;
        return true;
    }
}

/// <summary>
/// 将用户级 Editor Preferences 原子持久化到 AppData。
/// </summary>
internal sealed class EditorPreferencesStore
{
    private const string PreferencesPathEnvironmentVariable = "PIXELENGINE_EDITOR_PREFERENCES_PATH";

    private EditorPreferencesStore(string? path, EditorPreferencesDocument current, bool loadedFromDisk, string diagnostic)
    {
        StoragePath = path;
        Current = current;
        LoadedFromDisk = loadedFromDisk;
        LegacyMigrationHandled = loadedFromDisk;
        LastDiagnostic = diagnostic;
    }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelEngine",
        "editor-preferences.json");

    public EditorPreferencesDocument Current { get; private set; }

    public string LastDiagnostic { get; private set; }

    public bool LoadedFromDisk { get; private set; }

    public string? StoragePath { get; }

    private bool LegacyMigrationHandled { get; set; }

    public static EditorPreferencesStore LoadDefault()
    {
        string? overridePath = Environment.GetEnvironmentVariable(PreferencesPathEnvironmentVariable);
        return Load(string.IsNullOrWhiteSpace(overridePath) ? DefaultPath : overridePath);
    }

    public static EditorPreferencesStore CreateInMemory(EditorPreferencesDocument? initial = null)
    {
        EditorPreferencesDocument candidate = initial ?? new EditorPreferencesDocument();
        _ = candidate.TryNormalize(out EditorPreferencesDocument normalized, out _);
        return new EditorPreferencesStore(null, normalized, loadedFromDisk: false, string.Empty);
    }

    public static EditorPreferencesStore Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new EditorPreferencesStore(fullPath, new EditorPreferencesDocument(), loadedFromDisk: false, string.Empty);
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            EditorPreferencesDocument? document = JsonSerializer.Deserialize(
                json,
                EditorShellJsonContext.Default.EditorPreferencesDocument);
            return document is null
                ? new EditorPreferencesStore(
                    fullPath,
                    new EditorPreferencesDocument(),
                    loadedFromDisk: false,
                    "Editor Preferences 文件为空。")
                : document.TryNormalize(out EditorPreferencesDocument normalized, out string diagnostic)
                    ? new EditorPreferencesStore(fullPath, normalized, loadedFromDisk: true, string.Empty)
                    : new EditorPreferencesStore(
                    fullPath,
                    new EditorPreferencesDocument(),
                    loadedFromDisk: false,
                    diagnostic);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new EditorPreferencesStore(
                fullPath,
                new EditorPreferencesDocument(),
                loadedFromDisk: false,
                $"读取 Editor Preferences 失败：{exception.Message}");
        }
    }

    public bool TryUpdate(EditorPreferencesDocument next, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!next.TryNormalize(out EditorPreferencesDocument normalized, out diagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        if (!TryWrite(normalized, out diagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        Current = normalized;
        LoadedFromDisk = StoragePath is not null;
        LastDiagnostic = string.Empty;
        return true;
    }

    /// <summary>
    /// 仅在尚无有效全局文档时，从旧工程字段迁移编辑器级偏好。
    /// </summary>
    public bool TryMigrateLegacy(EditorPreferencesDto? legacy, out string diagnostic)
    {
        if (LegacyMigrationHandled || legacy is null)
        {
            diagnostic = string.Empty;
            return true;
        }

        string legacyCommand = legacy.ExternalScriptEditor?.Trim() ?? string.Empty;
        bool safeSystemDefaultCommand = string.IsNullOrEmpty(legacyCommand) ||
            string.Equals(legacyCommand, "system-default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(legacyCommand, "default", StringComparison.OrdinalIgnoreCase);
        bool migrated = TryUpdate(
            Current with
            {
                SaveLayoutOnExit = legacy.SaveLayoutOnExit,
                // 工程文件属于内容输入，不能静默迁移并执行其中的自定义 executable command。
                ExternalScriptEditor = safeSystemDefaultCommand ? string.Empty : Current.ExternalScriptEditor,
            },
            out diagnostic);
        LegacyMigrationHandled = migrated;
        if (migrated && !safeSystemDefaultCommand)
        {
            diagnostic = "已忽略旧工程中的自定义外部编辑器命令；请在 Edit > Preferences > External Tools 中重新确认。";
            LastDiagnostic = diagnostic;
        }

        return migrated;
    }

    private bool TryWrite(EditorPreferencesDocument document, out string diagnostic)
    {
        if (StoragePath is null)
        {
            diagnostic = string.Empty;
            return true;
        }

        string? directory = System.IO.Path.GetDirectoryName(StoragePath);
        string temporaryPath = $"{StoragePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                document,
                EditorShellJsonContext.Default.EditorPreferencesDocument);
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream, System.Text.Encoding.UTF8, bufferSize: 4096, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, StoragePath, overwrite: true);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"保存 Editor Preferences 失败：{exception.Message}";
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 主文件已通过原子 move 决定成败；临时文件清理失败不得覆盖该结果。
                }
            }
        }
    }
}
