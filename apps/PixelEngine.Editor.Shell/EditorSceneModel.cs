using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorSceneModel
{
    private readonly Dictionary<int, EditorGameObject> _objects = [];
    private readonly List<int> _roots = [];
    private int _nextStableId = 1;

    private EditorSceneModel(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "main" : name.Trim();
    }

    public string Name { get; set; }

    public int? SelectedStableId { get; private set; }

    public bool IsDirty { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<int> RootIds => _roots;

    public int Count => _objects.Count;

    public static EditorSceneModel Empty(string name = "main")
    {
        return new EditorSceneModel(name);
    }

    public static EditorSceneModel FromDocument(EngineSceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EditorSceneModel model = new(document.Name ?? "main");
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        for (int i = 0; i < entities.Length; i++)
        {
            EngineSceneEntityDocument entity = entities[i];
            EditorGameObject gameObject = new(entity.StableId, string.IsNullOrWhiteSpace(entity.Name) ? $"GameObject {entity.StableId}" : entity.Name!)
            {
                ParentId = entity.ParentId,
                Transform = FromDocumentTransform(entity.Transform),
                PrefabLink = FromDocumentPrefab(entity.Prefab),
            };

            EngineSceneBehaviourDocument[] behaviours = entity.Behaviours ?? [];
            for (int j = 0; j < behaviours.Length; j++)
            {
                if (string.IsNullOrWhiteSpace(behaviours[j].TypeName))
                {
                    continue;
                }

                EditorComponentModel component = new(behaviours[j].TypeName!);
                Dictionary<string, string>? serializedFields = behaviours[j].SerializedFields;
                if (serializedFields is not null)
                {
                    foreach (KeyValuePair<string, string> field in serializedFields)
                    {
                        component.SerializedFields[field.Key] = field.Value;
                    }
                }

                gameObject.Components.Add(component);
            }

            model.AddLoaded(gameObject);
        }

        model.RebuildChildren();
        model.IsDirty = false;
        model.Version = 0;
        return model;
    }

    public EngineSceneDocument ToDocument()
    {
        EditorGameObject[] ordered = [.. EnumerateDepthFirst()];
        EngineSceneEntityDocument[] entities = new EngineSceneEntityDocument[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            EditorGameObject gameObject = ordered[i];
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
                StableId = gameObject.StableId,
                Name = gameObject.Name,
                ParentId = gameObject.ParentId,
                Transform = ToDocumentTransform(gameObject.Transform),
                Prefab = ToDocumentPrefab(gameObject.PrefabLink),
                Behaviours = behaviours,
            };
        }

        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = Name,
            Entities = entities,
        };
    }

    public bool TryGet(int stableId, out EditorGameObject gameObject)
    {
        return _objects.TryGetValue(stableId, out gameObject!);
    }

    public EditorGameObject Get(int stableId)
    {
        return TryGet(stableId, out EditorGameObject? gameObject)
            ? gameObject
            : throw new KeyNotFoundException($"GameObject {stableId} 不存在。");
    }

    public EditorGameObject Create(string name, int? parentId = null, int? insertIndex = null)
    {
        EditorGameObject gameObject = new(AllocateStableId(), name);
        Insert(gameObject, parentId, insertIndex);
        Select(gameObject.StableId);
        MarkDirty();
        return gameObject;
    }

    public EditorGameObject DuplicateSubtree(int stableId)
    {
        EditorGameObject source = Get(stableId);
        Dictionary<int, int> remap = [];
        EditorGameObject root = DuplicateRecursive(source, source.ParentId, remap);
        Select(root.StableId);
        MarkDirty();
        return root;
    }

    public EditorSceneObjectSnapshot CaptureSubtree(int stableId)
    {
        EditorGameObject root = Get(stableId);
        List<EditorGameObject> objects = [];
        CaptureRecursive(root, objects);
        return new EditorSceneObjectSnapshot(root.ParentId, IndexInParent(root.StableId), [.. objects.Select(static item => item.CloneShallow())]);
    }

    public void RestoreSubtree(EditorSceneObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        for (int i = 0; i < snapshot.Objects.Length; i++)
        {
            EditorGameObject gameObject = snapshot.Objects[i].CloneShallow();
            gameObject.Children.Clear();
            _objects.Add(gameObject.StableId, gameObject);
            _nextStableId = Math.Max(_nextStableId, checked(gameObject.StableId + 1));
        }

        RebuildChildren();
        EditorGameObject root = Get(snapshot.Objects[0].StableId);
        Move(root.StableId, snapshot.ParentId, snapshot.Index);
        Select(root.StableId);
        MarkDirty();
    }

    public EditorSceneObjectSnapshot DeleteSubtree(int stableId)
    {
        EditorSceneObjectSnapshot snapshot = CaptureSubtree(stableId);
        RemoveSubtree(stableId);
        if (SelectedStableId == stableId || (SelectedStableId.HasValue && snapshot.Contains(SelectedStableId.Value)))
        {
            SelectedStableId = null;
        }

        MarkDirty();
        return snapshot;
    }

    public void Rename(int stableId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Get(stableId).Name = name.Trim();
        MarkDirty();
    }

    public void SetEnabled(int stableId, bool enabled)
    {
        Get(stableId).Enabled = enabled;
        MarkDirty();
    }

    public void SetTransform(int stableId, EditorSceneTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Get(stableId).Transform = transform.Clone();
        MarkDirty();
    }

    public void AddComponent(int stableId, EditorComponentModel component, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        List<EditorComponentModel> components = Get(stableId).Components;
        int index = insertIndex.HasValue ? Math.Clamp(insertIndex.Value, 0, components.Count) : components.Count;
        components.Insert(index, component.Clone());
        MarkDirty();
    }

    public EditorComponentModel RemoveComponent(int stableId, int componentIndex)
    {
        List<EditorComponentModel> components = Get(stableId).Components;
        if ((uint)componentIndex >= (uint)components.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(componentIndex), componentIndex, "组件索引越界。");
        }

        EditorComponentModel removed = components[componentIndex];
        components.RemoveAt(componentIndex);
        MarkDirty();
        return removed;
    }

    public void MoveComponent(int stableId, int fromIndex, int toIndex)
    {
        List<EditorComponentModel> components = Get(stableId).Components;
        if ((uint)fromIndex >= (uint)components.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, "组件索引越界。");
        }

        int target = Math.Clamp(toIndex, 0, components.Count - 1);
        if (fromIndex == target)
        {
            return;
        }

        EditorComponentModel component = components[fromIndex];
        components.RemoveAt(fromIndex);
        components.Insert(target, component);
        MarkDirty();
    }

    public void SetComponentField(int stableId, int componentIndex, string fieldName, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        List<EditorComponentModel> components = Get(stableId).Components;
        if ((uint)componentIndex >= (uint)components.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(componentIndex), componentIndex, "组件索引越界。");
        }

        if (value is null)
        {
            _ = components[componentIndex].SerializedFields.Remove(fieldName);
        }
        else
        {
            components[componentIndex].SerializedFields[fieldName] = value;
        }

        MarkDirty();
    }

    public void SetPrefabLink(int stableId, EditorPrefabLink? prefabLink)
    {
        Get(stableId).PrefabLink = prefabLink?.Clone();
        MarkDirty();
    }

    public void RecordPrefabOverride(int stableId, string propertyPath, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        EditorGameObject gameObject = Get(stableId);
        if (gameObject.PrefabLink is null)
        {
            return;
        }

        EditorPrefabLink link = gameObject.PrefabLink;
        string sourceStableId = string.IsNullOrWhiteSpace(link.SourceStableId)
            ? stableId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : link.SourceStableId!;
        for (int i = 0; i < link.Overrides.Count; i++)
        {
            EditorPrefabOverride existing = link.Overrides[i];
            if (string.Equals(existing.SourceStableId, sourceStableId, StringComparison.Ordinal) &&
                string.Equals(existing.PropertyPath, propertyPath, StringComparison.Ordinal))
            {
                existing.Value = value;
                MarkDirty();
                return;
            }
        }

        link.Overrides.Add(new EditorPrefabOverride
        {
            SourceStableId = sourceStableId,
            PropertyPath = propertyPath,
            Value = value,
        });
        MarkDirty();
    }

    public void ClearPrefabOverrides(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        if (gameObject.PrefabLink is null || gameObject.PrefabLink.Overrides.Count == 0)
        {
            return;
        }

        gameObject.PrefabLink.Overrides.Clear();
        MarkDirty();
    }

    public EditorGameObject ImportSubtree(IReadOnlyList<EditorGameObject> sourceObjects, int sourceRootStableId, int? parentId)
    {
        ArgumentNullException.ThrowIfNull(sourceObjects);
        Dictionary<int, EditorGameObject> sourceById = new(sourceObjects.Count);
        for (int i = 0; i < sourceObjects.Count; i++)
        {
            sourceById.Add(sourceObjects[i].StableId, sourceObjects[i]);
        }

        if (!sourceById.TryGetValue(sourceRootStableId, out EditorGameObject? sourceRoot))
        {
            throw new InvalidOperationException($"导入子树缺少根对象：{sourceRootStableId}。");
        }

        Dictionary<int, int> remap = [];
        EditorGameObject importedRoot = ImportRecursive(sourceRoot, parentId, sourceById, remap);
        Select(importedRoot.StableId);
        MarkDirty();
        return importedRoot;
    }

    public void Move(int stableId, int? parentId, int? insertIndex = null)
    {
        EditorGameObject gameObject = Get(stableId);
        if (parentId == stableId || (parentId.HasValue && IsDescendant(parentId.Value, stableId)))
        {
            throw new InvalidOperationException("不能把 GameObject 重父到自身或子节点。");
        }

        if (parentId.HasValue)
        {
            _ = Get(parentId.Value);
        }

        RemoveFromParentList(stableId);
        gameObject.ParentId = parentId;
        List<int> siblings = parentId.HasValue ? Get(parentId.Value).Children : _roots;
        int index = insertIndex.HasValue ? Math.Clamp(insertIndex.Value, 0, siblings.Count) : siblings.Count;
        siblings.Insert(index, stableId);
        MarkDirty();
    }

    public void Select(int? stableId)
    {
        if (stableId.HasValue)
        {
            _ = Get(stableId.Value);
        }

        SelectedStableId = stableId;
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }

    public IEnumerable<EditorGameObject> EnumerateDepthFirst()
    {
        for (int i = 0; i < _roots.Count; i++)
        {
            foreach (EditorGameObject gameObject in EnumerateDepthFirst(_roots[i]))
            {
                yield return gameObject;
            }
        }
    }

    public EditorSceneTransform ComputeWorldTransform(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        EditorSceneTransform local = gameObject.Transform.Clone();
        return gameObject.ParentId.HasValue
            ? Compose(ComputeWorldTransform(gameObject.ParentId.Value), local)
            : local;
    }

    public int IndexInParent(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        List<int> siblings = gameObject.ParentId.HasValue ? Get(gameObject.ParentId.Value).Children : _roots;
        return siblings.IndexOf(stableId);
    }

    private void Insert(EditorGameObject gameObject, int? parentId, int? insertIndex)
    {
        if (_objects.ContainsKey(gameObject.StableId))
        {
            throw new InvalidOperationException($"StableId 重复：{gameObject.StableId}。");
        }

        _objects.Add(gameObject.StableId, gameObject);
        _nextStableId = Math.Max(_nextStableId, checked(gameObject.StableId + 1));
        Move(gameObject.StableId, parentId, insertIndex);
    }

    private void AddLoaded(EditorGameObject gameObject)
    {
        if (_objects.ContainsKey(gameObject.StableId))
        {
            throw new InvalidOperationException($".scene 包含重复 StableId：{gameObject.StableId}。");
        }

        _objects.Add(gameObject.StableId, gameObject);
        _nextStableId = Math.Max(_nextStableId, checked(gameObject.StableId + 1));
    }

    private int AllocateStableId()
    {
        while (_objects.ContainsKey(_nextStableId))
        {
            _nextStableId++;
        }

        return _nextStableId++;
    }

    private EditorGameObject DuplicateRecursive(EditorGameObject source, int? parentId, Dictionary<int, int> remap)
    {
        string name = source.ParentId == parentId ? $"{source.Name} Copy" : source.Name;
        EditorGameObject copy = new(AllocateStableId(), name)
        {
            Enabled = source.Enabled,
            Transform = source.Transform.Clone(),
            PrefabLink = source.PrefabLink?.Clone(),
        };
        for (int i = 0; i < source.Components.Count; i++)
        {
            copy.Components.Add(source.Components[i].Clone());
        }

        Insert(copy, parentId, null);
        remap[source.StableId] = copy.StableId;
        for (int i = 0; i < source.Children.Count; i++)
        {
            _ = DuplicateRecursive(Get(source.Children[i]), copy.StableId, remap);
        }

        return copy;
    }

    private EditorGameObject ImportRecursive(
        EditorGameObject source,
        int? parentId,
        Dictionary<int, EditorGameObject> sourceById,
        Dictionary<int, int> remap)
    {
        EditorGameObject imported = new(AllocateStableId(), source.Name)
        {
            Enabled = source.Enabled,
            Transform = source.Transform.Clone(),
            PrefabLink = source.PrefabLink?.Clone(),
        };
        for (int i = 0; i < source.Components.Count; i++)
        {
            imported.Components.Add(source.Components[i].Clone());
        }

        Insert(imported, parentId, null);
        remap[source.StableId] = imported.StableId;
        for (int i = 0; i < source.Children.Count; i++)
        {
            if (sourceById.TryGetValue(source.Children[i], out EditorGameObject? child))
            {
                _ = ImportRecursive(child, imported.StableId, sourceById, remap);
            }
        }

        return imported;
    }

    private void CaptureRecursive(EditorGameObject gameObject, List<EditorGameObject> objects)
    {
        objects.Add(gameObject.CloneShallow());
        for (int i = 0; i < gameObject.Children.Count; i++)
        {
            CaptureRecursive(Get(gameObject.Children[i]), objects);
        }
    }

    private void RemoveSubtree(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        int[] children = [.. gameObject.Children];
        for (int i = 0; i < children.Length; i++)
        {
            RemoveSubtree(children[i]);
        }

        RemoveFromParentList(stableId);
        _ = _objects.Remove(stableId);
    }

    private void RemoveFromParentList(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        List<int> siblings = gameObject.ParentId.HasValue && _objects.TryGetValue(gameObject.ParentId.Value, out EditorGameObject? parent)
            ? parent.Children
            : _roots;
        _ = siblings.Remove(stableId);
    }

    private void RebuildChildren()
    {
        _roots.Clear();
        foreach (EditorGameObject gameObject in _objects.Values)
        {
            gameObject.Children.Clear();
        }

        foreach (EditorGameObject gameObject in _objects.Values.OrderBy(static item => item.StableId))
        {
            if (gameObject.ParentId.HasValue && _objects.TryGetValue(gameObject.ParentId.Value, out EditorGameObject? parent))
            {
                parent.Children.Add(gameObject.StableId);
            }
            else
            {
                gameObject.ParentId = null;
                _roots.Add(gameObject.StableId);
            }
        }
    }

    private bool IsDescendant(int possibleDescendant, int ancestor)
    {
        EditorGameObject current = Get(possibleDescendant);
        while (current.ParentId.HasValue)
        {
            if (current.ParentId.Value == ancestor)
            {
                return true;
            }

            current = Get(current.ParentId.Value);
        }

        return false;
    }

    private IEnumerable<EditorGameObject> EnumerateDepthFirst(int stableId)
    {
        EditorGameObject gameObject = Get(stableId);
        yield return gameObject;
        for (int i = 0; i < gameObject.Children.Count; i++)
        {
            foreach (EditorGameObject child in EnumerateDepthFirst(gameObject.Children[i]))
            {
                yield return child;
            }
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
        Version++;
    }

    private static EditorSceneTransform FromDocumentTransform(EngineSceneTransformDocument? transform)
    {
        return transform is null
            ? new EditorSceneTransform()
            : new EditorSceneTransform
            {
                X = transform.X,
                Y = transform.Y,
                RotationRadians = transform.RotationRadians,
                ScaleX = transform.ScaleX,
                ScaleY = transform.ScaleY,
            };
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

    private static EditorPrefabLink? FromDocumentPrefab(EngineScenePrefabDocument? prefab)
    {
        if (prefab is null)
        {
            return null;
        }

        EditorPrefabLink link = new()
        {
            AssetPath = prefab.AssetPath,
            SourceStableId = prefab.SourceStableId,
        };
        EngineScenePrefabOverrideDocument[] overrides = prefab.Overrides ?? [];
        for (int i = 0; i < overrides.Length; i++)
        {
            link.Overrides.Add(new EditorPrefabOverride
            {
                SourceStableId = overrides[i].SourceStableId,
                PropertyPath = overrides[i].PropertyPath,
                Value = overrides[i].Value,
            });
        }

        return link;
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
            AssetPath = prefab.AssetPath,
            SourceStableId = prefab.SourceStableId,
            Overrides = overrides,
        };
    }

    private static EditorSceneTransform Compose(EditorSceneTransform parent, EditorSceneTransform local)
    {
        float scaledX = local.X * parent.ScaleX;
        float scaledY = local.Y * parent.ScaleY;
        float cos = MathF.Cos(parent.RotationRadians);
        float sin = MathF.Sin(parent.RotationRadians);
        return new EditorSceneTransform
        {
            X = parent.X + (scaledX * cos) - (scaledY * sin),
            Y = parent.Y + (scaledX * sin) + (scaledY * cos),
            RotationRadians = parent.RotationRadians + local.RotationRadians,
            ScaleX = parent.ScaleX * local.ScaleX,
            ScaleY = parent.ScaleY * local.ScaleY,
        };
    }
}

internal sealed class EditorSceneObjectSnapshot(int? parentId, int index, EditorGameObject[] objects)
{
    public int? ParentId { get; } = parentId;

    public int Index { get; } = index;

    public EditorGameObject[] Objects { get; } = objects.Length == 0 ? throw new ArgumentException("快照必须包含根对象。", nameof(objects)) : objects;

    public bool Contains(int stableId)
    {
        for (int i = 0; i < Objects.Length; i++)
        {
            if (Objects[i].StableId == stableId)
            {
                return true;
            }
        }

        return false;
    }
}
