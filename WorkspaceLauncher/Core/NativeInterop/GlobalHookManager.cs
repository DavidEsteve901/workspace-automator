using System.Runtime.InteropServices;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Installs WH_MOUSE_LL + WH_KEYBOARD_LL hooks on a dedicated thread.
/// Direct port of the Python GlobalHookManager class.
/// </summary>
public sealed class GlobalHookManager : IDisposable
{
    private nint  _hookMouse;
    private nint  _hookKeyboard;
    private Thread? _thread;
    private uint  _threadId;
    private bool  _running;
    private bool  _disposed;

    // Keep delegates alive to prevent GC collection
    private User32.HookProc? _mouseProc;
    private User32.HookProc? _kbProc;

    private bool _suppressedX1;
    private bool _suppressedX2;
    private bool _suppressedMiddle;

    // ── Public events ──────────────────────────────────────────────────────
    // Modifiers are captured at hook time (on the hook thread) and passed here
    // so handlers don't need to re-read GetAsyncKeyState on a thread pool thread
    // where the keys may have already been released.
    public event Action<bool, bool, bool, bool>? OnX1Down; // alt, ctrl, shift, win
    public event Action<bool, bool, bool, bool>? OnX2Down; // alt, ctrl, shift, win
    public event Action<bool, bool, bool, bool>? OnMiddleDown; // alt, ctrl, shift, win
    public event Action? OnX1Up;
    public event Action? OnX2Up;
    public event Action? OnMiddleUp;
    public event Action<int, bool, bool, bool, bool>? OnKeyDown; // vkCode, alt, ctrl, shift, win

    /// Optional predicate: given (button="x1"|"x2", alt, ctrl, shift, win) → true if the combo is mapped.
    /// If mapped, the button is always suppressed. If not mapped, pass-through when modifier held.
    /// </summary>
    public Func<string, bool, bool, bool, bool, bool>? CheckXMapped { get; set; }
    
    /// <summary>
    /// Optional predicate for keyboard: (vkCode, alt, ctrl, shift, win) → true to suppress
    /// </summary>
    public Func<int, bool, bool, bool, bool, bool>? CheckKeyMapped { get; set; }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread  = new Thread(InstallHooks) { IsBackground = true, Name = "GlobalHookThread" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_threadId != 0)
            User32.PostThreadMessageW(_threadId, User32.WM_QUIT, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    // ── Hook install loop ──────────────────────────────────────────────────
    private void InstallHooks()
    {
        _threadId  = Kernel32.GetCurrentThreadId();
        _mouseProc = MouseHookProc;
        _kbProc    = KeyboardHookProc;

        _hookMouse    = User32.SetWindowsHookExW(User32.WH_MOUSE_LL,    _mouseProc, 0, 0);
        _hookKeyboard = User32.SetWindowsHookExW(User32.WH_KEYBOARD_LL, _kbProc,    0, 0);

        if (_hookMouse == 0 || _hookKeyboard == 0)
        {
            Console.WriteLine($"[HookManager] Error installing hooks: {Marshal.GetLastWin32Error()}");
            _running = false;
            return;
        }

        Console.WriteLine("[HookManager] Win32 hooks installed.");

        while (_running)
        {
            int ret = User32.GetMessageW(out var msg, 0, 0, 0);
            if (ret <= 0) break;
            User32.TranslateMessage(ref msg);
            User32.DispatchMessageW(ref msg);
        }

        if (_hookMouse    != 0) User32.UnhookWindowsHookEx(_hookMouse);
        if (_hookKeyboard != 0) User32.UnhookWindowsHookEx(_hookKeyboard);
        Console.WriteLine("[HookManager] Hooks uninstalled.");
    }

    // ── Mouse hook callback ────────────────────────────────────────────────
    private nint MouseHookProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode == User32.HC_ACTION &&
            (wParam == User32.WM_XBUTTONDOWN || wParam == User32.WM_XBUTTONUP ||
             wParam == User32.WM_MBUTTONDOWN || wParam == User32.WM_MBUTTONUP))
        {
            var data   = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int button = (int)((data.mouseData >> 16) & 0xFFFF);

            bool alt   = (User32.GetAsyncKeyState(User32.VK_ALT)   & 0x8000) != 0;
            bool ctrl  = (User32.GetAsyncKeyState(User32.VK_CTRL)  & 0x8000) != 0;
            bool shift = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
            bool win   = (User32.GetAsyncKeyState(User32.VK_LWIN)  & 0x8000) != 0 || (User32.GetAsyncKeyState(User32.VK_RWIN) & 0x8000) != 0;

            if (wParam == User32.WM_MBUTTONDOWN)
            {
                if (!(CheckXMapped?.Invoke("mbutton", alt, ctrl, shift, win) ?? false))
                    return User32.CallNextHookEx(_hookMouse, nCode, wParam, lParam);
                _suppressedMiddle = true;
                bool am = alt, cm = ctrl, sm = shift, wm = win;
                Task.Run(() => OnMiddleDown?.Invoke(am, cm, sm, wm));
                return 1;
            }
            else if (wParam == User32.WM_MBUTTONUP)
            {
                if (_suppressedMiddle)
                {
                    _suppressedMiddle = false;
                    Task.Run(() => OnMiddleUp?.Invoke());
                    return 1;
                }
            }

            if (wParam == User32.WM_XBUTTONDOWN)
            {
                if (button == User32.XBUTTON1)
                {
                    if (!(CheckXMapped?.Invoke("x1", alt, ctrl, shift, win) ?? false))
                        return User32.CallNextHookEx(_hookMouse, nCode, wParam, lParam);
                    _suppressedX1 = true;
                    bool a1 = alt, c1 = ctrl, s1 = shift, w1 = win;
                    Task.Run(() => OnX1Down?.Invoke(a1, c1, s1, w1));
                    return 1;
                }
                if (button == User32.XBUTTON2)
                {
                    if (!(CheckXMapped?.Invoke("x2", alt, ctrl, shift, win) ?? false))
                        return User32.CallNextHookEx(_hookMouse, nCode, wParam, lParam);
                    _suppressedX2 = true;
                    bool a2 = alt, c2 = ctrl, s2 = shift, w2 = win;
                    Task.Run(() => OnX2Down?.Invoke(a2, c2, s2, w2));
                    return 1;
                }
            }
            else if (wParam == User32.WM_XBUTTONUP)
            {
                if (button == User32.XBUTTON1 && _suppressedX1)
                {
                    _suppressedX1 = false;
                    Task.Run(() => OnX1Up?.Invoke());
                    return 1;
                }
                if (button == User32.XBUTTON2 && _suppressedX2)
                {
                    _suppressedX2 = false;
                    Task.Run(() => OnX2Up?.Invoke());
                    return 1;
                }
            }
        }
        return User32.CallNextHookEx(_hookMouse, nCode, wParam, lParam);
    }

    // ── Keyboard hook callback ─────────────────────────────────────────────
    private nint KeyboardHookProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode == User32.HC_ACTION && (wParam == User32.WM_KEYDOWN || wParam == User32.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk   = (int)data.vkCode;

            if (vk == User32.VK_BROWSER_BACK || vk == User32.VK_BROWSER_FORWARD)
            {
                // Allow passthrough when Ctrl is held (user wants real browser nav)
                bool ctrlHeld = (User32.GetAsyncKeyState(User32.VK_CTRL) & 0x8000) != 0;
                if (!ctrlHeld) return 1; // Suppress – these are mapped to X1/X2 gestures
                // Ctrl+Back/Forward → pass through to browser
                return User32.CallNextHookEx(_hookKeyboard, nCode, wParam, lParam);
            }

            bool alt   = (User32.GetAsyncKeyState(User32.VK_ALT)   & 0x8000) != 0 || wParam == User32.WM_SYSKEYDOWN;
            bool ctrl  = (User32.GetAsyncKeyState(User32.VK_CTRL)  & 0x8000) != 0;
            bool shift = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
            bool win   = (User32.GetAsyncKeyState(User32.VK_LWIN)  & 0x8000) != 0 || (User32.GetAsyncKeyState(User32.VK_RWIN) & 0x8000) != 0;

            if (CheckKeyMapped?.Invoke(vk, alt, ctrl, shift, win) ?? false)
            {
                Task.Run(() => OnKeyDown?.Invoke(vk, alt, ctrl, shift, win));
                return 1; // Suppress
            }
        }
        return User32.CallNextHookEx(_hookKeyboard, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
