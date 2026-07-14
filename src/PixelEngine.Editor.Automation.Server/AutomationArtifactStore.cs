using System.Security.Cryptography;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// Session artifact root、单文件与总量配额配置。
/// </summary>
public sealed record AutomationArtifactStoreOptions
{
    /// <summary>artifact canonical root。</summary>
    public required string RootPath { get; init; }

    /// <summary>单 artifact 最大字节数。</summary>
    public long MaxArtifactBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>单 session 全部 artifact 最大字节数。</summary>
    public long MaxSessionBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>单 session artifact 数量上限。</summary>
    public int MaxArtifactsPerSession { get; init; } = 1024;

    /// <summary>当前 Editor 实例全部 session 的 artifact 总字节上限。</summary>
    public long MaxTotalBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>当前 Editor 实例全部 session 的 artifact 总数量上限。</summary>
    public int MaxArtifacts { get; init; } = 4096;

    /// <summary>当前 Editor 实例允许保留 artifact state 的 session 数量上限。</summary>
    public int MaxSessions { get; init; } = 256;

    /// <summary>可测试时钟。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// 制品附加的尺寸、编码与小型 metadata。
/// </summary>
public sealed record AutomationArtifactMetadata
{
    /// <summary>可选像素宽度。</summary>
    public int? Width { get; init; }

    /// <summary>可选像素高度。</summary>
    public int? Height { get; init; }

    /// <summary>编码名称。</summary>
    public string? Encoding { get; init; }

    /// <summary>capability 特有的小型 metadata。</summary>
    public System.Text.Json.JsonElement? Data { get; init; }
}

/// <summary>
/// 在后台把大型数据写入 session 隔离文件，并在同一遍写入中计算 SHA256。
/// </summary>
public sealed class AutomationArtifactStore : IAsyncDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly AutomationArtifactStoreOptions _options;
    private long _totalBytes;
    private long _reservedBytes;
    private int _artifactCount;
    private int _reservedArtifactCount;
    private int _disposed;

    /// <summary>
    /// 创建并保护 artifact root。
    /// </summary>
    /// <param name="options">配额与 root。</param>
    public AutomationArtifactStore(AutomationArtifactStoreOptions options)
    {
        _options = ValidateOptions(options);
        AutomationSecureStorage.EnsurePrivateDirectory(_options.RootPath);
        string volumeRoot = Path.GetPathRoot(_options.RootPath)
            ?? throw new InvalidOperationException("Automation artifact root 没有 volume root。");
        if (ContainsReparsePoint(_options.RootPath, volumeRoot))
        {
            throw PathNotAllowed("Automation artifact root 或其父目录包含 reparse point。");
        }
    }

    /// <summary>
    /// 原子写入 artifact；writer 只看到不可 seek 的有界 write stream。
    /// </summary>
    /// <param name="sessionId">所有者 session。</param>
    /// <param name="extension">不含路径的扩展名。</param>
    /// <param name="mediaType">IANA media type。</param>
    /// <param name="sourceRevision">生成源 snapshot revision。</param>
    /// <param name="writer">后台 writer。</param>
    /// <param name="metadata">尺寸/编码元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原子发布后的引用。</returns>
    public async ValueTask<AutomationArtifactReference> WriteAsync(
        string sessionId,
        string extension,
        string mediaType,
        AutomationRevisionSnapshot sourceRevision,
        Func<Stream, CancellationToken, ValueTask> writer,
        AutomationArtifactMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateIdentifier(sessionId, nameof(sessionId));
        string normalizedExtension = NormalizeExtension(extension);
        ValidateMediaType(mediaType);
        ValidateRevision(sourceRevision);
        ArgumentNullException.ThrowIfNull(writer);
        ValidateMetadata(metadata);

        SessionState session = GetOrCreateSession(sessionId);
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.IsDeleting)
            {
                throw SessionDeleting();
            }

            if (ContainsReparsePoint(session.DirectoryPath, _options.RootPath))
            {
                throw PathNotAllowed("Session artifact directory 包含 reparse point。");
            }

            if (session.Artifacts.Count >= _options.MaxArtifactsPerSession)
            {
                throw QuotaExceeded("该 automation session 的 artifact 数量已达上限。");
            }

            long remainingSessionBytes = _options.MaxSessionBytes - session.TotalBytes;
            if (remainingSessionBytes <= 0)
            {
                throw QuotaExceeded("该 automation session 的 artifact 字节配额已耗尽。");
            }

            long requestedByteLimit = Math.Min(_options.MaxArtifactBytes, remainingSessionBytes);
            GlobalReservation reservation = ReserveGlobal(requestedByteLimit);
            bool reservationActive = true;
            bool movedToFinalPath = false;
            bool registeredInSession = false;
            string? temporaryPath = null;
            string? finalPath = null;
            string? artifactId = null;
            try
            {
                artifactId = Guid.NewGuid().ToString("N");
                string fileName = $"{artifactId}.{normalizedExtension}";
                finalPath = EnsureWithinSession(Path.Combine(session.DirectoryPath, fileName), session.DirectoryPath);
                temporaryPath = EnsureWithinSession($"{finalPath}.{Guid.NewGuid():N}.tmp", session.DirectoryPath);
                long bytesWritten;
                string sha256;
                await using (FileStream file = new(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await using (BoundedHashingWriteStream bounded = new(file, reservation.ByteLimit))
                    {
                        await writer(bounded, cancellationToken).ConfigureAwait(false);
                        await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
                        if (bounded.BytesWritten == 0)
                        {
                            throw new InvalidDataException("Automation artifact 不得为空文件。");
                        }

                        bytesWritten = bounded.BytesWritten;
                        sha256 = bounded.GetHashAndSeal();
                    }

                    file.Flush(flushToDisk: true);
                }

                AutomationSecureStorage.EnsurePrivateFile(temporaryPath);
                File.Move(temporaryPath, finalPath);
                movedToFinalPath = true;

                AutomationArtifactReference reference = new()
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ArtifactId = artifactId,
                    Path = finalPath,
                    RelativePath = fileName,
                    MediaType = mediaType,
                    ByteLength = bytesWritten,
                    Sha256 = sha256,
                    CreatedAtUtc = _options.TimeProvider.GetUtcNow(),
                    SourceRevision = CloneRevision(sourceRevision),
                    Width = metadata?.Width,
                    Height = metadata?.Height,
                    Encoding = metadata?.Encoding,
                    Metadata = metadata?.Data?.Clone(),
                };
                session.Artifacts.Add(artifactId, reference);
                session.TotalBytes = checked(session.TotalBytes + reference.ByteLength);
                registeredInSession = true;
                CommitGlobalReservation(reservation, reference.ByteLength);
                reservationActive = false;
                return CloneReference(reference);
            }
            catch (Exception exception)
            {
                if (registeredInSession && artifactId is not null &&
                    session.Artifacts.Remove(artifactId, out AutomationArtifactReference? registered))
                {
                    session.TotalBytes -= registered.ByteLength;
                }

                if (reservationActive)
                {
                    ReleaseGlobalReservation(reservation);
                }

                List<Exception>? cleanupFailures = null;
                AddCleanupFailure(ref cleanupFailures, TryDeleteForCleanup(temporaryPath));
                if (movedToFinalPath)
                {
                    AddCleanupFailure(ref cleanupFailures, TryDeleteForCleanup(finalPath));
                }

                if (cleanupFailures is null)
                {
                    throw;
                }

                cleanupFailures.Insert(0, exception);
                throw new AggregateException("Automation artifact 写入失败，且原子清理遇到错误。", cleanupFailures);
            }
        }
        finally
        {
            _ = session.Gate.Release();
        }
    }

    /// <summary>
    /// 返回 session 内按创建时间/id 稳定排序的 artifact snapshot。
    /// </summary>
    /// <param name="sessionId">所有者 session。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不可变引用数组。</returns>
    public async ValueTask<AutomationArtifactReference[]> ListAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateIdentifier(sessionId, nameof(sessionId));
        SessionState? session = TryGetSession(sessionId);
        if (session is null)
        {
            return [];
        }

        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return session.IsDeleting
                ? []
                :
                [
                    .. session.Artifacts.Values
                        .OrderBy(static artifact => artifact.CreatedAtUtc)
                        .ThenBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
                        .Select(CloneReference),
                ];
        }
        finally
        {
            _ = session.Gate.Release();
        }
    }

    /// <summary>
    /// 删除属于 session 的单个 artifact。
    /// </summary>
    /// <param name="sessionId">所有者 session。</param>
    /// <param name="artifactId">artifact id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否存在并删除。</returns>
    public async ValueTask<bool> DeleteAsync(
        string sessionId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateIdentifier(sessionId, nameof(sessionId));
        ValidateIdentifier(artifactId, nameof(artifactId));
        SessionState? session = TryGetSession(sessionId);
        if (session is null)
        {
            return false;
        }

        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.IsDeleting)
            {
                return false;
            }

            if (!session.Artifacts.TryGetValue(artifactId, out AutomationArtifactReference? artifact))
            {
                return false;
            }

            string path = EnsureWithinSession(artifact.Path, session.DirectoryPath);
            if (ContainsReparsePoint(path, session.DirectoryPath))
            {
                throw PathNotAllowed("Artifact path 包含 reparse point。");
            }

            File.Delete(path);
            RemoveArtifactState(session, artifactId, artifact);
            return true;
        }
        finally
        {
            _ = session.Gate.Release();
        }
    }

    /// <summary>
    /// 从磁盘重新计算 length/SHA256，验证客户端将要消费的 artifact 未被替换。
    /// </summary>
    /// <param name="sessionId">所有者 session。</param>
    /// <param name="artifactId">artifact id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>length 与 SHA256 是否仍匹配。</returns>
    public async ValueTask<bool> VerifyAsync(
        string sessionId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateIdentifier(sessionId, nameof(sessionId));
        ValidateIdentifier(artifactId, nameof(artifactId));
        SessionState? session = TryGetSession(sessionId);
        if (session is null)
        {
            return false;
        }

        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.IsDeleting)
            {
                return false;
            }

            if (!session.Artifacts.TryGetValue(artifactId, out AutomationArtifactReference? artifact))
            {
                return false;
            }

            string path = EnsureWithinSession(artifact.Path, session.DirectoryPath);
            if (!File.Exists(path) || ContainsReparsePoint(path, session.DirectoryPath))
            {
                return false;
            }

            await using FileStream file = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            try
            {
                return file.Length == artifact.ByteLength &&
                    string.Equals(Convert.ToHexStringLower(hash), artifact.Sha256, StringComparison.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            _ = session.Gate.Release();
        }
    }

    /// <summary>
    /// 删除 session 的全部 artifact 和目录。
    /// </summary>
    /// <param name="sessionId">所有者 session。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateIdentifier(sessionId, nameof(sessionId));
        SessionState? session;
        TaskCompletionSource<bool> deletionCompletion;
        bool ownsDeletion;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
            {
                return;
            }

            if (session.DeletionCompletion is { } existing)
            {
                deletionCompletion = existing;
                ownsDeletion = false;
            }
            else
            {
                deletionCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.DeletionCompletion = deletionCompletion;
                session.SetDeleting(true);
                ownsDeletion = true;
            }
        }

        if (!ownsDeletion)
        {
            bool deleted = await deletionCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!deleted)
            {
                throw new IOException("并发的 session artifact 删除未成功。");
            }

            return;
        }

        bool gateTaken = false;
        try
        {
            await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateTaken = true;
            if (Directory.Exists(session.DirectoryPath))
            {
                if (ContainsReparsePoint(session.DirectoryPath, _options.RootPath))
                {
                    throw PathNotAllowed("Session artifact directory 包含 reparse point。");
                }

                AutomationArtifactReference[] artifacts = [.. session.Artifacts.Values];
                for (int i = 0; i < artifacts.Length; i++)
                {
                    AutomationArtifactReference artifact = artifacts[i];
                    string path = EnsureWithinSession(artifact.Path, session.DirectoryPath);
                    if (ContainsReparsePoint(path, session.DirectoryPath))
                    {
                        throw PathNotAllowed("Session artifact 文件包含 reparse point。");
                    }
                }

                for (int i = 0; i < artifacts.Length; i++)
                {
                    AutomationArtifactReference artifact = artifacts[i];
                    File.Delete(artifact.Path);
                    RemoveArtifactState(session, artifact.ArtifactId, artifact);
                }

                foreach (string file in Directory.EnumerateFiles(
                             session.DirectoryPath,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (ContainsReparsePoint(file, session.DirectoryPath))
                    {
                        throw PathNotAllowed("Session artifact 文件包含 reparse point。");
                    }

                    File.Delete(file);
                }

                Directory.Delete(session.DirectoryPath, recursive: false);
            }
            else
            {
                AutomationArtifactReference[] missing = [.. session.Artifacts.Values];
                for (int i = 0; i < missing.Length; i++)
                {
                    RemoveArtifactState(session, missing[i].ArtifactId, missing[i]);
                }
            }
        }
        catch
        {
            lock (_sync)
            {
                session.SetDeleting(false);
                session.DeletionCompletion = null;
            }

            _ = deletionCompletion.TrySetResult(false);
            throw;
        }
        finally
        {
            if (gateTaken)
            {
                _ = session.Gate.Release();
            }
        }

        lock (_sync)
        {
            if (_sessions.TryGetValue(sessionId, out SessionState? current) && ReferenceEquals(current, session))
            {
                _ = _sessions.Remove(sessionId);
            }
        }

        _ = deletionCompletion.TrySetResult(true);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SessionState[] sessions;
        lock (_sync)
        {
            sessions = [.. _sessions.Values];
            for (int i = 0; i < sessions.Length; i++)
            {
                sessions[i].SetDeleting(true);
            }

            _sessions.Clear();
        }

        for (int i = 0; i < sessions.Length; i++)
        {
            await sessions[i].Gate.WaitAsync().ConfigureAwait(false);
            _ = sessions[i].Gate.Release();
        }

        lock (_sync)
        {
            _totalBytes = 0;
            _reservedBytes = 0;
            _artifactCount = 0;
            _reservedArtifactCount = 0;
        }
    }

    private GlobalReservation ReserveGlobal(long requestedByteLimit)
    {
        lock (_sync)
        {
            if (_artifactCount + _reservedArtifactCount >= _options.MaxArtifacts)
            {
                throw QuotaExceeded("当前 Editor automation artifact 总数量已达上限。");
            }

            long remainingBytes = _options.MaxTotalBytes - _totalBytes - _reservedBytes;
            if (remainingBytes <= 0)
            {
                throw QuotaExceeded("当前 Editor automation artifact 总字节配额已耗尽。");
            }

            long byteLimit = Math.Min(requestedByteLimit, remainingBytes);
            _reservedBytes += byteLimit;
            _reservedArtifactCount++;
            return new GlobalReservation(byteLimit);
        }
    }

    private void CommitGlobalReservation(GlobalReservation reservation, long actualBytes)
    {
        lock (_sync)
        {
            if (actualBytes <= 0 || actualBytes > reservation.ByteLimit ||
                _reservedBytes < reservation.ByteLimit || _reservedArtifactCount <= 0)
            {
                throw new InvalidOperationException("Automation artifact global reservation 状态无效。");
            }

            _reservedBytes -= reservation.ByteLimit;
            _reservedArtifactCount--;
            _totalBytes += actualBytes;
            _artifactCount++;
        }
    }

    private void ReleaseGlobalReservation(GlobalReservation reservation)
    {
        lock (_sync)
        {
            _reservedBytes -= reservation.ByteLimit;
            _reservedArtifactCount--;
        }
    }

    private void RemovePublishedArtifacts(long bytes, int count)
    {
        lock (_sync)
        {
            _totalBytes -= bytes;
            _artifactCount -= count;
        }
    }

    private void RemoveArtifactState(
        SessionState session,
        string artifactId,
        AutomationArtifactReference expected)
    {
        if (!session.Artifacts.Remove(artifactId, out AutomationArtifactReference? removed) ||
            !ReferenceEquals(removed, expected))
        {
            throw new InvalidOperationException("Automation artifact session index 状态无效。");
        }

        session.TotalBytes -= removed.ByteLength;
        RemovePublishedArtifacts(removed.ByteLength, 1);
    }

    private SessionState GetOrCreateSession(string sessionId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_sessions.TryGetValue(sessionId, out SessionState? existing))
            {
                return existing.IsDeleting ? throw SessionDeleting() : existing;
            }

            if (_sessions.Count >= _options.MaxSessions)
            {
                throw QuotaExceeded("Automation artifact session 数量已达上限。");
            }

            string directory = EnsureWithinSession(Path.Combine(_options.RootPath, sessionId), _options.RootPath);
            string volumeRoot = Path.GetPathRoot(_options.RootPath)
                ?? throw new InvalidOperationException("Automation artifact root 没有 volume root。");
            if (ContainsReparsePoint(_options.RootPath, volumeRoot))
            {
                throw PathNotAllowed("Automation artifact root 或其父目录包含 reparse point。");
            }

            AutomationSecureStorage.EnsurePrivateDirectory(directory);
            if (ContainsReparsePoint(directory, _options.RootPath))
            {
                throw PathNotAllowed("Session artifact directory 包含 reparse point。");
            }

            SessionState created = new(directory);
            _sessions.Add(sessionId, created);
            return created;
        }
    }

    private SessionState? TryGetSession(string sessionId)
    {
        lock (_sync)
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    private static AutomationArtifactStoreOptions ValidateOptions(AutomationArtifactStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxSessionBytes, options.MaxArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxArtifactsPerSession);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxTotalBytes, options.MaxSessionBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxArtifacts, options.MaxArtifactsPerSession);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSessions);
        return options with { RootPath = Path.GetFullPath(options.RootPath) };
    }

    private static void ValidateRevision(AutomationRevisionSnapshot revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            revision.GlobalRevision < 0 || revision.Resources is null ||
            revision.Resources.Length > AutomationProtocolConstants.MaxRevisionResources)
        {
            throw new ArgumentException("Artifact source revision 无效。", nameof(revision));
        }

        HashSet<string> resourceIds = new(StringComparer.Ordinal);
        for (int i = 0; i < revision.Resources.Length; i++)
        {
            AutomationResourceRevision resource = revision.Resources[i]
                ?? throw new ArgumentException("Artifact source resource revision 不得为空。", nameof(revision));
            if (resource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                string.IsNullOrWhiteSpace(resource.ResourceId) ||
                resource.ResourceId.Length > AutomationProtocolConstants.MaxResourceIdLength ||
                resource.ResourceId.Any(char.IsControl) || resource.Revision < 0 ||
                !resourceIds.Add(resource.ResourceId))
            {
                throw new ArgumentException("Artifact source resource revision 无效或重复。", nameof(revision));
            }
        }
    }

    private static void ValidateMetadata(AutomationArtifactMetadata? metadata)
    {
        if (metadata?.Width is <= 0 || metadata?.Height is <= 0 ||
            (metadata?.Width.HasValue != metadata?.Height.HasValue) ||
            (metadata?.Encoding is not null &&
             (string.IsNullOrWhiteSpace(metadata.Encoding) || metadata.Encoding.Length > 64 ||
              metadata.Encoding.Any(char.IsControl))) ||
            (metadata?.Data is { } data &&
             (data.ValueKind == System.Text.Json.JsonValueKind.Undefined ||
              System.Text.Encoding.UTF8.GetByteCount(data.GetRawText()) > 64 * 1024)))
        {
            throw new ArgumentException("Artifact dimensions/encoding metadata 无效。", nameof(metadata));
        }
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.TrimStart('.').ToLowerInvariant();
        return normalized.Length is >= 1 and <= 16 &&
            normalized.All(static character => char.IsAsciiLetterOrDigit(character))
            ? normalized
            : throw new ArgumentException(
                "Artifact extension 只能包含 1..16 个 ASCII 字母或数字。",
                nameof(extension));
    }

    private static void ValidateMediaType(string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        string[] parts = mediaType.Split('/');
        if (mediaType.Length > 128 || parts.Length != 2 ||
            parts.Any(static part => part.Length == 0 || !part.All(IsMediaTypeTokenCharacter)))
        {
            throw new ArgumentException("Artifact media type 无效。", nameof(mediaType));
        }
    }

    private static bool IsMediaTypeTokenCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '!' or '#' or '$' or '&' or '^' or '_' or
            '.' or '+' or '-';
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128 || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Automation identifier 只能包含 ASCII 字母、数字、'-' 与 '_'。", parameterName);
        }
    }

    private static string EnsureWithinSession(string path, string root)
    {
        string canonical = Path.GetFullPath(path);
        string canonicalRoot = Path.GetFullPath(root);
        string prefix = Path.TrimEndingDirectorySeparator(canonicalRoot) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return canonical.StartsWith(prefix, comparison)
            ? canonical
            : throw PathNotAllowed("Artifact path 越过 session/root 边界。");
    }

    private static bool ContainsReparsePoint(string path, string root)
    {
        string current = Path.GetFullPath(path);
        string canonicalRoot = Path.GetFullPath(root);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            if (string.Equals(current, canonicalRoot, comparison))
            {
                return false;
            }

            current = Path.GetDirectoryName(current)
                ?? throw PathNotAllowed("Artifact path 无法回溯到获准 root。");
        }
    }

    private static AutomationRequestException QuotaExceeded(string message)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.ArtifactQuotaExceeded,
            Category = AutomationErrorCategory.Availability,
            Message = message,
            Transient = false,
        });
    }

    private static AutomationRequestException PathNotAllowed(string message)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.PathNotAllowed,
            Category = AutomationErrorCategory.Authorization,
            Message = message,
            Transient = false,
        });
    }

    private static AutomationRequestException SessionDeleting()
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.Busy,
            Category = AutomationErrorCategory.Availability,
            Message = "Automation artifact session 正在删除。",
            Transient = true,
            RetryAfterMilliseconds = 25,
        });
    }

    private static AutomationRevisionSnapshot CloneRevision(AutomationRevisionSnapshot revision)
    {
        return revision with
        {
            Resources =
            [
                .. revision.Resources.Select(static resource => resource with { }),
            ],
        };
    }

    private static AutomationArtifactReference CloneReference(AutomationArtifactReference artifact)
    {
        return artifact with
        {
            SourceRevision = CloneRevision(artifact.SourceRevision),
            Metadata = artifact.Metadata?.Clone(),
        };
    }

    private static Exception? TryDeleteForCleanup(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            File.Delete(path);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void AddCleanupFailure(ref List<Exception>? failures, Exception? failure)
    {
        if (failure is not null)
        {
            (failures ??= []).Add(failure);
        }
    }

    private readonly record struct GlobalReservation(long ByteLimit);

    private sealed class SessionState(string directoryPath)
    {
        public string DirectoryPath { get; } = directoryPath;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Dictionary<string, AutomationArtifactReference> Artifacts { get; } = new(StringComparer.Ordinal);

        public long TotalBytes { get; set; }

        private int _deleting;

        public bool IsDeleting => Volatile.Read(ref _deleting) != 0;

        public void SetDeleting(bool deleting)
        {
            Volatile.Write(ref _deleting, deleting ? 1 : 0);
        }

        public TaskCompletionSource<bool>? DeletionCompletion { get; set; }
    }

    private sealed class BoundedHashingWriteStream(Stream inner, long limit) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _sealed;

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !_sealed;

        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public string GetHashAndSeal()
        {
            ObjectDisposedException.ThrowIf(_sealed, this);
            _sealed = true;
            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCanWrite(buffer.Length);
            inner.Write(buffer);
            _hash.AppendData(buffer);
            BytesWritten += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCanWrite(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _hash.AppendData(buffer.Span);
            BytesWritten += buffer.Length;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        private void EnsureCanWrite(int count)
        {
            ObjectDisposedException.ThrowIf(_sealed, this);
            if (count < 0 || BytesWritten > limit - count)
            {
                throw QuotaExceeded($"Automation artifact 超过单文件/剩余 session 配额 {limit} 字节。");
            }
        }
    }
}
