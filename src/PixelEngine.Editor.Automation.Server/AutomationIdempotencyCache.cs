using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

internal sealed class AutomationIdempotencyCache
{
    private readonly Lock _sync = new();
    private readonly Dictionary<CacheKey, Entry> _entries = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly int _capacity;

    public AutomationIdempotencyCache(TimeProvider timeProvider, TimeSpan retention, int capacity)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _retention = retention;
        _capacity = capacity;
    }

    public AutomationIdempotencyLookup GetOrAdd(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        Func<Task<AutomationHandlerResult>> factory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(factory);
        string idempotencyKey = context.IdempotencyKey
            ?? throw new ArgumentException("请求缺少 idempotency key。", nameof(context));
        string scopeId = context.TransactionId is not null ||
            method.StartsWith("transaction.", StringComparison.Ordinal)
            ? context.SessionId
            : context.ClientInstanceId;
        CacheKey key = new(context.PrincipalId, scopeId, idempotencyKey);
        string fingerprint = ComputeFingerprint(context, method, payload);
        Entry entry;
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            PruneExpiredLocked(now);
            if (_entries.TryGetValue(key, out Entry? existing))
            {
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw Conflict();
                }

                existing.LastAccessUtc = now;
                return new AutomationIdempotencyLookup(existing.Task, Created: false);
            }

            EnsureCapacityLocked();
            Task<AutomationHandlerResult> task = factory();
            entry = new Entry(fingerprint, task, now);
            _entries.Add(key, entry);
        }

        _ = entry.Task.ContinueWith(
            static (task, state) =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    (AutomationIdempotencyCache Cache, CacheKey Key, Entry Entry) tuple =
                        ((AutomationIdempotencyCache, CacheKey, Entry))state!;
                    tuple.Cache.RemoveFaulted(tuple.Key, tuple.Entry);
                }
            },
            (this, key, entry),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return new AutomationIdempotencyLookup(entry.Task, Created: true);
    }

    private void RemoveFaulted(CacheKey key, Entry entry)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out Entry? current) && ReferenceEquals(current, entry))
            {
                _ = _entries.Remove(key);
            }
        }
    }

    private void PruneExpiredLocked(DateTimeOffset now)
    {
        List<CacheKey>? expired = null;
        foreach ((CacheKey key, Entry entry) in _entries)
        {
            if (entry.Task.IsCompleted && now - entry.LastAccessUtc >= _retention)
            {
                (expired ??= []).Add(key);
            }
        }

        if (expired is null)
        {
            return;
        }

        for (int i = 0; i < expired.Count; i++)
        {
            _ = _entries.Remove(expired[i]);
        }
    }

    private void EnsureCapacityLocked()
    {
        if (_entries.Count < _capacity)
        {
            return;
        }

        throw new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.Busy,
            Category = AutomationErrorCategory.Availability,
            Message = "Automation idempotency retention cache 已满；为保持 exactly-once 窗口，不会提前淘汰成功结果。",
            Transient = true,
            RetryAfterMilliseconds = 25,
        });
    }

    private static string ComputeFingerprint(
        AutomationRequestContext context,
        string method,
        JsonElement? payload)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, method);
        Append(hash, context.TransactionId ?? string.Empty);
        if (payload is null)
        {
            Append(hash, "absent");
        }
        else
        {
            AppendCanonicalJson(hash, payload.Value);
        }

        if (context.ExpectedRevision is not null)
        {
            Append(hash, "revision");
            Append(hash, context.ExpectedRevision.GlobalRevision?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "absent");
            AutomationExpectedResourceRevision[] resources =
            [
                .. context.ExpectedRevision.Resources.OrderBy(
                    static resource => resource.ResourceId,
                    StringComparer.Ordinal),
            ];
            for (int i = 0; i < resources.Length; i++)
            {
                Append(hash, resources[i].ResourceId);
                Append(hash, resources[i].Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        else
        {
            Append(hash, []);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendCanonicalJson(IncrementalHash hash, JsonElement element)
    {
        Append(hash, element.ValueKind.ToString());
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                JsonProperty[] properties =
                [
                    .. element.EnumerateObject().OrderBy(
                        static property => property.Name,
                        StringComparer.Ordinal),
                ];
                Append(hash, properties.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                for (int i = 0; i < properties.Length; i++)
                {
                    Append(hash, properties[i].Name);
                    AppendCanonicalJson(hash, properties[i].Value);
                }

                break;
            case JsonValueKind.Array:
                int length = element.GetArrayLength();
                Append(hash, length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AppendCanonicalJson(hash, item);
                }

                break;
            case JsonValueKind.String:
                Append(hash, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                Append(hash, element.GetRawText());
                break;
            case JsonValueKind.True:
                Append(hash, "true");
                break;
            case JsonValueKind.False:
                Append(hash, "false");
                break;
            case JsonValueKind.Null:
                Append(hash, "null");
                break;
            case JsonValueKind.Undefined:
                Append(hash, "undefined");
                break;
            default:
                throw new InvalidOperationException($"未知 JSON value kind {element.ValueKind}。");
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        try
        {
            Append(hash, utf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static AutomationRequestException Conflict()
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.IdempotencyConflict,
            Category = AutomationErrorCategory.Conflict,
            Message = "同一 idempotency key 已被不同请求内容使用。",
            Transient = false,
        });
    }

    private readonly record struct CacheKey(string PrincipalId, string ScopeId, string IdempotencyKey);

    private sealed class Entry(string fingerprint, Task<AutomationHandlerResult> task, DateTimeOffset now)
    {
        public string Fingerprint { get; } = fingerprint;

        public Task<AutomationHandlerResult> Task { get; } = task;

        public DateTimeOffset LastAccessUtc { get; set; } = now;
    }
}

internal readonly record struct AutomationIdempotencyLookup(
    Task<AutomationHandlerResult> Task,
    bool Created);
