using PixelEngine.Audio;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 生产音频试听桥接测试。
/// </summary>
public sealed class EditorAudioPreviewServiceTests
{
    /// <summary>
    /// 验证 Project Window 只试听 Content/audio 下的 WAV，并复用缓存而不增加引用计数。
    /// </summary>
    [Fact]
    public void PreviewLoadsNewWaveThenReusesRuntimeClip()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-audio-preview-" + Guid.NewGuid().ToString("N"));
        string audioRoot = Path.Combine(root, "audio");
        _ = Directory.CreateDirectory(Path.Combine(audioRoot, "ui"));
        WriteWave(Path.Combine(audioRoot, "ui", "click.wav"));
        try
        {
            using NullAudioBackend backend = new();
            using AudioClipCache clips = new(backend, new DirectoryAudioAssetStore(audioRoot), new WavDecoder());
            using AudioSystem audio = new();
            audio.Initialize(new AudioSettings { MaxVoices = 4 }, backend);
            audio.AttachClipCache(clips);
            EditorAudioPreviewService preview = new(audio, clips);

            Assert.False(preview.TryPlayPreview("ScriptSource/ui/click.wav"));
            Assert.False(preview.TryPlayPreview("Content/textures/click.wav"));
            Assert.False(preview.TryPlayPreview("Content/audio/click.ogg"));
            Assert.True(preview.TryPlayPreview("Content/audio/ui/click.wav"));
            Assert.True(preview.TryPlayPreview("Content/audio/ui/click.wav"));
            Assert.Equal(2, backend.PlayCalls);
            Assert.Equal(1, clips.LoadedCount);
            Assert.True(clips.TryGetLoaded("ui/click.wav", out AudioClip? clip));
            Assert.NotNull(clip);
            Assert.Equal(1, clip.RefCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteWave(string path)
    {
        const int sampleRate = 8_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        short[] samples = [0, 1_000, -1_000, 0];
        int dataBytes = samples.Length * sizeof(short);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (int i = 0; i < samples.Length; i++)
        {
            writer.Write(samples[i]);
        }
    }
}
