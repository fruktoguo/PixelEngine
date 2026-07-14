using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor Shell automation 组合根、phase wiring 与统一 Undo history 回归。
/// </summary>
public sealed class EditorAutomationRuntimeTests
{
    /// <summary>验证 Shell runtime 发布真实实例，并在 EditorIngress 返回无项目结构化错误。</summary>
    [Fact]
#pragma warning disable xUnit1031 // Scheduler contract pins the runtime to this exact OS thread.
    public void ShellRuntimeIsDiscoverableAndDrainsRequestsOnOwnerThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "PixelEngine", "AutomationShellTests", Guid.NewGuid().ToString("N"));
        string discoveryRoot = Path.Combine(root, "discovery");
        string artifactRoot = Path.Combine(root, "artifacts");
        try
        {
            EditorShellOptions options = EditorShellOptions.Parse(
            [
                "--automation-discovery-root",
                discoveryRoot,
                "--automation-artifact-root",
                artifactRoot,
                "--ephemeral-user-state",
            ]);
            EditorUserDataPaths paths = EditorUserDataPaths.Resolve(
                options,
                environmentUserDataDirectory: null,
                ephemeralDirectory: Path.Combine(root, "user-data"));
            EditorShellApp app = EditorShellApp.CreateForTests();
            using EditorAutomationRuntime runtime = EditorAutomationRuntime.Start(app, options, paths)
                ?? throw new InvalidOperationException("Windows Shell automation runtime 未启动。");
            AutomationDiscoverySnapshot discovery = AutomationDiscovery.DiscoverAsync(discoveryRoot)
                .AsTask().GetAwaiter().GetResult();
            AutomationDiscoveredInstance instance = Assert.Single(discovery.Instances);
            Assert.Empty(discovery.Diagnostics);
            Assert.Equal(runtime.InstanceId, instance.Descriptor.InstanceId);
            Assert.Null(instance.Descriptor.Project);
            Assert.True(Directory.Exists(Path.Combine(artifactRoot, runtime.InstanceId)));

            EditorAutomationClient client = EditorAutomationClient.ConnectAsync(
                instance,
                new AutomationClientOptions
                {
                    ClientInstanceId = "shell-runtime-test",
                    ClientName = "hosting-tests",
                    ClientVersion = "1.0",
                    RequestedScopes = [AutomationScopes.EditorRead, AutomationScopes.EditorControl],
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    RequestTimeout = TimeSpan.FromSeconds(5),
                }).AsTask().GetAwaiter().GetResult();
            try
            {
                AutomationPingResponse ping = client.PingAsync().AsTask().GetAwaiter().GetResult();
                Assert.Equal(runtime.InstanceId, ping.InstanceId);
                AutomationTransactionBeginRequest request = new()
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Name = "No Project Transaction",
                    LeaseMilliseconds = 1000,
                };
                Task<AutomationInvocationResult> pending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.TransactionBeginMethod,
                    JsonSerializer.SerializeToElement(
                        request,
                        AutomationJsonContext.Default.AutomationTransactionBeginRequest),
                    new AutomationInvocationOptions { IdempotencyKey = "no-project-transaction" })
                    .AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();

                AutomationRemoteException exception = Assert.Throws<AutomationRemoteException>(
                    () => pending.GetAwaiter().GetResult());
                Assert.Equal(AutomationErrorCodes.StateUnavailable, exception.Error.Code);
            }
            finally
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
#pragma warning restore xUnit1031

    /// <summary>验证 phase driver 为 12 个固定 Engine phase 各登记一个零轮询 drain hook。</summary>
    [Fact]
    public void PhaseDriverRegistersEveryEngineSafePhaseExactlyOnce()
    {
        using AutomationMainThreadScheduler scheduler = new(
            [],
            new AutomationRevisionStore(),
            new NoopUndoSink(),
            new NoopTransactionParticipant());
        EnginePhasePipeline phases = new(new EngineCommandQueue());
        new EditorAutomationPhaseDriver(scheduler).RegisterPhases(phases);

        for (int raw = 0; raw <= (int)EnginePhase.WorldStreaming; raw++)
        {
            Assert.Equal(1, phases.Count((EnginePhase)raw));
        }
    }

    /// <summary>验证手动 Execute/Undo/Redo 发通知，而 scheduler 的 RecordExecuted 不被重复计数。</summary>
    [Fact]
    public void UnifiedUndoHistoryDistinguishesManualApplyFromAutomationRecord()
    {
        EditorSceneModel scene = EditorSceneModel.Empty();
        EditorUndoStack undo = new();
        List<EditorUndoMutationKind> mutations = [];
        undo.HistoryApplied = (_, mutation) => mutations.Add(mutation);
        TestEditorCommand manual = new();

        undo.Execute(scene, manual);
        Assert.True(undo.Undo(scene));
        Assert.True(undo.Redo(scene));
        undo.RecordExecuted(new TestEditorCommand());

        Assert.Equal(
            [EditorUndoMutationKind.Execute, EditorUndoMutationKind.Undo, EditorUndoMutationKind.Redo],
            mutations);
    }

    private sealed class TestEditorCommand : IEditorCommand
    {
        public string Name => "Test";

        public void Execute(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
        }

        public void Undo(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
        }
    }

    private sealed class NoopUndoSink : IAutomationUndoSink
    {
        public void RecordExecuted(IAutomationUndoAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
        }
    }

    private sealed class NoopTransactionParticipant : IAutomationTransactionParticipant
    {
        public object CaptureState()
        {
            return new object();
        }

        public void RestoreState(object state)
        {
            ArgumentNullException.ThrowIfNull(state);
        }
    }
}
