using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Prefab 资产文件的读写与实例化。
/// </summary>
internal sealed class EditorPrefabAssetStore(string contentRoot, EditorAssetManifestStore? assets = null)
{
    private readonly string _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
        ? throw new ArgumentException("content 根目录不能为空。", nameof(contentRoot))
        : Path.GetFullPath(contentRoot);
    private readonly EditorAssetManifestStore? _assets = assets;

    public string AllocatePrefabPath(string objectName)
    {
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(objectName) ? "Prefab" : objectName);
        for (int i = 0; i < 1000; i++)
        {
            string suffix = i == 0 ? string.Empty : $"-{i + 1}";
            string relative = $"prefabs/{safeName}{suffix}.prefab";
            if (!File.Exists(ResolveFullPath(relative)))
            {
                return relative;
            }
        }

        throw new InvalidOperationException("无法为 prefab 分配可用文件名。");
    }

    public void CreatePrefabFromSubtree(EditorSceneModel scene, int stableId, string assetPath)
    {
        bool manifestSynchronized = false;
        CreatePrefabFromSubtree(scene, stableId, assetPath, ref manifestSynchronized);
    }

    internal void CreatePrefabFromSubtree(
        EditorSceneModel scene,
        int stableId,
        string assetPath,
        ref bool manifestSynchronized)
    {
        ArgumentNullException.ThrowIfNull(scene);
        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        EditorSceneObjectSnapshot snapshot = scene.CaptureSubtree(stableId);
        Dictionary<int, int> remap = BuildStableIdRemap(snapshot.Objects);
        EngineSceneDocument document = BuildPrefabDocument(snapshot.Objects, remap, Path.GetFileNameWithoutExtension(normalizedAssetPath));
        EngineSceneDocumentLoader.SaveDocument(document, PrepareWritePath(normalizedAssetPath));
        string? assetId = TryEnsurePrefabAssetId(normalizedAssetPath);
        manifestSynchronized = _assets is not null;
        for (int i = 0; i < snapshot.Objects.Length; i++)
        {
            int originalId = snapshot.Objects[i].StableId;
            scene.SetPrefabLink(originalId, new EditorPrefabLink
            {
                AssetId = assetId,
                AssetPath = normalizedAssetPath,
                SourceStableId = remap[originalId].ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }
    }

    public bool TryReadAsset(string assetPath, out byte[] bytes)
    {
        string fullPath = ResolveFullPath(assetPath);
        if (!File.Exists(fullPath))
        {
            bytes = [];
            return false;
        }

        bytes = File.ReadAllBytes(fullPath);
        return true;
    }

    public void RestoreAsset(string assetPath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        string fullPath = PrepareWritePath(normalizedAssetPath);
        WriteAllBytesAtomically(fullPath, bytes);
        _ = TryEnsurePrefabAssetId(normalizedAssetPath);
    }

    public void DeleteAsset(string assetPath)
    {
        string fullPath = ResolveFullPath(assetPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void RemoveAssetRecord(string assetPath)
    {
        if (_assets is not null)
        {
            _ = _assets.RemoveAssetRecord(NormalizeAssetPath(assetPath));
        }
    }

    public EditorGameObject InstantiatePrefab(EditorSceneModel scene, string assetPath, int? parentId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (parentId is { } targetParentId)
        {
            _ = scene.Get(targetParentId);
        }

        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        EditorSceneModel prefabModel = LoadPrefabModel(normalizedAssetPath);
        // 先完整验证/展开 prefab，再写 manifest。损坏或循环 prefab 不得留下幽灵资产记录。
        string? assetId = TryEnsurePrefabAssetId(normalizedAssetPath);
        EditorGameObject sourceRoot = ResolvePrefabRoot(prefabModel);
        foreach (EditorGameObject gameObject in prefabModel.EnumerateDepthFirst())
        {
            gameObject.PrefabLink = new EditorPrefabLink
            {
                AssetId = assetId,
                AssetPath = normalizedAssetPath,
                SourceStableId = gameObject.StableId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        return scene.ImportSubtree([.. prefabModel.EnumerateDepthFirst()], sourceRoot.StableId, parentId);
    }

    public EditorSceneModel LoadPrefabModel(string assetPath)
    {
        return LoadPrefabModel(assetPath, []);
    }

    public void RefreshPrefabInstances(EditorSceneModel scene)
    {
        RefreshPrefabInstances(scene, []);
    }

    private string? TryEnsurePrefabAssetId(string assetPath)
    {
        return _assets?.EnsureAsset(assetPath).Id;
    }

    private bool TryResolvePrefabPath(EditorPrefabLink link, out string assetPath, out string? assetId)
    {
        if (!string.IsNullOrWhiteSpace(link.AssetId) &&
            _assets is not null &&
            _assets.TryResolveAssetId(link.AssetId, out EditorAssetRecord asset))
        {
            assetPath = NormalizeAssetPath(asset.LogicalPath);
            assetId = asset.Id;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(link.AssetPath))
        {
            assetPath = NormalizeAssetPath(link.AssetPath);
            assetId = _assets is null || !_assets.TryResolveLogicalPath(assetPath, out EditorAssetRecord byPath)
                ? link.AssetId
                : byPath.Id;
            return true;
        }

        assetPath = string.Empty;
        assetId = null;
        return false;
    }

    private string ResolveFullPath(string assetPath)
    {
        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        string fullPath = Path.GetFullPath(Path.Combine(_contentRoot, normalizedAssetPath));
        string rootWithSeparator = _contentRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _contentRoot
            : _contentRoot + Path.DirectorySeparatorChar;
        bool insideContentRoot = string.Equals(fullPath, _contentRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        if (!insideContentRoot)
        {
            throw new InvalidOperationException($"prefab 路径越过 content 根目录：{assetPath}");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private string PrepareWritePath(string assetPath)
    {
        string fullPath = ResolveFullPath(assetPath);
        if (!Directory.Exists(_contentRoot))
        {
            _ = Directory.CreateDirectory(_contentRoot);
        }

        RejectReparsePoint(_contentRoot);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("prefab 写入路径缺少父目录。");
        string relativeDirectory = Path.GetRelativePath(_contentRoot, directory);
        string current = _contentRoot;
        if (!string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            string[] segments = relativeDirectory.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                if (!Directory.Exists(current))
                {
                    _ = Directory.CreateDirectory(current);
                }

                RejectReparsePoint(current);
            }
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private void EnsureNoReparsePoints(string fullPath)
    {
        string relative = Path.GetRelativePath(_contentRoot, fullPath);
        if (relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("prefab 路径越过 content 根目录。");
        }

        string current = _contentRoot;
        if (Directory.Exists(current) || File.Exists(current))
        {
            RejectReparsePoint(current);
        }

        string[] segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"prefab 路径不允许经过 reparse point：{path}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"无法安全检查 prefab 路径：{path}", exception);
        }
    }

    private static void WriteAllBytesAtomically(string fullPath, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("prefab 写入路径缺少父目录。");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private EditorSceneModel LoadPrefabModel(string assetPath, HashSet<string> resolving)
    {
        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (!resolving.Add(normalizedAssetPath))
        {
            throw new InvalidOperationException($"检测到循环嵌套 prefab：{normalizedAssetPath}");
        }

        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(ResolveFullPath(normalizedAssetPath));
        EditorSceneModel model = EditorSceneModel.FromDocument(document);
        RefreshPrefabInstances(model, resolving);
        _ = resolving.Remove(normalizedAssetPath);
        return model;
    }

    private void RefreshPrefabInstances(EditorSceneModel scene, HashSet<string> resolving)
    {
        foreach (EditorGameObject gameObject in scene.EnumerateDepthFirst())
        {
            if (gameObject.PrefabLink is not { } prefabLink ||
                !TryResolvePrefabPath(prefabLink, out string assetPath, out string? assetId) ||
                !int.TryParse(prefabLink.SourceStableId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int sourceStableId))
            {
                continue;
            }

            EditorPrefabLink instanceLink = prefabLink.Clone();
            instanceLink.AssetId = assetId;
            instanceLink.AssetPath = assetPath;
            EditorSceneModel prefab = LoadPrefabModel(assetPath, resolving);
            if (!prefab.TryGet(sourceStableId, out EditorGameObject? source))
            {
                throw new InvalidOperationException($"prefab {assetPath} 缺少源对象 {sourceStableId}。");
            }

            ApplyPrefabBaseline(gameObject, source, instanceLink);
        }
    }

    private static void ApplyPrefabBaseline(EditorGameObject target, EditorGameObject source, EditorPrefabLink instanceLink)
    {
        target.Name = OverrideValue(instanceLink, "Name") ?? source.Name;
        target.Enabled = bool.TryParse(OverrideValue(instanceLink, "Enabled"), out bool enabled) ? enabled : source.Enabled;
        target.Transform = ApplyTransformOverrides(source.Transform, instanceLink);
        target.WebCanvas = ApplyWebCanvasOverrides(source.WebCanvas, instanceLink);
        target.CanvasScaler = ApplyCanvasScalerOverrides(source.CanvasScaler, instanceLink);
        target.Components.Clear();
        for (int i = 0; i < source.Components.Count; i++)
        {
            target.Components.Add(source.Components[i].Clone());
        }

        ApplyComponentOverrides(target, instanceLink);
        target.PrefabLink = instanceLink;
    }

    private static EditorSceneTransform ApplyTransformOverrides(EditorSceneTransform source, EditorPrefabLink link)
    {
        return new EditorSceneTransform
        {
            X = FloatOverride(link, "Transform.X", source.X),
            Y = FloatOverride(link, "Transform.Y", source.Y),
            RotationRadians = FloatOverride(link, "Transform.RotationRadians", source.RotationRadians),
            ScaleX = FloatOverride(link, "Transform.ScaleX", source.ScaleX),
            ScaleY = FloatOverride(link, "Transform.ScaleY", source.ScaleY),
        };
    }

    private static EditorWebCanvasComponent? ApplyWebCanvasOverrides(
        EditorWebCanvasComponent? source,
        EditorPrefabLink link)
    {
        string? existsOverride = OverrideValue(link, "WebCanvas.Exists");
        bool exists = bool.TryParse(existsOverride, out bool overriddenExists)
            ? overriddenExists
            : source is not null;
        if (!exists)
        {
            return null;
        }

        EditorWebCanvasComponent result = source?.Clone(clearPrimary: true) ?? new EditorWebCanvasComponent();
        string? manifestAssetId = OverrideValue(link, "WebCanvas.ManifestAssetId");
        if (manifestAssetId is not null)
        {
            result.ManifestAssetId = NormalizeOptional(manifestAssetId);
        }

        string? manifestPath = OverrideValue(link, "WebCanvas.ManifestPath");
        if (manifestPath is not null)
        {
            result.ManifestPath = NormalizeOptional(manifestPath);
        }

        string? initialScreenId = OverrideValue(link, "WebCanvas.InitialScreenId");
        if (initialScreenId is not null)
        {
            result.InitialScreenId = NormalizeOptional(initialScreenId);
        }

        result.Enabled = bool.TryParse(OverrideValue(link, "WebCanvas.Enabled"), out bool enabled)
            ? enabled
            : result.Enabled;
        result.SortingOrder = int.TryParse(
            OverrideValue(link, "WebCanvas.SortingOrder"),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int sortingOrder)
                ? sortingOrder
                : result.SortingOrder;
        result.Primary = bool.TryParse(OverrideValue(link, "WebCanvas.Primary"), out bool primary) && primary;
        return result;
    }

    private static EditorCanvasScalerComponent? ApplyCanvasScalerOverrides(
        EditorCanvasScalerComponent? source,
        EditorPrefabLink link)
    {
        string? existsOverride = OverrideValue(link, "CanvasScaler.Exists");
        bool exists = bool.TryParse(existsOverride, out bool overriddenExists)
            ? overriddenExists
            : source is not null;
        if (!exists)
        {
            return null;
        }

        PixelEngine.UI.UiCanvasScalerSettings settings = source?.Settings ?? PixelEngine.UI.UiCanvasScalerSettings.Default;
        settings = settings with
        {
            ScaleFactor = FloatOverride(link, "CanvasScaler.ScaleFactor", settings.ScaleFactor),
            ReferenceWidth = FloatOverride(link, "CanvasScaler.ReferenceWidth", settings.ReferenceWidth),
            ReferenceHeight = FloatOverride(link, "CanvasScaler.ReferenceHeight", settings.ReferenceHeight),
            MatchWidthOrHeight = FloatOverride(link, "CanvasScaler.MatchWidthOrHeight", settings.MatchWidthOrHeight),
            FallbackScreenDpi = FloatOverride(link, "CanvasScaler.FallbackScreenDpi", settings.FallbackScreenDpi),
            DefaultSpriteDpi = FloatOverride(link, "CanvasScaler.DefaultSpriteDpi", settings.DefaultSpriteDpi),
            ReferencePixelsPerUnit = FloatOverride(link, "CanvasScaler.ReferencePixelsPerUnit", settings.ReferencePixelsPerUnit),
        };
        if (Enum.TryParse(
            OverrideValue(link, "CanvasScaler.ScaleMode"),
            ignoreCase: true,
            out PixelEngine.UI.UiScaleMode scaleMode))
        {
            settings = settings with { ScaleMode = scaleMode };
        }

        if (Enum.TryParse(
            OverrideValue(link, "CanvasScaler.ScreenMatchMode"),
            ignoreCase: true,
            out PixelEngine.UI.UiScreenMatchMode screenMatchMode))
        {
            settings = settings with { ScreenMatchMode = screenMatchMode };
        }

        if (Enum.TryParse(
            OverrideValue(link, "CanvasScaler.PhysicalUnit"),
            ignoreCase: true,
            out PixelEngine.UI.UiPhysicalUnit physicalUnit))
        {
            settings = settings with { PhysicalUnit = physicalUnit };
        }

        return new EditorCanvasScalerComponent { Settings = settings };
    }

    private static void ApplyComponentOverrides(EditorGameObject target, EditorPrefabLink link)
    {
        for (int i = 0; i < link.Overrides.Count; i++)
        {
            EditorPrefabOverride item = link.Overrides[i];
            if (item.PropertyPath is null || !item.PropertyPath.StartsWith("Component:", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = item.PropertyPath.Split(':', 3);
            if (parts.Length != 3)
            {
                continue;
            }

            EditorComponentModel? component = target.Components.FirstOrDefault(component => string.Equals(component.TypeName, parts[1], StringComparison.Ordinal));
            if (component is not null && item.Value is not null)
            {
                component.SerializedFields[parts[2]] = item.Value;
            }
        }
    }

    private static float FloatOverride(EditorPrefabLink link, string propertyPath, float fallback)
    {
        string? value = OverrideValue(link, propertyPath);
        return value is null
            ? fallback
            : float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? OverrideValue(EditorPrefabLink link, string propertyPath)
    {
        string sourceStableId = link.SourceStableId ?? string.Empty;
        for (int i = link.Overrides.Count - 1; i >= 0; i--)
        {
            EditorPrefabOverride item = link.Overrides[i];
            if (string.Equals(item.SourceStableId, sourceStableId, StringComparison.Ordinal) &&
                string.Equals(item.PropertyPath, propertyPath, StringComparison.Ordinal))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static string NormalizeAssetPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        string normalized = assetPath.Trim().Replace('\\', '/');
        if (normalized.Length > 32767 ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(normalized) ||
            normalized.Any(char.IsControl) ||
            !normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"prefab 资产必须是 root-relative .prefab path：{assetPath}");
        }

        string[] segments = normalized.Split('/');
        return segments.Length != 0 && !segments.Any(IsInvalidPathSegment)
            ? string.Join('/', segments)
            : throw new InvalidOperationException(
                $"prefab 资产包含非法或越界 path segment：{assetPath}");
    }

    private static bool IsInvalidPathSegment(string segment)
    {
        if (segment.Length is 0 or > 255 ||
            segment is "." or ".." ||
            segment.EndsWith(' ') ||
            segment.EndsWith('.') ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0)
        {
            return true;
        }

        string deviceName = segment.Split('.', 2)[0];
        return deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (deviceName.Length == 4 &&
             (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             (deviceName[3] is (>= '1' and <= '9') or '¹' or '²' or '³'));
    }

    private static Dictionary<int, int> BuildStableIdRemap(EditorGameObject[] objects)
    {
        Dictionary<int, int> remap = new(objects.Length);
        for (int i = 0; i < objects.Length; i++)
        {
            remap.Add(objects[i].StableId, i + 1);
        }

        return remap;
    }

    private static EngineSceneDocument BuildPrefabDocument(EditorGameObject[] objects, Dictionary<int, int> remap, string? name)
    {
        EngineSceneEntityDocument[] entities = new EngineSceneEntityDocument[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            EditorGameObject gameObject = objects[i];
            EngineSceneBehaviourDocument[] behaviours = new EngineSceneBehaviourDocument[gameObject.Components.Count];
            for (int j = 0; j < gameObject.Components.Count; j++)
            {
                EditorComponentModel component = gameObject.Components[j];
                behaviours[j] = new EngineSceneBehaviourDocument
                {
                    TypeName = component.TypeName,
                    SerializedFields = component.SerializedFields.Count == 0
                        ? null
                        : new Dictionary<string, string>(component.SerializedFields, StringComparer.Ordinal),
                };
            }

            entities[i] = new EngineSceneEntityDocument
            {
                StableId = remap[gameObject.StableId],
                Name = gameObject.Name,
                ParentId = gameObject.ParentId.HasValue && remap.TryGetValue(gameObject.ParentId.Value, out int parentId)
                    ? parentId
                    : null,
                Enabled = gameObject.Enabled,
                Transform = ToDocumentTransform(gameObject.Transform),
                Prefab = ToDocumentPrefab(gameObject.PrefabLink),
                WebCanvas = gameObject.WebCanvas?.ToDocument(clearPrimary: true),
                CanvasScaler = gameObject.CanvasScaler?.ToDocument(),
                Behaviours = behaviours,
            };
        }

        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = string.IsNullOrWhiteSpace(name) ? "Prefab" : name,
            Entities = entities,
        };
    }

    private static EditorGameObject ResolvePrefabRoot(EditorSceneModel prefabModel)
    {
        return prefabModel.RootIds.Count == 0
            ? throw new InvalidOperationException("prefab 资产必须至少包含一个根 GameObject。")
            : prefabModel.Get(prefabModel.RootIds[0]);
    }

    private static EngineSceneTransformDocument ToDocumentTransform(EditorSceneTransform transform)
    {
        return new EngineSceneTransformDocument
        {
            X = transform.X,
            Y = transform.Y,
            RotationRadians = transform.RotationRadians,
            ScaleX = transform.ScaleX,
            ScaleY = transform.ScaleY,
        };
    }

    private static EngineScenePrefabDocument? ToDocumentPrefab(EditorPrefabLink? prefab)
    {
        if (prefab is null)
        {
            return null;
        }

        EngineScenePrefabOverrideDocument[] overrides = new EngineScenePrefabOverrideDocument[prefab.Overrides.Count];
        for (int i = 0; i < prefab.Overrides.Count; i++)
        {
            overrides[i] = new EngineScenePrefabOverrideDocument
            {
                SourceStableId = prefab.Overrides[i].SourceStableId,
                PropertyPath = prefab.Overrides[i].PropertyPath,
                Value = prefab.Overrides[i].Value,
            };
        }

        return new EngineScenePrefabDocument
        {
            AssetId = prefab.AssetId,
            AssetPath = prefab.AssetPath,
            SourceStableId = prefab.SourceStableId,
            Overrides = overrides,
        };
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
            {
                chars[i] = '-';
            }
        }

        return new string(chars);
    }
}
