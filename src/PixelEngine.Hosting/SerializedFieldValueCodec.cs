using System.Globalization;
using System.Numerics;

namespace PixelEngine.Hosting;

/// <summary>
/// authoring <c>SerializedFields</c> 中复合数值的稳定文本编码。
/// </summary>
/// <remarks>
/// 向量始终使用 invariant、逗号分隔的 <c>x,y[,z,w]</c> 形式，避免依赖
/// <see cref="Vector2.ToString()"/> 的区域性与展示格式。
/// </remarks>
public static class SerializedFieldValueCodec
{
    /// <summary>把 <see cref="Vector2"/> 编码为可稳定入盘的 <c>x,y</c>。</summary>
    /// <exception cref="ArgumentOutOfRangeException">任一分量不是有限值。</exception>
    public static string Format(Vector2 value)
    {
        EnsureFinite(value.X, value.Y);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R}");
    }

    /// <summary>把 <see cref="Vector3"/> 编码为可稳定入盘的 <c>x,y,z</c>。</summary>
    /// <exception cref="ArgumentOutOfRangeException">任一分量不是有限值。</exception>
    public static string Format(Vector3 value)
    {
        EnsureFinite(value.X, value.Y, value.Z);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R},{value.Z:R}");
    }

    /// <summary>把 <see cref="Vector4"/> 编码为可稳定入盘的 <c>x,y,z,w</c>。</summary>
    /// <exception cref="ArgumentOutOfRangeException">任一分量不是有限值。</exception>
    public static string Format(Vector4 value)
    {
        EnsureFinite(value.X, value.Y, value.Z, value.W);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R},{value.Z:R},{value.W:R}");
    }

    /// <summary>尝试从 invariant <c>x,y</c> 文本解码 <see cref="Vector2"/>。</summary>
    public static bool TryParseVector2(string? text, out Vector2 value)
    {
        Span<float> components = stackalloc float[2];
        if (!TryParseComponents(text, components))
        {
            value = default;
            return false;
        }

        value = new Vector2(components[0], components[1]);
        return true;
    }

    /// <summary>尝试从 invariant <c>x,y,z</c> 文本解码 <see cref="Vector3"/>。</summary>
    public static bool TryParseVector3(string? text, out Vector3 value)
    {
        Span<float> components = stackalloc float[3];
        if (!TryParseComponents(text, components))
        {
            value = default;
            return false;
        }

        value = new Vector3(components[0], components[1], components[2]);
        return true;
    }

    /// <summary>尝试从 invariant <c>x,y,z,w</c> 文本解码 <see cref="Vector4"/>。</summary>
    public static bool TryParseVector4(string? text, out Vector4 value)
    {
        Span<float> components = stackalloc float[4];
        if (!TryParseComponents(text, components))
        {
            value = default;
            return false;
        }

        value = new Vector4(components[0], components[1], components[2], components[3]);
        return true;
    }

    private static bool TryParseComponents(string? text, Span<float> destination)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> remaining = text.AsSpan().Trim();
        for (int i = 0; i < destination.Length; i++)
        {
            int separator = remaining.IndexOf(',');
            ReadOnlySpan<char> component;
            if (i == destination.Length - 1)
            {
                if (separator >= 0)
                {
                    return false;
                }

                component = remaining;
                remaining = [];
            }
            else
            {
                if (separator < 0)
                {
                    return false;
                }

                component = remaining[..separator];
                remaining = remaining[(separator + 1)..];
            }

            if (!float.TryParse(
                component.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out destination[i]) ||
                !float.IsFinite(destination[i]))
            {
                return false;
            }
        }

        return remaining.IsEmpty;
    }

    private static void EnsureFinite(params ReadOnlySpan<float> components)
    {
        for (int i = 0; i < components.Length; i++)
        {
            if (!float.IsFinite(components[i]))
            {
                throw new ArgumentOutOfRangeException(nameof(components), "向量分量必须是有限数值。");
            }
        }
    }
}
