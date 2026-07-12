using System.Globalization;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View authoring 世界边界，单位为 cell。
/// </summary>
internal readonly record struct SceneAuthoringBounds(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;
}

/// <summary>
/// Scene View 中可绘制、可选或仅用于说明的 marker。
/// </summary>
internal readonly record struct SceneAuthoringMarker(
    int? StableId,
    string Name,
    Vector2 Position,
    SceneAuthoringMarkerKind Kind,
    float RotationRadians,
    float ScaleX,
    float ScaleY);

internal enum SceneAuthoringMarkerKind
{
    GameObject,
    PlayerSpawn,
    Goal,
}

/// <summary>
/// 从场景 authoring 数据构建的只读预览；不执行项目脚本。
/// </summary>
internal sealed record SceneAuthoringPreview(
    string SceneName,
    SceneAuthoringBounds Bounds,
    SceneAuthoringMarker[] Markers,
    bool HasAuthoritativeWorld,
    int? WorldOwnerStableId,
    bool IsTestScene,
    bool IsExplicitEmptyScene,
    string StatusLabel);

/// <summary>
/// 把声明式 Scene 与显式 authoring world provider 快照投影为受控预览。
/// </summary>
internal static class SceneAuthoringPreviewBuilder
{
    private const float DefaultWidth = 320f;
    private const float DefaultHeight = 180f;

    public static SceneAuthoringPreview Build(EditorSceneModel scene)
    {
        return Build(scene, default);
    }

    public static SceneAuthoringPreview Build(
        EditorSceneModel scene,
        AuthoringWorldPreviewSnapshot authoringWorld)
    {
        ArgumentNullException.ThrowIfNull(scene);
        List<SceneAuthoringMarker> markers = [];
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        bool hasPlayerSpawnObject = false;
        bool hasGoalObject = false;
        Vector2? legacyPlayerSpawn = null;
        Vector2? legacyGoal = null;

        foreach (EditorGameObject gameObject in scene.EnumerateDepthFirst())
        {
            // Unity 的 Scene Visibility 是纯编辑器状态：隐藏对象及其子级只影响 authoring 预览，
            // 不修改 GameObject active，也不进入 .scene / runtime 投影。
            if (!scene.IsSceneVisible(gameObject.StableId))
            {
                continue;
            }

            EditorSceneTransform transform = scene.ComputeWorldTransform(gameObject.StableId);
            Vector2 position = new(transform.X, transform.Y);
            SceneAuthoringMarkerKind markerKind = ResolveMarkerKind(gameObject);
            markers.Add(new SceneAuthoringMarker(
                gameObject.StableId,
                gameObject.Name,
                position,
                markerKind,
                transform.RotationRadians,
                transform.ScaleX,
                transform.ScaleY));
            hasPlayerSpawnObject |= markerKind == SceneAuthoringMarkerKind.PlayerSpawn;
            hasGoalObject |= markerKind == SceneAuthoringMarkerKind.Goal;
            minX = MathF.Min(minX, position.X);
            minY = MathF.Min(minY, position.Y);
            maxX = MathF.Max(maxX, position.X);
            maxY = MathF.Max(maxY, position.Y);

            for (int i = 0; i < gameObject.Components.Count; i++)
            {
                EditorComponentModel component = gameObject.Components[i];
                if (!authoringWorld.HasWorld ||
                    authoringWorld.WorldOwnerStableId != gameObject.StableId)
                {
                    continue;
                }

                float minLegacyX = -authoringWorld.Bounds.Width;
                float maxLegacyX = authoringWorld.Bounds.Width * 2f;
                float minLegacyY = -authoringWorld.Bounds.Height;
                float maxLegacyY = authoringWorld.Bounds.Height * 2f;
                if (!legacyPlayerSpawn.HasValue &&
                    TryReadFiniteFloat(component, "PlayerSpawnX", minLegacyX, maxLegacyX, out float spawnX) &&
                    TryReadFiniteFloat(component, "PlayerSpawnY", minLegacyY, maxLegacyY, out float spawnY))
                {
                    legacyPlayerSpawn = new Vector2(spawnX, spawnY);
                }

                if (!legacyGoal.HasValue &&
                    TryReadFiniteFloat(component, "GoalX", minLegacyX, maxLegacyX, out float goalX) &&
                    TryReadFiniteFloat(component, "GoalY", minLegacyY, maxLegacyY, out float goalY))
                {
                    legacyGoal = new Vector2(goalX, goalY);
                }
            }
        }

        // v1/probe 场景没有真实 anchor GameObject 时仍显示只读兼容 marker；
        // v2 场景的 PlayerSpawnPoint/GoalPoint 拥有 StableId，会走标准选择、Inspector、gizmo 与保存链。
        if (!hasPlayerSpawnObject && legacyPlayerSpawn.HasValue)
        {
            markers.Add(new SceneAuthoringMarker(
                null,
                EditorLocalization.Get("scene.playerSpawn", "Player Spawn"),
                legacyPlayerSpawn.Value,
                SceneAuthoringMarkerKind.PlayerSpawn,
                0f,
                1f,
                1f));
        }

        if (!hasGoalObject && legacyGoal.HasValue)
        {
            markers.Add(new SceneAuthoringMarker(
                null,
                EditorLocalization.Get("scene.goal", "Goal"),
                legacyGoal.Value,
                SceneAuthoringMarkerKind.Goal,
                0f,
                1f,
                1f));
        }

        bool explicitEmpty = scene.Count == 0;
        bool testScene = IsTestScene(scene.Name);
        SceneAuthoringBounds bounds = authoringWorld.HasWorld
            ? authoringWorld.Bounds
            : BuildObjectBounds(minX, minY, maxX, maxY);
        string status = explicitEmpty
            ? testScene ? "测试场景 · 显式空场景" : "显式空场景"
            : testScene ? "测试场景 · Authoring Preview" : "Authoring Preview";
        return new SceneAuthoringPreview(
            scene.Name,
            bounds,
            [.. markers],
            authoringWorld.HasWorld,
            authoringWorld.HasWorld ? authoringWorld.WorldOwnerStableId : null,
            testScene,
            explicitEmpty,
            status);
    }

    private static SceneAuthoringBounds BuildObjectBounds(float minX, float minY, float maxX, float maxY)
    {
        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
        {
            return new SceneAuthoringBounds(0f, 0f, DefaultWidth, DefaultHeight);
        }

        const float margin = 48f;
        float width = MathF.Max(96f, maxX - minX + (margin * 2f));
        float height = MathF.Max(96f, maxY - minY + (margin * 2f));
        return new SceneAuthoringBounds(minX - margin, minY - margin, width, height);
    }

    private static bool TryReadFiniteFloat(
        EditorComponentModel component,
        string fieldName,
        float min,
        float max,
        out float value)
    {
        if (component.SerializedFields.TryGetValue(fieldName, out string? text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            float.IsFinite(value))
        {
            value = Math.Clamp(value, min, max);
            return true;
        }

        value = 0f;
        return false;
    }

    private static bool IsTestScene(string sceneName)
    {
        return sceneName.EndsWith("-probe", StringComparison.OrdinalIgnoreCase) ||
            sceneName.Contains("probe", StringComparison.OrdinalIgnoreCase);
    }

    private static SceneAuthoringMarkerKind ResolveMarkerKind(EditorGameObject gameObject)
    {
        for (int i = 0; i < gameObject.Components.Count; i++)
        {
            string typeName = gameObject.Components[i].TypeName;
            if (typeName.EndsWith(".PlayerSpawnPoint", StringComparison.Ordinal) ||
                string.Equals(typeName, "PlayerSpawnPoint", StringComparison.Ordinal))
            {
                return SceneAuthoringMarkerKind.PlayerSpawn;
            }

            if (typeName.EndsWith(".GoalPoint", StringComparison.Ordinal) ||
                string.Equals(typeName, "GoalPoint", StringComparison.Ordinal))
            {
                return SceneAuthoringMarkerKind.Goal;
            }
        }

        return SceneAuthoringMarkerKind.GameObject;
    }
}
