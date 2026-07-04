using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 项目的工程纪律测试。
/// </summary>
public sealed class HostingProjectDisciplineTests
{
    /// <summary>
    /// 验证 Demo 只经 Hosting 与 Scripting 公开入口引用引擎。
    /// </summary>
    [Fact]
    public void DemoProjectReferencesOnlyPublicEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "demo", "PixelEngine.Demo", "PixelEngine.Demo.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Scripting"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
        Assert.Empty(ReadIncludes(project, "PackageReference"));
    }

    /// <summary>
    /// 验证独立编辑器壳位于 apps 层，只引用 Hosting、Editor 与 Gui 三个公开装配入口。
    /// </summary>
    [Fact]
    public void EditorShellProjectReferencesOnlyShellEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "PixelEngine.Editor.Shell.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Editor", "PixelEngine.Gui"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
    }

    /// <summary>
    /// 验证编辑器壳只通过中性 bootstrap 创建唯一窗口，不直接散落创建 RenderWindow。
    /// </summary>
    [Fact]
    public void EditorShellCreatesWindowOnlyThroughNeutralBootstrap()
    {
        string root = FindRepositoryRoot();
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "apps", "PixelEngine.Editor.Shell"), "*.cs").Select(File.ReadAllText));

        Assert.Contains("EditorShellWindow.Create()", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorHostBootstrap.Create", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderWindow.Create", shellSource.Replace("EditorHostBootstrap.Create", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证独立编辑器壳已经接入工程模型、最近工程、项目选择器、主菜单和布局宿主。
    /// </summary>
    [Fact]
    public void EditorShellWiresProjectPickerMenuAndLayout()
    {
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));

        Assert.Contains("project.pixelproj", shellSource, StringComparison.Ordinal);
        Assert.Contains("EngineProject", shellSource, StringComparison.Ordinal);
        Assert.Contains("RecentProjectsStore.LoadDefault()", shellSource, StringComparison.Ordinal);
        Assert.Contains("ProjectPicker.Draw(this)", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorMainMenuBar", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorShellLayout", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorDockSpace", shellSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Shell 菜单覆盖 plan/19 要求的顶级菜单，并包含 Build Settings 与 Reset Layout 入口。
    /// </summary>
    [Fact]
    public void EditorShellMainMenuDeclaresRequiredMenus()
    {
        string root = FindRepositoryRoot();
        string menu = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorMainMenuBar.cs"));

        foreach (string item in new[] { "File", "Edit", "GameObject", "Window", "Play", "Help", "Build Settings...", "Reset Layout" })
        {
            Assert.Contains(item, menu, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 3 通过 Shell adapter 接入 Engine，而不是让 Hosting 重新引用 Editor。
    /// </summary>
    [Fact]
    public void EditorShellSessionAttachesEngineThroughHostExtension()
    {
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));

        Assert.Contains("EditorProjectSession.Open", source, StringComparison.Ordinal);
        Assert.Contains(".WithProject(project.ToEngineProject(sceneRelativePath))", source, StringComparison.Ordinal);
        Assert.Contains(".UseVSync(true)", source, StringComparison.Ordinal);
        Assert.Contains(".AddEditorHostExtension(editorHost)", source, StringComparison.Ordinal);
        Assert.Contains("engine.AttachWindowRuntime(window)", source, StringComparison.Ordinal);
        Assert.Contains("EditorShellHostExtension : IEditorHostExtension", source, StringComparison.Ordinal);
        Assert.Contains("EditorRenderBridge.AttachIfEnabled", source, StringComparison.Ordinal);
        Assert.Contains("EditorWindowInputConnector", source, StringComparison.Ordinal);
        Assert.Contains("EngineEditorPlaySessionService", source, StringComparison.Ordinal);
        Assert.Contains("EngineWorldSnapshotStore", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSession.RunOneTick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderWindow.Create", source.Replace("EditorHostBootstrap.Create", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 4 拥有独立 authoring 模型、StableId 映射、层级面板与命令栈。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresGameObjectAuthoringModelAndHierarchy()
    {
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string editorSelection = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "EditorSelection.cs"));
        string hostingEngine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));

        Assert.Contains("class EditorSceneModel", source, StringComparison.Ordinal);
        Assert.Contains("class EditorGameObject", source, StringComparison.Ordinal);
        Assert.Contains("class EditorComponentModel", source, StringComparison.Ordinal);
        Assert.Contains("class EditorUndoStack", source, StringComparison.Ordinal);
        Assert.Contains("interface IEditorCommand", source, StringComparison.Ordinal);
        Assert.Contains("class GameObjectHierarchyPanel", source, StringComparison.Ordinal);
        Assert.Contains("class GameObjectInspectorPanel", source, StringComparison.Ordinal);
        Assert.Contains("EditorSceneRuntimeProjection", source, StringComparison.Ordinal);
        Assert.Contains("StableIdToEntityId", source, StringComparison.Ordinal);
        Assert.Contains("EngineSceneDocument", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureAuthoring(sceneModel, undoStack, prefabs)", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectHierarchyPanel(_sceneModel, _undoStack, _prefabs)", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectInspectorPanel(", source, StringComparison.Ordinal);
        Assert.Contains("engine.Context.GetService<ScriptAssemblyRegistry>()", source, StringComparison.Ordinal);
        Assert.Contains("new CreateGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new DeleteGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new ReparentGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new DuplicateGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("SetTransformCommand", source, StringComparison.Ordinal);
        Assert.Contains("AddComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("RemoveComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("MoveComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("SetComponentFieldCommand", source, StringComparison.Ordinal);
        Assert.Contains("ScriptInspector.InspectFields", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectStableId", editorSelection, StringComparison.Ordinal);
        Assert.Contains("SelectGameObject", editorSelection, StringComparison.Ordinal);
        Assert.Contains("AttachScriptScene(PixelEngine.Scripting.Scene scriptScene)", hostingEngine, StringComparison.Ordinal);
        Assert.DoesNotContain("new SceneHierarchyPanel", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 6 接入 Scene View、真实相机控制、ImGuizmo 变换与点选拾取。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresSceneViewGizmoCameraAndPicking()
    {
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        XDocument shellProject = XDocument.Load(Path.Combine(shellDirectory, "PixelEngine.Editor.Shell.csproj"));

        Assert.Contains("Hexa.NET.ImGuizmo", ReadIncludes(shellProject, "PackageReference"));
        Assert.Contains("class SceneViewPanel", source, StringComparison.Ordinal);
        Assert.Contains("new SceneViewPanel(", source, StringComparison.Ordinal);
        Assert.Contains("engine.Context.GetService<ScriptCameraApi>()", source, StringComparison.Ordinal);
        Assert.Contains("ScriptCameraApi camera", source, StringComparison.Ordinal);
        Assert.Contains("_camera.SetZoom", source, StringComparison.Ordinal);
        Assert.Contains("_camera.SetCenter", source, StringComparison.Ordinal);
        Assert.Contains("ViewportPanel.FitTexture", source, StringComparison.Ordinal);
        Assert.Contains("ViewportPanel.CreateTextureRef", source, StringComparison.Ordinal);
        Assert.Contains("MaterialBrushPalettePanel? brushPanel", source, StringComparison.Ordinal);
        Assert.Contains("brushPanel.ApplyAt", source, StringComparison.Ordinal);
        Assert.Contains("WantCaptureMouse", source, StringComparison.Ordinal);
        Assert.Contains("IsGizmoCapturingMouse", source, StringComparison.Ordinal);
        Assert.Contains("TryPick", source, StringComparison.Ordinal);
        Assert.Contains("SelectGameObject", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmo.Manipulate", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.Translate", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.RotateZ", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.Scale", source, StringComparison.Ordinal);
        Assert.Contains("new SetTransformCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ViewportPanel(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 7 接入 .scene 保存/Save As、当前场景路径与 prefab authoring 边界。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresSceneSaveAndPrefabAuthoring()
    {
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string editorAssetSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "AssetBrowserDataSource.cs"));
        string hostingSceneDocument = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "EngineSceneDocument.cs"));

        Assert.Contains("SceneOverridePath", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSceneRelativePath", source, StringComparison.Ordinal);
        Assert.Contains("SaveSceneAsAuto", source, StringComparison.Ordinal);
        Assert.Contains("SaveSceneAs(", source, StringComparison.Ordinal);
        Assert.Contains("Project.UpsertScene", source, StringComparison.Ordinal);
        Assert.Contains("Engine.SaveSceneDocument", source, StringComparison.Ordinal);
        Assert.Contains("class EditorPrefabAssetStore", source, StringComparison.Ordinal);
        Assert.Contains("CreatePrefabFromSubtree", source, StringComparison.Ordinal);
        Assert.Contains("InstantiatePrefab", source, StringComparison.Ordinal);
        Assert.Contains("CreatePrefabAssetCommand", source, StringComparison.Ordinal);
        Assert.Contains("InstantiatePrefabCommand", source, StringComparison.Ordinal);
        Assert.Contains("RevertPrefabOverridesCommand", source, StringComparison.Ordinal);
        Assert.Contains("RecordPrefabOverride", source, StringComparison.Ordinal);
        Assert.Contains("EngineScenePrefabDocument", hostingSceneDocument, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Prefab", editorAssetSource, StringComparison.Ordinal);
        Assert.Contains(".prefab", editorAssetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorPrefabAssetStore", File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Scripting", "Scene.cs")), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo 源码不绕过 Hosting/Scripting 公开入口访问内容或模拟实现。
    /// </summary>
    [Fact]
    public void DemoSourcesDoNotBypassHostingFacade()
    {
        string root = FindRepositoryRoot();
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "demo", "PixelEngine.Demo"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("using PixelEngine.Content", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using PixelEngine.Simulation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EngineContentLoader", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Materials.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Reactions.Count", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo lava-mine 验收场景以 .scene 文件落盘，并通过公开场景文档格式引用 LevelDirector。
    /// </summary>
    [Fact]
    public void DemoLavaMineSceneFileUsesLevelDirectorBehaviour()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine.scene");

        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        Assert.Equal("lava-mine", document.Name);
        EngineSceneBehaviourDocument behaviour = Assert.Single(Assert.Single(document.Entities!).Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("640", behaviour.SerializedFields!["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
        Assert.Equal("true", behaviour.SerializedFields["BuildScriptEntities"]);
    }

    /// <summary>
    /// 验证音频窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoAudioProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-audio-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-audio-probe");
    }

    /// <summary>
    /// 验证粒子 / 光照窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoParticleLightProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-particle-light-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-particle-light-probe");
    }

    /// <summary>
    /// 验证 Demo 可见内容包 API 不泄漏 Content / Simulation 实现类型。
    /// </summary>
    [Fact]
    public void EngineContentPackagePublicApiDoesNotExposeImplementationAssemblies()
    {
        foreach (MemberInfo member in typeof(EngineContentPackage).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (member is MethodInfo method)
            {
                AssertAllowedPublicType(method.ReturnType, member.Name);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
            else if (member is PropertyInfo property)
            {
                AssertAllowedPublicType(property.PropertyType, member.Name);
            }
            else if (member is ConstructorInfo constructor)
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
        }
    }

    /// <summary>
    /// 验证 Hosting 公开 API 都带中文 XML 文档注释。
    /// </summary>
    [Fact]
    public void HostingPublicApiMembersHaveChineseXmlDocumentation()
    {
        string root = FindRepositoryRoot();
        string projectDirectory = Path.Combine(root, "src", "PixelEngine.Hosting");
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string[] lines = File.ReadAllLines(file);
            int braceDepth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (braceDepth <= 1 && IsPublicApiDeclaration(lines[i]))
                {
                    int previous = PreviousNonAttributeLine(lines, i);
                    Assert.True(
                        previous >= 0 && IsChineseXmlDocumentationBlock(lines, previous),
                        $"{file}:{i + 1} 公开 API 缺少中文 XML 文档注释。");
                }

                braceDepth += GetBraceDepthDelta(lines[i]);
            }
        }
    }

    private static bool IsPublicApiDeclaration(string line)
    {
        string trimmed = line.TrimStart();
        return Regex.IsMatch(
            trimmed,
            @"^public\s+(sealed\s+|static\s+|readonly\s+|record\s+|enum\s+|interface\s+|class\s+|struct\s+|delegate\s+|[A-Z_a-z])");
    }

    private static bool IsChineseXmlDocumentationBlock(string[] lines, int lastDocumentationLine)
    {
        bool hasDocumentation = false;
        bool hasChinese = false;
        bool hasInheritdoc = false;
        for (int i = lastDocumentationLine; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            hasDocumentation = true;
            hasChinese |= Regex.IsMatch(trimmed, @"\p{IsCJKUnifiedIdeographs}");
            hasInheritdoc |= trimmed.Contains("<inheritdoc", StringComparison.Ordinal);
        }

        return hasDocumentation && hasChinese && !hasInheritdoc;
    }

    private static int PreviousNonAttributeLine(string[] lines, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetBraceDepthDelta(string line)
    {
        int delta = 0;
        foreach (char character in line)
        {
            if (character == '{')
            {
                delta++;
            }
            else if (character == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static void AssertAllowedPublicType(Type type, string memberName)
    {
        Type publicType = UnwrapType(type);
        Assert.False(
            publicType.Namespace?.StartsWith("PixelEngine.Simulation", StringComparison.Ordinal) == true ||
            publicType.Namespace?.StartsWith("PixelEngine.Content", StringComparison.Ordinal) == true,
            $"{memberName} 泄漏了实现类型 {publicType.FullName}。");
        foreach (Type argument in publicType.GenericTypeArguments)
        {
            AssertAllowedPublicType(argument, memberName);
        }
    }

    private static void AssertProbeSceneUsesVisibleLevelDirector(string scenePath, string expectedName)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        Assert.Equal(expectedName, document.Name);
        EngineSceneBehaviourDocument behaviour = Assert.Single(Assert.Single(document.Entities!).Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("true", behaviour.SerializedFields!["BuildScriptEntities"]);
        Assert.Equal("640", behaviour.SerializedFields["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
    }

    private static Type UnwrapType(Type type)
    {
        Type current = type;
        while (current.IsByRef || current.IsPointer || current.IsArray)
        {
            current = current.GetElementType()!;
        }

        return current;
    }

    private static string[] ReadIncludes(XDocument project, string elementName)
    {
        return
        [
            .. project
                .Descendants(elementName)
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!),
        ];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
