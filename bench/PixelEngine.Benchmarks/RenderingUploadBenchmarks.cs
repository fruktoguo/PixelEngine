using BenchmarkDotNet.Attributes;
using PixelEngine.Rendering;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 纹理上传 CPU 侧 copy/dirty-rect/persistent-mapped 路径基准。
/// GL DMA 路径由显式 smoke 覆盖，本基准用于校验 plan/14 §4.6 与架构 §9.2 带宽量级。
/// </summary>
[MemoryDiagnoser]
public class TextureUploadBenchmark
{
    private uint[] _source = [];
    private uint[] _staging = [];
    private uint[] _persistentMapped = [];
    private PixelUploadRect[] _dirtyRects = [];
    private int _width;
    private int _height;

    /// <summary>
    /// 视口配置。
    /// </summary>
    [Params("1280x720", "1920x1080")]
    public string Viewport { get; set; } = "1920x1080";

    /// <summary>
    /// 准备 BGRA8 源 buffer 与模拟 PBO staging buffer。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (_width, _height) = ParseViewport(Viewport);
        int length = checked(_width * _height);
        _source = GC.AllocateArray<uint>(length, pinned: true);
        _staging = GC.AllocateArray<uint>(length, pinned: true);
        _persistentMapped = GC.AllocateArray<uint>(length, pinned: true);
        for (int i = 0; i < _source.Length; i++)
        {
            _source[i] = 0xFF000000u | (uint)i;
        }

        _dirtyRects =
        [
            new PixelUploadRect(0, 0, _width / 4, _height / 4),
            new PixelUploadRect(_width / 2, 0, _width / 4, _height / 4),
            new PixelUploadRect(0, _height / 2, _width / 4, _height / 4),
            new PixelUploadRect(_width / 2, _height / 2, _width / 4, _height / 4),
        ];
    }

    /// <summary>
    /// 模拟默认全帧上传前的 CPU memcpy：整张 BGRA8 render buffer 复制到 PBO staging。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void FullFramePboCopy()
    {
        _source.AsSpan().CopyTo(_staging);
    }

    /// <summary>
    /// 模拟 dirty-rect 上传前的 CPU 子区 copy：逐脏矩形逐行复制。
    /// </summary>
    [Benchmark]
    public void DirtyRectSubUploadCopy()
    {
        for (int i = 0; i < _dirtyRects.Length; i++)
        {
            PixelUploadRect rect = _dirtyRects[i];
            for (int y = 0; y < rect.Height; y++)
            {
                int offset = ((rect.Y + y) * _width) + rect.X;
                _source.AsSpan(offset, rect.Width).CopyTo(_staging.AsSpan(offset, rect.Width));
            }
        }
    }

    /// <summary>
    /// 模拟 GL 4.4 persistent-mapped 快车道：render 阶段直接写入持久映射目标，无额外 PBO staging copy。
    /// </summary>
    [Benchmark]
    public void PersistentMappedDirectWrite()
    {
        Span<uint> destination = _persistentMapped;
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = 0xFF000000u | (uint)i;
        }
    }

    /// <summary>
    /// 当前视口全帧字节数，用于从 benchmark 结果换算 MB/s。
    /// </summary>
    public int FullFrameBytes => checked(_width * _height * sizeof(uint));

    /// <summary>
    /// 当前 dirty-rect 子上传字节数，用于从 benchmark 结果换算 MB/s。
    /// </summary>
    public int DirtyRectBytes
    {
        get
        {
            int pixels = 0;
            for (int i = 0; i < _dirtyRects.Length; i++)
            {
                PixelUploadRect rect = _dirtyRects[i];
                pixels = checked(pixels + (rect.Width * rect.Height));
            }

            return checked(pixels * sizeof(uint));
        }
    }

    private static (int Width, int Height) ParseViewport(string viewport)
    {
        int separator = viewport.IndexOf('x', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException("Viewport 必须形如 1920x1080。", nameof(viewport));
        }

        int width = int.Parse(viewport.AsSpan(0, separator));
        int height = int.Parse(viewport.AsSpan(separator + 1));
        return (width, height);
    }
}

/// <summary>
/// 旧入口保留给已有 benchmark filter/脚本；实际实现见 <see cref="TextureUploadBenchmark"/>。
/// </summary>
public class RenderingUploadBenchmarks : TextureUploadBenchmark
{
}
