namespace PixelEngine.UI;

#pragma warning disable IDE0032

/// <summary>
/// 管理已载入 UI 文档与可见屏栈。
/// </summary>
public sealed class UiDocumentManager
{
    private readonly UiDocumentSlot[] _documents;
    private readonly UiScreenStackEntry[] _stack;
    private int _documentCount;
    private int _stackCount;
    private int _nextScreenHandle = 1;

    /// <summary>
    /// 创建文档管理器。
    /// </summary>
    /// <param name="maxDocuments">文档容量。</param>
    /// <param name="maxStackDepth">屏栈容量。</param>
    public UiDocumentManager(int maxDocuments, int maxStackDepth)
    {
        if (maxDocuments <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDocuments));
        }

        if (maxStackDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStackDepth));
        }

        _documents = new UiDocumentSlot[maxDocuments];
        _stack = new UiScreenStackEntry[maxStackDepth];
    }

    /// <summary>
    /// 已载入文档数量。
    /// </summary>
    public int DocumentCount => _documentCount;

    /// <summary>
    /// 当前可见屏栈深度。
    /// </summary>
    public int StackCount => _stackCount;

    /// <summary>
    /// 当前栈顶是否为模态屏。
    /// </summary>
    public bool HasModalTop => _stackCount > 0 && _stack[_stackCount - 1].Modal;

    /// <summary>
    /// 注册已由后端载入的文档。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="document">后端文档句柄。</param>
    /// <param name="sourceKind">文档来源类型。</param>
    public void Register(UiScreenId screenId, UiDocumentHandle document, UiDocumentSourceKind sourceKind)
    {
        screenId.Validate();
        document.Validate();
        if (TryGetDocument(screenId, out _))
        {
            throw new InvalidOperationException($"UI 屏幕 {screenId.Value} 已注册。");
        }

        if (_documentCount >= _documents.Length)
        {
            throw new InvalidOperationException("UI 文档容量已满。");
        }

        _documents[_documentCount++] = new UiDocumentSlot(screenId, document, sourceKind);
    }

    /// <summary>
    /// 查找屏幕对应文档。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="document">后端文档句柄。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetDocument(UiScreenId screenId, out UiDocumentHandle document)
    {
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i].ScreenId == screenId)
            {
                document = _documents[i].Document;
                return true;
            }
        }

        document = default;
        return false;
    }

    /// <summary>
    /// 查找可见屏幕对应的文档。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="document">后端文档句柄。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetDocument(UiScreenHandle screen, out UiDocumentHandle document)
    {
        for (int i = 0; i < _stackCount; i++)
        {
            if (_stack[i].Handle == screen)
            {
                document = _stack[i].Document;
                return true;
            }
        }

        document = default;
        return false;
    }

    /// <summary>
    /// 查找文档当前对应的最上层可见屏幕。
    /// </summary>
    /// <param name="document">后端文档句柄。</param>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetVisibleScreen(UiDocumentHandle document, out UiScreenHandle screen)
    {
        for (int i = _stackCount - 1; i >= 0; i--)
        {
            if (_stack[i].Document == document)
            {
                screen = _stack[i].Handle;
                return true;
            }
        }

        screen = default;
        return false;
    }

    /// <summary>
    /// 显示一个屏幕。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="document">文档句柄。</param>
    /// <param name="modal">是否模态。</param>
    /// <returns>屏幕句柄。</returns>
    public UiScreenHandle Show(UiScreenId screenId, UiDocumentHandle document, bool modal)
    {
        screenId.Validate();
        document.Validate();
        if (_stackCount >= _stack.Length)
        {
            throw new InvalidOperationException("UI 屏栈容量已满。");
        }

        // 屏栈只追加不覆盖：同一 screenId 多次 Show 会生成多个 handle，由 Hide 精确移除。
        UiScreenHandle handle = new(_nextScreenHandle++);
        if (_nextScreenHandle <= 0)
        {
            _nextScreenHandle = 1;
        }

        _stack[_stackCount++] = new UiScreenStackEntry(handle, screenId, document, modal);
        return handle;
    }

    /// <summary>
    /// 隐藏指定屏幕。
    /// </summary>
    /// <param name="screen">屏幕句柄。</param>
    /// <returns>找到并移除则返回 true。</returns>
    public bool Hide(UiScreenHandle screen)
    {
        for (int i = _stackCount - 1; i >= 0; i--)
        {
            if (_stack[i].Handle == screen)
            {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 弹出栈顶模态屏幕。
    /// </summary>
    /// <param name="screen">被弹出的屏幕句柄。</param>
    /// <returns>栈顶是模态屏且弹出成功则返回 true。</returns>
    public bool PopModal(out UiScreenHandle screen)
    {
        if (_stackCount == 0 || !_stack[_stackCount - 1].Modal)
        {
            screen = default;
            return false;
        }

        screen = _stack[_stackCount - 1].Handle;
        _stack[--_stackCount] = default;
        return true;
    }

    /// <summary>
    /// 把当前可见屏栈复制到调用方提供的缓冲。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>写入项数量。</returns>
    public int CopyStack(Span<UiScreenStackEntry> destination)
    {
        int count = Math.Min(destination.Length, _stackCount);
        _stack.AsSpan(0, count).CopyTo(destination);
        return count;
    }

    private void RemoveAt(int index)
    {
        int moveCount = _stackCount - index - 1;
        if (moveCount > 0)
        {
            _stack.AsSpan(index + 1, moveCount).CopyTo(_stack.AsSpan(index, moveCount));
        }

        _stack[--_stackCount] = default;
    }
}
#pragma warning restore IDE0032
