using System.Runtime.InteropServices;

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
    /// PHASE 4: Apply a zone rect to a window with full DWM compensation.
    ///
    /// Sequence (as per Python script):
    /// 1. Restore if minimized or maximized (cannot resize maximized windows)
    /// 2. Force focus with anti-block sequence
    /// 3. Apply DWM-compensated coordinates with SWP_SHOWWINDOW
    /// 4. Verify using VISUAL rect (not logical) and retry if needed
    /// </summary>
    // Lógica recomendada para DwmHelper.ApplyZoneRect
    public static async Task ApplyZoneRectWithCompensation(nint hwnd, RECT targetRect)
    {
        // 1. Obtener el rect visual (lo que el usuario ve)
        NativeMethods.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frameRect, (uint)Marshal.SizeOf<RECT>());
        
        // 2. Obtener el rect lógico (el que incluye la sombra)
        NativeMethods.GetWindowRect(hwnd, out RECT windowRect);

        // 3. Calcular el "offset" o margen de la sombra
        int offsetX = (windowRect.Left - frameRect.Left);
        int offsetY = (windowRect.Top - frameRect.Top);
        int offsetW = (windowRect.Right - windowRect.Left) - (frameRect.Right - frameRect.Left);
        int offsetH = (windowRect.Bottom - windowRect.Top) - (frameRect.Bottom - frameRect.Top);

        // 4. Ajustar el target añadiendo esos márgenes
        RECT compensatedRect = new RECT(
            targetRect.Left + offsetX,
            targetRect.Top + offsetY,
            targetRect.Width + offsetW,
            targetRect.Height + offsetH
        );

        // 5. Mover la ventana
        await WindowManager.SnapToRectAsync(hwnd, compensatedRect);
    }

    /// <summary>
    /// Apply the logical rect to SetWindowPos and verify the visual result converges.
    /// Uses SWP_SHOWWINDOW so the window is always brought to visible state.
    /// </summary>
    private static async Task<bool> SnapToZoneAsync(nint hwnd, RECT zoneRect, int retries)
    {
        for (int attempt = 0; attempt < retries; attempt++)
        {
            // Compute DWM-compensated logical rect for this attempt
            RECT logicalRect = CompensateForSetWindowPos(hwnd, zoneRect);

            // SetWindowPos with SWP_SHOWWINDOW to ensure the window is visible and sized
            User32.SetWindowPos(
                hwnd,
                nint.Zero,
                logicalRect.Left,
                logicalRect.Top,
                logicalRect.Width,
                logicalRect.Height,
                SWP_SHOW_MOVE_SIZE);  // SWP_SHOWWINDOW — critical for correct sizing

            await Task.Delay(attempt == 0 ? 200 : 350); // First try faster, retries slower

            // Verify using the VISUAL rect (DWM), not logical
            int hr = Dwmapi.DwmGetWindowAttribute(
                hwnd,
                Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT actualVisual,
                (uint)Marshal.SizeOf<RECT>());

            RECT checkRect = hr == 0 ? actualVisual : GetLogicalRectFallback(hwnd);

            if (VisualRectsClose(checkRect, zoneRect, threshold: 12))
            {
                Console.WriteLine($"[DwmHelper] Snap converged on attempt {attempt + 1}. Visual: [{checkRect.Left},{checkRect.Top},{checkRect.Right},{checkRect.Bottom}]");
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
           Math.Abs(a.Top  - b.Top)  <= threshold &&
           Math.Abs(a.Right  - b.Right)  <= threshold &&
           Math.Abs(a.Bottom - b.Bottom) <= threshold;

    /// <summary>
    /// PHASE 4 - Force Focus anti-block sequence.
    /// Direct port of the Python anti-block chain:
    ///   SwitchToThisWindow → ALT key hack → SetForegroundWindow + BringWindowToTop → TOPMOST toggle
    /// </summary>
    public static void ForceFocus(nint hwnd)
    {
        try
        {
            // 1. SwitchToThisWindow (undocumented but reliable for foreground forcing)
            SwitchToThisWindow(hwnd, true);

            // 2. ALT key hack: simulating ALT press/release temporarily unlocks
            //    Windows' SetForegroundWindow restriction when called from a non-foreground thread
            keybd_event(VK_MENU, 0, 0, 0);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);

            // 3. SetForegroundWindow + BringWindowToTop
            User32.SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);

            // 4. TOPMOST toggle: briefly make topmost then revert —
            //    this forces the window manager to reorder the Z-stack reliably
            User32.SetWindowPos(hwnd, HWND_TOPMOST,   0, 0, 0, 0, SWP_TOPMOST_FLAGS);
            User32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DwmHelper] ForceFocus error: {ex.Message}");
        }
    }
}
