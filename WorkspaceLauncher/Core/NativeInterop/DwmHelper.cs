using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Compensates for DWM shadow/frame offsets when positioning windows.
///
/// WHY THIS IS NEEDED:
/// Windows 10/11 windows have an invisible shadow border. When you call
/// GetWindowRect() you get LOGICAL coordinates (including shadow).
/// When you call SetWindowPos(x, y, w, h) Windows uses those LOGICAL coords.
/// But visually the content is shifted inward by the shadow size.
///
/// DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) returns the VISUAL rect
/// (what the user actually sees). The difference between logical and visual
/// is the offset we must compensate.
///
/// FORMULA (exact port from Python):
///   offset_left   = visual.Left   - logical.Left    (positive = shadow on left)
///   offset_top    = visual.Top    - logical.Top      (positive = shadow on top)
///   offset_right  = logical.Right  - visual.Right    (positive = shadow on right)
///   offset_bottom = logical.Bottom - visual.Bottom   (positive = shadow on bottom)
///
///   SetWindowPos args:
///     x      = zone.Left   - offset_left          (extend left to cover shadow)
///     y      = zone.Top    - offset_top            (extend top to cover shadow)
///     width  = zone.Width  + offset_left + offset_right   (expand to cover both shadows)
///     height = zone.Height + offset_top  + offset_bottom  (expand to cover both shadows)
/// </summary>
public static class DwmHelper
{
    // Z-order constants for TOPMOST toggle trick
    private static readonly nint HWND_TOPMOST   = new(-1);
    private static readonly nint HWND_NOTOPMOST = new(-2);

    // SWP flags
    private const uint SWP_SHOW_MOVE_SIZE  = 0x0040; // SWP_SHOWWINDOW
    private const uint SWP_TOPMOST_FLAGS   = 0x0043; // SWP_SHOWWINDOW | SWP_NOSIZE | SWP_NOMOVE

    // SwitchToThisWindow is undocumented but works perfectly for foreground forcing
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint hwnd, bool fAltTab);

    // ALT key sequence to unlock SetForegroundWindow restriction from background threads
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);
    private const byte VK_MENU = 0x12; // ALT
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);

    /// <summary>
    /// Calculate the DWM shadow offsets (logical vs visual rect difference).
    /// Returns (left, top, right, bottom) where each value is how many pixels
    /// the logical rect extends BEYOND the visual rect on that side.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) GetShadowOffsets(nint hwnd)
    {
        // Get logical rect (includes invisible shadow border)
        User32.GetWindowRect(hwnd, out RECT logicalRect);

        // Get visual rect (what the user actually sees)
        int hr = Dwmapi.DwmGetWindowAttribute(
            hwnd,
            Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT visualRect,
            (uint)Marshal.SizeOf<RECT>());

        if (hr != 0)
        {
            // DWM not compositing (rare) — no compensation needed
            return (0, 0, 0, 0);
        }

        // Calculate how far the logical rect extends beyond the visual rect
        // (positive values = shadow exists on that side)
        int offsetLeft   = visualRect.Left   - logicalRect.Left;   // e.g. +8  = shadow 8px on left
        int offsetTop    = visualRect.Top    - logicalRect.Top;     // e.g. +0  = no shadow on top
        int offsetRight  = logicalRect.Right  - visualRect.Right;   // e.g. +8  = shadow 8px on right
        int offsetBottom = logicalRect.Bottom - visualRect.Bottom;  // e.g. +8  = shadow 8px on bottom

        Console.WriteLine($"[DwmHelper] Shadow offsets for {hwnd}: L={offsetLeft} T={offsetTop} R={offsetRight} B={offsetBottom}");
        Console.WriteLine($"[DwmHelper]   Logical:  [{logicalRect.Left},{logicalRect.Top},{logicalRect.Right},{logicalRect.Bottom}]");
        Console.WriteLine($"[DwmHelper]   Visual:   [{visualRect.Left},{visualRect.Top},{visualRect.Right},{visualRect.Bottom}]");

        return (offsetLeft, offsetTop, offsetRight, offsetBottom);
    }

    /// <summary>
    /// Given the TARGET zone rect (visual/desired coordinates), calculate the
    /// LOGICAL coordinates to pass to SetWindowPos so the visual result matches the target.
    ///
    /// SetWindowPos works in LOGICAL coords, so we must EXPAND the logical rect
    /// by the shadow offsets to compensate.
    /// </summary>
    public static RECT CompensateForSetWindowPos(nint hwnd, RECT visualTarget)
    {
        var (offL, offT, offR, offB) = GetShadowOffsets(hwnd);

        // To make the visual result = visualTarget, the logical coords must be:
        //   logical.Left   = visual.Left   - offsetLeft   (extend left to hide shadow)
        //   logical.Top    = visual.Top    - offsetTop    (extend top to hide shadow)
        //   logical.Right  = visual.Right  + offsetRight  (extend right to hide shadow)
        //   logical.Bottom = visual.Bottom + offsetBottom (extend bottom to hide shadow)
        var adjusted = new RECT
        {
            Left   = visualTarget.Left   - offL,
            Top    = visualTarget.Top    - offT,
            Right  = visualTarget.Right  + offR,
            Bottom = visualTarget.Bottom + offB,
        };

        Console.WriteLine($"[DwmHelper] Zone target:  [{visualTarget.Left},{visualTarget.Top},{visualTarget.Right},{visualTarget.Bottom}] (w={visualTarget.Width},h={visualTarget.Height})");
        Console.WriteLine($"[DwmHelper] Logical call: [{adjusted.Left},{adjusted.Top},{adjusted.Right},{adjusted.Bottom}] (w={adjusted.Width},h={adjusted.Height})");
        return adjusted;
    }
    /// <summary>
    /// Returns the VISUAL bounds of a window (DWM extended frame, excluding shadow).
    /// Falls back to logical GetWindowRect if DWM is unavailable.
    /// Use this for drift comparisons — NOT GetWindowRect which returns logical (shadow-inclusive) coords.
    /// </summary>
    public static RECT GetVisualBounds(nint hwnd)
    {
        int hr = Dwmapi.DwmGetWindowAttribute(
            hwnd,
            Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT visual,
            (uint)Marshal.SizeOf<RECT>());

        if (hr == 0) return visual;

        // DWM unavailable (rare — e.g. remote desktop) — fall back to logical rect
        User32.GetWindowRect(hwnd, out RECT logical);
        return logical;
    }

    /// <summary>
    /// Public entry point: restore → focus → snap with DWM compensation.
    /// Sequence mirrors the Python script: SW_RESTORE → ALT hack → SetWindowPos compensated.
    /// If 'silent' is true, it avoids forcing focus to prevent virtual desktop jumps.
    /// </summary>
    public static async Task<bool> ApplyZoneRect(nint hwnd, RECT zoneRect, int retries = 3, bool silent = false)
    {
        // 1. Restore if minimized OR maximized — both prevent SetWindowPos from working correctly.
        //    A maximized window ignores explicit size/position calls on some Windows builds.
        bool needsRestore = IsIconic(hwnd) || IsZoomed(hwnd);
        if (needsRestore)
        {
            User32.ShowWindow(hwnd, silent ? User32.SW_SHOWNOACTIVATE : User32.SW_RESTORE);
            await Task.Delay(silent ? 150 : 280);
        }

        // 2. Force foreground (only if NOT silent)
        if (!silent)
        {
            ForceFocus(hwnd);
            await Task.Delay(150);
        }

        // 3. Snap with DWM compensation + retry
        return await SnapToZoneAsync(hwnd, zoneRect, retries, silent);
    }

    /// <summary>
    /// Apply the logical rect to SetWindowPos and verify the visual result converges.
    /// Uses SWP_SHOWWINDOW so the window is always brought to visible state.
    /// If 'silent' is true, it uses SWP_NOACTIVATE and SWP_NOZORDER to avoid stealing focus or changing Z-order.
    /// </summary>
    private static async Task<bool> SnapToZoneAsync(nint hwnd, RECT zoneRect, int retries, bool silent)
    {
        // For silent mode, we avoid SWP_SHOWWINDOW and use SWP_NOACTIVATE + SWP_NOZORDER
        // to prevent Windows from switching the current Virtual Desktop to where the window is.
        uint flags = silent 
            ? (User32.SWP_NOACTIVATE | User32.SWP_NOZORDER) 
            : (User32.SWP_SHOWWINDOW);

        for (int attempt = 0; attempt < retries; attempt++)
        {
            // Compute DWM-compensated logical rect for this attempt
            RECT logicalRect = CompensateForSetWindowPos(hwnd, zoneRect);

            // Execute positioning
            User32.SetWindowPos(
                hwnd,
                nint.Zero,
                logicalRect.Left,
                logicalRect.Top,
                logicalRect.Width,
                logicalRect.Height,
                flags);

            await Task.Delay(attempt == 0 ? 200 : 350); // First try faster, retries slower

            // Verify using the VISUAL rect (DWM), not logical
            int hr = Dwmapi.DwmGetWindowAttribute(
                hwnd,
                Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT actualVisual,
                (uint)Marshal.SizeOf<RECT>());

            RECT checkRect = hr == 0 ? actualVisual : GetLogicalRectFallback(hwnd);

            if (VisualRectsClose(checkRect, zoneRect, threshold: 1))
            {
                Logger.Info($"[DwmHelper] Snap converged on attempt {attempt + 1} (precision <= 1px).");
                return true;
            }

            Console.WriteLine($"[DwmHelper] Attempt {attempt + 1} not converged. Visual: [{checkRect.Left},{checkRect.Top},{checkRect.Right},{checkRect.Bottom}] vs Target [{zoneRect.Left},{zoneRect.Top},{zoneRect.Right},{zoneRect.Bottom}]");
        }

        return false;
    }

    private static RECT GetLogicalRectFallback(nint hwnd)
    {
        User32.GetWindowRect(hwnd, out RECT r);
        return r;
    }

    private static bool VisualRectsClose(RECT a, RECT b, int threshold)
        => Math.Abs(a.Left - b.Left) <= threshold &&
           Math.Abs(a.Top - b.Top) <= threshold &&
           Math.Abs(a.Width - b.Width) <= threshold &&
           Math.Abs(a.Height - b.Height) <= threshold;

    /// <summary>
    /// PHASE 4 - Force Focus anti-block sequence.
    /// Direct port of the Python anti-block chain:
    ///   SwitchToThisWindow → ALT key hack → SetForegroundWindow + BringWindowToTop → TOPMOST toggle
    /// </summary>
    public static void ForceFocus(nint hwnd)
    {
        try
        {
            if (!User32.IsWindow(hwnd)) return;

            // 1. SwitchToThisWindow (undocumented but reliable for foreground forcing)
            SwitchToThisWindow(hwnd, true);

            // 2. ALT key hack: simulating ALT press/release temporarily unlocks
            //    Windows' SetForegroundWindow restriction when called from a non-foreground thread
            keybd_event(VK_MENU, 0, 0, 0);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);

            // 3. AttachThreadInput trick to the FOREGROUND window (matches user's latest Preference)
            uint currentTid = Kernel32.GetCurrentThreadId();
            nint fgWin      = User32.GetForegroundWindow();
            uint fgTid      = fgWin != 0 ? User32.GetWindowThreadProcessId(fgWin, out _) : 0;
            bool attached   = fgTid != 0 && fgTid != currentTid
                              && User32.AttachThreadInput(currentTid, fgTid, true);

            // 4. SetForegroundWindow + BringWindowToTop
            User32.SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);

            if (attached)
                User32.AttachThreadInput(currentTid, fgTid, false);

            // 5. TOPMOST toggle: briefly make topmost then revert —
            //    this forces the window manager to reorder the Z-stack reliably
            User32.SetWindowPos(hwnd, HWND_TOPMOST,   0, 0, 0, 0, SWP_TOPMOST_FLAGS);
            User32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DwmHelper] ForceFocus error: {ex.Message}");
        }
    }

    /// <summary>
    /// Lightweight focus for zone cycling.
    /// - Does NOT inject Alt key events (user may be holding Alt for the gesture)
    /// - Uses AttachThreadInput to reliably call SetForegroundWindow from a background thread
    /// - SwitchToThisWindow as primary + TOPMOST toggle as Z-order backstop
    /// </summary>
    public static void FocusForCycling(nint hwnd)
    {
        try
        {
            if (!User32.IsWindow(hwnd)) return;

            if (IsIconic(hwnd))
                User32.ShowWindow(hwnd, User32.SW_RESTORE);

            // AttachThreadInput trick: attach our thread to the FOREGROUND thread
            // (not the target thread). This grants our thread "last input" rights so
            // SetForegroundWindow succeeds from a background thread.
            uint currentTid = Kernel32.GetCurrentThreadId();
            nint fgWin      = User32.GetForegroundWindow();
            uint fgTid      = fgWin != 0 ? User32.GetWindowThreadProcessId(fgWin, out _) : 0;
            bool attached   = fgTid != 0 && fgTid != currentTid
                              && User32.AttachThreadInput(currentTid, fgTid, true);

            User32.SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);

            if (attached)
                User32.AttachThreadInput(currentTid, fgTid, false);

            // TOPMOST toggle forces Z-order update even if SetForegroundWindow fails
            User32.SetWindowPos(hwnd, HWND_TOPMOST,   0, 0, 0, 0, SWP_TOPMOST_FLAGS);
            User32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DwmHelper] FocusForCycling error: {ex.Message}");
        }
    }

    public static void UseImmersiveDarkMode(nint hwnd, bool enabled)
    {
        int useDarkMode = enabled ? 1 : 0;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, (uint)sizeof(int));
    }

    /// <summary>
    /// Reads the Windows system accent color from the registry.
    /// Registry key: HKCU\SOFTWARE\Microsoft\Windows\DWM\AccentColor
    /// Format: ABGR DWORD (byte0=A, byte1=B, byte2=G, byte3=R).
    /// Returns "#RRGGBB" hex string, or empty string if unavailable.
    /// </summary>
    public static string GetWindowsAccentColor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                uint uval = unchecked((uint)raw);
                int b = (int)((uval >> 8)  & 0xFF);  // byte1 = B
                int g = (int)((uval >> 16) & 0xFF);  // byte2 = G
                int r = (int)((uval >> 24) & 0xFF);  // byte3 = R
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }
        catch { }
        return "";
    }
}
