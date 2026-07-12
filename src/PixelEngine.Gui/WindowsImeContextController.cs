using PixelEngine.Interop;

namespace PixelEngine.Gui;

/// <summary>
/// 把单个 Dear ImGui context 的文本输入请求注册到同一 Win32 HWND 的共享 IME 仲裁器。
/// </summary>
internal sealed class WindowsImeContextController
{
    private readonly WindowsImeContextRegistry _registry;
    private WindowsImeContextRegistry.ClientState? _client;

    internal WindowsImeContextController(WindowsImeContextRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal void Attach(IntPtr hwnd)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Windows IME context controller 已经注册。");
        }

        _client = _registry.Attach(hwnd);
    }

    internal void UpdateRequest(
        bool wantsTextInput,
        bool visible,
        bool hasForms,
        in Win32CompositionForm composition,
        in Win32CandidateForm candidate)
    {
        if (_client is null)
        {
            return;
        }

        _registry.UpdateRequest(
            _client,
            wantsTextInput,
            visible,
            hasForms,
            in composition,
            in candidate);
    }

    internal void SetFocused(bool focused)
    {
        if (_client is not null)
        {
            _registry.SetFocused(_client, focused);
        }
    }

    internal void Detach()
    {
        if (_client is null)
        {
            return;
        }

        _registry.Detach(_client);
        _client = null;
    }
}

/// <summary>
/// 按 HWND 共享唯一 HIMC 生命周期，防止 Editor 与 Game/UI 等多个 ImGui context 相互解除输入上下文。
/// </summary>
/// <remarks>
/// 关联切换只发生在所有 client 的聚合文本请求或窗口焦点转换点；先释放 <c>ImmGetContext</c>，
/// 再调用 <c>ImmAssociateContext</c>，避免部分 IME 在 context 仍被调用方持有时重入冻结。
/// </remarks>
internal sealed class WindowsImeContextRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<IntPtr, WindowState> _windows = [];
    private readonly IWindowsImeContextNative _native;
    private readonly bool _enabled;

    internal static WindowsImeContextRegistry Shared { get; } = new(
        WindowsImeContextNative.Instance,
        OperatingSystem.IsWindows());

    internal WindowsImeContextRegistry(IWindowsImeContextNative native, bool enabled)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _enabled = enabled;
    }

    internal ClientState Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("IME HWND 不能为空。", nameof(hwnd));
        }

        lock (_gate)
        {
            if (!_windows.TryGetValue(hwnd, out WindowState? window))
            {
                window = new WindowState(hwnd, _native, _enabled);
                _windows.Add(hwnd, window);
            }

            return window.AttachClient();
        }
    }

    internal void UpdateRequest(
        ClientState client,
        bool wantsTextInput,
        bool visible,
        bool hasForms,
        in Win32CompositionForm composition,
        in Win32CandidateForm candidate)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            client.Window.UpdateRequest(
                client,
                wantsTextInput,
                visible,
                hasForms,
                in composition,
                in candidate);
        }
    }

    internal void SetFocused(ClientState client, bool focused)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            client.Window.SetFocused(client, focused);
        }
    }

    internal void Detach(ClientState client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            WindowState window = client.Window;
            if (!window.DetachClient(client))
            {
                return;
            }

            _ = _windows.Remove(window.Hwnd);
        }
    }

    internal sealed class ClientState(WindowState window)
    {
        internal WindowState Window { get; } = window;

        internal Win32CompositionForm Composition;

        internal Win32CandidateForm Candidate;

        internal long RequestOrder;

        internal bool WantsTextInput;

        internal bool HasForms;

        internal bool Attached = true;
    }

    internal sealed class WindowState
    {
        private readonly List<ClientState> _clients = [];
        private readonly IWindowsImeContextNative _native;
        private readonly bool _enabled;
        private IntPtr _suspendedContext;
        private ClientState? _activeClient;
        private long _requestOrder;
        private bool _focused = true;
        private bool _contextSuspended;

        internal WindowState(IntPtr hwnd, IWindowsImeContextNative native, bool enabled)
        {
            Hwnd = hwnd;
            _native = native;
            _enabled = enabled;
            SuspendContext();
        }

        internal IntPtr Hwnd { get; }

        internal ClientState AttachClient()
        {
            ClientState client = new(this);
            _clients.Add(client);
            return client;
        }

        internal void UpdateRequest(
            ClientState client,
            bool wantsTextInput,
            bool visible,
            bool hasForms,
            in Win32CompositionForm composition,
            in Win32CandidateForm candidate)
        {
            RequireAttached(client);
            client.WantsTextInput = wantsTextInput;
            client.HasForms = wantsTextInput && visible && hasForms;
            if (wantsTextInput)
            {
                client.RequestOrder = ++_requestOrder;
            }

            if (client.HasForms)
            {
                client.Composition = composition;
                client.Candidate = candidate;
            }

            ApplyRequestedState();
        }

        internal void SetFocused(ClientState client, bool focused)
        {
            RequireAttached(client);
            if (_focused == focused)
            {
                return;
            }

            _focused = focused;
            ApplyRequestedState();
        }

        /// <returns>移除最后一个 client 时为 true。</returns>
        internal bool DetachClient(ClientState client)
        {
            if (!client.Attached)
            {
                return false;
            }

            RequireAttached(client);
            client.Attached = false;
            _ = _clients.Remove(client);
            if (_clients.Count != 0)
            {
                ApplyRequestedState();
                return false;
            }

            if (_activeClient is not null)
            {
                CancelCurrentComposition();
            }

            _activeClient = null;
            RestoreContext();
            return true;
        }

        private void ApplyRequestedState()
        {
            if (!_enabled)
            {
                return;
            }

            ClientState? next = _focused ? FindMostRecentRequester() : null;
            if (!_focused || next is null)
            {
                _activeClient = null;
                SuspendContext();
                return;
            }

            if (_activeClient is not null && !ReferenceEquals(_activeClient, next))
            {
                CancelCurrentComposition();
            }

            _activeClient = next;
            RestoreContext();
            if (next.HasForms)
            {
                ApplyForms(next);
            }
        }

        private ClientState? FindMostRecentRequester()
        {
            ClientState? result = null;
            long newest = long.MinValue;
            foreach (ClientState client in _clients)
            {
                if (client.WantsTextInput && client.RequestOrder >= newest)
                {
                    newest = client.RequestOrder;
                    result = client;
                }
            }

            return result;
        }

        private void SuspendContext()
        {
            if (!_enabled || _contextSuspended)
            {
                return;
            }

            IntPtr context = _native.GetContext(Hwnd);
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _ = _native.CancelComposition(context);
                _ = _native.CloseCandidate(context);
            }
            finally
            {
                _ = _native.ReleaseContext(Hwnd, context);
            }

            // ImmAssociateContext 必须在 ImmGetContext/ImmReleaseContext 区间之外调用，否则部分 IME 会重入冻结。
            IntPtr previous = _native.AssociateContext(Hwnd, IntPtr.Zero);
            if (previous == IntPtr.Zero)
            {
                return;
            }

            _suspendedContext = previous;
            _contextSuspended = true;
        }

        private void RestoreContext()
        {
            if (!_enabled || !_contextSuspended)
            {
                return;
            }

            _ = _native.AssociateContext(Hwnd, _suspendedContext);
            _suspendedContext = IntPtr.Zero;
            _contextSuspended = false;
        }

        private void CancelCurrentComposition()
        {
            IntPtr context = _native.GetContext(Hwnd);
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _ = _native.CancelComposition(context);
                _ = _native.CloseCandidate(context);
            }
            finally
            {
                _ = _native.ReleaseContext(Hwnd, context);
            }
        }

        private void ApplyForms(ClientState client)
        {
            IntPtr context = _native.GetContext(Hwnd);
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _ = _native.SetCompositionWindow(context, in client.Composition);
                _ = _native.SetCandidateWindow(context, in client.Candidate);
            }
            finally
            {
                _ = _native.ReleaseContext(Hwnd, context);
            }
        }

        private static void RequireAttached(ClientState client)
        {
            if (!client.Attached)
            {
                throw new InvalidOperationException("IME client 已解除注册。");
            }
        }

    }
}

internal interface IWindowsImeContextNative
{
    IntPtr GetContext(IntPtr hwnd);

    bool ReleaseContext(IntPtr hwnd, IntPtr context);

    IntPtr AssociateContext(IntPtr hwnd, IntPtr context);

    bool CancelComposition(IntPtr context);

    bool CloseCandidate(IntPtr context);

    bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form);

    bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form);
}

internal sealed class WindowsImeContextNative : IWindowsImeContextNative
{
    internal static WindowsImeContextNative Instance { get; } = new();

    private WindowsImeContextNative()
    {
    }

    public IntPtr GetContext(IntPtr hwnd)
    {
        return Win32ImeNative.GetContext(hwnd);
    }

    public bool ReleaseContext(IntPtr hwnd, IntPtr context)
    {
        return Win32ImeNative.ReleaseContext(hwnd, context);
    }

    public IntPtr AssociateContext(IntPtr hwnd, IntPtr context)
    {
        return Win32ImeNative.AssociateContext(hwnd, context);
    }

    public bool CancelComposition(IntPtr context)
    {
        return Win32ImeNative.CancelComposition(context);
    }

    public bool CloseCandidate(IntPtr context)
    {
        return Win32ImeNative.CloseCandidate(context);
    }

    public bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form)
    {
        return Win32ImeNative.SetCompositionWindow(context, in form);
    }

    public bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form)
    {
        return Win32ImeNative.SetCandidateWindow(context, in form);
    }
}
