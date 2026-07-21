using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 的 Content / ScriptSource 双根、缓存与增量刷新契约。
/// </summary>
public sealed class EditorAssetBrowserDualRootTests
{
    /// <summary>
    /// 验证生产数据源公开两个 logical root，且 Script 创建落到真正参与编译的 ScriptSource。
    /// </summary>
    [Fact]
    public void ProductionBrowserSeparatesRootsAndCreatesScriptsInCompiledSourceDirectory()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "DualRoot"), "Dual Root");
        File.WriteAllText(Path.Combine(project.ContentRootPath, "materials.json"), "{\"materials\":[]}");
        File.WriteAllText(
            Path.Combine(project.ScriptSourcePath, "ExistingBehaviour.cs"),
            "public sealed class ExistingBehaviour { }");

        using EditorAssetBrowserDataSource source = new(project);

        IReadOnlyList<AssetBrowserItem> initial = source.ListAssets();
        Assert.Contains(initial, item => item.Path == "Content/materials.json" && item.Kind == AssetBrowserItemKind.Material);
        Assert.Contains(initial, item => item.Path == "ScriptSource/ExistingBehaviour.cs" && item.Kind == AssetBrowserItemKind.Script);
        Assert.Contains(source.ListFolders(), folder => folder.Path == "Content");
        Assert.Contains(source.ListFolders(), folder => folder.Path == "ScriptSource");

        AssetBrowserCreateResult created = source.CreateAsset(
            new AssetBrowserCreateRequest("scripts/NewBehaviour.cs", AssetBrowserItemKind.Script));

        Assert.True(created.Succeeded, created.Diagnostic);
        Assert.Equal("ScriptSource/NewBehaviour.cs", created.Path);
        Assert.True(File.Exists(Path.Combine(project.ScriptSourcePath, "NewBehaviour.cs")));
        Assert.False(File.Exists(Path.Combine(project.ContentRootPath, "scripts", "NewBehaviour.cs")));
        Assert.Contains(
            source.ListAssets(),
            item => item.Path == "ScriptSource/NewBehaviour.cs" && item.AssetId == created.AssetId);
        Assert.True(File.Exists(Path.Combine(project.ProjectRoot, ".pixelengine", "script-assets.json")));
    }

    /// <summary>
    /// 验证 Demo 常见配置、音频、UI、字体与 probe 场景具备可理解用途，启动/当前 badge 随内存 Session 更新。
    /// </summary>
    [Fact]
    public void ProductionBrowserDescribesDemoResourceRolesAndDynamicBadges()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "DemoSemantics"), "Demo Semantics");
        string scenes = Path.Combine(project.ContentRootPath, "scenes");
        File.Copy(Path.Combine(scenes, "main.scene"), Path.Combine(scenes, "lava-mine.scene"));
        File.Copy(Path.Combine(scenes, "main.scene"), Path.Combine(scenes, "lava-mine-camera-probe.scene"));
        project.UpsertScene("scenes/lava-mine.scene", makeStartScene: true);
        File.WriteAllText(Path.Combine(project.ContentRootPath, "materials.json"), "{\"materials\":[]}");
        File.WriteAllText(Path.Combine(project.ContentRootPath, "reactions.json"), "{\"reactions\":[]}");
        File.WriteAllText(Path.Combine(project.ContentRootPath, "startup.json"), "{\"startScene\":\"scenes/lava-mine.scene\"}");
        File.WriteAllText(Path.Combine(project.ContentRootPath, "weapons.json"), "{\"items\":[]}");
        string audio = Path.Combine(project.ContentRootPath, "audio");
        _ = Directory.CreateDirectory(audio);
        File.WriteAllText(Path.Combine(audio, "cues.json"), "{\"cues\":[]}");
        File.WriteAllBytes(Path.Combine(audio, "lava_bubble_loop.wav"), [1, 2, 3, 4]);
        string screens = Path.Combine(project.ContentRootPath, "ui", "screens");
        string fonts = Path.Combine(project.ContentRootPath, "ui", "fonts");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(fonts);
        File.WriteAllText(Path.Combine(project.ContentRootPath, "ui", "ui-manifest.json"), "{\"screens\":[]}");
        File.WriteAllText(Path.Combine(screens, "main-menu.xhtml"), "<rml title=\"Main Menu\" />");
        File.WriteAllBytes(Path.Combine(fonts, "NotoSansSC-VF.ttf"), [1, 2, 3]);
        File.WriteAllText(
            Path.Combine(project.ScriptSourcePath, "DemoSceneAuthoringBehaviours.cs"),
            "public sealed class DemoSceneAuthoringBehaviours { }");
        string currentScene = "scenes/lava-mine.scene";

        using EditorAssetBrowserDataSource source = new(project, currentScenePath: () => currentScene);

        AssetBrowserItem materials = Assert.Single(source.ListAssets(), item => item.Path == "Content/materials.json");
        AssetBrowserItem reactions = Assert.Single(source.ListAssets(), item => item.Path == "Content/reactions.json");
        AssetBrowserItem startup = Assert.Single(source.ListAssets(), item => item.Path == "Content/startup.json");
        AssetBrowserItem weapons = Assert.Single(source.ListAssets(), item => item.Path == "Content/weapons.json");
        AssetBrowserItem cues = Assert.Single(source.ListAssets(), item => item.Path == "Content/audio/cues.json");
        AssetBrowserItem clip = Assert.Single(source.ListAssets(), item => item.Path == "Content/audio/lava_bubble_loop.wav");
        AssetBrowserItem uiManifest = Assert.Single(source.ListAssets(), item => item.Path == "Content/ui/ui-manifest.json");
        AssetBrowserItem screen = Assert.Single(source.ListAssets(), item => item.Path == "Content/ui/screens/main-menu.xhtml");
        AssetBrowserItem font = Assert.Single(source.ListAssets(), item => item.Path == "Content/ui/fonts/NotoSansSC-VF.ttf");
        AssetBrowserItem probe = Assert.Single(source.ListAssets(), item => item.Path == "Content/scenes/lava-mine-camera-probe.scene");
        AssetBrowserItem script = Assert.Single(source.ListAssets(), item => item.Path == "ScriptSource/DemoSceneAuthoringBehaviours.cs");

        Assert.Contains("CA 材质", materials.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Equal("材质反应规则", reactions.Descriptor?.TypeLabel);
        Assert.Contains("启动场景", startup.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine.scene", startup.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("武器", weapons.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("0 项", weapons.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("Cue", cues.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("0 项", cues.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("运行时音效", clip.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("预加载", uiManifest.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("游戏界面", screen.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Equal("字体", font.Descriptor?.TypeLabel);
        Assert.Equal(AssetBrowserBadge.Test, probe.Descriptor?.Badges);
        Assert.Contains("热重载", script.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Equal(
            AssetBrowserBadge.Startup | AssetBrowserBadge.Current,
            source.GetContextBadges("Content/scenes/lava-mine.scene"));
        Assert.Equal(AssetBrowserBadge.Startup, source.GetContextBadges("Content/startup.json"));

        currentScene = "scenes/lava-mine-camera-probe.scene";

        Assert.Equal(AssetBrowserBadge.Startup, source.GetContextBadges("Content/scenes/lava-mine.scene"));
        Assert.Equal(AssetBrowserBadge.Current, source.GetContextBadges("Content/scenes/lava-mine-camera-probe.scene"));
    }

    /// <summary>
    /// 验证重复只读查询只消费缓存，不改写两个 manifest。
    /// </summary>
    [Fact]
    public void CachedQueriesDoNotRewriteManifests()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Cached"), "Cached");
        using EditorAssetBrowserDataSource source = new(project);
        string contentManifest = Path.Combine(project.ProjectRoot, EditorAssetManifestStore.ManifestRelativePath);
        string scriptManifest = Path.Combine(project.ProjectRoot, ".pixelengine", "script-assets.json");
        DateTime marker = new(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(contentManifest, marker);
        File.SetLastWriteTimeUtc(scriptManifest, marker);

        for (int i = 0; i < 100; i++)
        {
            _ = source.ListAssets();
            _ = source.ListFolders();
        }

        Assert.Equal(marker, File.GetLastWriteTimeUtc(contentManifest));
        Assert.Equal(marker, File.GetLastWriteTimeUtc(scriptManifest));
        Assert.False(source.ApplyPendingChanges());
    }

    /// <summary>
    /// 验证 rooted move 保持 stable id，且创建和移动都不能跨越 Content / ScriptSource 职责边界。
    /// </summary>
    [Fact]
    public void RootedOperationsPreserveStableIdAndRejectCrossRootWrites()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Operations"), "Operations");
        using EditorAssetBrowserDataSource source = new(project);
        AssetBrowserCreateResult script = source.CreateAsset(
            new AssetBrowserCreateRequest("ScriptSource/Game/Player.cs", AssetBrowserItemKind.Script));
        Assert.True(script.Succeeded, script.Diagnostic);

        AssetBrowserMoveResult moved = source.MoveAsset(new AssetBrowserMoveRequest(
            script.Path!,
            script.AssetId!,
            AssetBrowserItemKind.Script,
            "ScriptSource/Runtime/Player.cs"));
        AssetBrowserMoveResult crossRoot = source.MoveAsset(new AssetBrowserMoveRequest(
            "ScriptSource/Runtime/Player.cs",
            script.AssetId!,
            AssetBrowserItemKind.Script,
            "Content/scripts/Player.cs"));
        AssetBrowserCreateResult wrongRoot = source.CreateAsset(
            new AssetBrowserCreateRequest("Content/scripts/Wrong.cs", AssetBrowserItemKind.Script));
        AssetBrowserFolderMoveResult moveRoot = source.MoveFolder(
            new AssetBrowserFolderMoveRequest("Content", "ContentMoved"));
        AssetBrowserFolderDeleteResult deleteRoot = source.DeleteFolder(
            new AssetBrowserFolderDeleteRequest("ScriptSource", [], Confirmed: true));

        Assert.True(moved.Succeeded, moved.Diagnostic);
        Assert.False(crossRoot.Succeeded);
        Assert.False(wrongRoot.Succeeded);
        Assert.False(moveRoot.Succeeded);
        Assert.False(deleteRoot.Succeeded);
        Assert.Contains("不能跨", crossRoot.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("ScriptSource", wrongRoot.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("logical root", moveRoot.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("logical root", deleteRoot.Diagnostic, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(project.ScriptSourcePath, "Game", "Player.cs")));
        Assert.True(File.Exists(Path.Combine(project.ScriptSourcePath, "Runtime", "Player.cs")));
        AssetBrowserItem movedItem = Assert.Single(
            source.ListAssets(),
            item => item.Path == "ScriptSource/Runtime/Player.cs");
        Assert.Equal(script.AssetId, movedItem.AssetId);
    }

    /// <summary>
    /// 验证 ScriptSource 位于 Content 子目录时脚本仍只显示一次，并归属 ScriptSource 根。
    /// </summary>
    [Fact]
    public void OverlappingPhysicalRootsDoNotDuplicateCompiledScripts()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Overlap"), "Overlap");
        project.ApplyProjectSettings(ProjectSettingsDto.CreateDefault(project.Name) with
        {
            ContentRoot = project.ContentRoot,
            ScriptSourceDir = "content/scripts",
            StartScene = project.StartScene,
        });
        _ = Directory.CreateDirectory(project.ScriptSourcePath);
        File.WriteAllText(
            Path.Combine(project.ScriptSourcePath, "CompiledBehaviour.cs"),
            "public sealed class CompiledBehaviour { }");

        using EditorAssetBrowserDataSource source = new(project);

        AssetBrowserItem script = Assert.Single(
            source.ListAssets(),
            item => item.DisplayName == "CompiledBehaviour.cs");
        Assert.Equal("ScriptSource/CompiledBehaviour.cs", script.Path);
        Assert.DoesNotContain(source.ListAssets(), item => item.Path == "Content/scripts/CompiledBehaviour.cs");
    }

    /// <summary>
    /// 验证 ScriptSource 资产移动仍会扫描 Content 场景并重写 typed script reference。
    /// </summary>
    [Fact]
    public void ScriptSourceMoveRewritesReferencesStoredInContentScenes()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "ScriptReferences"), "Script References");
        using EditorAssetBrowserDataSource source = new(project);
        AssetBrowserCreateResult script = source.CreateAsset(
            new AssetBrowserCreateRequest("ScriptSource/Game/Receiver.cs", AssetBrowserItemKind.Script));
        Assert.True(script.Succeeded, script.Diagnostic);
        string encoded = EditorAssetReferenceCodec.Encode(
            script.AssetId!,
            "Game/Receiver.cs",
            EditorAssetType.Script);
        string scenePath = Path.Combine(project.ContentRootPath, "scenes", "script-reference.scene");
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "script-reference",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = "Receiver",
                        Transform = new EngineSceneTransformDocument(),
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = "Probe",
                                SerializedFields = new Dictionary<string, string> { ["Script"] = encoded },
                            },
                        ],
                    },
                ],
            },
            scenePath);

        AssetBrowserMoveResult moved = source.MoveAsset(new AssetBrowserMoveRequest(
            script.Path!,
            script.AssetId!,
            AssetBrowserItemKind.Script,
            "ScriptSource/Runtime/Receiver.cs"));

        Assert.True(moved.Succeeded, moved.Diagnostic);
        string rewritten = EngineSceneDocumentLoader.LoadDocument(scenePath)
            .Entities![0]
            .Behaviours![0]
            .SerializedFields!["Script"];
        Assert.True(EditorAssetReferenceCodec.TryDecode(rewritten, out EditorAssetReference reference));
        Assert.Equal(script.AssetId, reference.AssetId);
        Assert.Equal("Runtime/Receiver.cs", reference.LogicalPath);
    }

    /// <summary>
    /// 验证监视器漏掉 rename 事件后，手动完整刷新会根据唯一身份映射继续重写 Content 场景引用。
    /// </summary>
    [Fact]
    public void FullRefreshReconcilesUnambiguousExternalMoveReferencesAfterWatcherMiss()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "RefreshReconcile"), "Refresh Reconcile");
        string texturePath = Path.Combine(project.ContentRootPath, "textures", "source.png");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllText(texturePath, "identity-preserving-texture");
        File.SetLastWriteTimeUtc(texturePath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        EditorAssetManifestStore store = new(project.ProjectRoot, project.ContentRootPath);
        using EditorAssetBrowserDataSource source = new(store);
        AssetBrowserItem texture = Assert.Single(
            source.ListAssets(),
            item => item.Path == "textures/source.png");
        string scenePath = Path.Combine(project.ContentRootPath, "scenes", "refresh-reference.scene");
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "refresh-reference",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = "Reference Holder",
                        Transform = new EngineSceneTransformDocument(),
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = "Probe",
                                SerializedFields = new Dictionary<string, string>
                                {
                                    ["Texture"] = EditorAssetReferenceCodec.Encode(
                                        texture.AssetId!,
                                        "textures/source.png",
                                        EditorAssetType.Texture),
                                },
                            },
                        ],
                    },
                ],
            },
            scenePath);
        source.RefreshAssets();
        string movedPath = Path.Combine(project.ContentRootPath, "textures", "moved.png");

        File.Move(texturePath, movedPath);
        source.RefreshAssets();

        AssetBrowserItem moved = Assert.Single(
            source.ListAssets(),
            item => item.Path == "textures/moved.png");
        Assert.Equal(texture.AssetId, moved.AssetId);
        string rewritten = EngineSceneDocumentLoader.LoadDocument(scenePath)
            .Entities![0]
            .Behaviours![0]
            .SerializedFields!["Texture"];
        Assert.True(EditorAssetReferenceCodec.TryDecode(rewritten, out EditorAssetReference reference));
        Assert.Equal(texture.AssetId, reference.AssetId);
        Assert.Equal("textures/moved.png", reference.LogicalPath);
    }

    /// <summary>
    /// 验证外部重命名启动场景时 stable id、Project StartScene 与磁盘 project 文档同步迁移。
    /// </summary>
    [Fact]
    public void ExternalStartSceneRenamePreservesIdAndSynchronizesProjectSettings()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "ExternalScene"), "External Scene");
        using EditorAssetBrowserDataSource source = new(project);
        AssetBrowserItem original = Assert.Single(
            source.ListAssets(),
            item => item.Path == "Content/scenes/main.scene");
        string oldPath = Path.Combine(project.ContentRootPath, "scenes", "main.scene");
        string newPath = Path.Combine(project.ContentRootPath, "scenes", "renamed.scene");

        File.Move(oldPath, newPath);

        Assert.True(SpinWait.SpinUntil(() =>
        {
            _ = source.ApplyPendingChanges();
            return source.ListAssets().Any(item => item.Path == "Content/scenes/renamed.scene");
        }, TimeSpan.FromSeconds(5)));
        AssetBrowserItem renamed = Assert.Single(
            source.ListAssets(),
            item => item.Path == "Content/scenes/renamed.scene");
        Assert.Equal(original.AssetId, renamed.AssetId);
        Assert.Equal("scenes/renamed.scene", project.StartScene);
        EditorProject reloaded = EditorProject.Load(project.ProjectRoot);
        Assert.Equal("scenes/renamed.scene", reloaded.StartScene);
        Assert.DoesNotContain(reloaded.Scenes, scene => scene.Path == "scenes/main.scene");
    }

    /// <summary>
    /// 验证 Project Window 文件夹移动会同步其中 Scene 的启动与工程目录配置。
    /// </summary>
    [Fact]
    public void ContentFolderMoveSynchronizesContainedSceneSettings()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "SceneFolder"), "Scene Folder");
        using EditorAssetBrowserDataSource source = new(project);

        AssetBrowserFolderMoveResult moved = source.MoveFolder(
            new AssetBrowserFolderMoveRequest("Content/scenes", "Content/levels"));

        Assert.True(moved.Succeeded, moved.Diagnostic);
        Assert.Equal("levels/main.scene", project.StartScene);
        Assert.True(File.Exists(Path.Combine(project.ContentRootPath, "levels", "main.scene")));
        Assert.False(Directory.Exists(Path.Combine(project.ContentRootPath, "scenes")));
        Assert.Equal("levels/main.scene", EditorProject.Load(project.ProjectRoot).StartScene);
        Assert.Contains("settings=", moved.Diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 FileSystemWatcher 只给出文件夹 rename 时仍能批量迁移其中 Scene 的稳定身份与启动配置。
    /// </summary>
    [Fact]
    public void ExternalContentFolderRenameSynchronizesContainedSceneSettings()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "ExternalFolder"), "External Folder");
        using EditorAssetBrowserDataSource source = new(project);
        string stableId = Assert.Single(source.ListAssets(), item => item.Path == "Content/scenes/main.scene").AssetId!;
        Directory.Move(
            Path.Combine(project.ContentRootPath, "scenes"),
            Path.Combine(project.ContentRootPath, "levels"));

        Assert.True(SpinWait.SpinUntil(() =>
        {
            _ = source.ApplyPendingChanges();
            return source.ListAssets().Any(item => item.Path == "Content/levels/main.scene");
        }, TimeSpan.FromSeconds(5)));
        AssetBrowserItem moved = Assert.Single(source.ListAssets(), item => item.Path == "Content/levels/main.scene");
        Assert.Equal(stableId, moved.AssetId);
        Assert.Equal("levels/main.scene", project.StartScene);
        Assert.Equal("levels/main.scene", EditorProject.Load(project.ProjectRoot).StartScene);
    }

    /// <summary>
    /// 验证外部扩展名变更不会打断 Editor 帧，并把旧资产作为删除、新资产作为新类型处理。
    /// </summary>
    [Fact]
    public void ExternalTypeChangingRenameDoesNotBreakIncrementalPump()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "TypeRename"), "Type Rename");
        using EditorAssetBrowserDataSource source = new(project);
        AssetBrowserCreateResult json = source.CreateAsset(
            new AssetBrowserCreateRequest("Content/data/value.json", AssetBrowserItemKind.Json));
        Assert.True(json.Succeeded, json.Diagnostic);
        File.Move(
            Path.Combine(project.ContentRootPath, "data", "value.json"),
            Path.Combine(project.ContentRootPath, "data", "value.txt"));

        Assert.True(SpinWait.SpinUntil(() =>
        {
            _ = source.ApplyPendingChanges();
            return source.ListAssets().Any(item => item.Path == "Content/data/value.txt");
        }, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain(source.ListAssets(), item => item.Path == "Content/data/value.json");
        AssetBrowserItem replacement = Assert.Single(source.ListAssets(), item => item.Path == "Content/data/value.txt");
        Assert.Equal(AssetBrowserItemKind.Other, replacement.Kind);
    }

    /// <summary>
    /// 验证 ScriptSource 资产删除仍以 Content 场景中的 typed reference 为预检依据。
    /// </summary>
    [Fact]
    public void ScriptSourceDeleteIsBlockedByContentSceneReference()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "ScriptDelete"), "Script Delete");
        using EditorAssetBrowserDataSource source = new(project);
        AssetBrowserCreateResult script = source.CreateAsset(
            new AssetBrowserCreateRequest("ScriptSource/Game/Referenced.cs", AssetBrowserItemKind.Script));
        Assert.True(script.Succeeded, script.Diagnostic);
        string encoded = EditorAssetReferenceCodec.Encode(script.AssetId!, "Game/Referenced.cs", EditorAssetType.Script);
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "script-delete-reference",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = "Receiver",
                        Transform = new EngineSceneTransformDocument(),
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = "Probe",
                                SerializedFields = new Dictionary<string, string> { ["Script"] = encoded },
                            },
                        ],
                    },
                ],
            },
            Path.Combine(project.ContentRootPath, "scenes", "script-delete-reference.scene"));

        AssetBrowserDeleteResult deleted = source.DeleteAsset(new AssetBrowserDeleteRequest(
            script.Path!,
            script.AssetId!,
            AssetBrowserItemKind.Script,
            Confirmed: true));

        Assert.False(deleted.Succeeded);
        Assert.False(deleted.RequiresConfirmation);
        Assert.Contains("仍被", deleted.Diagnostic, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.ScriptSourcePath, "Game", "Referenced.cs")));
    }

    /// <summary>
    /// 验证外部脚本重命名通过 watcher 增量刷新，并沿用原 stable asset id。
    /// </summary>
    [Fact]
    public void ExternalScriptRenameRefreshesIncrementallyAndPreservesStableId()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Incremental"), "Incremental");
        string oldPath = Path.Combine(project.ScriptSourcePath, "OldBehaviour.cs");
        string newPath = Path.Combine(project.ScriptSourcePath, "RenamedBehaviour.cs");
        File.WriteAllText(oldPath, "public sealed class OldBehaviour { }");
        using EditorAssetBrowserDataSource source = new(project);
        string stableId = Assert.Single(
            source.ListAssets(),
            item => item.Path == "ScriptSource/OldBehaviour.cs").AssetId!;

        File.Move(oldPath, newPath);

        bool applied = SpinWait.SpinUntil(source.ApplyPendingChanges, TimeSpan.FromSeconds(5));
        Assert.True(applied);
        AssetBrowserItem renamed = Assert.Single(
            source.ListAssets(),
            item => item.Path == "ScriptSource/RenamedBehaviour.cs");
        Assert.Equal(stableId, renamed.AssetId);
        Assert.DoesNotContain(source.ListAssets(), item => item.Path == "ScriptSource/OldBehaviour.cs");
    }

    /// <summary>
    /// 验证真实 Demo 的配置、音频、UI、字体、材质图与 probe 场景都带可理解用途和动态角色 badge。
    /// </summary>
    [Fact]
    public void DemoProjectAssetsExposeSemanticDescriptorsAndDynamicBadges()
    {
        string demoRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo");
        EditorProject project = EditorProject.Load(demoRoot);
        string currentScene = project.StartScene;
        using EditorAssetBrowserDataSource source = new(project, currentScenePath: () => currentScene);

        AssetBrowserItem materials = Find("Content/materials.json");
        AssetBrowserItem reactions = Find("Content/reactions.json");
        AssetBrowserItem startup = Find("Content/startup.json");
        AssetBrowserItem weapons = Find("Content/weapons.json");
        AssetBrowserItem cues = Find("Content/audio/cues.json");
        AssetBrowserItem audio = Find("Content/audio/lava_bubble_loop.wav");
        AssetBrowserItem uiManifest = Find("Content/ui/ui-manifest.json");
        AssetBrowserItem uiScreen = Find("Content/ui/screens/main-menu.xhtml");
        AssetBrowserItem telemetryScreen = Find("Content/ui/screens/telemetry.xhtml");
        AssetBrowserItem font = Find("Content/ui/fonts/NotoSansSC-VF.ttf");
        AssetBrowserItem materialMap = Find("Content/maps/ai-cavern-material-map.png");
        AssetBrowserItem mainScene = Find("Content/scenes/infinite-sandbox.scene");
        AssetBrowserItem cameraProbe = Find("Content/scenes/lava-mine-camera-probe.scene");
        AssetBrowserItem script = Find("ScriptSource/LevelDirector.cs");

        Assert.Contains("CA 材质", materials.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("材质目录：21", materials.PreviewSummary, StringComparison.Ordinal);
        Assert.Equal("材质反应规则", reactions.Descriptor?.TypeLabel);
        Assert.Contains("反应规则：9", reactions.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("启动场景", startup.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("scenes/infinite-sandbox.scene", startup.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("武器", weapons.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("武器目录：6", weapons.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("Cue", cues.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("音频 Cue 映射：13", cues.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("运行时音效", audio.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("预加载", uiManifest.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("10 个 Screen", uiManifest.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("10 个预加载", uiManifest.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("Web-first", uiScreen.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("id=main-menu", uiScreen.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("demo.webfirst.main-menu/v2", uiScreen.PreviewSummary, StringComparison.Ordinal);
        Assert.Contains("id=telemetry", telemetryScreen.PreviewSummary, StringComparison.Ordinal);
        Assert.Equal("字体", font.Descriptor?.TypeLabel);
        Assert.Contains("初始世界材质图", materialMap.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Contains("相机跟随", cameraProbe.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Equal(AssetBrowserBadge.Test, cameraProbe.Descriptor?.Badges);
        Assert.Contains("热重载", script.Descriptor?.Purpose, StringComparison.Ordinal);
        Assert.Equal(
            AssetBrowserBadge.Startup | AssetBrowserBadge.Current,
            source.GetContextBadges(mainScene.Path));
        Assert.Equal(AssetBrowserBadge.Startup, source.GetContextBadges(startup.Path));

        currentScene = "scenes/lava-mine-camera-probe.scene";
        Assert.Equal(AssetBrowserBadge.Startup, source.GetContextBadges(mainScene.Path));
        Assert.Equal(AssetBrowserBadge.Current, source.GetContextBadges(cameraProbe.Path));
        Assert.Equal("scenes/infinite-sandbox.scene", project.StartScene);

        AssetBrowserItem Find(string path)
        {
            return Assert.Single(source.ListAssets(), item => item.Path == path);
        }
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

        throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-dual-root-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
