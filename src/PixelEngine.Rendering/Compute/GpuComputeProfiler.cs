using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// GPU timer query 收集器。结果异步解析到 Core 诊断细分相位，不参与 sim 决策数据流。
/// </summary>
public sealed class GpuComputeProfiler : IDisposable
{
    private const int PendingCapacity = 32;

    private readonly IComputeBackend _backend;
    private readonly PendingQuery[] _pending = new PendingQuery[PendingCapacity];
    private int _writeIndex;
    private bool _active;
    private bool _disposed;

    /// <summary>
    /// 创建 GPU compute profiler。
    /// </summary>
    public GpuComputeProfiler(IComputeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>
    /// 开始测量一个 GPU pass。不可用后端返回空 scope，不调用 GL query。
    /// </summary>
    public GpuTimerScope Measure(string passName, FrameSubPhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_backend.IsAvailable)
        {
            return default;
        }

        if (_active)
        {
            throw new InvalidOperationException("已有 GPU timer query 处于开启状态。");
        }

        uint query = _backend.BeginTimerQuery(passName);
        _active = true;
        return new GpuTimerScope(this, query, phase);
    }

    /// <summary>
    /// 非阻塞解析已经完成的 GPU timer query，并写入 profiler 细分相位。
    /// </summary>
    public void ResolveCompleted(FrameProfiler? profiler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_backend.IsAvailable)
        {
            return;
        }

        for (int i = 0; i < _pending.Length; i++)
        {
            uint query = _pending[i].QueryHandle;
            if (query == 0)
            {
                continue;
            }

            if (_backend.TryGetTimerResult(query, out ulong elapsedNanoseconds))
            {
                profiler?.RecordSub(_pending[i].Phase, elapsedNanoseconds / 1_000_000.0);
                _pending[i] = default;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < _pending.Length; i++)
        {
            uint query = _pending[i].QueryHandle;
            if (query != 0)
            {
                _backend.DeleteTimerQuery(query);
                _pending[i] = default;
            }
        }

        _disposed = true;
    }

    private void End(uint queryHandle, FrameSubPhase phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active)
        {
            throw new InvalidOperationException("没有开启中的 GPU timer query。");
        }

        _backend.EndTimerQuery();
        _active = false;
        if (queryHandle == 0)
        {
            return;
        }

        if (_pending[_writeIndex].QueryHandle != 0)
        {
            _backend.DeleteTimerQuery(_pending[_writeIndex].QueryHandle);
        }

        _pending[_writeIndex] = new PendingQuery(queryHandle, phase);
        _writeIndex = (_writeIndex + 1) & (PendingCapacity - 1);
    }

    private readonly record struct PendingQuery(uint QueryHandle, FrameSubPhase Phase);

    /// <summary>
    /// GPU timer using-scope。
    /// </summary>
    public readonly struct GpuTimerScope : IDisposable
    {
        private readonly GpuComputeProfiler? _owner;
        private readonly uint _queryHandle;
        private readonly FrameSubPhase _phase;

        internal GpuTimerScope(GpuComputeProfiler owner, uint queryHandle, FrameSubPhase phase)
        {
            _owner = owner;
            _queryHandle = queryHandle;
            _phase = phase;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _owner?.End(_queryHandle, _phase);
        }
    }
}
