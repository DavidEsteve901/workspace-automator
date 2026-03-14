using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.NativeInterop;

// ── Structs ────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width  => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT   pt;
    public uint    mouseData;
    public uint    flags;
    public uint    time;
    public nint    dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public nint   hwnd;
    public uint   message;
    public nuint  wParam;
    public nint   lParam;
    public uint   time;
    public POINT  pt;
}

[StructLayout(LayoutKind.Sequential)]
public struct MINMAXINFO
{
    public POINT ptReserved;
    public POINT ptMaxSize;
    public POINT ptMaxPosition;
    public POINT ptMinTrackSize;
    public POINT ptMaxTrackSize;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public uint  cbSize;
    public RECT  rcMonitor;
    public RECT  rcWork;
    public uint  dwFlags;
}

// ── Win32 API Signatures ───────────────────────────────────────────────────
public static partial class User32
{
    public delegate nint HookProc(int nCode, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint Msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint FindWindowW([MarshalAs(UnmanagedType.LPWStr)] string? lpClassName, [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hWnd);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    // Window style constants
    public const uint SWP_NOSIZE      = 0x0001;
    public const uint SWP_NOMOVE      = 0x0002;
    public const uint SWP_NOZORDER    = 0x0004;
    public const uint SWP_NOACTIVATE  = 0x0010;
    public const uint SWP_SHOWWINDOW  = 0x0040;
    public const int  SW_RESTORE      = 9;
    public const int  SW_SHOWNOACTIVATE = 4;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // Hook type constants
    public const int WH_MOUSE_LL    = 14;
    public const int WH_KEYBOARD_LL = 13;

    // Mouse messages
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP   = 0x0208;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP   = 0x020C;

    // Keyboard messages
    public const uint WM_KEYDOWN    = 0x0100;
    public const uint WM_KEYUP      = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP   = 0x0105;
    public const uint WM_QUIT       = 0x0012;
    public const uint WM_GETMINMAXINFO = 0x0024;

    // Virtual keys
    public const int VK_BROWSER_BACK    = 0xA6;
    public const int VK_BROWSER_FORWARD = 0xA7;
    public const int VK_ALT             = 0x12;
    public const int VK_CTRL            = 0x11;
    public const int VK_SHIFT           = 0x10;
    public const int VK_LWIN            = 0x5B;
    public const int VK_RWIN            = 0x5C;
    public const int VK_PRIOR           = 0x21; // Page Up
    public const int VK_NEXT            = 0x22; // Page Down
    public const int VK_LEFT            = 0x25;
    public const int VK_RIGHT           = 0x27;

    public const int XBUTTON1 = 0x0001;
    public const int XBUTTON2 = 0x0002;
    public const int HC_ACTION = 0;

    // Window messages for close/send
    public const uint WM_CLOSE = 0x0010;

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    // Display device enumeration
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    // Cursor / hit-test
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT Point);

    // Window ancestor / validation
    // GA_ROOT(2) returns the top-level root window — needed because WindowFromPoint
    // can return child windows (browser content pane, editor area, etc.)
    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);
    public const uint GA_ROOT = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    // Thread input attachment — allows SetForegroundWindow from a background thread
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo,
        [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    // WinEvent hook (for auto-registering windows moved to zones)
    public delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax,
        nint hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    // WinEvent constants
    public const uint EVENT_SYSTEM_MOVESIZEEND   = 0x000B;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint WINEVENT_OUTOFCONTEXT      = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS    = 0x0002;

    // Extended window styles
    public const int GWL_EXSTYLE        = -20;
    public const nint WS_EX_TOOLWINDOW  = 0x00000080;
    public const nint WS_EX_TRANSPARENT = 0x00000020;
    public const nint WS_EX_LAYERED     = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint hwnd);

    // ── System Parameters (Animations) ──────────────────────────────────────
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

    public static void SetSystemAnimations(bool enabled)
    {
        try
        {
            uint val = enabled ? 1u : 0u;
            bool success = SystemParametersInfoW(SPI_SETCLIENTAREAANIMATION, 0, (nint)val, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Logger.Info($"[User32] System animations set to {enabled} (Success: {success})");
        }
        catch (Exception ex)
        {
            Logger.Error($"[User32] Failed to set system animations: {ex.Message}");
        }
    }
}

public static class DpiHelper
{
    private static readonly nint DPI_AWARENESS_CONTEXT_UNAWARE = new(-1);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    public static nint SetUnaware() => SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_UNAWARE);
    public static void Restore(nint prev) { if (prev != nint.Zero) SetThreadDpiAwarenessContext(prev); }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAY_DEVICE
{
    public uint cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;
    public uint StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;
}

public static partial class Kernel32
{
    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();
}

public static partial class Dwmapi
{
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);
}
