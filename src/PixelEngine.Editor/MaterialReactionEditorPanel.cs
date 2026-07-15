using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

#pragma warning disable IDE0032, IDE0290

/// <summary>
/// 材质与反应实时编辑器，负责编辑 JSON 文档并通过内容服务触发稳定热重载。
/// </summary>
public sealed class MaterialReactionEditorPanel(IMaterialReactionContentService content) : IEditorPanel
{
    private readonly IMaterialReactionContentService _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly MaterialLegendPreview _legendPreview = new();
    private int _selectedMaterial;
    private int _selectedReaction;

    /// <inheritdoc />
    public string Title => EditorDockSpace.MaterialReactionEditorWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次状态消息。
    /// </summary>
    public string Status { get; private set; } = "未加载";

    /// <summary>
    /// 当前编辑文档。
    /// </summary>
    public MaterialReactionEditorDocument Document { get; private set; } = new();

    /// <summary>
    /// 材质图例只读预览。
    /// </summary>
    public MaterialLegendPreview LegendPreview => _legendPreview;

    /// <summary>捕获草稿、状态与当前行选择的完整 before-image。</summary>
    /// <returns>可供 Undo/Redo 恢复的游离面板状态。</returns>
    public MaterialReactionEditorPanelState CaptureState()
    {
        return new MaterialReactionEditorPanelState(
            Document.Clone(),
            Status,
            _selectedMaterial,
            _selectedReaction);
    }

    /// <summary>检查面板草稿、状态和当前行选择是否仍与快照一致。</summary>
    /// <param name="state">待验证的面板状态。</param>
    /// <returns>全部可观察状态未变化时为 true。</returns>
    public bool StateEquals(MaterialReactionEditorPanelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _selectedMaterial == state.SelectedMaterial &&
            _selectedReaction == state.SelectedReaction &&
            string.Equals(Status, state.Status, StringComparison.Ordinal) &&
            Document.ContentEquals(state.Document);
    }

    /// <summary>恢复完整面板 before/after-image。</summary>
    /// <param name="state">由 <see cref="CaptureState"/> 捕获的状态。</param>
    public void RestoreState(MaterialReactionEditorPanelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Document = state.Document.Clone();
        Status = state.Status;
        _selectedMaterial = ClampSelection(state.SelectedMaterial, Document.Materials.Count);
        _selectedReaction = ClampSelection(state.SelectedReaction, Document.Reactions.Count);
        _legendPreview.Rebuild(Document);
    }

    /// <summary>捕获当前可编辑文档的深副本。</summary>
    /// <returns>不共享面板行对象的文档。</returns>
    public MaterialReactionEditorDocument CaptureDocument()
    {
        return Document.Clone();
    }

    /// <summary>原子替换面板草稿并更新可观察状态。</summary>
    /// <param name="document">目标完整文档。</param>
    /// <param name="status">新的状态文本。</param>
    public void ReplaceDocument(MaterialReactionEditorDocument document, string status)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(status);
        Document = document.Clone();
        _selectedMaterial = ClampSelection(_selectedMaterial, Document.Materials.Count);
        _selectedReaction = ClampSelection(_selectedReaction, Document.Reactions.Count);
        Status = status;
        _legendPreview.Rebuild(Document);
    }

    /// <summary>更新面板最近一次操作状态。</summary>
    /// <param name="status">完整状态文本。</param>
    public void SetStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Status = status;
    }

    /// <summary>
    /// 重新加载文件文档。
    /// </summary>
    public void Reload()
    {
        Document = _content.Load();
        _selectedMaterial = Math.Min(_selectedMaterial, Math.Max(0, Document.Materials.Count - 1));
        _selectedReaction = Math.Min(_selectedReaction, Math.Max(0, Document.Reactions.Count - 1));
        Status = $"已加载 {Document.Materials.Count} 个材质、{Document.Reactions.Count} 条源反应";
    }

    /// <summary>
    /// 预览 tag 展开与 packed reaction 数量。
    /// </summary>
    public MaterialReactionPreviewResult Preview()
    {
        MaterialReactionPreviewResult preview = _content.Preview(Document);
        Status = preview.Message;
        return preview;
    }

    /// <summary>
    /// 写回 JSON 并触发稳定热重载。
    /// </summary>
    public MaterialReactionApplyResult Apply()
    {
        MaterialReactionApplyResult result = _content.Apply(Document);
        Status = FormatApplyStatus(result);
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
        DrawToolbar();
        ImGui.Separator();
        DrawMaterials();
        ImGui.Separator();
        DrawReactions();
        ImGui.Separator();
        DrawTagRepresentatives();
        ImGui.TextUnformatted(Status);
        ImGui.End();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("重新加载"))
        {
            Reload();
        }

        ImGui.SameLine();
        if (ImGui.Button("预览展开"))
        {
            _ = Preview();
        }

        ImGui.SameLine();
        if (ImGui.Button("保存并热重载"))
        {
            _ = Apply();
        }
    }

    /// <summary>构建人工面板与 automation 共用的 Apply 状态文本。</summary>
    /// <param name="result">热重载结果。</param>
    /// <returns>完整状态文本。</returns>
    public static string FormatApplyStatus(MaterialReactionApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string status = $"{result.DiagnosticMessage}；新增 {result.MaterialReload.AddedCount}，保留 {result.MaterialReload.PreservedCount}，packed reaction {result.PackedReactionCount}";
        return result.CleanupPending
            ? $"{status}；journal 清理待处理：{result.RetainedJournalPath}"
            : status;
    }

    private static int ClampSelection(int selection, int count)
    {
        return count == 0 ? 0 : Math.Clamp(selection, 0, count - 1);
    }

    private void DrawMaterials()
    {
        if (ImGui.Button("新增材质"))
        {
            Document.Materials.Add(new MaterialEditorRow { Name = "new_material", Type = "Powder", HeatCapacity = 1f, TextureId = -1 });
            _selectedMaterial = Document.Materials.Count - 1;
        }

        if (Document.Materials.Count == 0)
        {
            ImGui.TextUnformatted("无材质");
            return;
        }

        if (ImGui.BeginTable("material_rows", 10, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("runtime id");
            ImGui.TableSetupColumn("name");
            ImGui.TableSetupColumn("type");
            ImGui.TableSetupColumn("density");
            ImGui.TableSetupColumn("flow rate");
            ImGui.TableSetupColumn("max integrity");
            ImGui.TableSetupColumn("rubble target");
            ImGui.TableSetupColumn("render style");
            ImGui.TableSetupColumn("legend category");
            ImGui.TableSetupColumn("tags");
            ImGui.TableHeadersRow();
            for (int i = 0; i < Document.Materials.Count; i++)
            {
                MaterialEditorRow row = Document.Materials[i];
                ImGui.TableNextRow();
                _ = ImGui.TableNextColumn();
                if (ImGui.Selectable(row.RuntimeId?.ToString() ?? "new", _selectedMaterial == i))
                {
                    _selectedMaterial = i;
                }

                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_name_{i}", row.Name, value => row.Name = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_type_{i}", row.Type, value => row.Type = value, 32);
                _ = ImGui.TableNextColumn();
                _ = InputInt($"##mat_density_{i}", row.Density, value => row.Density = value);
                _ = ImGui.TableNextColumn();
                _ = InputInt($"##mat_flow_{i}", row.FlowRate, value => row.FlowRate = value);
                _ = ImGui.TableNextColumn();
                _ = InputInt($"##mat_integrity_{i}", row.MaxIntegrity, value => row.MaxIntegrity = value);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_rubble_{i}", row.RubbleTarget, value => row.RubbleTarget = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_style_{i}", row.RenderStyle, value => row.RenderStyle = value, 64);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_legend_{i}", row.LegendCategory, value => row.LegendCategory = value, 64);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##mat_tags_{i}", row.Tags, value => row.Tags = value, 192);
            }

            ImGui.EndTable();
        }

        DrawSelectedMaterial(Document.Materials[Math.Clamp(_selectedMaterial, 0, Document.Materials.Count - 1)]);
        _legendPreview.Rebuild(Document);
        _legendPreview.Draw();
    }

    private static void DrawSelectedMaterial(MaterialEditorRow row)
    {
        ImGui.TextUnformatted($"编辑材质：{row.Name}  id={(row.RuntimeId.HasValue ? row.RuntimeId.Value.ToString() : "new")}");
        _ = InputInt("flow rate", row.FlowRate, value => row.FlowRate = value);
        _ = Checkbox("liquid static", row.LiquidStatic, value => row.LiquidStatic = value);
        _ = Checkbox("liquid sand", row.LiquidSand, value => row.LiquidSand = value);
        _ = InputInt("flammability", row.Flammability, value => row.Flammability = value);
        _ = InputInt("auto ignition", row.AutoIgnitionTemp, value => row.AutoIgnitionTemp = value);
        _ = InputInt("fire hp", row.FireHp, value => row.FireHp = value);
        _ = InputInt("temperature of fire", row.TemperatureOfFire, value => row.TemperatureOfFire = value);
        _ = InputInt("generates smoke", row.GeneratesSmoke, value => row.GeneratesSmoke = value);
        DrawNullableFloat("melt point", row.MeltPoint, value => row.MeltPoint = value);
        _ = InputText("melt target", row.MeltTarget, value => row.MeltTarget = value, 96);
        DrawNullableFloat("freeze point", row.FreezePoint, value => row.FreezePoint = value);
        _ = InputText("freeze target", row.FreezeTarget, value => row.FreezeTarget = value, 96);
        DrawNullableFloat("boil point", row.BoilPoint, value => row.BoilPoint = value);
        _ = InputText("boil target", row.BoilTarget, value => row.BoilTarget = value, 96);
        _ = InputInt("heat conduct", row.HeatConduct, value => row.HeatConduct = value);
        _ = InputFloat("heat capacity", row.HeatCapacity, value => row.HeatCapacity = value);
        _ = InputInt("default lifetime", row.DefaultLifetime, value => row.DefaultLifetime = value);
        _ = InputInt("durability", row.Durability, value => row.Durability = value);
        _ = InputInt("max integrity", row.MaxIntegrity, value => row.MaxIntegrity = value);
        _ = InputText("rubble target", row.RubbleTarget, value => row.RubbleTarget = value, 96);
        _ = InputInt("debris count", row.DebrisCount, value => row.DebrisCount = value);
        _ = InputInt("mine yield", row.MineYield, value => row.MineYield = value);
        _ = InputInt("texture id", row.TextureId, value => row.TextureId = value);
        int baseColor = unchecked((int)row.BaseColor);
        if (ImGui.InputInt("base color BGRA", ref baseColor))
        {
            row.BaseColor = unchecked((uint)baseColor);
        }

        _ = InputInt("color noise", row.ColorNoise, value => row.ColorNoise = value);
        _ = InputText("render style", row.RenderStyle, value => row.RenderStyle = value, 64);
        _ = InputText("legend category", row.LegendCategory, value => row.LegendCategory = value, 64);
        int outlineColor = unchecked((int)row.OutlineColorBGRA);
        if (ImGui.InputInt("outline color BGRA", ref outlineColor))
        {
            row.OutlineColorBGRA = unchecked((uint)outlineColor);
        }

        _ = InputInt("alpha", row.Alpha, value => row.Alpha = value);
        int flowTint = unchecked((int)row.FlowTintBGRA);
        if (ImGui.InputInt("flow tint BGRA", ref flowTint))
        {
            row.FlowTintBGRA = unchecked((uint)flowTint);
        }

        _ = InputText("display name", row.DisplayName, value => row.DisplayName = value, 96);
        _ = Checkbox("legend visible", row.LegendVisible, value => row.LegendVisible = value);
        _ = InputInt("audio impact", row.ImpactCue, value => row.ImpactCue = value);
        _ = InputInt("audio fire", row.FireCue, value => row.FireCue = value);
        _ = InputInt("audio splash", row.SplashCue, value => row.SplashCue = value);
        _ = InputInt("audio explosion", row.ExplosionCue, value => row.ExplosionCue = value);
        _ = InputInt("audio shatter", row.ShatterCue, value => row.ShatterCue = value);
        _ = InputInt("audio ambient", row.AmbientCue, value => row.AmbientCue = value);
    }

    private void DrawReactions()
    {
        if (ImGui.Button("新增反应"))
        {
            Document.Reactions.Add(new ReactionEditorRow { Probability = 100 });
            _selectedReaction = Document.Reactions.Count - 1;
        }

        if (Document.Reactions.Count == 0)
        {
            ImGui.TextUnformatted("无反应");
            return;
        }

        if (ImGui.BeginTable("reaction_rows", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("input A");
            ImGui.TableSetupColumn("input B");
            ImGui.TableSetupColumn("output A");
            ImGui.TableSetupColumn("output B");
            ImGui.TableSetupColumn("probability");
            ImGui.TableSetupColumn("flags");
            ImGui.TableHeadersRow();
            for (int i = 0; i < Document.Reactions.Count; i++)
            {
                ReactionEditorRow row = Document.Reactions[i];
                ImGui.TableNextRow();
                _ = ImGui.TableNextColumn();
                if (ImGui.Selectable($"##reaction_select_{i}", _selectedReaction == i))
                {
                    _selectedReaction = i;
                }

                ImGui.SameLine();
                _ = InputText($"##rx_a_{i}", row.InputA, value => row.InputA = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##rx_b_{i}", row.InputB, value => row.InputB = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##rx_oa_{i}", row.OutputA, value => row.OutputA = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##rx_ob_{i}", row.OutputB, value => row.OutputB = value, 96);
                _ = ImGui.TableNextColumn();
                _ = InputInt($"##rx_p_{i}", row.Probability, value => row.Probability = value);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##rx_flags_{i}", row.Flags, value => row.Flags = value, 128);
            }

            ImGui.EndTable();
        }
    }

    private void DrawTagRepresentatives()
    {
        if (ImGui.Button("新增 tag representative"))
        {
            Document.TagRepresentatives.Add(new TagRepresentativeEditorRow());
        }

        if (ImGui.BeginTable("tag_representatives", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("tag");
            ImGui.TableSetupColumn("material");
            ImGui.TableHeadersRow();
            for (int i = 0; i < Document.TagRepresentatives.Count; i++)
            {
                TagRepresentativeEditorRow row = Document.TagRepresentatives[i];
                ImGui.TableNextRow();
                _ = ImGui.TableNextColumn();
                _ = InputText($"##tag_{i}", row.Tag, value => row.Tag = value, 64);
                _ = ImGui.TableNextColumn();
                _ = InputText($"##tag_mat_{i}", row.Material, value => row.Material = value, 96);
            }

            ImGui.EndTable();
        }
    }

    private static void DrawNullableFloat(string label, float? value, Action<float?> assign)
    {
        bool enabled = value.HasValue;
        if (ImGui.Checkbox($"{label} enabled", ref enabled))
        {
            value = enabled ? 0f : null;
            assign(value);
        }

        if (enabled)
        {
            float concrete = value.GetValueOrDefault();
            if (ImGui.InputFloat(label, ref concrete))
            {
                assign(concrete);
            }
        }
    }

    private static bool InputText(string label, string value, Action<string> assign, uint maxLength)
    {
        string editable = value;
        bool changed = ImGui.InputText(label, ref editable, maxLength);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }

    private static bool InputInt(string label, int value, Action<int> assign)
    {
        int editable = value;
        bool changed = ImGui.InputInt(label, ref editable);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }

    private static bool InputFloat(string label, float value, Action<float> assign)
    {
        float editable = value;
        bool changed = ImGui.InputFloat(label, ref editable);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }

    private static bool Checkbox(string label, bool value, Action<bool> assign)
    {
        bool editable = value;
        bool changed = ImGui.Checkbox(label, ref editable);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }
}

/// <summary>材质/反应面板 Undo/Redo 使用的完整不可变 before/after-image。</summary>
public sealed class MaterialReactionEditorPanelState
{
    internal MaterialReactionEditorPanelState(
        MaterialReactionEditorDocument document,
        string status,
        int selectedMaterial,
        int selectedReaction)
    {
        Document = document;
        Status = status;
        SelectedMaterial = selectedMaterial;
        SelectedReaction = selectedReaction;
    }

    internal MaterialReactionEditorDocument Document { get; }

    internal string Status { get; }

    internal int SelectedMaterial { get; }

    internal int SelectedReaction { get; }
}

#pragma warning restore IDE0032, IDE0290
