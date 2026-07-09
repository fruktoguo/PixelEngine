using System.Buffers.Binary;
using System.Numerics;
using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Audio.Tests;

/// <summary>
/// 音频片段缓存测试：加载、复用与驱逐。
/// </summary>
public sealed class AudioClipCacheTests
{
    /// <summary>
    /// 验证WAV 解码器解码 PCM16 单声道。
    /// </summary>
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

    /// <summary>
    /// 验证WAV 解码器拒绝不支持或畸形输入。
    /// </summary>
    [Fact]
    public void WavDecoderRejectsUnsupportedOrMalformedInput()
    {
        WavDecoder decoder = new();

        Assert.False(decoder.TryDecode([1, 2, 3], out _));
        Assert.False(decoder.TryDecode(CreateWav(channels: 1, bitsPerSample: 24, sampleRate: 44_100, [0, 0, 0]), out _));
    }

    /// <summary>
    /// 验证Ogg Vorbis 解码器解码单声道 Vorbis 为 PCM16。
    /// </summary>
    [Fact]
    public void OggVorbisDecoderDecodesMonoVorbisToPcm16()
    {
        OggVorbisDecoder decoder = new();
        byte[] ogg = Convert.FromBase64String(OggToneBase64Valid);

        bool decoded = decoder.TryDecode(ogg, out DecodedAudioData data);

        Assert.True(decoded);
        Assert.Equal(AudioSampleFormat.Mono16, data.Format);
        Assert.Equal(8_000, data.SampleRate);
        Assert.Equal(1, data.Channels);
        Assert.Equal(16, data.BitsPerSample);
        Assert.True(data.Pcm.Length > 0);
        Assert.Equal(0, data.Pcm.Length % 2);
    }

    /// <summary>
    /// 验证Ogg Vorbis 解码器拒绝非 Ogg 输入。
    /// </summary>
    [Fact]
    public void OggVorbisDecoderRejectsNonOggInput()
    {
        OggVorbisDecoder decoder = new();

        Assert.False(decoder.TryDecode([1, 2, 3, 4], out _));
        Assert.False(decoder.TryDecode(CreateWav(channels: 1, bitsPerSample: 16, sampleRate: 8_000, [0, 0]), out _));
    }

    /// <summary>
    /// 验证片段缓存加载并缓存And删除后端缓冲。
    /// </summary>
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

    /// <summary>
    /// 验证目录资源库加载相对路径音频And拒绝路径逃逸。
    /// </summary>
    [Fact]
    public async Task DirectoryAssetStoreLoadsRelativeAudioAndRejectsEscapes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-audio-assets-{Guid.NewGuid():N}");
        try
        {
            string nested = Path.Combine(root, "sfx");
            _ = Directory.CreateDirectory(nested);
            string path = Path.Combine(nested, "hit.wav");
            byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);
            await File.WriteAllBytesAsync(path, wav);
            DirectoryAudioAssetStore store = new(root);

            byte[] loaded = await store.LoadBytesAsync("sfx/hit.wav");

            Assert.Equal(wav, loaded);
            _ = Assert.Throws<ArgumentException>(() => store.LoadBytesAsync("../hit.wav").AsTask().GetAwaiter().GetResult());
            _ = Assert.Throws<ArgumentException>(() => store.LoadBytesAsync(path).AsTask().GetAwaiter().GetResult());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证音频系统播放已加载的一次性音效And发布诊断信息。
    /// </summary>
    [Fact]
    public async Task AudioSystemPlaysLoadedOneShotAndPublishesDiagnostics()
    {
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);
        using NullAudioBackend backend = new();
        MemoryAssetStore assets = new(wav);
        AudioClipCache cache = new(backend, assets, new WavDecoder());
        _ = await cache.LoadAsync("ui/click.wav");
        AudioSystem system = new();
        system.Initialize(new AudioSettings { MaxVoices = 2, SfxVolume = 0.25f, UiVolume = 0.5f }, backend);
        system.AttachClipCache(cache);

        bool played = system.TryPlayLoadedOneShot("ui/click.wav", new Vector2(32f, 0f), volume: 0.5f, pitch: 1.1f);
        bool playedUi = cache.TryGetLoaded("ui/click.wav", out AudioClip? clip) && clip is not null && system.PlayUi(clip, volume: 0.5f);

        Assert.True(played);
        Assert.True(playedUi);
        Assert.Equal(2, backend.PlayCalls);
        Assert.Equal(0.125f, backend.GetSourceGain(1), precision: 5);
        Assert.Equal(0.25f, backend.GetSourceGain(2), precision: 5);
        Assert.Equal(2, system.Diagnostics.ActiveVoices);
        Assert.Equal(1, system.Diagnostics.LoadedClips);
        EngineCounters counters = new();
        system.PublishDiagnostics(counters);
        Assert.Equal(2, counters.AudioActiveVoices);
        Assert.Equal(1, counters.AudioLoadedClips);
        system.Shutdown();
    }

    /// <summary>
    /// 验证音频系统关闭时释放自有片段缓存But保留借用的缓存。
    /// </summary>
    [Fact]
    public async Task AudioSystemShutdownDisposesOwnedClipCacheButLeavesBorrowedCacheAlive()
    {
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);

        using NullAudioBackend ownedBackend = new();
        AudioClipCache ownedCache = new(ownedBackend, new MemoryAssetStore(wav), new WavDecoder());
        _ = await ownedCache.LoadAsync("ui/owned.wav");
        using AudioSystem ownedSystem = new();
        ownedSystem.Initialize(new AudioSettings { MaxVoices = 1 }, ownedBackend);
        ownedSystem.AttachClipCache(ownedCache, takeOwnership: true);

        Assert.Equal(1, ownedBackend.LiveBufferCount);

        ownedSystem.Shutdown();

        Assert.Equal(0, ownedBackend.LiveObjectCount);
        _ = Assert.Throws<ObjectDisposedException>(() => ownedCache.TryGetLoaded("ui/owned.wav", out _));

        using NullAudioBackend borrowedBackend = new();
        AudioClipCache borrowedCache = new(borrowedBackend, new MemoryAssetStore(wav), new WavDecoder());
        _ = await borrowedCache.LoadAsync("ui/borrowed.wav");
        using AudioSystem borrowedSystem = new();
        borrowedSystem.Initialize(new AudioSettings { MaxVoices = 1 }, borrowedBackend);
        borrowedSystem.AttachClipCache(borrowedCache, takeOwnership: false);

        borrowedSystem.Shutdown();

        Assert.Equal(1, borrowedBackend.LiveBufferCount);
        Assert.True(borrowedCache.TryGetLoaded("ui/borrowed.wav", out AudioClip? borrowedClip));
        Assert.NotNull(borrowedClip);

        borrowedCache.Dispose();

        Assert.Equal(0, borrowedBackend.LiveObjectCount);
        _ = Assert.Throws<ObjectDisposedException>(() => borrowedCache.TryGetLoaded("ui/borrowed.wav", out _));
    }

    /// <summary>
    /// 验证Stream Player Queues And Returns Processed Buffers On Worker。
    /// </summary>
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

#pragma warning disable IDE0051
    private const string OggToneBase64 = "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";

#pragma warning restore IDE0051

    private const string OggToneBase64Valid = """
T2dnUwACAAAAAAAAAAAh/TsKAAAAADA/+u4BHgF2b3JiaXMAAAAAAUAfAAAAAAAAsDYAAAAAAACZAU9nZ1MAAAAAAAAAAAAA
If07CgEAAAAS7I6DCz7///////////+1A3ZvcmJpcwwAAABMYXZmNjAuMy4xMDABAAAAHgAAAGVuY29kZXI9TGF2YzYwLjMu
MTAwIGxpYnZvcmJpcwEFdm9yYmlzEkJDVgEAAAEADFIUISUZU0pjCJVSUikFHWNQW0cdY9Q5RiFkEFOISRmle08qlVhKyBFS
WClFHVNMU0mVUpYpRR1jFFNIIVPWMWWhcxRLhkkJJWxNrnQWS+iZY5YxRh1jzlpKnWPWMUUdY1JSSaFzGDpmJWQUOkbF6GJ8
MDqVokIovsfeUukthYpbir3XGlPrLYQYS2nBCGFz7bXV3EpqxRhjjDHGxeJTKILQkFUAAAEAAEAEAUJDVgEACgAAwlAMRVGA
0JBVAEAGAIAAFEVxFMdxHEeSJMsCQkNWAQBAAAACAAAojuEokiNJkmRZlmVZlqZ5lqi5qi/7ri7rru3qug6EhqwEAMgAABiG
IYfeScyQU5BJJilVzDkIofUOOeUUZNJSxphijFHOkFMMMQUxhtAphRDUTjmlDCIIQ0idZM4gSz3o4GLnOBAasiIAiAIAAIxB
jCHGkHMMSgYhco5JyCBEzjkpnZRMSiittJZJCS2V1iLnnJROSialtBZSy6SU1kIrBQAABDgAAARYCIWGrAgAogAAEIOQUkgp
xJRiTjGHlFKOKceQUsw5xZhyjDHoIFTMMcgchEgpxRhzTjnmIGQMKuYchAwyAQAAAQ4AAAEWQqEhKwKAOAEAgyRpmqVpomhp
mih6pqiqoiiqquV5pumZpqp6oqmqpqq6rqmqrmx5nml6pqiqnimqqqmqrmuqquuKqmrLpqvatumqtuzKsm67sqzbnqrKtqm6
sm6qrm27smzrrizbuuR5quqZput6pum6quvasuq6su2ZpuuKqivbpuvKsuvKtq3Ksq5rpum6oqvarqm6su3Krm27sqz7puvq
turKuq7Ksu7btq77sq0Lu+i6tq7Krq6rsqzrsi3rtmzbQsnzVNUzTdf1TNN1Vde1bdV1bVszTdc1XVeWRdV1ZdWVdV11ZVv3
TNN1TVeVZdNVZVmVZd12ZVeXRde1bVWWfV11ZV+Xbd33ZVnXfdN1dVuVZdtXZVn3ZV33hVm3fd1TVVs3XVfXTdfVfVvXfWG2
bd8XXVfXVdnWhVWWdd/WfWWYdZ0wuq6uq7bs66os676u68Yw67owrLpt/K6tC8Or68ax676u3L6Patu+8Oq2Mby6bhy7sBu/
7fvGsamqbZuuq+umK+u6bOu+b+u6cYyuq+uqLPu66sq+b+u68Ou+Lwyj6+q6Ksu6sNqyr8u6Lgy7rhvDatvC7tq6cMyyLgy3
7yvHrwtD1baF4dV1o6vbxm8Lw9I3dr4AAIABBwCAABPKQKEhKwKAOAEABiEIFWMQKsYghBBSCiGkVDEGIWMOSsYclBBKSSGU
0irGIGSOScgckxBKaKmU0EoopaVQSkuhlNZSai2m1FoMobQUSmmtlNJaaim21FJsFWMQMuekZI5JKKW0VkppKXNMSsagpA5C
KqWk0kpJrWXOScmgo9I5SKmk0lJJqbVQSmuhlNZKSrGl0kptrcUaSmktpNJaSam11FJtrbVaI8YgZIxByZyTUkpJqZTSWuac
lA46KpmDkkopqZWSUqyYk9JBKCWDjEpJpbWSSiuhlNZKSrGFUlprrdWYUks1lJJaSanFUEprrbUaUys1hVBSC6W0FkpprbVW
a2ottlBCa6GkFksqMbUWY22txRhKaa2kElspqcUWW42ttVhTSzWWkmJsrdXYSi051lprSi3W0lKMrbWYW0y5xVhrDSW0Fkpp
rZTSWkqtxdZaraGU1koqsZWSWmyt1dhajDWU0mIpKbWQSmyttVhbbDWmlmJssdVYUosxxlhzS7XVlFqLrbVYSys1xhhrbjXl
UgAAwIADAECACWWg0JCVAEAUAABgDGOMQWgUcsw5KY1SzjknJXMOQggpZc5BCCGlzjkIpbTUOQehlJRCKSmlFFsoJaXWWiwA
AKDAAQAgwAZNicUBCg1ZCQBEAQAgxijFGITGIKUYg9AYoxRjECqlGHMOQqUUY85ByBhzzkEpGWPOQSclhBBCKaWEEEIopZQC
AAAKHAAAAmzQlFgcoNCQFQFAFAAAYAxiDDGGIHRSOikRhExKJ6WREloLKWWWSoolxsxaia3E2EgJrYXWMmslxtJiRq3EWGIq
AADswAEA7MBCKDRkJQCQBwBAGKMUY845ZxBizDkIITQIMeYchBAqxpxzDkIIFWPOOQchhM455yCEEELnnHMQQgihgxBCCKWU
0kEIIYRSSukghBBCKaV0EEIIoZRSCgAAKnAAAAiwUWRzgpGgQkNWAgB5AACAMUo5JyWlRinGIKQUW6MUYxBSaq1iDEJKrcVY
MQYhpdZi7CCk1FqMtXYQUmotxlpDSq3FWGvOIaXWYqw119RajLXm3HtqLcZac865AADcBQcAsAMbRTYnGAkqNGQlAJAHAEAg
pBRjjDmHlGKMMeecQ0oxxphzzinGGHPOOecUY4w555xzjDHnnHPOOcaYc84555xzzjnnoIOQOeecc9BB6JxzzjkIIXTOOecc
hBAKAAAqcAAACLBRZHOCkaBCQ1YCAOEAAIAxlFJKKaWUUkqoo5RSSimllFICIaWUUkoppZRSSimllFJKKaWUUkoppZRSSiml
lFJKKaWUUkoppZRSSimllFJKKaWUUkoppZRSSimllFJKKaWUUkoppZRSSimllFJKKaWUUkoppZRSSimllFJKKaWUUkoppZRS
SimllFJKKZVSSimllFJKKaWUUkoppQAg3woHAP8HG2dYSTorHA0uNGQlABAOAAAYwxiEjDknJaWGMQildE5KSSU1jEEopXMS
Ukopg9BaaqWk0lJKGYSUYgshlZRaCqW0VmspqbWUUigpxRpLSqml1jLnJKSSWkuttpg5B6Wk1lpqrcUQQkqxtdZSa7F1UlJJ
rbXWWm0tpJRaay3G1mJsJaWWWmupxdZaTKm1FltLLcbWYkutxdhiizHGGgsA4G5wAIBIsHGGlaSzwtHgQkNWAgAhAQAEMko5
55yDEEIIIVKKMeeggxBCCCFESjHmnIMQQgghhIwx5yCEEEIIoZSQMeYchBBCCCGEUjrnIIRQSgmllFJK5xyEEEIIpZRSSgkh
hBBCKKWUUkopIYQQSimllFJKKSWEEEIopZRSSimlhBBCKKWUUkoppZQQQiillFJKKaWUEkIIoZRSSimllFJCCKWUUkoppZRS
SighhFJKKaWUUkoJJZRSSimllFJKKSGUUkoppZRSSimlAACAAwcAgAAj6CSjyiJsNOHCAxAAAAACAAJMAIEBgoJRCAKEEQgA
AAAAAAgA+AAASAqAiIho5gwOEBIUFhgaHB4gIiQAAAAAAAAAAAAAAAAET2dnUwAEoAAAAAAAAAAh/TsKAgAAAJtuxN0CHxmW
mBlzU7yBfQUAFUBU1DATaoRXxRRzzgXXdV338/Qpls6Q3DWvAOA1BYAEAABwHOeDcb9GFvHACA==
""";

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
