using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// Automation 大型数据制品的原子发布、完整性、配额与生命周期测试。
/// </summary>
public sealed class AutomationArtifactStoreTests
{
    /// <summary>验证引用元数据、文件内容、SHA256、deep snapshot 与篡改检测。</summary>
    [Fact]
    public async Task WritePublishesVerifiableArtifactAndReturnsIndependentSnapshots()
    {
        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = CreateStore(temporary.Path);
        byte[] content = "pixelengine-artifact"u8.ToArray();
        AutomationRevisionSnapshot sourceRevision = CreateRevision(17, ("scene:main", 9));
        JsonElement metadata = JsonSerializer.SerializeToElement(new { colorSpace = "srgb" });

        AutomationArtifactReference artifact = await store.WriteAsync(
            "session_1",
            ".png",
            "image/png",
            sourceRevision,
            (stream, cancellationToken) => stream.WriteAsync(content, cancellationToken),
            new AutomationArtifactMetadata
            {
                Width = 320,
                Height = 180,
                Encoding = "png",
                Data = metadata,
            });

        Assert.True(Path.IsPathFullyQualified(artifact.Path));
        Assert.Equal($"{artifact.ArtifactId}.png", artifact.RelativePath);
        Assert.Equal(content.Length, artifact.ByteLength);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), artifact.Sha256);
        Assert.Equal(content, await File.ReadAllBytesAsync(artifact.Path));
        Assert.Equal(320, artifact.Width);
        Assert.Equal("srgb", artifact.Metadata?.GetProperty("colorSpace").GetString());
        Assert.True(await store.VerifyAsync("session_1", artifact.ArtifactId));

        artifact.SourceRevision.Resources[0] = new AutomationResourceRevision
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ResourceId = "mutated-return-value",
            Revision = 998,
        };
        AutomationArtifactReference[] firstList = await store.ListAsync("session_1");
        Assert.Equal("scene:main", firstList[0].SourceRevision.Resources[0].ResourceId);
        firstList[0].SourceRevision.Resources[0] = new AutomationResourceRevision
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ResourceId = "mutated",
            Revision = 999,
        };
        AutomationArtifactReference[] secondList = await store.ListAsync("session_1");
        Assert.Equal("scene:main", secondList[0].SourceRevision.Resources[0].ResourceId);

        await File.AppendAllTextAsync(artifact.Path, "tampered");
        Assert.False(await store.VerifyAsync("session_1", artifact.ArtifactId));
        Assert.True(await store.DeleteAsync("session_1", artifact.ArtifactId));
        Assert.False(File.Exists(artifact.Path));
        Assert.Empty(await store.ListAsync("session_1"));
    }

    /// <summary>验证单文件、session 总量与数量配额失败均不发布半成品。</summary>
    [Fact]
    public async Task QuotaFailuresLeaveNoPartialFilesAndReleasedQuotaCanBeReused()
    {
        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = new(new AutomationArtifactStoreOptions
        {
            RootPath = temporary.Path,
            MaxArtifactBytes = 4,
            MaxSessionBytes = 8,
            MaxArtifactsPerSession = 2,
        });
        AutomationRevisionSnapshot revision = CreateRevision(0);

        AutomationRequestException oversized = await Assert.ThrowsAsync<AutomationRequestException>(
            async () => await WriteBytesAsync(store, "quota", [1, 2, 3, 4, 5], revision));
        Assert.Equal(AutomationErrorCodes.ArtifactQuotaExceeded, oversized.Error.Code);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(temporary.Path, "quota")));

        AutomationArtifactReference first = await WriteBytesAsync(store, "quota", [1, 2, 3, 4], revision);
        _ = await WriteBytesAsync(store, "quota", [5, 6, 7, 8], revision);
        AutomationRequestException countExceeded = await Assert.ThrowsAsync<AutomationRequestException>(
            async () => await WriteBytesAsync(store, "quota", [9], revision));
        Assert.Equal(AutomationErrorCodes.ArtifactQuotaExceeded, countExceeded.Error.Code);
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(temporary.Path, "quota")).Count());

        Assert.True(await store.DeleteAsync("quota", first.ArtifactId));
        _ = await WriteBytesAsync(store, "quota", [9, 10, 11, 12], revision);
        Assert.Equal(2, (await store.ListAsync("quota")).Length);
    }

    /// <summary>验证多个认证 session 共享实例级字节配额，删除后额度才会重新可用。</summary>
    [Fact]
    public async Task GlobalQuotaIsSharedAcrossSessionsAndReleasedByDelete()
    {
        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = new(new AutomationArtifactStoreOptions
        {
            RootPath = temporary.Path,
            MaxArtifactBytes = 4,
            MaxSessionBytes = 4,
            MaxArtifactsPerSession = 2,
            MaxTotalBytes = 4,
            MaxArtifacts = 4,
        });
        AutomationRevisionSnapshot revision = CreateRevision(0);
        AutomationArtifactReference first = await WriteBytesAsync(store, "first", [1, 2, 3], revision);

        AutomationRequestException exhausted = await Assert.ThrowsAsync<AutomationRequestException>(
            async () => await WriteBytesAsync(store, "second", [4, 5], revision));
        Assert.Equal(AutomationErrorCodes.ArtifactQuotaExceeded, exhausted.Error.Code);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(temporary.Path, "second")));

        Assert.True(await store.DeleteAsync("first", first.ArtifactId));
        AutomationArtifactReference recovered = await WriteBytesAsync(store, "second", [4, 5], revision);
        Assert.True(await store.VerifyAsync("second", recovered.ArtifactId));
    }

    /// <summary>验证 writer 取消后临时文件被清除，session 后续仍可正常写入。</summary>
    [Fact]
    public async Task CancelledWriterRemovesTemporaryFileAndReleasesSessionGate()
    {
        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = CreateStore(temporary.Path);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        ValueTask<AutomationArtifactReference> pending = store.WriteAsync(
            "cancel",
            "bin",
            "application/octet-stream",
            CreateRevision(0),
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(new byte[] { 1, 2 }, cancellationToken);
                _ = started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(temporary.Path, "cancel")));

        AutomationArtifactReference recovered = await WriteBytesAsync(
            store,
            "cancel",
            [3, 4],
            CreateRevision(0));
        Assert.True(await store.VerifyAsync("cancel", recovered.ArtifactId));
    }

    /// <summary>验证 session 删除与写入互斥，并发删除等待同一真实删除结果。</summary>
    [Fact]
    public async Task ConcurrentSessionDeletionRejectsNewWritesAndWaitsForActiveWriter()
    {
        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = CreateStore(temporary.Path);
        TaskCompletionSource writerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWriter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AutomationArtifactReference> activeWrite = store.WriteAsync(
            "deleting",
            "bin",
            "application/octet-stream",
            CreateRevision(0),
            async (stream, cancellationToken) =>
            {
                _ = writerStarted.TrySetResult();
                await releaseWriter.Task.WaitAsync(cancellationToken);
                await stream.WriteAsync(new byte[] { 1 }, cancellationToken);
            }).AsTask();
        await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task firstDeletion = store.DeleteSessionAsync("deleting").AsTask();
        Task secondDeletion = store.DeleteSessionAsync("deleting").AsTask();
        AutomationRequestException rejected = await Assert.ThrowsAsync<AutomationRequestException>(
            async () => await WriteBytesAsync(store, "deleting", [2], CreateRevision(0)));
        Assert.Equal(AutomationErrorCodes.Busy, rejected.Error.Code);

        _ = releaseWriter.TrySetResult();
        _ = await activeWrite;
        await Task.WhenAll(firstDeletion, secondDeletion).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "deleting")));
        Assert.Empty(await store.ListAsync("deleting"));
    }

    /// <summary>验证批量删除中途遇到锁定文件时，已删除项会同步移出索引，剩余项可重试。</summary>
    [Fact]
    public async Task PartialSessionDeletionFailureLeavesDiskAndIndexConsistentForRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using AutomationArtifactStore store = CreateStore(temporary.Path);
        AutomationArtifactReference first = await WriteBytesAsync(
            store,
            "retry_delete",
            [1],
            CreateRevision(0));
        AutomationArtifactReference second = await WriteBytesAsync(
            store,
            "retry_delete",
            [2],
            CreateRevision(0));

        await using (FileStream locked = new(
                         second.Path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None,
                         bufferSize: 1,
                         useAsync: true))
        {
            _ = await Assert.ThrowsAsync<IOException>(
                async () => await store.DeleteSessionAsync("retry_delete"));
            AutomationArtifactReference remaining = Assert.Single(
                await store.ListAsync("retry_delete"));
            Assert.Equal(second.ArtifactId, remaining.ArtifactId);
            Assert.False(File.Exists(first.Path));
            Assert.True(File.Exists(second.Path));
        }

        await store.DeleteSessionAsync("retry_delete");
        Assert.Empty(await store.ListAsync("retry_delete"));
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "retry_delete")));
    }

    private static AutomationArtifactStore CreateStore(string rootPath)
    {
        return new AutomationArtifactStore(new AutomationArtifactStoreOptions
        {
            RootPath = rootPath,
            MaxArtifactBytes = 1024,
            MaxSessionBytes = 4096,
            MaxArtifactsPerSession = 8,
        });
    }

    private static ValueTask<AutomationArtifactReference> WriteBytesAsync(
        AutomationArtifactStore store,
        string sessionId,
        byte[] bytes,
        AutomationRevisionSnapshot revision)
    {
        return store.WriteAsync(
            sessionId,
            "bin",
            "application/octet-stream",
            revision,
            (stream, cancellationToken) => stream.WriteAsync(bytes, cancellationToken));
    }

    private static AutomationRevisionSnapshot CreateRevision(
        long globalRevision,
        params (string ResourceId, long Revision)[] resources)
    {
        return new AutomationRevisionSnapshot
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = globalRevision,
            Resources =
            [
                .. resources.Select(static resource => new AutomationResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }
}
