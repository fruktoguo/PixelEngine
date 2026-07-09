using System.Buffers;
using System.Buffers.Binary;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// 基于 32x32 chunk region 文件的随机访问 chunk blob 存储。
/// </summary>
public sealed class RegionFileStore : IChunkStore
{
    private const int RegionSizeChunks = 32;
    private const int RegionChunkCount = RegionSizeChunks * RegionSizeChunks;
    private const int IndexEntrySize = sizeof(long) + sizeof(int);
    private const int IndexBytes = RegionChunkCount * IndexEntrySize;

    private readonly string _regionsDirectory;

    /// <summary>
    /// 创建指向世界根目录的 region 文件存储。
    /// </summary>
    /// <param name="rootPath">世界根目录；region 文件位于其 <c>regions</c> 子目录。</param>
    public RegionFileStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _regionsDirectory = Path.Combine(rootPath, "regions");
    }

    /// <summary>
    /// 从 region 文件读取指定 chunk 的 blob；缺失时返回 <see langword="false" />。
    /// </summary>
    public bool TryRead(ChunkCoord coord, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        string path = RegionPath(coord);
        if (!File.Exists(path))
        {
            return false;
        }

        int localIndex = LocalIndex(coord);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // region 文件头为 32x32 固定索引表，按 localIndex 随机定位 chunk blob。
        RegionIndexEntry entry = ReadIndexEntry(stream, localIndex);
        if (!entry.Exists)
        {
            return false;
        }

        ValidatePayloadBounds(entry, stream.Length, path);
        Span<byte> span = destination.GetSpan(entry.Length)[..entry.Length];
        stream.Position = entry.Offset;
        stream.ReadExactly(span);
        destination.Advance(entry.Length);
        return true;
    }

    /// <summary>
    /// 将指定 chunk blob 原子写入所属 region 文件。
    /// </summary>
    public void Write(ChunkCoord coord, ReadOnlySpan<byte> blob)
    {
        MutateRegion(coord, blob, delete: false);
    }

    /// <summary>
    /// 判断指定 chunk blob 是否已存在且 region 索引有效。
    /// </summary>
    public bool Exists(ChunkCoord coord)
    {
        string path = RegionPath(coord);
        if (!File.Exists(path))
        {
            return false;
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        RegionIndexEntry entry = ReadIndexEntry(stream, LocalIndex(coord));
        if (!entry.Exists)
        {
            return false;
        }

        ValidatePayloadBounds(entry, stream.Length, path);
        return true;
    }

    /// <summary>
    /// 删除指定 chunk blob；region 文件缺失时保持幂等。
    /// </summary>
    public void Delete(ChunkCoord coord)
    {
        string path = RegionPath(coord);
        if (!File.Exists(path))
        {
            return;
        }

        MutateRegion(coord, [], delete: true);
    }

    private void MutateRegion(ChunkCoord coord, ReadOnlySpan<byte> blob, bool delete)
    {
        _ = Directory.CreateDirectory(_regionsDirectory);
        string path = RegionPath(coord);
        string tempPath = Path.Combine(_regionsDirectory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            // 写路径：复制旧 region → 追加/删 blob → 更新索引 → 原子替换，崩溃不致损坏主文件。
            using (FileStream temp = new(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(path))
                {
                    using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (source.Length < IndexBytes)
                    {
                        throw new InvalidDataException($"Region 文件索引区不完整：{path}");
                    }

                    source.CopyTo(temp);
                }
                else
                {
                    temp.SetLength(IndexBytes);
                }

                RegionIndexEntry entry = default;
                if (!delete)
                {
                    entry = AppendBlob(temp, blob);
                }

                WriteIndexEntry(temp, LocalIndex(coord), entry);
                temp.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static RegionIndexEntry AppendBlob(FileStream stream, ReadOnlySpan<byte> blob)
    {
        // region 采用追加写：新 blob 落在文件尾，索引项指向 offset/length。
        long offset = Math.Max(stream.Length, IndexBytes);
        stream.Position = offset;
        stream.Write(blob);
        return new RegionIndexEntry(offset, blob.Length);
    }

    private static RegionIndexEntry ReadIndexEntry(FileStream stream, int localIndex)
    {
        if (stream.Length < IndexBytes)
        {
            throw new InvalidDataException("Region 文件索引区不完整。");
        }

        Span<byte> entryBytes = stackalloc byte[IndexEntrySize];
        stream.Position = localIndex * IndexEntrySize;
        stream.ReadExactly(entryBytes);

        long offset = BinaryPrimitives.ReadInt64LittleEndian(entryBytes[..sizeof(long)]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[sizeof(long)..]);
        return offset == 0 && length == 0
            ? default
            : offset > 0 && length >= 0
            ? new RegionIndexEntry(offset, length)
            : throw new InvalidDataException("Region 文件索引项损坏。");
    }

    private static void WriteIndexEntry(FileStream stream, int localIndex, RegionIndexEntry entry)
    {
        Span<byte> entryBytes = stackalloc byte[IndexEntrySize];
        BinaryPrimitives.WriteInt64LittleEndian(entryBytes[..sizeof(long)], entry.Offset);
        BinaryPrimitives.WriteInt32LittleEndian(entryBytes[sizeof(long)..], entry.Length);
        stream.Position = localIndex * IndexEntrySize;
        stream.Write(entryBytes);
    }

    private static void ValidatePayloadBounds(RegionIndexEntry entry, long fileLength, string path)
    {
        if (entry.Offset < IndexBytes || entry.Offset > fileLength || entry.Length < 0)
        {
            throw new InvalidDataException($"Region 文件索引项越界：{path}");
        }

        long end = entry.Offset + entry.Length;
        if (end < entry.Offset || end > fileLength)
        {
            throw new InvalidDataException($"Region 文件 blob 长度越界：{path}");
        }
    }

    private string RegionPath(ChunkCoord coord)
    {
        int rx = FloorDiv(coord.X, RegionSizeChunks);
        int ry = FloorDiv(coord.Y, RegionSizeChunks);
        return Path.Combine(_regionsDirectory, $"r.{rx}.{ry}.rgn");
    }

    private static int LocalIndex(ChunkCoord coord)
    {
        int rx = FloorDiv(coord.X, RegionSizeChunks);
        int ry = FloorDiv(coord.Y, RegionSizeChunks);
        int lx = coord.X - (rx * RegionSizeChunks);
        int ly = coord.Y - (ry * RegionSizeChunks);
        return (ly * RegionSizeChunks) + lx;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct RegionIndexEntry(long Offset, int Length)
    {
        internal bool Exists => Offset != 0 || Length != 0;
    }
}
