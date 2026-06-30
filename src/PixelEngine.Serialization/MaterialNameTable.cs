using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PixelEngine.Serialization;

/// <summary>
/// 存档中的 saved material id 到稳定 name 映射表。
/// </summary>
public sealed class MaterialNameTable
{
    private readonly (ushort Id, string Name)[] _entries;

    /// <summary>
    /// 创建材质 name 表。
    /// </summary>
    public MaterialNameTable(ReadOnlySpan<(ushort Id, string Name)> entries)
    {
        _entries = entries.ToArray();
        Array.Sort(_entries, static (left, right) => left.Id.CompareTo(right.Id));
        Validate(_entries);
    }

    /// <summary>
    /// 条目数量。
    /// </summary>
    public int Count => _entries.Length;

    /// <summary>
    /// 最大 saved id；空表返回 -1。
    /// </summary>
    public int MaxSavedId => _entries.Length == 0 ? -1 : _entries[^1].Id;

    /// <summary>
    /// 枚举所有条目。
    /// </summary>
    public ReadOnlySpan<(ushort Id, string Name)> Entries => _entries;

    /// <summary>
    /// 写入紧凑二进制材质 name 表。
    /// </summary>
    public void Write(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Span<byte> countSpan = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(countSpan[..sizeof(int)], _entries.Length);
        writer.Advance(sizeof(int));
        for (int i = 0; i < _entries.Length; i++)
        {
            WriteEntry(writer, _entries[i].Id, _entries[i].Name);
        }
    }

    /// <summary>
    /// 读取紧凑二进制材质 name 表。
    /// </summary>
    public static MaterialNameTable Read(ReadOnlySpan<byte> source, out int bytesConsumed)
    {
        bytesConsumed = 0;
        if (source.Length < sizeof(int))
        {
            throw new InvalidDataException("MaterialNameTable 头部不完整。");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(source[..sizeof(int)]);
        if (count < 0)
        {
            throw new InvalidDataException("MaterialNameTable 条目数不能为负。");
        }

        bytesConsumed = sizeof(int);
        (ushort Id, string Name)[] entries = new (ushort Id, string Name)[count];
        for (int i = 0; i < count; i++)
        {
            entries[i] = ReadEntry(source[bytesConsumed..], out int consumed);
            bytesConsumed += consumed;
        }

        return new MaterialNameTable(entries);
    }

    private static void WriteEntry(IBufferWriter<byte> writer, ushort id, string name)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name);
        if (utf8.Length > ushort.MaxValue)
        {
            throw new InvalidDataException($"材质 name 过长：{name}。");
        }

        Span<byte> header = writer.GetSpan(sizeof(ushort) + sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(header[..sizeof(ushort)], id);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(sizeof(ushort), sizeof(ushort)), (ushort)utf8.Length);
        writer.Advance(sizeof(ushort) + sizeof(ushort));

        Span<byte> payload = writer.GetSpan(utf8.Length);
        utf8.CopyTo(payload);
        writer.Advance(utf8.Length);
    }

    private static (ushort Id, string Name) ReadEntry(ReadOnlySpan<byte> source, out int bytesConsumed)
    {
        if (source.Length < sizeof(ushort) + sizeof(ushort))
        {
            throw new InvalidDataException("MaterialNameTable 条目头部不完整。");
        }

        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(source[..sizeof(ushort)]);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(sizeof(ushort), sizeof(ushort)));
        if (source.Length < sizeof(ushort) + sizeof(ushort) + length)
        {
            throw new InvalidDataException("MaterialNameTable 条目 name 字节不完整。");
        }

        string name = Encoding.UTF8.GetString(source.Slice(sizeof(ushort) + sizeof(ushort), length));
        bytesConsumed = sizeof(ushort) + sizeof(ushort) + length;
        return (id, name);
    }

    private static void Validate(ReadOnlySpan<(ushort Id, string Name)> entries)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        ushort previous = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(entries[i].Name))
            {
                throw new ArgumentException("MaterialNameTable 不能包含空 name。");
            }

            if (i != 0 && entries[i].Id <= previous)
            {
                throw new ArgumentException("MaterialNameTable 包含重复或逆序 id。");
            }

            if (!names.Add(entries[i].Name))
            {
                throw new ArgumentException($"MaterialNameTable 包含重复 name：{entries[i].Name}。");
            }

            previous = entries[i].Id;
        }
    }
}
