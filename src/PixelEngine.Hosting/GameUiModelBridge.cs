using RuntimeUi = PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 游戏 UI 模型推送入口，由 Hosting 相位驱动在 UI Update 前调用。
/// </summary>
public interface IGameUiModelPusher
{
    /// <summary>
    /// 把已绑定脚本模型的当前值推送到 UI 后端。
    /// </summary>
    void PushGameUiModels();
}

/// <summary>
/// 将脚本侧 <see cref="ScriptUi.IUiModel"/> 按当前 UI 文档声明的模型路径推送到运行时 UI 后端。
/// </summary>
public sealed class GameUiModelBridge : IGameUiModelPusher
{
    private readonly RuntimeUi.GameUiHost _host;
    private readonly ModelBinding[] _bindings;
    private readonly RuntimeUi.UiPathId[] _paths;
    private int _bindingCount;

    /// <summary>
    /// 创建 Game UI 模型桥。
    /// </summary>
    /// <param name="host">运行时 UI 宿主。</param>
    /// <param name="maxBindings">最大模型绑定数量。</param>
    /// <param name="maxPathsPerScreen">单屏每帧最多推送的模型路径数量。</param>
    public GameUiModelBridge(RuntimeUi.GameUiHost host, int maxBindings = 64, int maxPathsPerScreen = 256)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBindings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPathsPerScreen);
        _bindings = new ModelBinding[maxBindings];
        _paths = new RuntimeUi.UiPathId[maxPathsPerScreen];
    }

    /// <summary>
    /// 绑定脚本模型到指定 UI 屏幕。
    /// </summary>
    /// <param name="screen">目标可见屏幕。</param>
    /// <param name="modelName">模型名。</param>
    /// <param name="model">模型读取接口。</param>
    public void BindModel(ScriptUi.UiScreenHandle screen, ScriptUi.UiModelName modelName, ScriptUi.IUiModel model)
    {
        Validate(screen);
        Validate(modelName);
        ArgumentNullException.ThrowIfNull(model);
        PruneHiddenBindings();

        for (int i = 0; i < _bindingCount; i++)
        {
            if (_bindings[i].Screen.Value == screen.Value && _bindings[i].ModelName.Value == modelName.Value)
            {
                _bindings[i] = new ModelBinding(new RuntimeUi.UiScreenHandle(screen.Value), modelName, model);
                return;
            }
        }

        if (_bindingCount == _bindings.Length)
        {
            throw new InvalidOperationException("Game UI 模型绑定容量已满。");
        }

        _bindings[_bindingCount++] = new ModelBinding(new RuntimeUi.UiScreenHandle(screen.Value), modelName, model);
    }

    /// <summary>
    /// 把所有已绑定脚本模型中、当前 UI 文档实际声明的路径值推送到 UI 后端。
    /// </summary>
    public void PushGameUiModels()
    {
        for (int i = 0; i < _bindingCount; i++)
        {
            ModelBinding binding = _bindings[i];
            if (!_host.TryGetDocument(binding.Screen, out _))
            {
                RemoveBindingAt(i);
                i--;
                continue;
            }

            int pathCount = _host.CopyModelPaths(binding.Screen, _paths);
            for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
            {
                RuntimeUi.UiPathId runtimePath = _paths[pathIndex];
                if (!binding.Model.TryGetValue(new ScriptUi.UiPathId(runtimePath.Value), out ScriptUi.UiValue scriptValue))
                {
                    continue;
                }

                RuntimeUi.UiValue runtimeValue = ToRuntimeValue(in scriptValue);
                _host.SetModelValue(binding.Screen, runtimePath, in runtimeValue);
            }
        }
    }

    private void PruneHiddenBindings()
    {
        for (int i = 0; i < _bindingCount; i++)
        {
            if (_host.TryGetDocument(_bindings[i].Screen, out _))
            {
                continue;
            }

            RemoveBindingAt(i);
            i--;
        }
    }

    private void RemoveBindingAt(int index)
    {
        int moveCount = _bindingCount - index - 1;
        if (moveCount > 0)
        {
            _bindings.AsSpan(index + 1, moveCount).CopyTo(_bindings.AsSpan(index, moveCount));
        }

        _bindings[--_bindingCount] = default;
    }

    private static RuntimeUi.UiValue ToRuntimeValue(in ScriptUi.UiValue value)
    {
        return value.Kind switch
        {
            ScriptUi.UiValueKind.Empty => default,
            ScriptUi.UiValueKind.Boolean => RuntimeUi.UiValue.FromBoolean(value.AsBoolean()),
            ScriptUi.UiValueKind.Int64 => new RuntimeUi.UiValue(value.AsInt64()),
            ScriptUi.UiValueKind.Double => new RuntimeUi.UiValue(value.AsDouble()),
            ScriptUi.UiValueKind.StringHandle => RuntimeUi.UiValue.FromStringHandle(new RuntimeUi.UiStringHandle(value.AsStringHandle().Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "未知脚本 UI 值类型。"),
        };
    }

    private static void Validate(ScriptUi.UiScreenHandle screen)
    {
        if (screen.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screen), "UI 屏幕句柄必须为正数。");
        }
    }

    private static void Validate(ScriptUi.UiModelName modelName)
    {
        if (modelName.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modelName), "UI 模型名必须为正数。");
        }
    }

    private readonly record struct ModelBinding(
        RuntimeUi.UiScreenHandle Screen,
        ScriptUi.UiModelName ModelName,
        ScriptUi.IUiModel Model);
}
