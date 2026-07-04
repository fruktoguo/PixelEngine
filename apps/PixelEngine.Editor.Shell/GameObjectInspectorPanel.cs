using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Editor.Shell;

internal sealed class GameObjectInspectorPanel(EditorSceneModel scene, EditorUndoStack undo, ScriptAssemblyRegistry scripts) : IEditorPanel
{
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly ScriptAssemblyRegistry _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private string _componentSearch = string.Empty;

    public string Title => EditorDockSpace.InspectorWindowTitle;

    public bool Visible { get; set; } = true;

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
        int? stableId = context.Selection.GameObjectStableId ?? _scene.SelectedStableId;
        if (!stableId.HasValue || !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject))
        {
            ImGui.TextUnformatted("未选中 GameObject");
            ImGui.End();
            return;
        }

        DrawHeader(gameObject);
        ImGui.SeparatorText("Transform");
        DrawTransform(gameObject);
        ImGui.SeparatorText("Components");
        DrawComponents(gameObject);
        ImGui.End();
    }

    private void DrawHeader(EditorGameObject gameObject)
    {
        string name = gameObject.Name;
        if (ImGui.InputText("Name", ref name, 128) && !string.IsNullOrWhiteSpace(name) && name != gameObject.Name)
        {
            _undo.Execute(_scene, new RenameGameObjectCommand(gameObject.StableId, name));
        }

        bool enabled = gameObject.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled) && enabled != gameObject.Enabled)
        {
            _undo.Execute(_scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled));
        }

        ImGui.TextUnformatted($"StableId: {gameObject.StableId}");
        ImGui.TextUnformatted(gameObject.PrefabLink?.AssetPath is { Length: > 0 } prefab
            ? $"Prefab: {prefab}"
            : "Prefab: none");
    }

    private void DrawTransform(EditorGameObject gameObject)
    {
        EditorSceneTransform transform = gameObject.Transform.Clone();
        float x = transform.X;
        float y = transform.Y;
        bool changed = false;
        if (ImGui.InputFloat("X", ref x))
        {
            transform.X = x;
            changed = true;
        }

        if (ImGui.InputFloat("Y", ref y))
        {
            transform.Y = y;
            changed = true;
        }

        float rotation = transform.RotationRadians;
        if (ImGui.InputFloat("Rotation", ref rotation))
        {
            transform.RotationRadians = rotation;
            changed = true;
        }

        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;
        if (ImGui.InputFloat("Scale X", ref scaleX))
        {
            transform.ScaleX = scaleX;
            changed = true;
        }

        if (ImGui.InputFloat("Scale Y", ref scaleY))
        {
            transform.ScaleY = scaleY;
            changed = true;
        }

        if (changed)
        {
            _undo.Execute(_scene, new SetTransformCommand(gameObject.StableId, transform));
        }
    }

    private void DrawComponents(EditorGameObject gameObject)
    {
        for (int i = 0; i < gameObject.Components.Count; i++)
        {
            DrawComponent(gameObject, i);
        }

        ImGui.Separator();
        _ = ImGui.InputText("Search", ref _componentSearch, 128);
        Type[] behaviours = GetBehaviourTypes(_componentSearch);
        if (ImGui.BeginCombo("Add Component", behaviours.Length == 0 ? "No Behaviour" : "Select Behaviour"))
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                Type behaviour = behaviours[i];
                string label = behaviour.FullName ?? behaviour.Name;
                if (ImGui.Selectable(label))
                {
                    _undo.Execute(_scene, new AddComponentCommand(gameObject.StableId, new EditorComponentModel(label)));
                    _componentSearch = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawComponent(EditorGameObject gameObject, int componentIndex)
    {
        EditorComponentModel component = gameObject.Components[componentIndex];
        if (!ImGui.CollapsingHeader($"{component.TypeName}##component_{componentIndex}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (ImGui.Button($"Remove##component_remove_{componentIndex}"))
        {
            _undo.Execute(_scene, new RemoveComponentCommand(gameObject.StableId, componentIndex));
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button($"Up##component_up_{componentIndex}") && componentIndex > 0)
        {
            _undo.Execute(_scene, new MoveComponentCommand(gameObject.StableId, componentIndex, componentIndex - 1));
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button($"Down##component_down_{componentIndex}") && componentIndex < gameObject.Components.Count - 1)
        {
            _undo.Execute(_scene, new MoveComponentCommand(gameObject.StableId, componentIndex, componentIndex + 1));
            return;
        }

        if (!TryCreateBehaviour(component.TypeName, out Behaviour? behaviour))
        {
            ImGui.TextUnformatted("Behaviour type unavailable");
            return;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(gameObject.StableId, componentIndex, component, fields[i]);
        }
    }

    private void DrawField(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            ImGui.TextUnformatted($"{field.Name}: {ReadFieldValue(component, field)}");
            return;
        }

        switch (field.Kind)
        {
            case ScriptFieldKind.Boolean:
                DrawBoolean(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Number:
                DrawNumber(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.String:
                DrawString(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Enum:
                DrawEnum(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Vector:
            case ScriptFieldKind.Material:
            case ScriptFieldKind.Unsupported:
            default:
                DrawString(stableId, componentIndex, component, field);
                break;
        }
    }

    private void DrawBoolean(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        bool value = bool.TryParse(ReadFieldValue(component, field), out bool parsed) && parsed;
        if (ImGui.Checkbox(field.Name, ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value.ToString()));
        }
    }

    private void DrawNumber(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        float value = float.TryParse(ReadFieldValue(component, field), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;
        if (ImGui.InputFloat(field.Name, ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(
                stableId,
                componentIndex,
                field.Name,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private void DrawString(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        if (ImGui.InputText(field.Name, ref value, 256))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value));
        }
    }

    private void DrawEnum(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        Type enumType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        string[] names = Enum.GetNames(enumType);
        string current = ReadFieldValue(component, field);
        int index = Math.Max(0, Array.IndexOf(names, current));
        if (ImGui.Combo(field.Name, ref index, names, names.Length) && index >= 0 && index < names.Length)
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, names[index]));
        }
    }

    private static string ReadFieldValue(EditorComponentModel component, ScriptFieldDescriptor field)
    {
        return component.SerializedFields.TryGetValue(field.Name, out string? value)
            ? value
            : SerializeDefaultValue(field.Value);
    }

    private static string SerializeDefaultValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean.ToString(),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private Type[] GetBehaviourTypes(string filter)
    {
        List<Type> result = [];
        for (int i = 0; i < _scripts.Assemblies.Count; i++)
        {
            foreach (Type type in _scripts.Assemblies[i].GetTypes())
            {
                if (!IsConcreteBehaviour(type))
                {
                    continue;
                }

                string name = type.FullName ?? type.Name;
                if (string.IsNullOrWhiteSpace(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(type);
                }
            }
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.FullName ?? a.Name, b.FullName ?? b.Name));
        return [.. result];
    }

    private bool TryCreateBehaviour(string typeName, out Behaviour behaviour)
    {
        for (int i = 0; i < _scripts.Assemblies.Count; i++)
        {
            Type? type = _scripts.Assemblies[i].GetType(typeName, throwOnError: false);
            if (IsConcreteBehaviour(type))
            {
                behaviour = (Behaviour)Activator.CreateInstance(type!)!;
                return true;
            }
        }

        behaviour = null!;
        return false;
    }

    private static bool IsConcreteBehaviour(Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }
}
