using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.ZoneEngine;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Processes keyboard and mouse hotkeys based on the global hooks.
/// Handles desktop switching, zone cycling, and utility actions.
/// </summary>
public sealed class HotkeyProcessor
{
    private GlobalHookManager? _hookManager;

    public static readonly HotkeyProcessor Instance = new();
    
    public bool Enabled { get; set; } = true;

    private HotkeyProcessor() { }

    public void Initialize(GlobalHookManager hookManager)
    {
        _hookManager = hookManager;

        // Connect side buttons
        _hookManager.OnX1Down += HandleX1;
        _hookManager.OnX2Down += HandleX2;
        _hookManager.OnKeyDown += HandleKeyDown;

        _hookManager.CheckXMapped = (button, alt, ctrl, shift, win) =>
        {
            if (!Enabled) return false;
            var config = ConfigManager.Instance.Config;
            // Check if bare X buttons are mapped for desktop cycling
            if (config.Hotkeys.DesktopCycleEnabled && !alt && !ctrl && !shift && !win)
                return true;
            // Check if modifier combos are mapped for zone cycling
            if (config.Hotkeys.ZoneCycleEnabled)
            {
                if (IsHotKeyActive(config.Hotkeys.CycleForward, button, alt, ctrl, shift, win)) return true;
                if (IsHotKeyActive(config.Hotkeys.CycleBackward, button, alt, ctrl, shift, win)) return true;
            }
            return false;
        };

        _hookManager.CheckKeyMapped = (vk, alt, ctrl, shift, win) =>
        {
            if (!Enabled) return false;
            var config = ConfigManager.Instance.Config;
            if (config.Hotkeys.ZoneCycleEnabled)
            {
                if (IsHotKeyMatch(config.Hotkeys.CycleForward, vk, alt, ctrl, shift, win)) return true;
                if (IsHotKeyMatch(config.Hotkeys.CycleBackward, vk, alt, ctrl, shift, win)) return true;
            }
            if (config.Hotkeys.DesktopCycleEnabled)
            {
                if (IsHotKeyMatch(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win)) return true;
                if (IsHotKeyMatch(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win)) return true;
            }
            if (IsHotKeyMatch(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win)) return true;
            return false;
        };
    }

    private void HandleX1()
    {
        if (!Enabled) return;
        var config = ConfigManager.Instance.Config;

        // Read current modifier state to distinguish combos
        bool alt = (User32.GetAsyncKeyState(User32.VK_ALT) & 0x8000) != 0;
        bool ctrl = (User32.GetAsyncKeyState(User32.VK_CTRL) & 0x8000) != 0;
        bool shift = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
        bool win = (User32.GetAsyncKeyState(User32.VK_LWIN) & 0x8000) != 0 || (User32.GetAsyncKeyState(User32.VK_RWIN) & 0x8000) != 0;

        // Check zone cycling first (e.g., alt+x1)
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActive(config.Hotkeys.CycleForward, "x1", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X1+mod detected -> CycleZoneForward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActive(config.Hotkeys.CycleBackward, "x1", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X1+mod detected -> CycleZoneBackward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Bare X1 → desktop switching
        if (config.Hotkeys.DesktopCycleEnabled && !alt && !ctrl && !shift && !win)
        {
            Console.WriteLine("[HotkeyProcessor] X1 detected -> SwitchNextDesktop");
            VirtualDesktopManager.Instance.SwitchNextDesktop();
        }
    }

    private void HandleX2()
    {
        if (!Enabled) return;
        var config = ConfigManager.Instance.Config;

        bool alt = (User32.GetAsyncKeyState(User32.VK_ALT) & 0x8000) != 0;
        bool ctrl = (User32.GetAsyncKeyState(User32.VK_CTRL) & 0x8000) != 0;
        bool shift = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
        bool win = (User32.GetAsyncKeyState(User32.VK_LWIN) & 0x8000) != 0 || (User32.GetAsyncKeyState(User32.VK_RWIN) & 0x8000) != 0;

        // Check zone cycling first (e.g., alt+x2)
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActive(config.Hotkeys.CycleForward, "x2", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X2+mod detected -> CycleZoneForward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActive(config.Hotkeys.CycleBackward, "x2", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X2+mod detected -> CycleZoneBackward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Bare X2 → desktop switching
        if (config.Hotkeys.DesktopCycleEnabled && !alt && !ctrl && !shift && !win)
        {
            Console.WriteLine("[HotkeyProcessor] X2 detected -> SwitchPreviousDesktop");
            VirtualDesktopManager.Instance.SwitchPreviousDesktop();
        }
    }

    private void HandleKeyDown(int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        if (!Enabled) return;
        var config = ConfigManager.Instance.Config;

        // Zone cycling (keyboard)
        if (config.Hotkeys.ZoneCycleEnabled)
        {
            if (IsHotKeyMatch(config.Hotkeys.CycleForward, vk, alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] CycleForward hotkey detected");
                var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
                if (key != null) ZoneCycler.Instance.CycleForward(key);
                return;
            }
            if (IsHotKeyMatch(config.Hotkeys.CycleBackward, vk, alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] CycleBackward hotkey detected");
                var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
                if (key != null) ZoneCycler.Instance.CycleBackward(key);
                return;
            }
        }

        // Desktop cycling (keyboard)
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            if (IsHotKeyMatch(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win))
            {
                VirtualDesktopManager.Instance.SwitchNextDesktop();
                return;
            }
            if (IsHotKeyMatch(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win))
            {
                VirtualDesktopManager.Instance.SwitchPreviousDesktop();
                return;
            }
        }

        // Utility: reload layouts
        if (IsHotKeyMatch(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] Reloading layouts...");
            FancyZones.FancyZonesReader.SyncCacheFromDisk();
            ConfigManager.Instance.Load();
        }
    }

    private static bool IsHotKeyMatch(string? combo, int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo)) return false;
        var parts = combo.ToLowerInvariant().Split('+');

        bool reqCtrl = parts.Contains("ctrl");
        bool reqAlt = parts.Contains("alt");
        bool reqShift = parts.Contains("shift");
        bool reqWin = parts.Contains("win");
        string key = parts.Last();

        // Skip mouse button combos
        if (key is "x1" or "x2" or "mouse_left" or "mouse_right" or "mouse_middle") return false;

        if (alt != reqAlt || ctrl != reqCtrl || shift != reqShift || win != reqWin) return false;

        return key switch
        {
            "pagedown" => vk == User32.VK_NEXT,
            "pageup" => vk == User32.VK_PRIOR,
            "l" => vk == 0x4C,
            "left" => vk == User32.VK_LEFT,
            "right" => vk == User32.VK_RIGHT,
            "up" => vk == 0x26,
            "down" => vk == 0x28,
            "tab" => vk == 0x09,
            "space" => vk == 0x20,
            "enter" or "return" => vk == 0x0D,
            _ when key.Length == 1 && char.IsLetterOrDigit(key[0]) => vk == char.ToUpper(key[0]),
            _ => false
        };
    }

    private static bool IsHotKeyActive(string? combo, string button, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo)) return false;
        var parts = combo.ToLowerInvariant().Split('+');
        return parts.Contains(button.ToLowerInvariant()) &&
               parts.Contains("alt") == alt &&
               parts.Contains("ctrl") == ctrl &&
               parts.Contains("shift") == shift &&
               parts.Contains("win") == win;
    }
}
