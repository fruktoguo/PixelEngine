using System.Runtime.CompilerServices;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>执行 build tool preflight。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>preflight 结果及其权威 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationBuildPreflightResult>> PreflightBuildAsync(
        CancellationToken cancellationToken = default)
    {
        return InvokeDetailedAsync(
            AutomationProtocolConstants.BuildPreflightMethod,
            AutomationJsonContext.Default.AutomationBuildPreflightResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>启动真实 build-player job。</summary>
    /// <param name="request">是否在成功后启动 player 的逐次命令参数。</param>
    /// <param name="options">expected revision、幂等 key 与 timeout。</param>
    /// <param name="cancellationToken">取消请求等待；不伪装成已取消运行中的 build。</param>
    /// <returns>已接受 build job 的稳定初始快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationBuildSnapshot>> StartBuildAsync(
        AutomationBuildStartRequest request,
        AutomationInvocationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        return InvokeDetailedAsync(
            AutomationProtocolConstants.BuildStartMethod,
            request,
            AutomationJsonContext.Default.AutomationBuildStartRequest,
            AutomationJsonContext.Default.AutomationBuildSnapshot,
            options,
            cancellationToken);
    }

    /// <summary>按稳定 filter/sort/cursor 展开 build jobs。</summary>
    /// <param name="request">可选 filter、sort、page size 与起始 cursor。</param>
    /// <param name="options">每页请求的 timeout 选项。</param>
    /// <param name="cancellationToken">停止分页枚举。</param>
    /// <returns>按服务端确定顺序展开的 build 快照流。</returns>
    public async IAsyncEnumerable<AutomationBuildSnapshot> EnumerateBuildsAsync(
        AutomationPageRequest? request = null,
        AutomationInvocationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (AutomationBuildSnapshot item in EnumeratePagesAsync(
            AutomationProtocolConstants.BuildListMethod,
            request ?? new AutomationPageRequest(),
            AutomationJsonContext.Default.AutomationBuildListResponse,
            static response => response.Items,
            static response => response.Page,
            options,
            cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>读取稳定 build job。</summary>
    /// <param name="buildId">实例作用域 build ID。</param>
    /// <param name="options">请求 timeout。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前 build 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationBuildSnapshot>> GetBuildAsync(
        string buildId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return InvokeBuildRequestAsync(
            AutomationProtocolConstants.BuildGetMethod,
            buildId,
            options,
            cancellationToken);
    }

    /// <summary>异步等待 build 进入真实终态。</summary>
    /// <param name="buildId">实例作用域 build ID。</param>
    /// <param name="options">等待 timeout。</param>
    /// <param name="cancellationToken">只取消当前等待，不隐式取消 build。</param>
    /// <returns>终态 build 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationBuildSnapshot>> WaitForBuildAsync(
        string buildId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return InvokeBuildRequestAsync(
            AutomationProtocolConstants.BuildWaitMethod,
            buildId,
            options,
            cancellationToken);
    }

    /// <summary>向仍在运行的 build-player 进程树发出取消请求。</summary>
    /// <param name="buildId">实例作用域 build ID。</param>
    /// <param name="options">expected revision、幂等 key 与 timeout。</param>
    /// <param name="cancellationToken">取消请求等待。</param>
    /// <returns>已记录 cancellationRequested 的 build 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationBuildSnapshot>> CancelBuildAsync(
        string buildId,
        AutomationInvocationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InvokeBuildRequestAsync(
            AutomationProtocolConstants.BuildCancelMethod,
            buildId,
            options,
            cancellationToken);
    }

    /// <summary>将指定 build 的有界结构化日志导出为带 SHA256 的 artifact。</summary>
    /// <param name="buildId">实例作用域 build ID。</param>
    /// <param name="options">请求 timeout。</param>
    /// <param name="cancellationToken">取消 artifact 生成。</param>
    /// <returns>build log artifact reference 及来源 revision。</returns>
    public async ValueTask<AutomationTypedInvocationResult<AutomationArtifactReference>> ExportBuildLogAsync(
        string buildId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        AutomationBuildRequest request = CreateBuildRequest(buildId);
        return await InvokeDetailedAsync(
            AutomationProtocolConstants.BuildLogExportMethod,
            request,
            AutomationJsonContext.Default.AutomationBuildRequest,
            AutomationJsonContext.Default.AutomationArtifactReference,
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从成功 build 的受信 launcher 启动一个 player process。</summary>
    /// <param name="buildId">包含受信 launcher 的成功 build ID。</param>
    /// <param name="options">expected revision、幂等 key 与 timeout。</param>
    /// <param name="cancellationToken">取消请求等待；进程启动后不谎报取消成功。</param>
    /// <returns>稳定 player process 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot>> LaunchPlayerAsync(
        string buildId,
        AutomationInvocationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        AutomationPlayerLaunchRequest request = new()
        {
            BuildId = ValidateStableId(buildId, nameof(buildId)),
        };
        return InvokeDetailedAsync(
            AutomationProtocolConstants.PlayerLaunchMethod,
            request,
            AutomationJsonContext.Default.AutomationPlayerLaunchRequest,
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
            options,
            cancellationToken);
    }

    /// <summary>按稳定 filter/sort/cursor 展开由 Editor 启动的 player processes。</summary>
    /// <param name="request">可选 filter、sort、page size 与起始 cursor。</param>
    /// <param name="options">每页请求的 timeout 选项。</param>
    /// <param name="cancellationToken">停止分页枚举。</param>
    /// <returns>按服务端确定顺序展开的 player process 快照流。</returns>
    public async IAsyncEnumerable<AutomationPlayerProcessSnapshot> EnumeratePlayersAsync(
        AutomationPageRequest? request = null,
        AutomationInvocationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (AutomationPlayerProcessSnapshot item in EnumeratePagesAsync(
            AutomationProtocolConstants.PlayerListMethod,
            request ?? new AutomationPageRequest(),
            AutomationJsonContext.Default.AutomationPlayerProcessListResponse,
            static response => response.Items,
            static response => response.Page,
            options,
            cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>读取稳定 player process。</summary>
    /// <param name="playerProcessId">实例作用域 player process ID。</param>
    /// <param name="options">请求 timeout。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前 process 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot>> GetPlayerAsync(
        string playerProcessId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return InvokePlayerRequestAsync(
            AutomationProtocolConstants.PlayerGetMethod,
            playerProcessId,
            options,
            cancellationToken);
    }

    /// <summary>异步等待 player process 真实退出。</summary>
    /// <param name="playerProcessId">实例作用域 player process ID。</param>
    /// <param name="options">等待 timeout。</param>
    /// <param name="cancellationToken">只取消当前等待，不终止 player。</param>
    /// <returns>退出后的 process 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot>> WaitForPlayerAsync(
        string playerProcessId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return InvokePlayerRequestAsync(
            AutomationProtocolConstants.PlayerWaitMethod,
            playerProcessId,
            options,
            cancellationToken);
    }

    /// <summary>向 player 发出终止信号；用 wait 确认最终退出。</summary>
    /// <param name="playerProcessId">实例作用域 player process ID。</param>
    /// <param name="entireProcessTree">是否终止完整子进程树。</param>
    /// <param name="options">expected revision、幂等 key 与 timeout。</param>
    /// <param name="cancellationToken">取消请求等待。</param>
    /// <returns>带 terminationRequested/exit 状态的 process 快照及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot>> TerminatePlayerAsync(
        string playerProcessId,
        bool entireProcessTree,
        AutomationInvocationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        AutomationPlayerTerminateRequest request = new()
        {
            PlayerProcessId = ValidateStableId(playerProcessId, nameof(playerProcessId)),
            EntireProcessTree = entireProcessTree,
        };
        return InvokeDetailedAsync(
            AutomationProtocolConstants.PlayerTerminateMethod,
            request,
            AutomationJsonContext.Default.AutomationPlayerTerminateRequest,
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
            options,
            cancellationToken);
    }

    /// <summary>让 Server 重新校验 artifact 的长度与 SHA256。</summary>
    /// <param name="artifactId">当前 session 的 artifact ID。</param>
    /// <param name="options">请求 timeout。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Server 权威索引的重验结果及 revision。</returns>
    public ValueTask<AutomationTypedInvocationResult<AutomationArtifactVerifyResult>> VerifyArtifactRemoteAsync(
        string artifactId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        AutomationArtifactRequest request = new()
        {
            ArtifactId = ValidateStableId(artifactId, nameof(artifactId)),
        };
        return InvokeDetailedAsync(
            AutomationProtocolConstants.ArtifactVerifyMethod,
            request,
            AutomationJsonContext.Default.AutomationArtifactRequest,
            AutomationJsonContext.Default.AutomationArtifactVerifyResult,
            options,
            cancellationToken);
    }

    private ValueTask<AutomationTypedInvocationResult<AutomationBuildSnapshot>> InvokeBuildRequestAsync(
        string method,
        string buildId,
        AutomationInvocationOptions? options,
        CancellationToken cancellationToken)
    {
        return InvokeDetailedAsync(
            method,
            CreateBuildRequest(buildId),
            AutomationJsonContext.Default.AutomationBuildRequest,
            AutomationJsonContext.Default.AutomationBuildSnapshot,
            options,
            cancellationToken);
    }

    private ValueTask<AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot>> InvokePlayerRequestAsync(
        string method,
        string playerProcessId,
        AutomationInvocationOptions? options,
        CancellationToken cancellationToken)
    {
        AutomationPlayerProcessRequest request = new()
        {
            PlayerProcessId = ValidateStableId(playerProcessId, nameof(playerProcessId)),
        };
        return InvokeDetailedAsync(
            method,
            request,
            AutomationJsonContext.Default.AutomationPlayerProcessRequest,
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
            options,
            cancellationToken);
    }

    private static AutomationBuildRequest CreateBuildRequest(string buildId)
    {
        return new AutomationBuildRequest
        {
            BuildId = ValidateStableId(buildId, nameof(buildId)),
        };
    }

    private static string ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Length == 32 && value.All(static character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')
            ? value
            : throw new ArgumentException(
                "Stable ID 必须是 32 位小写十六进制。",
                parameterName);
    }
}
