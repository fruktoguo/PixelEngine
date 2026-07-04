using BenchmarkDotNet.Attributes;
using PixelEngine.Core;
using PixelEngine.Serialization;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 序列化相位 11 字节准备稳态零分配基准。
/// </summary>
[MemoryDiagnoser]
public class SerializationAllocationBenchmarks
{
    private const int WriterCapacity = 64 * 1024;

    private readonly ushort[] _material = new ushort[EngineConstants.ChunkArea];
    private readonly byte[] _flags = new byte[EngineConstants.ChunkArea];
    private readonly byte[] _lifetime = new byte[EngineConstants.ChunkArea];
    private readonly byte[] _damage = new byte[EngineConstants.ChunkArea];
    private readonly Half[] _temperature = new Half[ChunkSnapshot.TemperatureCellCount];
    private readonly ChunkCodec _codec = new();
    private readonly PooledByteBufferWriter _outputWriter = new(WriterCapacity);
    private readonly PooledByteBufferWriter _payloadWriter = new(WriterCapacity);

    /// <summary>
    /// 预热 ArrayPool 与 LZ4 路径，避免把首次租用成本计入稳态。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        for (int i = 0; i < _material.Length; i++)
        {
            _material[i] = (ushort)(i & 3);
            _flags[i] = (i & 7) == 0 ? CellFlags.Burning : (byte)0;
            _lifetime[i] = (byte)(i & 31);
            _damage[i] = (byte)(i & 15);
        }

        for (int i = 0; i < _temperature.Length; i++)
        {
            _temperature[i] = (Half)(i * 0.25f);
        }

        EncodeChunkToPooledWriters();
    }

    /// <summary>
    /// chunk snapshot → RLE payload → LZ4 block 的稳态字节准备路径。
    /// </summary>
    [Benchmark]
    public void EncodeChunkToPooledWriters()
    {
        _outputWriter.Clear();
        _payloadWriter.Clear();
        ChunkSnapshot snapshot = new(
            new ChunkCoord(7, -3),
            _material,
            _flags,
            _lifetime,
            _damage,
            _temperature);
        _codec.Encode(snapshot, _outputWriter, _payloadWriter);
    }
}
