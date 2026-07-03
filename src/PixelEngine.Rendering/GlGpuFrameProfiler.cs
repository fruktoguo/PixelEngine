using PixelEngine.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 使用 OpenGL timestamp query 异步测量整帧 GPU 执行耗时；不阻塞主线程回读。
/// </summary>
internal sealed class GlGpuFrameProfiler : IDisposable
{
    private const int PendingCapacity = 32;

    private readonly GL _gl;
    private readonly PendingFrame[] _pending = new PendingFrame[PendingCapacity];
    private int _writeIndex;
    private bool _disposed;

    public GlGpuFrameProfiler(GL gl, GlCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(capabilities);
        _gl = gl;
        IsAvailable = !capabilities.IsGles &&
            (capabilities.MajorVersion > 3 || (capabilities.MajorVersion == 3 && capabilities.MinorVersion >= 3));
    }

    /// <summary>
    /// 当前上下文是否支持桌面 GL timestamp query。
    /// </summary>
    public bool IsAvailable { get; }

    public GpuFrameScope Measure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
        {
            return default;
        }

        uint start = _gl.GenQuery();
        uint end = _gl.GenQuery();
        GlResourceTracker.TrackCreated(GlResourceKind.TimerQuery, start);
        GlResourceTracker.TrackCreated(GlResourceKind.TimerQuery, end);
        _gl.QueryCounter(start, QueryCounterTarget.Timestamp);
        return new GpuFrameScope(this, start, end);
    }

    public void ResolveCompleted(FrameProfiler? profiler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
        {
            return;
        }

        for (int i = 0; i < _pending.Length; i++)
        {
            PendingFrame pending = _pending[i];
            if (pending.StartQuery == 0 || pending.EndQuery == 0)
            {
                continue;
            }

            _gl.GetQueryObject(pending.EndQuery, QueryObjectParameterName.ResultAvailable, out int available);
            if (available == 0)
            {
                continue;
            }

            _gl.GetQueryObject(pending.StartQuery, QueryObjectParameterName.Result, out ulong startTimestamp);
            _gl.GetQueryObject(pending.EndQuery, QueryObjectParameterName.Result, out ulong endTimestamp);
            if (endTimestamp >= startTimestamp)
            {
                profiler?.RecordSub(FrameSubPhase.GpuFrame, (endTimestamp - startTimestamp) / 1_000_000.0);
            }

            Delete(pending.StartQuery);
            Delete(pending.EndQuery);
            _pending[i] = default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < _pending.Length; i++)
        {
            PendingFrame pending = _pending[i];
            if (pending.StartQuery != 0)
            {
                Delete(pending.StartQuery);
            }

            if (pending.EndQuery != 0)
            {
                Delete(pending.EndQuery);
            }

            _pending[i] = default;
        }

        _disposed = true;
    }

    private void End(uint startQuery, uint endQuery)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (startQuery == 0 || endQuery == 0)
        {
            return;
        }

        _gl.QueryCounter(endQuery, QueryCounterTarget.Timestamp);
        PendingFrame overwritten = _pending[_writeIndex];
        if (overwritten.StartQuery != 0)
        {
            Delete(overwritten.StartQuery);
        }

        if (overwritten.EndQuery != 0)
        {
            Delete(overwritten.EndQuery);
        }

        _pending[_writeIndex] = new PendingFrame(startQuery, endQuery);
        _writeIndex = (_writeIndex + 1) & (PendingCapacity - 1);
    }

    private void Delete(uint query)
    {
        _gl.DeleteQuery(query);
        GlResourceTracker.TrackDeleted(GlResourceKind.TimerQuery, query);
    }

    private readonly record struct PendingFrame(uint StartQuery, uint EndQuery);

    public readonly struct GpuFrameScope : IDisposable
    {
        private readonly GlGpuFrameProfiler? _owner;
        private readonly uint _startQuery;
        private readonly uint _endQuery;

        internal GpuFrameScope(GlGpuFrameProfiler owner, uint startQuery, uint endQuery)
        {
            _owner = owner;
            _startQuery = startQuery;
            _endQuery = endQuery;
        }

        public void Dispose()
        {
            _owner?.End(_startQuery, _endQuery);
        }
    }
}
