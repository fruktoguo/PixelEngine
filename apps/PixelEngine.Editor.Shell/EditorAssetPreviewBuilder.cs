using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 为 Project Window 当前选择构建有界、类型化的详细预览。
/// </summary>
internal static class EditorAssetPreviewBuilder
{
    internal const int MaximumTextPreviewCharacters = 12_000;

    /// <summary>
    /// 从已经过 rooted-path 校验的物理文件构建详细预览。
    /// </summary>
    internal static AssetBrowserDetailedPreview Build(in AssetBrowserItem item, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        List<AssetBrowserPreviewProperty> properties = [];
        string summary = item.PreviewSummary ?? item.Descriptor?.Purpose ?? "暂无摘要";
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        AssetBrowserPreviewContentKind contentKind = ResolveContentKind(in item, extension);
        string? textContent = null;
        string? diagnostic = null;

        try
        {
            switch (contentKind)
            {
                case AssetBrowserPreviewContentKind.Image:
                    properties.Add(new AssetBrowserPreviewProperty("格式", FormatExtension(extension)));
                    if (TryReadImageDimensions(fullPath, extension, out int width, out int height))
                    {
                        properties.Add(new AssetBrowserPreviewProperty(
                            "尺寸",
                            $"{width.ToString(CultureInfo.InvariantCulture)} × {height.ToString(CultureInfo.InvariantCulture)} px"));
                    }

                    break;
                case AssetBrowserPreviewContentKind.Audio:
                    properties.Add(new AssetBrowserPreviewProperty("格式", FormatExtension(extension)));
                    if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) &&
                        TryReadWaveMetadata(fullPath, out WavePreviewMetadata wave))
                    {
                        properties.Add(new AssetBrowserPreviewProperty("时长", FormatDuration(wave.DurationSeconds)));
                        properties.Add(new AssetBrowserPreviewProperty(
                            "采样率",
                            $"{wave.SampleRate.ToString("N0", CultureInfo.InvariantCulture)} Hz"));
                        properties.Add(new AssetBrowserPreviewProperty("声道", FormatChannels(wave.Channels)));
                        properties.Add(new AssetBrowserPreviewProperty(
                            "位深",
                            $"{wave.BitsPerSample.ToString(CultureInfo.InvariantCulture)} bit"));
                        properties.Add(new AssetBrowserPreviewProperty("编码", FormatWaveEncoding(wave.AudioFormat)));
                    }

                    break;
                case AssetBrowserPreviewContentKind.Text:
                    properties.Add(new AssetBrowserPreviewProperty("格式", FormatExtension(extension)));
                    textContent = ReadTextExcerpt(fullPath, out bool truncated);
                    if (truncated)
                    {
                        diagnostic = $"预览已限制为前 {MaximumTextPreviewCharacters.ToString(CultureInfo.InvariantCulture)} 个字符。";
                    }

                    break;
                case AssetBrowserPreviewContentKind.Summary:
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        properties.Add(new AssetBrowserPreviewProperty("格式", FormatExtension(extension)));
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), contentKind, "未知资产预览内容形态。");
            }
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            diagnostic = $"详细预览不可用：{ex.Message}";
            textContent = null;
        }

        // Unity 风格预览优先展示当前资产最有辨识度的字段（尺寸、时长、格式），
        // 通用类型/路径/时间信息随后补充，避免紧凑 Project Window 把关键值挤出首屏。
        properties.AddRange(BuildCommonProperties(in item));

        return new AssetBrowserDetailedPreview(
            item.DisplayName,
            contentKind,
            summary,
            properties,
            textContent,
            diagnostic);
    }

    private static List<AssetBrowserPreviewProperty> BuildCommonProperties(in AssetBrowserItem item)
    {
        List<AssetBrowserPreviewProperty> properties =
        [
            new("类型", item.Descriptor?.TypeLabel ?? item.Kind.ToString()),
            new("路径", item.Path),
            new("大小", FormatSize(item.SizeBytes)),
            new("修改", item.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ];
        if (!string.IsNullOrWhiteSpace(item.Descriptor?.Purpose))
        {
            properties.Add(new AssetBrowserPreviewProperty("用途", item.Descriptor.Value.Purpose));
        }

        return properties;
    }

    private static AssetBrowserPreviewContentKind ResolveContentKind(
        in AssetBrowserItem item,
        string extension)
    {
        return item.Kind switch
        {
            AssetBrowserItemKind.Texture => AssetBrowserPreviewContentKind.Image,
            AssetBrowserItemKind.Audio => AssetBrowserPreviewContentKind.Audio,
            AssetBrowserItemKind.Material or
            AssetBrowserItemKind.Scene or
            AssetBrowserItemKind.Prefab or
            AssetBrowserItemKind.Script or
            AssetBrowserItemKind.UiScreen or
            AssetBrowserItemKind.Json => AssetBrowserPreviewContentKind.Text,
            AssetBrowserItemKind.Other when IsTextExtension(extension) => AssetBrowserPreviewContentKind.Text,
            AssetBrowserItemKind.Folder or AssetBrowserItemKind.Other => AssetBrowserPreviewContentKind.Summary,
            _ => AssetBrowserPreviewContentKind.Summary,
        };
    }

    private static bool IsTextExtension(string extension)
    {
        return extension is ".txt" or ".md" or ".ini" or ".xml" or ".css" or ".rml" or ".html" or ".xhtml";
    }

    private static string ReadTextExcerpt(string fullPath, out bool truncated)
    {
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        char[] buffer = new char[MaximumTextPreviewCharacters + 1];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        truncated = read > MaximumTextPreviewCharacters;
        int length = Math.Min(read, MaximumTextPreviewCharacters);
        string text = new string(buffer, 0, length).Replace("\r\n", "\n", StringComparison.Ordinal);
        return truncated ? text.TrimEnd() + "\n…" : text.TrimEnd();
    }

    private static bool TryReadImageDimensions(
        string fullPath,
        string extension,
        out int width,
        out int height)
    {
        Span<byte> header = stackalloc byte[26];
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: header.Length,
            FileOptions.SequentialScan);
        int read = stream.Read(header);
        if (extension == ".png" &&
            read >= 24 &&
            header[0] == 0x89 &&
            header[1..8].SequenceEqual("PNG\r\n\x1A\n"u8))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            return width > 0 && height > 0;
        }

        if (extension == ".bmp" && read >= 26 && header[..2].SequenceEqual("BM"u8))
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(header[18..22]);
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header[22..26]));
            return width > 0 && height > 0;
        }

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryReadWaveMetadata(string fullPath, out WavePreviewMetadata metadata)
    {
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        Span<byte> riff = stackalloc byte[12];
        if (!TryReadExactly(stream, riff) ||
            !riff[..4].SequenceEqual("RIFF"u8) ||
            !riff[8..12].SequenceEqual("WAVE"u8))
        {
            metadata = default;
            return false;
        }

        ushort audioFormat = 0;
        ushort channels = 0;
        int sampleRate = 0;
        int byteRate = 0;
        ushort bitsPerSample = 0;
        long dataBytes = 0;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];
        while (stream.Position + chunkHeader.Length <= stream.Length)
        {
            if (!TryReadExactly(stream, chunkHeader))
            {
                break;
            }

            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            long payloadStart = stream.Position;
            long payloadEnd = payloadStart + chunkSize;
            if (payloadEnd < payloadStart || payloadEnd > stream.Length)
            {
                break;
            }

            if (chunkHeader[..4].SequenceEqual("fmt "u8) && chunkSize >= format.Length)
            {
                if (!TryReadExactly(stream, format))
                {
                    break;
                }

                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(format[..2]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..4]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(format[4..8]);
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(format[8..12]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format[14..16]);
            }
            else if (chunkHeader[..4].SequenceEqual("data"u8))
            {
                dataBytes = chunkSize;
            }

            long paddedEnd = payloadEnd + (chunkSize & 1u);
            stream.Position = Math.Min(paddedEnd, stream.Length);
            if (audioFormat != 0 && dataBytes > 0)
            {
                break;
            }
        }

        if (audioFormat == 0 || channels == 0 || sampleRate <= 0 || byteRate <= 0 || bitsPerSample == 0 || dataBytes <= 0)
        {
            metadata = default;
            return false;
        }

        metadata = new WavePreviewMetadata(
            audioFormat,
            channels,
            sampleRate,
            bitsPerSample,
            dataBytes / (double)byteRate);
        return true;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static string FormatWaveEncoding(ushort audioFormat)
    {
        return audioFormat switch
        {
            1 => "PCM",
            3 => "IEEE Float",
            0xFFFE => "WAVE Extensible",
            _ => $"WAV codec {audioFormat.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatChannels(ushort channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            _ => $"{channels.ToString(CultureInfo.InvariantCulture)} channels",
        };
    }

    private static string FormatDuration(double seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return duration.TotalMinutes >= 1d
            ? duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)
            : $"{duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} s";
    }

    private static string FormatExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? "未知"
            : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }

        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int unit = -1;
        do
        {
            value /= 1024d;
            unit++;
        }
        while (value >= 1024d && unit < units.Length - 1);
        return $"{value.ToString(value >= 10d ? "0.0" : "0.00", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static bool IsPreviewFailure(Exception exception)
    {
        return exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException or
            InvalidDataException or
            NotSupportedException;
    }

    internal readonly record struct WavePreviewMetadata(
        ushort AudioFormat,
        ushort Channels,
        int SampleRate,
        ushort BitsPerSample,
        double DurationSeconds);
}
