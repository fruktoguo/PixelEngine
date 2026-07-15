using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private const string RecentProjectsResource = "editor:workspace:recent-projects";

    private AutomationBackgroundPreparation PrepareRecentProjectsList(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.WorkspaceRecentListMethod);
        RecentProjectsAutomationSnapshot source = _app.RecentProjects.CaptureAutomationSnapshot();
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                AutomationRecentProjectInfo[] items = new AutomationRecentProjectInfo[source.Entries.Length];
                for (int i = 0; i < source.Entries.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RecentProjectEntry entry = source.Entries[i];
                    items[i] = MapRecentProject(
                        entry,
                        File.Exists(Path.Combine(entry.ProjectPath, EditorProject.ProjectFileName)));
                }

                return ValueTask.FromResult<object?>(
                    new PreparedRecentProjectList(source, request, items));
            },
        };
    }

    private AutomationOperationResult ListRecentProjects(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        PreparedRecentProjectList prepared = context.RequirePreparedState<PreparedRecentProjectList>();
        if (!RecentProjectsStore.SnapshotsEqual(
            _app.RecentProjects.CaptureAutomationSnapshot(),
            prepared.Source))
        {
            throw StateUnavailable(
                "workspace.recent.list preparation 期间最近工程状态发生变化；请重试。");
        }

        AutomationRecentProjectInfo[] filtered = ApplyFilter(
            prepared.Items,
            prepared.Request.Filter,
            MatchRecentProject);
        SortRecentProjects(filtered, prepared.Request.Sort);
        string fingerprint = Fingerprint(
            prepared.Items,
            AutomationJsonContext.Default.AutomationRecentProjectInfoArray);
        PageSlice<AutomationRecentProjectInfo> page = SlicePage(
            "workspace.recent",
            fingerprint,
            prepared.Request,
            filtered);
        return Result(
            new AutomationRecentProjectListResponse
            {
                Items = page.Items,
                Page = page.Info,
            },
            AutomationJsonContext.Default.AutomationRecentProjectListResponse,
            [RecentProjectsResource]);
    }

    private AutomationBackgroundPreparation PrepareRecentProjectFavorite(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRecentProjectFavoriteSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRecentProjectFavoriteSetRequest,
            AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod);
        ValidateSchema(
            request.SchemaVersion,
            AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod);
        string projectId = ValidateRecentProjectId(request.ProjectId);
        return PrepareRecentProjectMutation(
            context,
            AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod,
            "Set Recent Project Favorite",
            projectId,
            (source, projectPath) => RecentProjectsStore.TryCreateFavoriteSnapshot(
                source,
                projectPath,
                request.Favorite,
                out RecentProjectsAutomationSnapshot result)
                    ? result
                    : source,
            new AutomationRecentProjectMutationResult
            {
                ProjectId = projectId,
                Removed = false,
                Favorite = request.Favorite,
            });
    }

    private AutomationBackgroundPreparation PrepareRecentProjectRemove(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRecentProjectRemoveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRecentProjectRemoveRequest,
            AutomationProtocolConstants.WorkspaceRecentRemoveMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WorkspaceRecentRemoveMethod);
        string projectId = ValidateRecentProjectId(request.ProjectId);
        return PrepareRecentProjectMutation(
            context,
            AutomationProtocolConstants.WorkspaceRecentRemoveMethod,
            "Remove Recent Project",
            projectId,
            (source, projectPath) => RecentProjectsStore.TryCreateRemoveSnapshot(
                source,
                projectPath,
                out RecentProjectsAutomationSnapshot result)
                    ? result
                    : source,
            new AutomationRecentProjectMutationResult
            {
                ProjectId = projectId,
                Removed = true,
            });
    }

    private AutomationBackgroundPreparation PrepareRecentProjectMutation(
        AutomationScheduledContext context,
        string method,
        string name,
        string projectId,
        Func<RecentProjectsAutomationSnapshot, string, RecentProjectsAutomationSnapshot> mutate,
        AutomationRecentProjectMutationResult response)
    {
        RecentProjectsStore store = _app.RecentProjects;
        AutomationPreparationScope scope = RequirePreparationScope(context, method);
        string? storagePath = store.StoragePath is null
            ? null
            : Path.GetFullPath(store.StoragePath);
        string? authorityRoot = storagePath is null
            ? null
            : Path.GetDirectoryName(storagePath) ??
                throw new InvalidOperationException("Recent projects storage path 缺少父目录。");
        string workspaceKey = $"pixelengine.settings.recent-projects:{storagePath ?? "memory"}";
        SettingsPreparationWorkspace<RecentProjectsAutomationSnapshot> workspace = scope.GetOrAdd(
            workspaceKey,
            () => new SettingsPreparationWorkspace<RecentProjectsAutomationSnapshot>(
                authorityRoot,
                store.CaptureAutomationSnapshot(),
                RecentProjectsStore.SnapshotsEqual));
        RecentProjectEntry entry = RequireRecentProject(workspace.PlannedState, projectId);
        RecentProjectsAutomationSnapshot after = mutate(workspace.PlannedState, entry.ProjectPath);
        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationRecentProjectMutationResult);
        return PrepareSettingsMutation(
            workspace,
            method,
            name,
            after,
            serialized,
            storagePath is null
                ? static (_, _) => EmptySettingsFiles
                : (_, cancellationToken) => SingleSettingsFile(
                    storagePath,
                    RecentProjectsStore.SerializeCanonical(after),
                    cancellationToken),
            store.CaptureAutomationSnapshot,
            store.RestoreAutomationSnapshot);
    }

    private AutomationOperationResult CommitPreparedRecentProjectsMutation(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        PreparedSettingsMutation<RecentProjectsAutomationSnapshot> prepared =
            context.RequirePreparedState<PreparedSettingsMutation<RecentProjectsAutomationSnapshot>>();
        if (!string.Equals(
                prepared.Method,
                AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod,
                StringComparison.Ordinal) &&
            !string.Equals(
                prepared.Method,
                AutomationProtocolConstants.WorkspaceRecentRemoveMethod,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared recent-project method 不受支持：{prepared.Method}。");
        }

        if (!prepared.IsSourceCurrent())
        {
            throw StateUnavailable(
                $"{prepared.Method} preparation 期间最近工程状态发生变化；请刷新 revision 后重试。");
        }

        if (!prepared.StateChanged)
        {
            prepared.CommitNoChange();
            return new AutomationOperationResult
            {
                Payload = prepared.Response,
                ResourceIds = [RecentProjectsResource],
            };
        }

        context.Revisions.EnsureCanAdvance([RecentProjectsResource]);
        prepared.ApplyCommand();
        AutomationRevisionSnapshot revision = context.Revisions.Advance([RecentProjectsResource]);
        return new AutomationOperationResult
        {
            Payload = prepared.Response,
            ResourceIds = [RecentProjectsResource],
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private AutomationOperationResult ListShortcuts(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.ShortcutListMethod);
        ReadOnlySpan<EditorShortcutDefinition> definitions = EditorShortcutCatalog.All;
        AutomationShortcutInfo[] items = new AutomationShortcutInfo[definitions.Length];
        for (int i = 0; i < definitions.Length; i++)
        {
            EditorShortcutDefinition definition = definitions[i];
            items[i] = new AutomationShortcutInfo
            {
                UiCommandId = definition.UiCommandId,
                Action = definition.Action,
                Key = definition.Key,
                Control = definition.Control,
                Shift = definition.Shift,
                Alt = definition.Alt,
                Super = definition.Super,
                DisplayText = definition.DisplayText,
            };
        }

        AutomationShortcutInfo[] filtered = ApplyFilter(items, request.Filter, MatchShortcut);
        SortShortcuts(filtered, request.Sort);
        string fingerprint = Fingerprint(
            items,
            AutomationJsonContext.Default.AutomationShortcutInfoArray);
        PageSlice<AutomationShortcutInfo> page = SlicePage(
            "settings.shortcuts",
            fingerprint,
            request,
            filtered);
        return Result(
            new AutomationShortcutListResponse
            {
                Items = page.Items,
                Page = page.Info,
            },
            AutomationJsonContext.Default.AutomationShortcutListResponse,
            [PreferencesResource]);
    }

    private static AutomationRecentProjectInfo MapRecentProject(
        RecentProjectEntry entry,
        bool projectFileExists)
    {
        return new AutomationRecentProjectInfo
        {
            ProjectId = EditorAutomationRuntime.StableProjectId(entry.ProjectPath),
            Name = entry.Name,
            RootPath = entry.ProjectPath,
            LastOpenedUtc = entry.LastOpenedUtc,
            Favorite = entry.Favorite,
            ProjectFileExists = projectFileExists,
        };
    }

    private static RecentProjectEntry RequireRecentProject(
        RecentProjectsAutomationSnapshot snapshot,
        string projectId)
    {
        for (int i = 0; i < snapshot.Entries.Length; i++)
        {
            RecentProjectEntry entry = snapshot.Entries[i];
            if (string.Equals(
                EditorAutomationRuntime.StableProjectId(entry.ProjectPath),
                projectId,
                StringComparison.Ordinal))
            {
                return entry;
            }
        }

        throw NotFound($"Recent project '{projectId}' 不存在。");
    }

    private static string ValidateRecentProjectId(string value)
    {
        string projectId = value?.Trim() ?? string.Empty;
        if (projectId.Length != 64)
        {
            throw Invalid("projectId 必须是 64 位小写十六进制稳定 ID。");
        }

        for (int i = 0; i < projectId.Length; i++)
        {
            char character = projectId[i];
            if (!char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f'))
            {
                throw Invalid("projectId 必须是 64 位小写十六进制稳定 ID。");
            }
        }

        return projectId;
    }

    private static bool MatchRecentProject(
        AutomationRecentProjectInfo item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "projectId" => MatchString(item.ProjectId, clause),
            "name" => MatchString(item.Name, clause),
            "rootPath" => MatchString(item.RootPath, clause),
            "favorite" => MatchBoolean(item.Favorite, clause),
            "projectFileExists" => MatchBoolean(item.ProjectFileExists, clause),
            "lastOpenedUtc" => MatchString(item.LastOpenedUtc.ToString("O"), clause),
            _ => throw Invalid($"Recent project filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortRecentProjects(
        AutomationRecentProjectInfo[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "projectId" => string.CompareOrdinal(left.ProjectId, right.ProjectId),
                    "name" => CompareText(left.Name, right.Name),
                    "rootPath" => CompareText(left.RootPath, right.RootPath),
                    "favorite" => left.Favorite.CompareTo(right.Favorite),
                    "projectFileExists" => left.ProjectFileExists.CompareTo(right.ProjectFileExists),
                    "lastOpenedUtc" => left.LastOpenedUtc.CompareTo(right.LastOpenedUtc),
                    _ => throw Invalid($"Recent project sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.ProjectId, right.ProjectId);
        });
    }

    private static bool MatchShortcut(
        AutomationShortcutInfo item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "uiCommandId" => MatchString(item.UiCommandId, clause),
            "action" => MatchString(item.Action, clause),
            "key" => MatchString(item.Key, clause),
            "control" => MatchBoolean(item.Control, clause),
            "shift" => MatchBoolean(item.Shift, clause),
            "alt" => MatchBoolean(item.Alt, clause),
            "super" => MatchBoolean(item.Super, clause),
            "displayText" => MatchString(item.DisplayText, clause),
            _ => throw Invalid($"Shortcut filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortShortcuts(
        AutomationShortcutInfo[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "uiCommandId" => string.CompareOrdinal(left.UiCommandId, right.UiCommandId),
                    "action" => CompareText(left.Action, right.Action),
                    "key" => CompareText(left.Key, right.Key),
                    "control" => left.Control.CompareTo(right.Control),
                    "shift" => left.Shift.CompareTo(right.Shift),
                    "alt" => left.Alt.CompareTo(right.Alt),
                    "super" => left.Super.CompareTo(right.Super),
                    "displayText" => CompareText(left.DisplayText, right.DisplayText),
                    _ => throw Invalid($"Shortcut sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.UiCommandId, right.UiCommandId);
        });
    }

    private sealed record PreparedRecentProjectList(
        RecentProjectsAutomationSnapshot Source,
        AutomationPageRequest Request,
        AutomationRecentProjectInfo[] Items);
}
