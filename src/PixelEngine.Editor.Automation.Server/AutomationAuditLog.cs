using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 不含请求 payload、认证 proof 或 secret 的固定字段审计记录。
/// </summary>
internal sealed record AutomationAuditRecord
{
    public required int SchemaVersion { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string InstanceId { get; init; }

    public string? SessionId { get; init; }

    public string? PrincipalId { get; init; }

    public string? ClientInstanceId { get; init; }

    public required string RequestId { get; init; }

    public required string CorrelationId { get; init; }

    public required string Capability { get; init; }

    public required string Result { get; init; }

    public string? ErrorCode { get; init; }

    public required long DurationMicroseconds { get; init; }

    public AutomationRevisionSnapshot? Revision { get; init; }

    public long? CurrentRevision { get; init; }

    public string? TransactionId { get; init; }
}

/// <summary>
/// 串行、WriteThrough、有界轮转的 current-user JSONL 审计日志。
/// </summary>
internal sealed class AutomationAuditLog : IAsyncDisposable
{
    private const int MaxRecordBytes = 4 * 1024 * 1024;
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private FileStream? _stream;
    private Exception? _fault;
    private int _disposed;

    public AutomationAuditLog(
        string rootPath,
        string instanceId,
        long maxFileBytes,
        int maxFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, MaxRecordBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFiles, 32);

        string root = Path.GetFullPath(rootPath);
        AutomationSecureStorage.EnsurePrivateDirectory(root);
        CurrentPath = Path.Combine(root, $"{instanceId}.jsonl");
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
        _stream = OpenCurrentFile();
    }

    public string CurrentPath { get; }

    public void ThrowIfFaulted()
    {
        Exception? fault = Volatile.Read(ref _fault);
        if (fault is not null)
        {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }
    }

    public async ValueTask AppendAsync(AutomationAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfFaulted();

        byte[]? json = null;
        bool lockTaken = false;
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ThrowIfFaulted();

            json = JsonSerializer.SerializeToUtf8Bytes(
                record,
                AutomationAuditJsonContext.Default.AutomationAuditRecord);
            if (json.Length > MaxRecordBytes || json.Length + NewLine.Length > _maxFileBytes)
            {
                throw new InvalidDataException(
                    $"Automation audit record 超过 {Math.Min(MaxRecordBytes, _maxFileBytes)} 字节上限。");
            }

            FileStream stream = _stream
                ?? throw new ObjectDisposedException(nameof(AutomationAuditLog));
            if (stream.Length != 0 && stream.Length + json.Length + NewLine.Length > _maxFileBytes)
            {
                await RotateAsync().ConfigureAwait(false);
                stream = _stream
                    ?? throw new ObjectDisposedException(nameof(AutomationAuditLog));
            }

            await stream.WriteAsync(json).ConfigureAwait(false);
            await stream.WriteAsync(NewLine).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _ = Interlocked.CompareExchange(ref _fault, exception, null);
            throw;
        }
        finally
        {
            if (lockTaken)
            {
                _ = _writeLock.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            FileStream? stream = _stream;
            _stream = null;
            if (stream is not null)
            {
                await stream.FlushAsync().ConfigureAwait(false);
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _writeLock.Release();
            _writeLock.Dispose();
        }
    }

    private FileStream OpenCurrentFile()
    {
        if (File.Exists(CurrentPath))
        {
            AutomationSecureStorage.EnsurePrivateFile(CurrentPath);
        }

        FileStream stream = new(
            CurrentPath,
            new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read | FileShare.Delete,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });
        try
        {
            AutomationSecureStorage.EnsurePrivateFile(CurrentPath);
            RepairTrailingPartialRecord(stream);
            stream.Position = stream.Length;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private async ValueTask RotateAsync()
    {
        FileStream stream = _stream
            ?? throw new ObjectDisposedException(nameof(AutomationAuditLog));
        _stream = null;
        await stream.DisposeAsync().ConfigureAwait(false);

        if (_maxFiles == 1)
        {
            DeleteRegularFileIfPresent(CurrentPath);
        }
        else
        {
            DeleteRegularFileIfPresent(GetRotatedPath(_maxFiles - 1));
            for (int index = _maxFiles - 1; index >= 2; index--)
            {
                MoveRegularFileIfPresent(GetRotatedPath(index - 1), GetRotatedPath(index));
            }

            MoveRegularFileIfPresent(CurrentPath, GetRotatedPath(1));
        }

        _stream = OpenCurrentFile();
    }

    private string GetRotatedPath(int index)
    {
        return $"{CurrentPath}.{index}";
    }

    private static void MoveRegularFileIfPresent(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        AutomationSecureStorage.EnsurePrivateFile(source);
        DeleteRegularFileIfPresent(destination);
        File.Move(source, destination);
        AutomationSecureStorage.EnsurePrivateFile(destination);
    }

    private static void DeleteRegularFileIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        AutomationSecureStorage.EnsurePrivateFile(path);
        File.Delete(path);
    }

    private static void RepairTrailingPartialRecord(FileStream stream)
    {
        long length = stream.Length;
        if (length == 0)
        {
            return;
        }

        stream.Position = length - 1;
        if (stream.ReadByte() == '\n')
        {
            return;
        }

        byte[] buffer = new byte[4096];
        long end = length;
        while (end > 0)
        {
            long start = Math.Max(0, end - buffer.Length);
            int count = checked((int)(end - start));
            stream.Position = start;
            stream.ReadExactly(buffer.AsSpan(0, count));
            for (int index = count - 1; index >= 0; index--)
            {
                if (buffer[index] != '\n')
                {
                    continue;
                }

                stream.SetLength(start + index + 1);
                return;
            }

            end = start;
        }

        stream.SetLength(0);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AutomationAuditRecord))]
internal sealed partial class AutomationAuditJsonContext : JsonSerializerContext;
