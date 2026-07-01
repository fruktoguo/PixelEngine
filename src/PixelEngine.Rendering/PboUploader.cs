using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 2-PBO ping-pong 上传器。每帧 orphan + unsynchronized map，避免 CPU render buffer 到世界纹理的同步停顿。
/// </summary>
public sealed unsafe class PboUploader : IDisposable
{
    private const ulong FenceWaitNanoseconds = 1_000_000;

    private readonly GL _gl;
    private readonly PboSlot[] _slots;
    private int _index;
    private bool _disposed;

    /// <summary>
    /// 创建 PBO 上传器。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="initialCapacityBytes">初始 PBO 容量。</param>
    public PboUploader(GL gl, int initialCapacityBytes)
        : this(gl, initialCapacityBytes, PboUploadMode.OrphanMap)
    {
    }

    /// <summary>
    /// 创建 PBO 上传器，并按能力快照选择实际上传路径。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="initialCapacityBytes">初始 PBO 容量。</param>
    /// <param name="capabilities">OpenGL 能力快照。</param>
    /// <param name="preferredMode">期望上传路径。persistent 路径会被 <see cref="GlCapabilities.HasBufferStorage"/> gate。</param>
    public PboUploader(GL gl, int initialCapacityBytes, GlCapabilities capabilities, PboUploadMode preferredMode)
        : this(
            gl,
            initialCapacityBytes,
            SelectActualMode(capabilities, preferredMode))
    {
    }

    private PboUploader(GL gl, int initialCapacityBytes, PboUploadMode mode)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (initialCapacityBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacityBytes));
        }

        _gl = gl;
        Mode = mode;
        _slots =
        [
            new PboSlot(gl),
            new PboSlot(gl),
        ];
        EnsureCapacity(initialCapacityBytes);
    }

    /// <summary>
    /// 当前 PBO 容量。
    /// </summary>
    public int CapacityBytes { get; private set; }

    /// <summary>
    /// 实际启用的 PBO 上传路径。默认是 <see cref="PboUploadMode.OrphanMap"/>。
    /// </summary>
    public PboUploadMode Mode { get; }

    /// <summary>
    /// 上传整张 render buffer 到世界纹理。
    /// </summary>
    /// <param name="texture">目标世界纹理。</param>
    /// <param name="buffer">源 render buffer。</param>
    public void UploadFull(WorldTexture texture, RenderBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSameSize(texture, buffer);

        PboSlot slot = CopyBufferToNextPbo(buffer);
        slot.Buffer.Bind();
        texture.Bind();
        _gl.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0,
            0,
            (uint)buffer.Width,
            (uint)buffer.Height,
            PixelFormat.Bgra,
            PixelType.UnsignedInt8888Rev,
            null);
        InsertFence(slot);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
    }

    /// <summary>
    /// 上传 dirty rect 子区。实现使用单张视口纹理与 PBO，不创建 per-chunk texture。
    /// </summary>
    /// <param name="texture">目标世界纹理。</param>
    /// <param name="buffer">源 render buffer。</param>
    /// <param name="rects">待上传矩形。</param>
    public void UploadDirtyRects(WorldTexture texture, RenderBuffer buffer, ReadOnlySpan<PixelUploadRect> rects)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSameSize(texture, buffer);
        if (rects.IsEmpty)
        {
            return;
        }

        foreach (PixelUploadRect rect in rects)
        {
            buffer.ValidateRect(rect);
        }

        PboSlot slot = CopyBufferToNextPbo(buffer);
        slot.Buffer.Bind();
        texture.Bind();
        _gl.PixelStore(GLEnum.UnpackRowLength, buffer.Width);

        foreach (PixelUploadRect rect in rects)
        {
            _gl.PixelStore(GLEnum.UnpackSkipPixels, rect.X);
            _gl.PixelStore(GLEnum.UnpackSkipRows, rect.Y);
            _gl.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                rect.X,
                rect.Y,
                (uint)rect.Width,
                (uint)rect.Height,
                PixelFormat.Bgra,
                PixelType.UnsignedInt8888Rev,
                null);
        }

        InsertFence(slot);
        _gl.PixelStore(GLEnum.UnpackRowLength, 0);
        _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
        _gl.PixelStore(GLEnum.UnpackSkipRows, 0);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
    }

    /// <summary>
    /// 确保 PBO 容量不小于指定字节数。
    /// </summary>
    /// <param name="requiredBytes">需要的最小容量。</param>
    public void EnsureCapacity(int requiredBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requiredBytes <= CapacityBytes)
        {
            return;
        }

        if (Mode == PboUploadMode.PersistentMapped)
        {
            foreach (PboSlot slot in _slots)
            {
                RecreatePersistentSlot(slot, requiredBytes);
            }
        }
        else
        {
            foreach (PboSlot slot in _slots)
            {
                slot.Buffer.Bind();
                slot.Buffer.Allocate((nuint)requiredBytes, BufferUsageARB.StreamDraw);
            }
        }

        CapacityBytes = requiredBytes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (PboSlot slot in _slots)
        {
            ReleasePersistentResources(slot);
            slot.Buffer.Dispose();
        }

        _disposed = true;
    }

    private PboSlot CopyBufferToNextPbo(RenderBuffer buffer)
    {
        EnsureCapacity(buffer.ByteLength);
        PboSlot slot = _slots[_index];
        _index = (_index + 1) & 1;

        if (Mode == PboUploadMode.PersistentMapped)
        {
            CopyToPersistentPbo(slot, buffer);
            return slot;
        }

        slot.Buffer.Bind();
        slot.Buffer.Allocate((nuint)buffer.ByteLength, BufferUsageARB.StreamDraw);
        void* destination = slot.Buffer.Map(
            0,
            (nuint)buffer.ByteLength,
            MapBufferAccessMask.WriteBit |
            MapBufferAccessMask.InvalidateBufferBit |
            MapBufferAccessMask.UnsynchronizedBit);
        fixed (uint* source = buffer.Pixels)
        {
            System.Buffer.MemoryCopy(source, destination, buffer.ByteLength, buffer.ByteLength);
        }

        _ = slot.Buffer.Unmap();
        return slot;
    }

    private void CopyToPersistentPbo(PboSlot slot, RenderBuffer buffer)
    {
        WaitAndDeleteFence(slot);
        fixed (uint* source = buffer.Pixels)
        {
            System.Buffer.MemoryCopy(source, slot.PersistentPointer, CapacityBytes, buffer.ByteLength);
        }
    }

    private static void ValidateSameSize(WorldTexture texture, RenderBuffer buffer)
    {
        if (texture.Width != buffer.Width || texture.Height != buffer.Height)
        {
            throw new ArgumentException("世界纹理与 render buffer 尺寸必须一致。", nameof(buffer));
        }
    }

    private static PboUploadMode SelectActualMode(GlCapabilities capabilities, PboUploadMode preferredMode)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return preferredMode == PboUploadMode.PersistentMapped && capabilities.HasBufferStorage
            ? PboUploadMode.PersistentMapped
            : PboUploadMode.OrphanMap;
    }

    private void RecreatePersistentSlot(PboSlot slot, int requiredBytes)
    {
        ReleasePersistentResources(slot);
        slot.Buffer.Dispose();
        slot.Buffer = new GlBuffer(_gl, BufferTargetARB.PixelUnpackBuffer);
        slot.Buffer.Bind();
        slot.Buffer.AllocateImmutable(
            (nuint)requiredBytes,
            BufferStorageMask.MapWriteBit |
            BufferStorageMask.MapPersistentBit |
            BufferStorageMask.MapCoherentBit);
        slot.PersistentPointer = slot.Buffer.Map(
            0,
            (nuint)requiredBytes,
            MapBufferAccessMask.WriteBit |
            MapBufferAccessMask.PersistentBit |
            MapBufferAccessMask.CoherentBit);
        if (slot.PersistentPointer is null)
        {
            throw new InvalidOperationException("无法映射 persistent PBO。");
        }
    }

    private void ReleasePersistentResources(PboSlot slot)
    {
        if (slot.Fence != IntPtr.Zero)
        {
            _gl.DeleteSync(slot.Fence);
            slot.Fence = IntPtr.Zero;
        }

        if (slot.PersistentPointer is not null)
        {
            slot.Buffer.Bind();
            _ = slot.Buffer.Unmap();
            slot.PersistentPointer = null;
        }
    }

    private void WaitAndDeleteFence(PboSlot slot)
    {
        if (slot.Fence == IntPtr.Zero)
        {
            return;
        }

        while (true)
        {
            GLEnum status = _gl.ClientWaitSync(slot.Fence, SyncObjectMask.Bit, FenceWaitNanoseconds);
            if (status is GLEnum.AlreadySignaled or GLEnum.ConditionSatisfied)
            {
                break;
            }

            if (status == GLEnum.WaitFailed)
            {
                throw new InvalidOperationException("等待 persistent PBO fence 失败。");
            }

            _ = Thread.Yield();
        }

        _gl.DeleteSync(slot.Fence);
        slot.Fence = IntPtr.Zero;
    }

    private void InsertFence(PboSlot slot)
    {
        if (Mode != PboUploadMode.PersistentMapped)
        {
            return;
        }

        slot.Fence = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
    }

    private sealed class PboSlot(GL gl) : IDisposable
    {
        public GlBuffer Buffer { get; set; } = new(gl, BufferTargetARB.PixelUnpackBuffer);

        public void* PersistentPointer { get; set; }

        public IntPtr Fence { get; set; }

        public void Dispose()
        {
            Buffer.Dispose();
        }
    }
}
