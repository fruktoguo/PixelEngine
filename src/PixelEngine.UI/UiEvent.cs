using System.Runtime.InteropServices;

namespace PixelEngine.UI;

/// <summary>
/// UI 到游戏的零分配事件。
/// </summary>
/// <param name="Document">来源文档。</param>
/// <param name="Element">元素 id。</param>
/// <param name="Action">动作 id。</param>
/// <param name="Payload">事件载荷。</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct UiEvent(
    UiDocumentHandle Document,
    UiElementId Element,
    UiActionId Action,
    UiValue Payload);
