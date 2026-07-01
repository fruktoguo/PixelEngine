using System.Buffers.Binary;
using System.Numerics;
using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioClipCacheTests
{
    [Fact]
    public void WavDecoderDecodesPcm16Mono()
    {
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 16, sampleRate: 22_050, [1, 0, 2, 0]);
        WavDecoder decoder = new();

        bool decoded = decoder.TryDecode(wav, out DecodedAudioData data);

        Assert.True(decoded);
        Assert.Equal(AudioSampleFormat.Mono16, data.Format);
        Assert.Equal(22_050, data.SampleRate);
        Assert.Equal(1, data.Channels);
        Assert.Equal(16, data.BitsPerSample);
        Assert.Equal([1, 0, 2, 0], data.Pcm);
    }

    [Fact]
    public void WavDecoderRejectsUnsupportedOrMalformedInput()
    {
        WavDecoder decoder = new();

        Assert.False(decoder.TryDecode([1, 2, 3], out _));
        Assert.False(decoder.TryDecode(CreateWav(channels: 1, bitsPerSample: 24, sampleRate: 44_100, [0, 0, 0]), out _));
    }

    [Fact]
    public async Task ClipCacheLoadsCachesAndDeletesBackendBuffer()
    {
        byte[] wav = CreateWav(channels: 2, bitsPerSample: 8, sampleRate: 44_100, [1, 2, 3, 4]);
        using NullAudioBackend backend = new();
        MemoryAssetStore assets = new(wav);
        AudioClipCache cache = new(backend, assets, new WavDecoder());

        AudioClip first = await cache.LoadAsync("sfx/hit.wav");
        AudioClip second = await cache.LoadAsync("sfx/hit.wav");

        Assert.Same(first, second);
        Assert.Equal(2, first.RefCount);
        Assert.Equal(1, cache.LoadedCount);
        Assert.Equal(1, backend.BufferCount);
        Assert.Equal(1, assets.LoadCount);
        Assert.True(cache.TryGetLoaded("sfx/hit.wav", out AudioClip? loaded));
        Assert.Same(first, loaded);

        cache.Unload(first);
        Assert.Equal(1, first.RefCount);
        cache.Unload(second);

        Assert.Equal(0, cache.LoadedCount);
        _ = Assert.Throws<ObjectDisposedException>(() => backend.UploadBuffer(first.Buffer.Handle, AudioSampleFormat.Stereo8, [1], 44_100));
    }

    [Fact]
    public async Task AudioSystemPlaysLoadedOneShotAndPublishesDiagnostics()
    {
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);
        using NullAudioBackend backend = new();
        MemoryAssetStore assets = new(wav);
        AudioClipCache cache = new(backend, assets, new WavDecoder());
        _ = await cache.LoadAsync("ui/click.wav");
        AudioSystem system = new();
        system.Initialize(new AudioSettings { MaxVoices = 2 }, backend);
        system.AttachClipCache(cache);

        bool played = system.TryPlayLoadedOneShot("ui/click.wav", new Vector2(32f, 0f), volume: 0.5f, pitch: 1.1f);

        Assert.True(played);
        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(1, system.Diagnostics.ActiveVoices);
        Assert.Equal(1, system.Diagnostics.LoadedClips);
        EngineCounters counters = new();
        system.PublishDiagnostics(counters);
        Assert.Equal(1, counters.AudioActiveVoices);
        Assert.Equal(1, counters.AudioLoadedClips);
        system.Shutdown();
    }

    [Fact]
    public void StreamPlayerQueuesAndReturnsProcessedBuffersOnWorker()
    {
        using NullAudioBackend backend = new();
        uint source = backend.CreateSource();
        uint buffer = backend.CreateBuffer();
        using AudioStreamPlayer stream = new(backend, source);

        stream.Enqueue(buffer);
        Assert.True(SpinWait.SpinUntil(() => stream.PendingCount == 0, TimeSpan.FromSeconds(1)));
        backend.MarkProcessedBuffers(source, 1);

        bool processed = SpinWait.SpinUntil(() => stream.TryDequeueProcessed(out uint returned) && returned == buffer, TimeSpan.FromSeconds(1));

        Assert.True(processed);
    }

    private static byte[] CreateWav(short channels, short bitsPerSample, int sampleRate, ReadOnlySpan<byte> pcm)
    {
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        byte[] wav = new byte[44 + pcm.Length];
        WriteAscii(wav, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        WriteAscii(wav, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            target[offset + i] = (byte)text[i];
        }
    }

    private sealed class MemoryAssetStore(byte[] bytes) : IAudioAssetStore
    {
        public int LoadCount { get; private set; }

        public ValueTask<byte[]> LoadBytesAsync(string assetId, CancellationToken cancellationToken = default)
        {
            _ = assetId;
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return ValueTask.FromResult(bytes);
        }
    }
}
