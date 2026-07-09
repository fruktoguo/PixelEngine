using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 脚本组件 Inspector 面板，基于脚本场景公开快照反射展示与编辑字段。
/// </summary>
/// <param name="scene">脚本场景。</param>
/// <param name="materials">材质表；为空时材质字段只显示运行时 id。</param>
/// <param name="hotReload">脚本热重载入口。</param>
public sealed class ScriptInspectorPanel(Scene scene, MaterialTable? materials = null, IScriptInspectorHotReload? hotReload = null) : IEditorPanel
{
    private readonly Scene _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly MaterialTable? _materials = materials;
    private readonly IScriptInspectorHotReload? _hotReload = hotReload;
    private int _selectedComponentIndex;

    /// <inheritdoc />
    public string Title => EditorDockSpace.InspectorWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次脚本场景快照。
    /// </summary>
    public ScriptEntityInspection[] LastSnapshot { get; private set; } = [];

    /// <summary>
    /// 最近一次热重载结果文本。
    /// </summary>
    public string Status { get; private set; } = "就绪";

    /// <summary>
    /// 刷新脚本场景快照。
    /// </summary>
    /// <returns>刷新后的实体快照。</returns>
    public ScriptEntityInspection[] Refresh()
    {
        LastSnapshot = _scene.CaptureInspectionSnapshot();
        return LastSnapshot;
    }

    /// <summary>
    /// 在当前 Inspector 快照中选择组件。
    /// </summary>
    /// <param name="entityHandle">脚本实体句柄。</param>
    /// <param name="componentIndex">组件索引。</param>
    /// <returns>选择成功返回 true。</returns>
    public bool TrySelect(string entityHandle, int componentIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityHandle);
        ScriptEntityInspection[] snapshot = LastSnapshot.Length == 0 ? Refresh() : LastSnapshot;
        if (!TryFindEntity(snapshot, entityHandle, out ScriptEntityInspection entity) ||
            (uint)componentIndex >= (uint)entity.Components.Length)
        {
            return false;
        }

        _selectedComponentIndex = componentIndex;
        return true;
    }

    /// <summary>
    /// 尝试写回指定组件字段。
    /// </summary>
    /// <param name="entityHandle">脚本实体句柄。</param>
    /// <param name="componentIndex">组件索引。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="value">待写入值。</param>
    /// <returns>写回成功返回 true。</returns>
    public bool TrySetFieldValue(string entityHandle, int componentIndex, string fieldName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!TrySelect(entityHandle, componentIndex) ||
            !TryGetSelectedBehaviour(entityHandle, out Behaviour? behaviour))
        {
            return false;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        ScriptFieldDescriptor field = fields.FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.Ordinal));
        return field.Name is not null &&
            TryNormalizeValue(field, value, _materials, out object? normalized) &&
            ScriptInspector.TrySetFieldValue(behaviour, fieldName, normalized);
    }

    /// <summary>
    /// 触发一次 Inspector 配置的脚本热重载。
    /// </summary>
    /// <returns>热重载入口返回的结果。</returns>
    public ScriptInspectorHotReloadResult TriggerHotReload()
    {
        if (_hotReload is null || !_hotReload.CanReload)
        {
            Status = "脚本热重载未配置";
            return new ScriptInspectorHotReloadResult(false, Status, []);
        }

        ScriptInspectorHotReloadResult result = _hotReload.ReloadNow();
        Status = result.Message;
        _ = Refresh();
        return result;
    }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        // 面板绘制顺序：热重载控件 → 实体列表 → 组件选择 → 反射字段编辑。
        DrawHotReloadControls();
        ScriptEntityInspection[] snapshot = Refresh();
        DrawEntityPicker(snapshot, context.Selection);
        if (TryResolveSelection(snapshot, context.Selection.EntityHandle, out ScriptEntityInspection entity))
        {
            DrawComponentPicker(entity);
            DrawSelectedComponent(entity);
        }
        else
        {
            ImGui.TextUnformatted("未选中脚本实体");
        }

        ImGui.End();
    }

    private void DrawHotReloadControls()
    {
        if (ImGui.Button("热重载脚本"))
        {
            _ = TriggerHotReload();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(Status);
    }

    private static void DrawEntityPicker(ReadOnlySpan<ScriptEntityInspection> snapshot, EditorSelection selection)
    {
        for (int i = 0; i < snapshot.Length; i++)
        {
            ScriptEntityInspection entity = snapshot[i];
            bool selected = string.Equals(selection.EntityHandle, entity.Handle, StringComparison.Ordinal);
            if (ImGui.Selectable($"{entity.Handle}  ({entity.Components.Length})", selected))
            {
                selection.SelectEntity(entity.Handle);
            }
        }
    }

    private void DrawComponentPicker(ScriptEntityInspection entity)
    {
        if (entity.Components.Length == 0)
        {
            _selectedComponentIndex = 0;
            ImGui.TextUnformatted("实体没有 Behaviour 组件");
            return;
        }

        if (_selectedComponentIndex >= entity.Components.Length)
        {
            _selectedComponentIndex = entity.Components.Length - 1;
        }

        string[] names = new string[entity.Components.Length];
        for (int i = 0; i < entity.Components.Length; i++)
        {
            names[i] = entity.Components[i].TypeName;
        }

        _ = ImGui.Combo("组件", ref _selectedComponentIndex, names, names.Length);
    }

    private void DrawSelectedComponent(ScriptEntityInspection entity)
    {
        if ((uint)_selectedComponentIndex >= (uint)entity.Components.Length)
        {
            return;
        }

        ScriptComponentInspection component = entity.Components[_selectedComponentIndex];
        ImGui.TextUnformatted(component.TypeName);
        ImGui.TextUnformatted(component.Faulted ? "状态: Faulted" : component.Enabled ? "状态: Enabled" : "状态: Disabled");

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(component.Behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(component.Behaviour, fields[i]);
        }
    }

    // 按 ScriptFieldKind 分发 ImGui 控件；写回经 ScriptInspector 反射落到 Behaviour 实例。
    private void DrawField(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            ImGui.TextUnformatted($"{field.Name}: {field.Value}");
            return;
        }

        switch (field.Kind)
        {
            case ScriptFieldKind.Unsupported:
                ImGui.TextUnformatted($"{field.Name}: {field.Value}");
                break;
            case ScriptFieldKind.Boolean:
                DrawBoolean(behaviour, field);
                break;
            case ScriptFieldKind.Number:
                DrawNumber(behaviour, field);
                break;
            case ScriptFieldKind.String:
                DrawString(behaviour, field);
                break;
            case ScriptFieldKind.Enum:
                DrawEnum(behaviour, field);
                break;
            case ScriptFieldKind.Vector:
                DrawVector(behaviour, field);
                break;
            case ScriptFieldKind.Material:
                DrawMaterial(behaviour, field);
                break;
            case ScriptFieldKind.AssetReference:
                ImGui.TextUnformatted($"{field.Name}: {field.Value}");
                break;
            default:
                ImGui.TextUnformatted($"{field.Name}: {field.Value}");
                break;
        }
    }

    private static void DrawBoolean(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        bool value = field.Value is bool current && current;
        if (ImGui.Checkbox(field.Name, ref value))
        {
            _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, value);
        }
    }

    private static void DrawNumber(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        if (IsFloating(field.FieldType))
        {
            float value = Convert.ToSingle(field.Value);
            bool changed = field.RangeMinimum is double min && field.RangeMaximum is double max
                ? ImGui.SliderFloat(field.Name, ref value, (float)min, (float)max)
                : ImGui.InputFloat(field.Name, ref value);
            if (changed && TryNormalizeValue(field, value, null, out object? normalized))
            {
                _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, normalized);
            }

            return;
        }

        int integer = Convert.ToInt32(field.Value);
        bool integerChanged = field.RangeMinimum is double intMin && field.RangeMaximum is double intMax
            ? ImGui.SliderInt(field.Name, ref integer, (int)intMin, (int)intMax)
            : ImGui.InputInt(field.Name, ref integer);
        if (integerChanged && TryNormalizeValue(field, integer, null, out object? normalizedInteger))
        {
            _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, normalizedInteger);
        }
    }

    private static void DrawString(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        string value = field.Value as string ?? string.Empty;
        if (ImGui.InputText(field.Name, ref value, 256))
        {
            _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, value);
        }
    }

    private static void DrawEnum(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        Type enumType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        string[] names = Enum.GetNames(enumType);
        int index = Math.Max(0, Array.IndexOf(names, field.Value?.ToString() ?? string.Empty));
        if (ImGui.Combo(field.Name, ref index, names, names.Length) &&
            index >= 0 &&
            index < names.Length)
        {
            object value = Enum.Parse(enumType, names[index]);
            _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, value);
        }
    }

    private static void DrawVector(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        if (field.Value is Vector2 vector2)
        {
            float x = vector2.X;
            float y = vector2.Y;
            bool changedX = ImGui.InputFloat($"{field.Name}.X", ref x);
            bool changedY = ImGui.InputFloat($"{field.Name}.Y", ref y);
            if (changedX || changedY)
            {
                _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, new Vector2(x, y));
            }

            return;
        }

        if (field.Value is Vector3 vector3)
        {
            float x = vector3.X;
            float y = vector3.Y;
            float z = vector3.Z;
            bool changedX = ImGui.InputFloat($"{field.Name}.X", ref x);
            bool changedY = ImGui.InputFloat($"{field.Name}.Y", ref y);
            bool changedZ = ImGui.InputFloat($"{field.Name}.Z", ref z);
            if (changedX || changedY || changedZ)
            {
                _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, new Vector3(x, y, z));
            }

            return;
        }

        if (field.Value is Vector4 vector4)
        {
            float x = vector4.X;
            float y = vector4.Y;
            float z = vector4.Z;
            float w = vector4.W;
            bool changedX = ImGui.InputFloat($"{field.Name}.X", ref x);
            bool changedY = ImGui.InputFloat($"{field.Name}.Y", ref y);
            bool changedZ = ImGui.InputFloat($"{field.Name}.Z", ref z);
            bool changedW = ImGui.InputFloat($"{field.Name}.W", ref w);
            if (changedX || changedY || changedZ || changedW)
            {
                _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, new Vector4(x, y, z, w));
            }
        }
    }

    private void DrawMaterial(Behaviour behaviour, ScriptFieldDescriptor field)
    {
        if (_materials is null)
        {
            ImGui.TextUnformatted($"{field.Name}: {field.Value}");
            return;
        }

        MaterialChoice[] choices = BuildMaterialChoices(_materials);
        if (choices.Length == 0)
        {
            ImGui.TextUnformatted($"{field.Name}: no live materials");
            return;
        }

        ushort current = field.Value is MaterialId material ? material.Value : ushort.MaxValue;
        string[] names = new string[choices.Length];
        int selected = 0;
        for (int i = 0; i < choices.Length; i++)
        {
            names[i] = choices[i].Name;
            if (choices[i].Id == current)
            {
                selected = i;
            }
        }

        if (ImGui.Combo(field.Name, ref selected, names, names.Length))
        {
            _ = ScriptInspector.TrySetFieldValue(behaviour, field.Name, new MaterialId(choices[selected].Id));
        }
    }

    private bool TryGetSelectedBehaviour(string entityHandle, [NotNullWhen(true)] out Behaviour? behaviour)
    {
        behaviour = null;
        ScriptEntityInspection[] snapshot = Refresh();
        if (!TryFindEntity(snapshot, entityHandle, out ScriptEntityInspection entity) ||
            (uint)_selectedComponentIndex >= (uint)entity.Components.Length)
        {
            return false;
        }

        behaviour = entity.Components[_selectedComponentIndex].Behaviour;
        return true;
    }

    private static bool TryResolveSelection(
        ReadOnlySpan<ScriptEntityInspection> snapshot,
        string? entityHandle,
        out ScriptEntityInspection entity)
    {
        if (entityHandle is not null && TryFindEntity(snapshot, entityHandle, out entity))
        {
            return true;
        }

        if (snapshot.Length > 0)
        {
            entity = snapshot[0];
            return true;
        }

        entity = default;
        return false;
    }

    private static bool TryFindEntity(
        ReadOnlySpan<ScriptEntityInspection> snapshot,
        string entityHandle,
        out ScriptEntityInspection entity)
    {
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (string.Equals(snapshot[i].Handle, entityHandle, StringComparison.Ordinal))
            {
                entity = snapshot[i];
                return true;
            }
        }

        entity = default;
        return false;
    }

    private static MaterialChoice[] BuildMaterialChoices(MaterialTable materials)
    {
        List<MaterialChoice> choices = new(materials.Count);
        for (ushort id = 0; id < materials.Count; id++)
        {
            if (materials.IsTombstone(id))
            {
                continue;
            }

            ref readonly MaterialDef def = ref materials.Get(id);
            choices.Add(new MaterialChoice(id, $"{def.Name} ({id})"));
        }

        return [.. choices];
    }

    private static bool TryNormalizeValue(
        ScriptFieldDescriptor field,
        object? value,
        MaterialTable? materials,
        out object? normalized)
    {
        normalized = null;
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        if (!IsValueInsideRange(field, value))
        {
            return false;
        }

        if (target == typeof(MaterialId))
        {
            return TryNormalizeMaterial(value, materials, out normalized);
        }

        if (target.IsEnum)
        {
            return TryNormalizeEnum(target, value, out normalized);
        }

        try
        {
            normalized = target == typeof(Vector2) || target == typeof(Vector3) || target == typeof(Vector4)
                ? value
                : target == typeof(string)
                    ? value?.ToString() ?? string.Empty
                    : target == typeof(bool)
                        ? Convert.ToBoolean(value)
                        : Convert.ChangeType(value, target);
            return normalized is not null && target.IsInstanceOfType(normalized);
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryNormalizeMaterial(object? value, MaterialTable? materials, out object? normalized)
    {
        normalized = null;
        if (value is MaterialId material)
        {
            normalized = material;
            return true;
        }

        if (value is string name && materials is not null && materials.TryGetId(name, out ushort namedId))
        {
            normalized = new MaterialId(namedId);
            return true;
        }

        try
        {
            ushort id = Convert.ToUInt16(value);
            normalized = new MaterialId(id);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryNormalizeEnum(Type target, object? value, out object? normalized)
    {
        normalized = null;
        if (value is string name && Enum.IsDefined(target, name))
        {
            normalized = Enum.Parse(target, name);
            return true;
        }

        if (value is not null && Enum.IsDefined(target, value))
        {
            normalized = Enum.ToObject(target, value);
            return true;
        }

        return false;
    }

    private static bool IsValueInsideRange(ScriptFieldDescriptor field, object? value)
    {
        if (field.RangeMinimum is not double min || field.RangeMaximum is not double max || value is null)
        {
            return true;
        }

        try
        {
            double numeric = Convert.ToDouble(value);
            return numeric >= min && numeric <= max;
        }
        catch (InvalidCastException)
        {
            return true;
        }
        catch (FormatException)
        {
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsFloating(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        return target == typeof(float) || target == typeof(double) || target == typeof(decimal);
    }

    private readonly record struct MaterialChoice(ushort Id, string Name);
}

/// <summary>
/// ScriptInspectorPanel 使用的热重载入口。
/// </summary>
public interface IScriptInspectorHotReload
{
    /// <summary>
    /// 当前是否具备热重载条件。
    /// </summary>
    bool CanReload { get; }

    /// <summary>
    /// 立即执行一次热重载。
    /// </summary>
    /// <returns>热重载结果。</returns>
    ScriptInspectorHotReloadResult ReloadNow();
}

/// <summary>
/// 基于 ScriptHotReloadController 的 Inspector 热重载入口。
/// </summary>
public sealed class ScriptInspectorHotReloadAdapter : IScriptInspectorHotReload
{
    private readonly ScriptHotReloadController _controller;
    private readonly string _assemblyName;
    private readonly string _sourceDirectory;

    /// <summary>
    /// 创建 Inspector 热重载入口。
    /// </summary>
    /// <param name="controller">脚本热重载控制器。</param>
    /// <param name="assemblyName">动态脚本程序集名。</param>
    /// <param name="sourceDirectory">脚本源目录。</param>
    public ScriptInspectorHotReloadAdapter(ScriptHotReloadController controller, string assemblyName, string sourceDirectory)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        _assemblyName = assemblyName;
        _sourceDirectory = sourceDirectory;
    }

    /// <inheritdoc />
    public bool CanReload => Directory.Exists(_sourceDirectory);

    /// <inheritdoc />
    public ScriptInspectorHotReloadResult ReloadNow()
    {
        if (!CanReload)
        {
            return new ScriptInspectorHotReloadResult(false, $"脚本源目录不存在：{_sourceDirectory}", []);
        }

        // Inspector 手动热重载：先排队目录内全部 .cs，再同步 Apply（与文件监视 debounce 路径共用控制器）。
        _controller.RequestReloadFromDirectory(_assemblyName, _sourceDirectory);
        ScriptHotReloadApplyResult result = _controller.ApplyPendingReload();
        bool success = result.Status == ScriptHotReloadStatus.Reloaded;
        string message = result.Status switch
        {
            ScriptHotReloadStatus.NoPendingReload => "没有待处理脚本热重载",
            ScriptHotReloadStatus.CompileFailed => "脚本编译失败",
            ScriptHotReloadStatus.ApplyFailed => "脚本热重载应用失败，旧脚本保持运行",
            ScriptHotReloadStatus.Reloaded => result.OldContextUnloaded ? "脚本热重载完成" : "脚本热重载完成，旧 ALC 尚未卸载",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "未知热重载状态。"),
        };
        return new ScriptInspectorHotReloadResult(success, message, result.Diagnostics);
    }
}

/// <summary>
/// Inspector 热重载结果。
/// </summary>
/// <param name="Success">热重载是否成功完成。</param>
/// <param name="Message">面板状态文本。</param>
/// <param name="Diagnostics">编译诊断文本。</param>
public readonly record struct ScriptInspectorHotReloadResult(
    bool Success,
    string Message,
    string[] Diagnostics);
