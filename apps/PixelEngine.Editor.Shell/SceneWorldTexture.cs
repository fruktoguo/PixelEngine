using PixelEngine.Core.Threading;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using Silk.NET.OpenGL;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View 独立的权威 cell 纹理；CPU 颜色生成与运行时复用同一 RenderBufferBuilder。
/// </summary>
internal interface IAuthoringWorldTexture : IDisposable
{
    long Revision { get; }

    SceneWorldTextureSnapshot GetTexture(SceneAuthoringBounds requestedBounds);

    void Invalidate();
}

/// <summary>
/// Scene View 独立的权威 cell 纹理；CPU 颜色生成与运行时复用同一 RenderBufferBuilder。
/// </summary>
internal sealed class SceneWorldTexture : IAuthoringWorldTexture
{
    private readonly IChunkSource _chunks;
    private readonly MaterialTable _materials;
    private readonly TemperatureField _temperature;
    private readonly RenderBufferBuilder _builder;
    private readonly RenderBuffer _buffer = new(1, 1);
    private readonly RenderAuxBuffers _aux = new(1, 1);
    private readonly WorldTexture _texture;
    private readonly PboUploader _uploader;
    private readonly int _maxTextureSize;
    private SceneAuthoringBounds _textureBounds = new(0f, 0f, 1f, 1f);
    private bool _dirty = true;
    private bool _disposed;

    public SceneWorldTexture(
        GL gl,
        IChunkSource chunks,
        MaterialTable materials,
        TemperatureField temperature,
        JobSystem? jobs = null)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
        _builder = new RenderBufferBuilder(jobs);
        gl.GetInteger(GLEnum.MaxTextureSize, out _maxTextureSize);
        if (_maxTextureSize <= 0)
        {
            throw new InvalidOperationException("OpenGL 未返回有效的最大纹理尺寸。");
        }

        _texture = new WorldTexture(gl, 1, 1);
        _uploader = new PboUploader(gl, sizeof(uint));
    }

    public long Revision { get; private set; }

    public SceneWorldTextureSnapshot GetTexture(SceneAuthoringBounds requestedBounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int originX = (int)MathF.Floor(requestedBounds.X);
        int originY = (int)MathF.Floor(requestedBounds.Y);
        int width = Math.Max(1, checked((int)MathF.Ceiling(requestedBounds.Right) - originX));
        int height = Math.Max(1, checked((int)MathF.Ceiling(requestedBounds.Bottom) - originY));
        if (width > _maxTextureSize || height > _maxTextureSize)
        {
            throw new InvalidOperationException(
                $"authoring world 纹理 {width}x{height} 超出当前 GPU 上限 {_maxTextureSize}x{_maxTextureSize}。");
        }

        SceneAuthoringBounds normalizedBounds = new(originX, originY, width, height);
        if (_texture.Width != width || _texture.Height != height)
        {
            _buffer.Resize(width, height);
            _aux.Resize(width, height);
            _texture.Resize(width, height);
            _textureBounds = normalizedBounds;
            _dirty = true;
        }
        else if (_textureBounds != normalizedBounds)
        {
            _textureBounds = normalizedBounds;
            _dirty = true;
        }

        if (_dirty)
        {
            CameraState camera = CameraState.OneToOne(originX, originY, width, height);
            RenderFrameContext context = new(
                _chunks,
                _materials,
                _temperature,
                camera,
                simStepped: true,
                forceRebuild: true);
            _builder.Build(in context, _buffer, _aux);
            _uploader.UploadFull(_texture, _buffer);
            Revision++;
            _dirty = false;
        }

        return new SceneWorldTextureSnapshot(
            new RenderViewportTexture(_texture.Handle, _texture.Width, _texture.Height, Revision),
            _textureBounds);
    }

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _builder.InvalidateWorldContent();
        _dirty = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _uploader.Dispose();
        _texture.Dispose();
        _disposed = true;
    }
}

internal readonly record struct SceneWorldTextureSnapshot(
    RenderViewportTexture Texture,
    SceneAuthoringBounds Bounds);
