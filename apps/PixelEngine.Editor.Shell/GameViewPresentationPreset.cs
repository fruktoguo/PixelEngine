using PixelEngine.Hosting;
using PixelEngine.Rendering;

namespace PixelEngine.Editor.Shell;

/// <summary>Game View preset 的尺寸语义。</summary>
internal enum GameViewPresentationPresetKind
{
    PlayerDefault,
    FreeAspect,
    AspectRatio,
    FixedResolution,
}

/// <summary>
/// Game View 内建或用户固定 preset；ValueA/ValueB 分别表示 ratio 分子分母或 pixel 宽高。
/// </summary>
internal readonly record struct GameViewPresentationPreset(
    string Id,
    string Label,
    GameViewPresentationPresetKind Kind,
    int ValueA,
    int ValueB)
{
    public static readonly GameViewPresentationPreset[] BuiltIns =
    [
        new("player-default", "Player Default", GameViewPresentationPresetKind.PlayerDefault, 0, 0),
        new("free-aspect", "Free Aspect", GameViewPresentationPresetKind.FreeAspect, 0, 0),
        new("aspect-16-9", "16:9", GameViewPresentationPresetKind.AspectRatio, 16, 9),
        new("aspect-16-10", "16:10", GameViewPresentationPresetKind.AspectRatio, 16, 10),
        new("aspect-4-3", "4:3", GameViewPresentationPresetKind.AspectRatio, 4, 3),
        new("aspect-5-4", "5:4", GameViewPresentationPresetKind.AspectRatio, 5, 4),
        new("aspect-3-2", "3:2", GameViewPresentationPresetKind.AspectRatio, 3, 2),
        new("aspect-9-16", "9:16", GameViewPresentationPresetKind.AspectRatio, 9, 16),
        new("resolution-640-360", "640×360", GameViewPresentationPresetKind.FixedResolution, 640, 360),
        new("resolution-1280-720", "1280×720", GameViewPresentationPresetKind.FixedResolution, 1280, 720),
        new("resolution-1920-1080", "1920×1080", GameViewPresentationPresetKind.FixedResolution, 1920, 1080),
        new("resolution-2560-1440", "2560×1440", GameViewPresentationPresetKind.FixedResolution, 2560, 1440),
    ];

    public static bool TryResolve(
        string id,
        ReadOnlySpan<EditorGameViewCustomPreset> customPresets,
        out GameViewPresentationPreset preset)
    {
        for (int i = 0; i < BuiltIns.Length; i++)
        {
            if (string.Equals(BuiltIns[i].Id, id, StringComparison.Ordinal))
            {
                preset = BuiltIns[i];
                return true;
            }
        }

        for (int i = 0; i < customPresets.Length; i++)
        {
            EditorGameViewCustomPreset custom = customPresets[i];
            if (string.Equals(custom.Id, id, StringComparison.Ordinal))
            {
                preset = new GameViewPresentationPreset(
                    custom.Id,
                    custom.Name,
                    GameViewPresentationPresetKind.FixedResolution,
                    custom.Width,
                    custom.Height);
                return true;
            }
        }

        preset = BuiltIns[0];
        return false;
    }
}

/// <summary>把 preset 与 toolbar 后的可用 framebuffer 区域解析成 pending presentation。</summary>
internal static class GameViewPresentationResolver
{
    public static bool TryResolve(
        in GameViewPresentationPreset preset,
        int playerDefaultWidth,
        int playerDefaultHeight,
        int availableFramebufferWidth,
        int availableFramebufferHeight,
        int maximumTextureSize,
        long requestRevision,
        out GamePresentationOverride request,
        out string diagnostic)
    {
        int width;
        int height;
        GamePresentationSource source;
        switch (preset.Kind)
        {
            case GameViewPresentationPresetKind.PlayerDefault:
                width = playerDefaultWidth;
                height = playerDefaultHeight;
                source = GamePresentationSource.PlayerDefault;
                break;
            case GameViewPresentationPresetKind.FreeAspect:
                width = availableFramebufferWidth;
                height = availableFramebufferHeight;
                source = GamePresentationSource.EditorFreeAspect;
                break;
            case GameViewPresentationPresetKind.AspectRatio:
                if (preset.ValueA <= 0 || preset.ValueB <= 0 ||
                    availableFramebufferWidth <= 0 || availableFramebufferHeight <= 0)
                {
                    request = default;
                    diagnostic = "Game View ratio preset 或可用区域无效。";
                    return false;
                }

                PresentationViewport fitted = PresentationViewport.Fit(
                    preset.ValueA,
                    preset.ValueB,
                    availableFramebufferWidth,
                    availableFramebufferHeight);
                width = fitted.Width;
                height = fitted.Height;
                source = GamePresentationSource.EditorAspectRatio;
                break;
            case GameViewPresentationPresetKind.FixedResolution:
                width = preset.ValueA;
                height = preset.ValueB;
                source = GamePresentationSource.EditorFixedResolution;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset.Kind, "未知 Game View preset kind。");
        }

        if (width <= 0 || height <= 0)
        {
            request = default;
            diagnostic = "Game View presentation 宽高必须为正数。";
            return false;
        }

        if (width > maximumTextureSize || height > maximumTextureSize)
        {
            request = default;
            diagnostic = $"Game View presentation {width}×{height} 超过 renderer 上限 {maximumTextureSize}。";
            return false;
        }

        request = new GamePresentationOverride(width, height, source, requestRevision);
        diagnostic = string.Empty;
        return true;
    }
}
