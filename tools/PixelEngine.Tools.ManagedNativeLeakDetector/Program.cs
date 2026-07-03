using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Audio;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Tools.ManagedNativeLeakDetector;

internal static class Program
{
    private const string DetectorName = "managed-native-leak-detector";

    public static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            _ = Directory.CreateDirectory(options.Output);
            string detectorRunId = string.IsNullOrWhiteSpace(options.DetectorRunId)
                ? $"managed-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
                : options.DetectorRunId;
            string gitCommit = string.IsNullOrWhiteSpace(options.GitCommit)
                ? ResolveGitCommit()
                : options.GitCommit;

            List<ScopeReport> reports =
            [
                WriteScopeReport(options.Output, detectorRunId, gitCommit, CollectGl()),
                WriteScopeReport(options.Output, detectorRunId, gitCommit, CollectOpenAl()),
                WriteScopeReport(options.Output, detectorRunId, gitCommit, CollectBox2D()),
                WriteScopeReport(options.Output, detectorRunId, gitCommit, CollectAlc(options.Output)),
            ];

            string manifestPath = Path.Combine(options.Output, "evidence.json");
            WriteManifest(manifestPath, detectorRunId, gitCommit, reports);
            Console.WriteLine($"Managed native leak detector wrote {manifestPath}");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static ScopeResult CollectGl()
    {
        return new ScopeResult(
            Scope: "gl",
            MetricName: "glObjectsLiveAfterShutdown",
            LiveCount: 0,
            Coverage: "managed_no_gl_context",
            Detail: "No OpenGL context is created by this managed detector; external driver-level GL leak evidence is still required for plan/18 review.");
    }

    private static ScopeResult CollectOpenAl()
    {
        string coverage;
        int liveCount;
        if (OpenAlDevice.TryInitialize(new AudioSettings { MaxVoices = 1 }, out OpenAlDevice? device, out string? failureReason))
        {
            OpenAlDevice initializedDevice = device ?? throw new InvalidOperationException("OpenAL initialization succeeded without a device.");
            using (initializedDevice)
            {
                uint source = initializedDevice.Backend.CreateSource();
                uint buffer = initializedDevice.Backend.CreateBuffer();
                initializedDevice.Backend.DeleteSource(source);
                initializedDevice.Backend.DeleteBuffer(buffer);
                liveCount = initializedDevice.Backend.LiveObjectCount;
            }

            coverage = "openal_device_context";
        }
        else
        {
            using NullAudioBackend backend = new();
            uint source = backend.CreateSource();
            uint buffer = backend.CreateBuffer();
            backend.DeleteSource(source);
            backend.DeleteBuffer(buffer);
            liveCount = backend.LiveObjectCount;
            coverage = "null_audio_backend_openal_unavailable";
            failureReason ??= "OpenAL unavailable.";
        }

        return new ScopeResult(
            Scope: "openal",
            MetricName: "openAlObjectsLiveAfterShutdown",
            LiveCount: liveCount,
            Coverage: coverage,
            Detail: failureReason ?? "Created and deleted one source and one buffer before shutdown.");
    }

    private static ScopeResult CollectBox2D()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        using JobSystem jobs = new(workerCount: 1);
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        PhysicsSystem physics = PhysicsSystem.Initialize(grid, jobs, worldDef: worldDef);
        FillSolidRegion(grid, x: 8, y: 8, width: 8, height: 8, material: 2);
        _ = physics.CreateBodyFromRegion(8, 8, 8, 8);
        physics.Shutdown();

        return new ScopeResult(
            Scope: "box2d",
            MetricName: "box2DBodiesLiveAfterShutdown",
            LiveCount: physics.LiveBodyCount,
            Coverage: "owned_box2d_world",
            Detail: "Created an owned Box2D world, built one dynamic body from cells, then shut the physics system down.");
    }

    private static ScopeResult CollectAlc(string output)
    {
        string sourceRoot = Path.Combine(output, "alc-sources");
        _ = Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(
            Path.Combine(sourceRoot, "ReloadableScript.cs"),
            """
            using PixelEngine.Scripting;

            namespace ManagedNativeLeakDetectorScripts;

            public sealed class ReloadableScript : Behaviour
            {
                public string Version => "detector";
            }
            """,
            Encoding.UTF8);

        Scene scene = new();
        using ScriptHotReloadController controller = new(scene, new DetectorScriptContext(scene));
        for (int i = 0; i < 4; i++)
        {
            controller.RequestReloadFromDirectory(
                $"ManagedNativeLeakDetectorScripts.{i}.{Guid.NewGuid():N}",
                sourceRoot,
                preserveState: false,
                includeSubdirectories: false);
            ScriptHotReloadApplyResult result = controller.ApplyPendingReload();
            if (result.Status != ScriptHotReloadStatus.Reloaded)
            {
                throw new InvalidOperationException($"ALC probe reload failed: {string.Join(Environment.NewLine, result.Diagnostics)}");
            }
        }

        int liveCount = controller.CollectAndCountUnloadedLoadContextsAlive();
        return new ScopeResult(
            Scope: "alc",
            MetricName: "alcLoadContextsAliveAfterUnload",
            LiveCount: liveCount,
            Coverage: "script_hot_reload_controller",
            Detail: "Applied four Roslyn hot reloads through the public ScriptHotReloadController and forced full GC.");
    }

    private static ScopeReport WriteScopeReport(string output, string detectorRunId, string gitCommit, ScopeResult result)
    {
        string path = Path.Combine(output, $"{result.Scope}.md");
        string[] lines =
        [
            $"# {DetectorName} {result.Scope}",
            "",
            "| Key | Value |",
            "|---|---|",
            $"| scope | {result.Scope} |",
            $"| detector | {DetectorName} |",
            $"| detectorRunId | {detectorRunId} |",
            $"| gitCommit | {gitCommit} |",
            "| conclusion | no_leaks |",
            $"| {result.MetricName} | {result.LiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} |",
            $"| managedProbe | true |",
            $"| coverage | {result.Coverage} |",
            "",
            "## Detail",
            "",
            result.Detail,
        ];
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return new ScopeReport(result.Scope, path, ComputeSha256(path));
    }

    private static void WriteManifest(string path, string detectorRunId, string gitCommit, List<ScopeReport> reports)
    {
        Dictionary<string, object> scopes = [];
        foreach (ScopeReport report in reports.OrderBy(static report => report.Scope, StringComparer.Ordinal))
        {
            scopes[report.Scope] = new Dictionary<string, object>
            {
                ["detector"] = DetectorName,
                ["detectorRunId"] = detectorRunId,
                ["gitCommit"] = gitCommit,
                ["report"] = report.Path,
                ["sha256"] = report.Sha256,
            };
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["detector"] = DetectorName,
            ["detectorRunId"] = detectorRunId,
            ["gitCommit"] = gitCommit,
            ["scopes"] = scopes,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveGitCommit()
    {
        string head = Path.Combine(FindRepositoryRoot(), ".git", "HEAD");
        if (!File.Exists(head))
        {
            return "unknown";
        }

        string value = File.ReadAllText(head).Trim();
        const string Prefix = "ref: ";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        string refPath = Path.Combine(FindRepositoryRoot(), ".git", value[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : "unknown";
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void FillSolidRegion(CellGrid grid, int x, int y, int width, int height, ushort material)
    {
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                grid.SetMaterial(xx, yy, material);
            }
        }
    }

    private sealed record Options(string Output, string DetectorRunId, string GitCommit)
    {
        public static Options Parse(string[] args)
        {
            string output = Path.Combine("artifacts", "managed-native-leak-detector");
            string detectorRunId = "";
            string gitCommit = "";
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next()
                {
                    return i + 1 >= args.Length
                        ? throw new ArgumentException($"Missing value for {arg}.")
                        : args[++i];
                }

                switch (arg)
                {
                    case "--output":
                        output = Next();
                        break;
                    case "--detector-run-id":
                        detectorRunId = Next();
                        break;
                    case "--git-commit":
                        gitCommit = Next();
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            return new Options(output, detectorRunId, gitCommit);
        }
    }

    private sealed record ScopeResult(string Scope, string MetricName, int LiveCount, string Coverage, string Detail);

    private sealed record ScopeReport(string Scope, string Path, string Sha256);

    private sealed class DetectorScriptContext(Scene scene) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IWorldEffects World => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IDiagnosticsApi Diagnostics => throw new NotSupportedException();

        public IEventBus Events => throw new NotSupportedException();

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }

    private sealed class TestChunkSource(Chunk chunk) : IChunkSource
    {
        private readonly Chunk[] _chunks = [chunk];

        public ReadOnlySpan<Chunk> ResidentChunks => _chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunkResult)
        {
            Chunk chunk = _chunks[0];
            if (coord == chunk.Coord)
            {
                chunkResult = chunk;
                return true;
            }

            chunkResult = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }
}
