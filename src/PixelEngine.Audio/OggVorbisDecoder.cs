using NVorbis;

namespace PixelEngine.Audio;

/// <summary>
/// 纯托管 Ogg Vorbis 解码器，输出 PCM16 mono/stereo，避免新增 native 依赖。
/// </summary>
public sealed class OggVorbisDecoder : IAudioDecoder
{
    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedAudioData data)
    {
        data = default!;
        if (bytes.Length < 4 || bytes[0] != (byte)'O' || bytes[1] != (byte)'g' || bytes[2] != (byte)'g' || bytes[3] != (byte)'S')
        {
            return false;
        }

        try
        {
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            using VorbisReader reader = new(stream, false);
            if (reader.Channels is not 1 and not 2 || reader.SampleRate <= 0)
            {
                return false;
            }

            AudioSampleFormat format = reader.Channels == 1 ? AudioSampleFormat.Mono16 : AudioSampleFormat.Stereo16;
            float[] sampleBuffer = new float[4096 * reader.Channels];
            List<byte> pcm = new(capacity: 16 * 1024);
            int read;
            while ((read = reader.ReadSamples(sampleBuffer, 0, sampleBuffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    short sample = FloatToPcm16(sampleBuffer[i]);
                    pcm.Add((byte)sample);
                    pcm.Add((byte)(sample >> 8));
                }
            }

            if (pcm.Count == 0)
            {
                return false;
            }

            byte[] decodedPcm = [.. pcm];
            data = new DecodedAudioData(decodedPcm, format, reader.SampleRate, (short)reader.Channels, 16);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            return false;
        }
    }

    private static short FloatToPcm16(float sample)
    {
        float clamped = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
        return clamped >= 0f
            ? (short)MathF.Round(clamped * short.MaxValue)
            : (short)MathF.Round(clamped * -short.MinValue);
    }
}
