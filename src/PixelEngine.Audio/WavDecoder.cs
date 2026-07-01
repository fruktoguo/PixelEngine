using System.Buffers.Binary;

namespace PixelEngine.Audio;

/// <summary>
/// 纯托管 WAV/PCM 解码器，支持 PCM 8/16-bit mono/stereo。
/// </summary>
public sealed class WavDecoder : IAudioDecoder
{
    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedAudioData data)
    {
        data = default!;
        if (bytes.Length < 12 || !Matches(bytes, 0, "RIFF") || !Matches(bytes, 8, "WAVE"))
        {
            return false;
        }

        bool hasFormat = false;
        bool hasData = false;
        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        ReadOnlySpan<byte> pcm = default;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> chunkId = bytes[offset..(offset + 4)];
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + 4)..(offset + 8)]);
            if (chunkSize < 0 || offset + 8 + chunkSize > bytes.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> chunk = bytes[(offset + 8)..(offset + 8 + chunkSize)];
            if (Matches(chunkId, "fmt "))
            {
                if (chunk.Length < 16)
                {
                    return false;
                }

                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(chunk[..2]);
                channels = BinaryPrimitives.ReadInt16LittleEndian(chunk[2..4]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..8]);
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(chunk[14..16]);
                hasFormat = true;
            }
            else if (Matches(chunkId, "data"))
            {
                pcm = chunk;
                hasData = true;
            }

            offset += 8 + chunkSize + (chunkSize & 1);
        }

        if (!hasFormat || !hasData || audioFormat != 1 || sampleRate <= 0)
        {
            return false;
        }

        if (!TryGetFormat(channels, bitsPerSample, out AudioSampleFormat format))
        {
            return false;
        }

        byte[] copy = pcm.ToArray();
        data = new DecodedAudioData(copy, format, sampleRate, channels, bitsPerSample);
        return true;
    }

    private static bool TryGetFormat(short channels, short bitsPerSample, out AudioSampleFormat format)
    {
        format = default;
        if (channels == 1 && bitsPerSample == 8)
        {
            format = AudioSampleFormat.Mono8;
            return true;
        }

        if (channels == 1 && bitsPerSample == 16)
        {
            format = AudioSampleFormat.Mono16;
            return true;
        }

        if (channels == 2 && bitsPerSample == 8)
        {
            format = AudioSampleFormat.Stereo8;
            return true;
        }

        if (channels == 2 && bitsPerSample == 16)
        {
            format = AudioSampleFormat.Stereo16;
            return true;
        }

        return false;
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, int offset, string value)
    {
        return bytes.Length >= offset + value.Length && Matches(bytes[offset..(offset + value.Length)], value);
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, string value)
    {
        if (bytes.Length != value.Length)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (bytes[i] != value[i])
            {
                return false;
            }
        }

        return true;
    }
}
