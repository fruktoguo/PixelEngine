using System.Numerics;

namespace PixelEngine.Rendering;

/// <summary>
/// UI 三角形批绘制状态。
/// </summary>
/// <param name="TextureHandle">可选 GL Texture2D 句柄；0 表示纯顶点色。</param>
/// <param name="Scissor">可选裁剪矩形。</param>
/// <param name="Transform">2D 变换矩阵。</param>
public readonly record struct UiDrawState(uint TextureHandle, UiScissorRect? Scissor, Matrix3x2 Transform)
{
    /// <summary>
    /// 默认绘制状态：无纹理、无裁剪、单位变换。
    /// </summary>
    public static UiDrawState Default => new(0, null, Matrix3x2.Identity);

    /// <summary>
    /// 创建纹理绘制状态。
    /// </summary>
    /// <param name="textureHandle">GL Texture2D 句柄。</param>
    /// <returns>绘制状态。</returns>
    public static UiDrawState Textured(uint textureHandle)
    {
        return new UiDrawState(textureHandle, null, Matrix3x2.Identity);
    }

    /// <summary>
    /// 校验绘制状态。
    /// </summary>
    public void Validate()
    {
        Scissor?.Validate();
    }
}
