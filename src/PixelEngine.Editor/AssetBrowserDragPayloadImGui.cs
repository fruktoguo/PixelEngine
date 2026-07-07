using System.Globalization;
using System.Text;
using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// Project Window 资产拖拽 payload 的 ImGui 编解码与传递工具。
/// </summary>
public static class AssetBrowserDragPayloadImGui
{
    /// <summary>
    /// Dear ImGui drag/drop payload 类型名。
    /// </summary>
    public const string PayloadType = "PixelEngineAsset";

    /// <summary>
    /// 把 Project Window typed asset payload 注册到当前 ImGui drag source。
    /// </summary>
    /// <param name="payload">Project Window typed asset payload。</param>
    /// <returns>payload 成功交给 ImGui 时返回 true。</returns>
    public static bool SetPayload(AssetBrowserDragPayload payload)
    {
        byte[] bytes = Encode(payload);
        unsafe
        {
            fixed (byte* data = bytes)
            {
                return ImGui.SetDragDropPayload(PayloadType, data, new UIntPtr((uint)bytes.Length));
            }
        }
    }

    /// <summary>
    /// 从当前 ImGui drop target 接收 Project Window typed asset payload。
    /// </summary>
    /// <param name="payload">解析出的 typed asset payload。</param>
    /// <returns>鼠标释放并成功解析 payload 时返回 true。</returns>
    public static unsafe bool TryAcceptPayload(out AssetBrowserDragPayload payload)
    {
        ImGuiPayloadPtr accepted = ImGui.AcceptDragDropPayload(PayloadType);
        if (accepted.IsNull || !accepted.Delivery || accepted.Data is null || accepted.DataSize <= 0)
        {
            payload = default;
            return false;
        }

        return TryDecode(new ReadOnlySpan<byte>(accepted.Data, accepted.DataSize), out payload);
    }

    /// <summary>
    /// 把 typed asset payload 编码为 ImGui 可复制的 UTF-8 bytes。
    /// </summary>
    /// <param name="payload">Project Window typed asset payload。</param>
    /// <returns>编码后的 payload bytes。</returns>
    public static byte[] Encode(AssetBrowserDragPayload payload)
    {
        string encoded = string.Join(
            '\n',
            ToBase64(payload.AssetId),
            ToBase64(payload.Path),
            ((int)payload.Kind).ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(encoded);
    }

    /// <summary>
    /// 尝试从 UTF-8 bytes 解析 typed asset payload。
    /// </summary>
    /// <param name="bytes">ImGui payload bytes。</param>
    /// <param name="payload">解析出的 typed asset payload。</param>
    /// <returns>payload 完整且资产类型有效时返回 true。</returns>
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out AssetBrowserDragPayload payload)
    {
        string encoded = Encoding.UTF8.GetString(bytes);
        string[] parts = encoded.Split('\n');
        if (parts.Length != 3 ||
            !TryFromBase64(parts[0], out string? assetId) ||
            !TryFromBase64(parts[1], out string? path) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue) ||
            !Enum.IsDefined(typeof(AssetBrowserItemKind), kindValue) ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(path))
        {
            payload = default;
            return false;
        }

        payload = new AssetBrowserDragPayload(assetId, path, (AssetBrowserItemKind)kindValue);
        return true;
    }

    private static string ToBase64(string? value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static bool TryFromBase64(string value, out string? decoded)
    {
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            decoded = null;
            return false;
        }
    }
}
