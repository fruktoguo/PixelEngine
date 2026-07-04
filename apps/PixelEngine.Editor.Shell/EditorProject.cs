using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorProject
{
    public const int CurrentFormatVersion = 1;
    public const string ProjectFileName = "project.pixelproj";
    public const string DefaultContentRoot = "content";
    public const string DefaultScriptSourceDir = "scripts";
    public const string DefaultStartScene = "scenes/main.scene";

    private EditorProject(
        string projectRoot,
        string projectFilePath,
        EditorProjectDocument document,
        EditorProjectSceneEntry[] scenes)
    {
        ProjectRoot = projectRoot;
        ProjectFilePath = projectFilePath;
        Document = document;
        Scenes = scenes;
    }

    public string ProjectRoot { get; }

    public string ProjectFilePath { get; }

    public EditorProjectDocument Document { get; private set; }

    public string Name => Document.Name;

    public string ContentRoot => Document.ContentRoot;

    public string ScriptSourceDir => Document.ScriptSourceDir;

    public string StartScene => Document.StartScene;

    public string ContentRootPath => Path.GetFullPath(Path.Combine(ProjectRoot, ContentRoot));

    public string ScriptSourcePath => Path.GetFullPath(Path.Combine(ProjectRoot, ScriptSourceDir));

    public IReadOnlyList<EditorProjectSceneEntry> Scenes { get; private set; }

    public static EditorProject CreateNew(string projectRoot, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string root = Path.GetFullPath(projectRoot);
        string projectFile = Path.Combine(root, ProjectFileName);
        if (File.Exists(projectFile))
        {
            throw new InvalidOperationException($"工程文件已存在：{projectFile}");
        }

        EditorProjectDocument document = new()
        {
            FormatVersion = CurrentFormatVersion,
            Name = name.Trim(),
            ContentRoot = DefaultContentRoot,
            ScriptSourceDir = DefaultScriptSourceDir,
            StartScene = DefaultStartScene,
            Scenes =
            [
                new EditorProjectSceneEntry
                {
                    Name = "main",
                    Path = DefaultStartScene,
                },
            ],
        };

        _ = Directory.CreateDirectory(root);
        _ = Directory.CreateDirectory(Path.Combine(root, document.ContentRoot));
        _ = Directory.CreateDirectory(Path.Combine(root, document.ContentRoot, "scenes"));
        _ = Directory.CreateDirectory(Path.Combine(root, document.ScriptSourceDir));

        SaveDocument(projectFile, document);
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "main",
                Entities = [],
            },
            Path.Combine(root, document.ContentRoot, DefaultStartScene));

        return Load(root);
    }

    public static EditorProject Load(string projectRootOrFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootOrFile);
        string full = Path.GetFullPath(projectRootOrFile);
        string projectFile = Directory.Exists(full) || !Path.GetExtension(full).Equals(".pixelproj", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(full, ProjectFileName)
            : full;
        if (!File.Exists(projectFile))
        {
            throw new FileNotFoundException("未找到 PixelEngine 工程文件。", projectFile);
        }

        string json = File.ReadAllText(projectFile);
        EditorProjectDocument document = JsonSerializer.Deserialize(
                json,
                EditorShellJsonContext.Default.EditorProjectDocument) ??
            throw new JsonException("工程文件为空或格式无效。");
        Validate(document, projectFile);
        EditorProjectDocument normalized = Normalize(document);
        EditorProjectSceneEntry[] scenes = normalized.Scenes is { Length: > 0 }
            ? normalized.Scenes
            :
            [
                new EditorProjectSceneEntry
                {
                    Name = Path.GetFileNameWithoutExtension(normalized.StartScene),
                    Path = normalized.StartScene,
                },
            ];

        return new EditorProject(Path.GetDirectoryName(projectFile)!, projectFile, normalized, scenes);
    }

    public void Save()
    {
        SaveDocument(ProjectFilePath, Document);
    }

    public EngineProject ToEngineProject(string? sceneOverridePath = null)
    {
        string startScenePath = ResolveSceneRelativePath(sceneOverridePath);
        EditorProjectSceneEntry[] entries = EnsureSceneEntry([.. Scenes], startScenePath);
        SceneDescriptor[] descriptors = new SceneDescriptor[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            EditorProjectSceneEntry scene = entries[i];
            descriptors[i] = new SceneDescriptor(scene.Name, SceneSourceKind.SceneFile, scene.Path);
        }

        string startSceneName = ResolveSceneName(startScenePath);
        return new EngineProject(ContentRootPath, startSceneName, descriptors);
    }

    public string ResolveDisplaySceneName(string? overrideScenePath)
    {
        return ResolveSceneName(ResolveSceneRelativePath(overrideScenePath));
    }

    public string ResolveSceneRelativePath(string? overrideScenePath)
    {
        string scenePath = string.IsNullOrWhiteSpace(overrideScenePath) ? StartScene : overrideScenePath.Trim();
        string relative = Path.IsPathRooted(scenePath)
            ? Path.GetRelativePath(ContentRootPath, scenePath)
            : scenePath;
        return relative.Replace('\\', '/');
    }

    public string ResolveSceneFullPath(string? overrideScenePath)
    {
        return Path.GetFullPath(Path.Combine(ContentRootPath, ResolveSceneRelativePath(overrideScenePath)));
    }

    public void UpsertScene(string relativePath, bool makeStartScene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        EditorProjectSceneEntry[] entries = EnsureSceneEntry([.. Scenes], normalizedPath);
        Document = new EditorProjectDocument
        {
            FormatVersion = CurrentFormatVersion,
            Name = Name,
            ContentRoot = ContentRoot,
            ScriptSourceDir = ScriptSourceDir,
            StartScene = makeStartScene ? normalizedPath : StartScene,
            Scenes = entries,
        };
        Scenes = entries;
        Save();
    }

    private string ResolveSceneName(string relativePath)
    {
        for (int i = 0; i < Scenes.Count; i++)
        {
            if (string.Equals(Scenes[i].Path, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(Scenes[i].Name)
                    ? Path.GetFileNameWithoutExtension(relativePath) ?? relativePath
                    : Scenes[i].Name;
            }
        }

        return Path.GetFileNameWithoutExtension(relativePath) ?? relativePath;
    }

    private static EditorProjectSceneEntry[] EnsureSceneEntry(EditorProjectSceneEntry[] entries, string relativePath)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.Equals(entries[i].Path, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return entries;
            }
        }

        string name = Path.GetFileNameWithoutExtension(relativePath) ?? relativePath;
        return
        [
            .. entries,
            new EditorProjectSceneEntry
            {
                Name = name,
                Path = relativePath,
            },
        ];
    }

    private static void SaveDocument(string projectFile, EditorProjectDocument document)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(projectFile));
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            Normalize(document),
            EditorShellJsonContext.Default.EditorProjectDocument);
        File.WriteAllText(projectFile, json);
    }

    private static EditorProjectDocument Normalize(EditorProjectDocument document)
    {
        return new EditorProjectDocument
        {
            FormatVersion = CurrentFormatVersion,
            Name = document.Name.Trim(),
            ContentRoot = string.IsNullOrWhiteSpace(document.ContentRoot) ? DefaultContentRoot : document.ContentRoot.Trim(),
            ScriptSourceDir = string.IsNullOrWhiteSpace(document.ScriptSourceDir) ? DefaultScriptSourceDir : document.ScriptSourceDir.Trim(),
            StartScene = string.IsNullOrWhiteSpace(document.StartScene) ? DefaultStartScene : document.StartScene.Trim(),
            Scenes = document.Scenes ?? [],
        };
    }

    private static void Validate(EditorProjectDocument document, string projectFile)
    {
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的工程格式版本：{document.FormatVersion}。");
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new InvalidOperationException($"{projectFile} 缺少工程名称。");
        }
    }
}

internal sealed class EditorProjectDocument
{
    public int FormatVersion { get; init; } = EditorProject.CurrentFormatVersion;

    public string Name { get; init; } = string.Empty;

    public string ContentRoot { get; init; } = EditorProject.DefaultContentRoot;

    public string ScriptSourceDir { get; init; } = EditorProject.DefaultScriptSourceDir;

    public string StartScene { get; init; } = EditorProject.DefaultStartScene;

    public EditorProjectSceneEntry[]? Scenes { get; init; }
}

internal sealed class EditorProjectSceneEntry
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(EditorProjectDocument))]
[JsonSerializable(typeof(RecentProjectsDocument))]
internal sealed partial class EditorShellJsonContext : JsonSerializerContext
{
}
