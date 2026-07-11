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
    SceneAuthoringMarkerKind Kind);

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
    bool HasProceduralWorld,
    bool IsTestScene,
    bool IsExplicitEmptyScene,
    string StatusLabel);

/// <summary>
/// 把声明式 Scene 与 LevelDirector 字段投影为受控 procedural preview。
/// </summary>
internal static class SceneAuthoringPreviewBuilder
{
    private const float DefaultWidth = 320f;
    private const float DefaultHeight = 180f;

    public static SceneAuthoringPreview Build(EditorSceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        List<SceneAuthoringMarker> markers = [];
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        float width = DefaultWidth;
        float height = DefaultHeight;
        bool hasProceduralWorld = false;
        bool hasPlayerSpawnObject = false;
        bool hasGoalObject = false;
        Vector2? legacyPlayerSpawn = null;
        Vector2? legacyGoal = null;

        foreach (EditorGameObject gameObject in scene.EnumerateDepthFirst())
        {
            EditorSceneTransform transform = scene.ComputeWorldTransform(gameObject.StableId);
            Vector2 position = new(transform.X, transform.Y);
            SceneAuthoringMarkerKind markerKind = ResolveMarkerKind(gameObject);
            markers.Add(new SceneAuthoringMarker(
                gameObject.StableId,
                gameObject.Name,
                position,
                markerKind));
            hasPlayerSpawnObject |= markerKind == SceneAuthoringMarkerKind.PlayerSpawn;
            hasGoalObject |= markerKind == SceneAuthoringMarkerKind.Goal;
            minX = MathF.Min(minX, position.X);
            minY = MathF.Min(minY, position.Y);
            maxX = MathF.Max(maxX, position.X);
            maxY = MathF.Max(maxY, position.Y);

            for (int i = 0; i < gameObject.Components.Count; i++)
            {
                EditorComponentModel component = gameObject.Components[i];
                if (!component.TypeName.EndsWith(".LevelDirector", StringComparison.Ordinal) &&
                    !string.Equals(component.TypeName, "LevelDirector", StringComparison.Ordinal))
                {
                    continue;
                }

                width = ReadFiniteFloat(component, "LevelWidth", DefaultWidth, 32f, 65536f);
                height = ReadFiniteFloat(component, "LevelHeight", DefaultHeight, 32f, 65536f);
                float spawnX = ReadFiniteFloat(component, "PlayerSpawnX", width * 0.1f, -width, width * 2f);
                float spawnY = ReadFiniteFloat(component, "PlayerSpawnY", height * 0.7f, -height, height * 2f);
                float goalX = ReadFiniteFloat(component, "GoalX", width * 0.9f, -width, width * 2f);
                float goalY = ReadFiniteFloat(component, "GoalY", height * 0.6f, -height, height * 2f);
                legacyPlayerSpawn = new Vector2(spawnX, spawnY);
                legacyGoal = new Vector2(goalX, goalY);
                hasProceduralWorld = true;
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
                SceneAuthoringMarkerKind.PlayerSpawn));
        }

        if (!hasGoalObject && legacyGoal.HasValue)
        {
            markers.Add(new SceneAuthoringMarker(
                null,
                EditorLocalization.Get("scene.goal", "Goal"),
                legacyGoal.Value,
                SceneAuthoringMarkerKind.Goal));
        }

        bool explicitEmpty = scene.Count == 0;
        bool testScene = IsTestScene(scene.Name);
        SceneAuthoringBounds bounds = hasProceduralWorld
            ? new SceneAuthoringBounds(0f, 0f, width, height)
            : BuildObjectBounds(minX, minY, maxX, maxY);
        string status = explicitEmpty
            ? testScene ? "测试场景 · 显式空场景" : "显式空场景"
            : testScene ? "测试场景 · Authoring Preview" : "Authoring Preview";
        return new SceneAuthoringPreview(
            scene.Name,
            bounds,
            [.. markers],
            hasProceduralWorld,
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

    private static float ReadFiniteFloat(
        EditorComponentModel component,
        string fieldName,
        float fallback,
        float min,
        float max)
    {
        return component.SerializedFields.TryGetValue(fieldName, out string? text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            float.IsFinite(value)
                ? Math.Clamp(value, min, max)
                : fallback;
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
