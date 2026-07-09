using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 资源浏览器面板测试。
/// 不变式：资源浏览与工程资产模型同步、过滤规则确定。
/// </summary>
public sealed class AssetBrowserPanelTests
{
    /// <summary>
    /// 验证文件系统数据源会分类 content 资产并接入缩略图提供器。
    /// </summary>
    [Fact]
    public void FileSystemDataSourceClassifiesContentAssetsAndThumbnails()
    {
        // Arrange：准备输入与初始状态
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-assets-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "materials.json"), "{}");
            _ = Directory.CreateDirectory(Path.Combine(root, "textures"));
            File.WriteAllBytes(Path.Combine(root, "textures", "sand.png"), [1, 2, 3]);
            _ = Directory.CreateDirectory(Path.Combine(root, "audio"));
            File.WriteAllBytes(Path.Combine(root, "audio", "hit.wav"), [4, 5, 6]);
            File.WriteAllText(Path.Combine(root, "demo.scene"), "{}");
            _ = Directory.CreateDirectory(Path.Combine(root, "prefabs"));
            File.WriteAllText(Path.Combine(root, "prefabs", "rock.prefab"), "{}");
            RecordingThumbnailProvider thumbnails = new();
            FileSystemAssetBrowserDataSource source = new(root, thumbnails);

            IReadOnlyList<AssetBrowserItem> assets = source.ListAssets();

            // Assert：验证预期结果
            Assert.Contains(assets, item => item.Path == "materials.json" && item.Kind == AssetBrowserItemKind.Material);
            _ = Directory.CreateDirectory(Path.Combine(root, "ui", "screens"));
            File.WriteAllText(Path.Combine(root, "ui", "screens", "hud.xhtml"), "<rml title=\"HUD\" />");
            assets = source.ListAssets();
            Assert.Contains(assets, item => item.Path == "ui/screens/hud.xhtml" && item.Kind == AssetBrowserItemKind.UiScreen);
            AssetBrowserItem texture = Assert.Single(assets, item => item.Path == "textures/sand.png");
            Assert.Equal(AssetBrowserItemKind.Texture, texture.Kind);
            Assert.Equal(new AssetThumbnail(12, 16, 16), texture.Thumbnail);
            Assert.Contains(assets, item => item.Path == "audio/hit.wav" && item.Kind == AssetBrowserItemKind.Audio);
            Assert.Contains(assets, item => item.Path == "demo.scene" && item.Kind == AssetBrowserItemKind.Scene);
            Assert.Contains(assets, item => item.Path == "prefabs/rock.prefab" && item.Kind == AssetBrowserItemKind.Prefab);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证资源浏览器可筛选、选择并试听音频。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelFiltersSelectsAndPreviewsAudio()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 10, DateTimeOffset.UnixEpoch, new AssetThumbnail(5, 8, 8)),
            new AssetBrowserItem("audio/hit.wav", AssetBrowserItemKind.Audio, 20, DateTimeOffset.UnixEpoch, null),
            new AssetBrowserItem("scenes/demo.scene", AssetBrowserItemKind.Scene, 30, DateTimeOffset.UnixEpoch, null),
        ]);
        RecordingAudioPreview preview = new();
        AssetBrowserPanel panel = new(source, preview);
        EditorSelection selection = new();

        _ = panel.Refresh();
        panel.SetSearch("audio");
        bool selected = panel.SelectAsset("audio/hit.wav", selection);
        bool played = panel.TryPreviewAudio("audio/hit.wav");

        // Assert：验证预期结果
        AssetBrowserItem filtered = Assert.Single(panel.FilteredAssets);
        Assert.Equal("audio/hit.wav", filtered.Path);
        Assert.True(selected);
        Assert.Equal("audio/hit.wav", selection.AssetPath);
        Assert.True(played);
        Assert.Equal(["audio/hit.wav"], preview.Played);
    }

    /// <summary>
    /// 验证 Project Window 选择资产与 Hierarchy 选择 GameObject 互斥，避免 Inspector 显示旧对象。
    /// </summary>
    [Fact]
    public void EditorSelectionSwitchesBetweenProjectAssetsAndGameObjects()
    {
        // Arrange：准备输入与初始状态
        EditorSelection selection = new();

        selection.SelectGameObject(7);
        selection.SelectAsset("scripts/Player.cs");

        // Assert：验证预期结果
        Assert.Equal("scripts/Player.cs", selection.AssetPath);
        Assert.Null(selection.GameObjectStableId);
        Assert.Null(selection.EntityHandle);
        Assert.Null(selection.BodyId);

        selection.SelectGameObject(9);

        Assert.Equal(9, selection.GameObjectStableId);
        Assert.Null(selection.AssetPath);
        Assert.Null(selection.EntityHandle);
        Assert.Null(selection.BodyId);
    }

    /// <summary>
    /// 验证 Project Window 支持类型过滤与稳定排序，满足资源搜索/过滤/排序产品契约。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelFiltersByKindAndSortsAssets()
    {
        // Arrange：准备输入与初始状态
        DateTimeOffset older = DateTimeOffset.UnixEpoch.AddMinutes(1);
        DateTimeOffset newer = DateTimeOffset.UnixEpoch.AddMinutes(2);
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("scripts/Player.cs", AssetBrowserItemKind.Script, 30, newer, null, "asset_script"),
            new AssetBrowserItem("textures/z-rock.png", AssetBrowserItemKind.Texture, 10, older, null, "asset_z_texture"),
            new AssetBrowserItem("audio/hit.wav", AssetBrowserItemKind.Audio, 90, older, null, "asset_audio"),
            new AssetBrowserItem("textures/a-sand.png", AssetBrowserItemKind.Texture, 60, newer, null, "asset_a_texture"),
        ]);
        AssetBrowserPanel panel = new(source);

        _ = panel.Refresh();
        panel.SetKindFilter(AssetBrowserItemKind.Texture);

        // Assert：验证预期结果
        Assert.Equal(AssetBrowserItemKind.Texture, panel.KindFilter);
        Assert.Equal(["textures/a-sand.png", "textures/z-rock.png"], panel.FilteredAssets.Select(item => item.Path));

        panel.SetSearch("rock");

        AssetBrowserItem filtered = Assert.Single(panel.FilteredAssets);
        Assert.Equal("textures/z-rock.png", filtered.Path);

        panel.SetSearch(string.Empty);
        panel.SetKindFilter(null);
        panel.SetSortMode(AssetBrowserSortMode.SizeDescending);

        Assert.Equal(AssetBrowserSortMode.SizeDescending, panel.SortMode);
        Assert.Equal(["audio/hit.wav", "textures/a-sand.png", "scripts/Player.cs", "textures/z-rock.png"], panel.FilteredAssets.Select(item => item.Path));

        panel.SetSortMode(AssetBrowserSortMode.KindThenPath);

        Assert.Equal(
            ["textures/a-sand.png", "textures/z-rock.png", "audio/hit.wav", "scripts/Player.cs"],
            panel.FilteredAssets.Select(item => item.Path));
    }

    /// <summary>
    /// 验证 Project Window 能为 Shell 资产拖拽语义创建 typed payload，并拒绝缺 stable id 的旧数据源项。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelCreatesTypedDragPayloadOnlyForStableAssets()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("prefabs/rock.prefab", AssetBrowserItemKind.Prefab, 10, DateTimeOffset.UnixEpoch, null, "asset_prefab"),
            new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
        ]);
        AssetBrowserPanel panel = new(source);

        _ = panel.Refresh();
        bool created = panel.TryCreateDragPayload("prefabs/rock.prefab", out AssetBrowserDragPayload payload);
        bool legacyCreated = panel.TryCreateDragPayload("textures/legacy.png", out AssetBrowserDragPayload legacyPayload);

        // Assert：验证预期结果
        Assert.True(created);
        Assert.Equal("asset_prefab", payload.AssetId);
        Assert.Equal("prefabs/rock.prefab", payload.Path);
        Assert.Equal(AssetBrowserItemKind.Prefab, payload.Kind);
        Assert.False(legacyCreated);
        Assert.Equal(default, legacyPayload);
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 只会把 script 资产交给外部编辑器回调，并把失败诊断写回状态。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelOpensOnlyScriptAssetsThroughCallbackAndStoresDiagnostics()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("scripts/PlayerController.cs", AssetBrowserItemKind.Script, 10, DateTimeOffset.UnixEpoch, null, "asset_script"),
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
        ]);
        List<string> opened = [];
        bool OpenScriptAsset(string path, out string diagnostic)
        {
            opened.Add(path);
            diagnostic = $"opened {path}";
            return true;
        }

        AssetBrowserPanel panel = new(source, openScriptAsset: OpenScriptAsset);

        _ = panel.Refresh();
        bool openedScript = panel.TryOpenScriptAsset("scripts/PlayerController.cs");
        bool openedTexture = panel.TryOpenScriptAsset("textures/sand.png");

        // Assert：验证预期结果
        Assert.True(openedScript);
        Assert.False(openedTexture);
        Assert.Equal(["scripts/PlayerController.cs"], opened);
        Assert.Contains("仅 script", panel.Status, StringComparison.Ordinal);

        static bool FailOpenScriptAsset(string path, out string diagnostic)
        {
            diagnostic = $"failed {path}";
            return false;
        }

        AssetBrowserPanel failingPanel = new(source, openScriptAsset: FailOpenScriptAsset);
        _ = failingPanel.Refresh();

        bool failed = failingPanel.TryOpenScriptAsset("scripts/PlayerController.cs");

        Assert.False(failed);
        Assert.Equal("failed scripts/PlayerController.cs", failingPanel.Status);
    }

    /// <summary>
    /// 验证 Project Window 删除动作必须先经确认回调，且缺 stable id 的旧数据源项不能删除。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelDeletesOnlyStableAssetsAfterConfirmation()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
            new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
        ]);
        List<AssetBrowserDeleteRequest> requests = [];
        AssetBrowserDeleteResult DeleteAsset(AssetBrowserDeleteRequest request)
        {
            requests.Add(request);
            return request.Confirmed
                ? new AssetBrowserDeleteResult(true, false, $"deleted {request.Path}")
                : new AssetBrowserDeleteResult(false, true, $"confirm {request.Path}");
        }

        AssetBrowserPanel panel = new(source, deleteAsset: DeleteAsset);

        _ = panel.Refresh();
        bool requested = panel.TryRequestDeleteAsset("textures/sand.png");
        bool confirmed = panel.TryConfirmDeleteAsset("textures/sand.png");
        bool legacy = panel.TryRequestDeleteAsset("textures/legacy.png");

        // Assert：验证预期结果
        Assert.False(requested);
        Assert.True(confirmed);
        Assert.False(legacy);
        Assert.Equal(2, requests.Count);
        Assert.False(requests[0].Confirmed);
        Assert.True(requests[1].Confirmed);
        Assert.Equal("asset_texture", requests[1].AssetId);
        Assert.Equal(AssetBrowserItemKind.Texture, requests[1].Kind);
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证删除确认绑定原始 stable asset id，资产刷新成同路径新 id 后不能复用旧确认。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelInvalidatesDeleteConfirmationWhenAssetIdChanges()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_old"),
        ]);
        List<AssetBrowserDeleteRequest> requests = [];
        AssetBrowserDeleteResult DeleteAsset(AssetBrowserDeleteRequest request)
        {
            requests.Add(request);
            return new AssetBrowserDeleteResult(false, true, $"confirm {request.AssetId}");
        }

        AssetBrowserPanel panel = new(source, deleteAsset: DeleteAsset);

        _ = panel.Refresh();
        bool requested = panel.TryRequestDeleteAsset("textures/sand.png");
        source.ReplaceAssets(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_new"),
        ]);
        _ = panel.Refresh();
        bool confirmed = panel.TryConfirmDeleteAsset("textures/sand.png");

        // Assert：验证预期结果
        Assert.False(requested);
        Assert.False(confirmed);
        AssetBrowserDeleteRequest request = Assert.Single(requests);
        Assert.Equal("asset_old", request.AssetId);
        Assert.False(request.Confirmed);
        Assert.Contains("删除确认已失效", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 移动 / 重命名动作必须携带 stable asset id，并在成功后刷新资产列表。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelMovesOnlyStableAssetsThroughCallbackAndRefreshes()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
            new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
        ]);
        List<AssetBrowserMoveRequest> requests = [];
        AssetBrowserMoveResult MoveAsset(AssetBrowserMoveRequest request)
        {
            requests.Add(request);
            source.ReplaceAssets(
            [
                new AssetBrowserItem(request.NewPath, request.Kind, 20, DateTimeOffset.UnixEpoch, null, request.AssetId),
                new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
            ]);
            return new AssetBrowserMoveResult(true, $"moved {request.NewPath}");
        }

        AssetBrowserPanel panel = new(source, moveAsset: MoveAsset);

        _ = panel.Refresh();
        bool moved = panel.TryMoveAsset("textures/sand.png", "textures/renamed/sand.png");
        bool legacy = panel.TryMoveAsset("textures/legacy.png", "textures/renamed/legacy.png");

        // Assert：验证预期结果
        Assert.True(moved);
        Assert.False(legacy);
        AssetBrowserMoveRequest request = Assert.Single(requests);
        Assert.Equal("asset_texture", request.AssetId);
        Assert.Equal("textures/sand.png", request.Path);
        Assert.Equal("textures/renamed/sand.png", request.NewPath);
        Assert.Equal(AssetBrowserItemKind.Texture, request.Kind);
        Assert.Contains(panel.LastAssets, asset => asset.Path == "textures/renamed/sand.png" && asset.AssetId == "asset_texture");
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 文件夹拖拽移动会把 stable typed payload 转成同一套 move request。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelMovesDragPayloadToFolderThroughStableRequest()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
            new AssetBrowserItem("archive/placeholder.png", AssetBrowserItemKind.Texture, 10, DateTimeOffset.UnixEpoch, null, "asset_placeholder"),
        ]);
        List<AssetBrowserMoveRequest> requests = [];
        AssetBrowserMoveResult MoveAsset(AssetBrowserMoveRequest request)
        {
            requests.Add(request);
            source.ReplaceAssets(
            [
                new AssetBrowserItem(request.NewPath, request.Kind, 20, DateTimeOffset.UnixEpoch, null, request.AssetId),
                new AssetBrowserItem("archive/placeholder.png", AssetBrowserItemKind.Texture, 10, DateTimeOffset.UnixEpoch, null, "asset_placeholder"),
            ]);
            return new AssetBrowserMoveResult(true, $"moved {request.NewPath}");
        }

        AssetBrowserPanel panel = new(source, moveAsset: MoveAsset);

        _ = panel.Refresh();
        bool moved = panel.TryMoveDragPayloadToFolder(
            new AssetBrowserDragPayload("asset_texture", "textures/sand.png", AssetBrowserItemKind.Texture),
            "archive");

        // Assert：验证预期结果
        Assert.True(moved);
        Assert.Contains(panel.FolderTargets, folder => folder.Path == string.Empty && folder.AssetCount == 2);
        Assert.Contains(panel.FolderTargets, folder => folder.Path == "archive");
        AssetBrowserMoveRequest request = Assert.Single(requests);
        Assert.Equal("asset_texture", request.AssetId);
        Assert.Equal("textures/sand.png", request.Path);
        Assert.Equal("archive/sand.png", request.NewPath);
        Assert.Equal(AssetBrowserItemKind.Texture, request.Kind);
        Assert.Contains(panel.LastAssets, asset => asset.Path == "archive/sand.png" && asset.AssetId == "asset_texture");
    }

    /// <summary>
    /// 验证文件夹拖拽移动绑定 stable asset id 和资产类型，旧 payload 不能移动同路径新资产。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelRejectsStaleDragMovePayloads()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_new"),
        ]);
        List<AssetBrowserMoveRequest> requests = [];
        AssetBrowserPanel panel = new(source, moveAsset: request =>
        {
            requests.Add(request);
            return new AssetBrowserMoveResult(true, $"moved {request.NewPath}");
        });

        _ = panel.Refresh();
        bool movedOldId = panel.TryMoveDragPayloadToFolder(
            new AssetBrowserDragPayload("asset_old", "textures/sand.png", AssetBrowserItemKind.Texture),
            "archive");
        bool movedWrongKind = panel.TryMoveDragPayloadToFolder(
            new AssetBrowserDragPayload("asset_new", "textures/sand.png", AssetBrowserItemKind.Audio),
            "archive");
        bool escaped = panel.TryMoveDragPayloadToFolder(
            new AssetBrowserDragPayload("asset_new", "textures/sand.png", AssetBrowserItemKind.Texture),
            "../outside");

        // Assert：验证预期结果
        Assert.False(movedOldId);
        Assert.False(movedWrongKind);
        Assert.False(escaped);
        Assert.Empty(requests);
        Assert.Contains("content 根目录", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证移动确认绑定原始 stable asset id，资产刷新成同路径新 id 后不能复用旧确认。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelInvalidatesMoveConfirmationWhenAssetIdChanges()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_old"),
        ]);
        List<AssetBrowserMoveRequest> requests = [];
        AssetBrowserPanel panel = new(source, moveAsset: request =>
        {
            requests.Add(request);
            return new AssetBrowserMoveResult(true, $"moved {request.NewPath}");
        });

        _ = panel.Refresh();
        bool requested = panel.BeginMoveAsset("textures/sand.png");
        source.ReplaceAssets(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_new"),
        ]);
        _ = panel.Refresh();
        bool confirmed = panel.TryConfirmMoveAsset("textures/sand.png");

        // Assert：验证预期结果
        Assert.True(requested);
        Assert.False(confirmed);
        Assert.Empty(requests);
        Assert.Contains("移动目标已失效", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 创建资产入口通过 Shell 回调创建稳定资产并刷新列表。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelCreatesAssetsThroughCallbackAndRefreshes()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new([]);
        List<AssetBrowserCreateRequest> requests = [];
        List<AssetBrowserItem> createdAssets = [];
        AssetBrowserCreateResult CreateAsset(AssetBrowserCreateRequest request)
        {
            requests.Add(request);
            createdAssets.Add(new AssetBrowserItem(request.Path, request.Kind, 10, DateTimeOffset.UnixEpoch, null, "asset_created"));
            source.ReplaceAssets(createdAssets);
            return new AssetBrowserCreateResult(true, $"created {request.Path}", "asset_created", request.Path);
        }

        AssetBrowserPanel panel = new(source, createAsset: CreateAsset);

        bool materialCreated = panel.TryCreateAsset("materials.json", AssetBrowserItemKind.Material);
        bool scriptCreated = panel.TryCreateAsset("scripts/NewBehaviour.cs", AssetBrowserItemKind.Script);
        bool uiScreenCreated = panel.TryCreateAsset("ui/screens/NewScreen.xhtml", AssetBrowserItemKind.UiScreen);
        bool unsupported = panel.TryCreateAsset("textures/generated.png", AssetBrowserItemKind.Texture);

        // Assert：验证预期结果
        Assert.True(materialCreated);
        Assert.True(scriptCreated);
        Assert.True(uiScreenCreated);
        Assert.False(unsupported);
        Assert.Equal(3, requests.Count);
        Assert.Equal("materials.json", requests[0].Path);
        Assert.Equal(AssetBrowserItemKind.Material, requests[0].Kind);
        Assert.Equal("scripts/NewBehaviour.cs", requests[1].Path);
        Assert.Equal(AssetBrowserItemKind.Script, requests[1].Kind);
        Assert.Equal("ui/screens/NewScreen.xhtml", requests[2].Path);
        Assert.Equal(AssetBrowserItemKind.UiScreen, requests[2].Kind);
        Assert.Contains(panel.LastAssets, asset => asset.Path == "materials.json" && asset.AssetId == "asset_created");
        Assert.Contains(panel.LastAssets, asset => asset.Path == "scripts/NewBehaviour.cs" && asset.AssetId == "asset_created");
        Assert.Contains(panel.LastAssets, asset => asset.Path == "ui/screens/NewScreen.xhtml" && asset.AssetId == "asset_created");
        Assert.Contains("暂不支持", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 创建 Folder 后会刷新空文件夹 drop target，而不是伪装成资产条目。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelCreatesFolderAndRefreshesEmptyFolderTargets()
    {
        // Arrange：准备输入与初始状态
        RecordingAssetSource source = new([]);
        List<AssetBrowserCreateRequest> requests = [];
        AssetBrowserCreateResult CreateAsset(AssetBrowserCreateRequest request)
        {
            requests.Add(request);
            source.ReplaceFolders([new AssetBrowserFolderItem(request.Path, 0)]);
            return new AssetBrowserCreateResult(true, $"created {request.Path}", null, request.Path);
        }

        AssetBrowserPanel panel = new(source, createAsset: CreateAsset);

        bool created = panel.TryCreateAsset("levels", AssetBrowserItemKind.Folder);

        // Assert：验证预期结果
        Assert.True(created);
        AssetBrowserCreateRequest request = Assert.Single(requests);
        Assert.Equal("levels", request.Path);
        Assert.Equal(AssetBrowserItemKind.Folder, request.Kind);
        Assert.Empty(panel.LastAssets);
        Assert.Contains(panel.FolderTargets, folder => folder.Path == string.Empty && folder.AssetCount == 0);
        Assert.Contains(panel.FolderTargets, folder => folder.Path == "levels" && folder.AssetCount == 0);
    }

    private sealed class RecordingThumbnailProvider : ITextureThumbnailProvider
    {
        public bool TryGetThumbnail(string assetPath, out AssetThumbnail thumbnail)
        {
            if (assetPath == "textures/sand.png")
            {
                thumbnail = new AssetThumbnail(12, 16, 16);
                return true;
            }

            thumbnail = default;
            return false;
        }
    }

    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) : IAssetBrowserDataSource, IAssetBrowserFolderDataSource
    {
        private IReadOnlyList<AssetBrowserItem> _assets = assets;
        private IReadOnlyList<AssetBrowserFolderItem> _folders = [];

        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return _assets;
        }

        public IReadOnlyList<AssetBrowserFolderItem> ListFolders()
        {
            return _folders;
        }

        public void ReplaceAssets(IReadOnlyList<AssetBrowserItem> assets)
        {
            _assets = assets;
        }

        public void ReplaceFolders(IReadOnlyList<AssetBrowserFolderItem> folders)
        {
            _folders = folders;
        }
    }

    private sealed class RecordingAudioPreview : IAudioPreviewService
    {
        public List<string> Played { get; } = [];

        public bool TryPlayPreview(string assetPath)
        {
            Played.Add(assetPath);
            return true;
        }
    }
}
