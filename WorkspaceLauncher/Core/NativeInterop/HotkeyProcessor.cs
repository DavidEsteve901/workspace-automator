using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Processes keyboard and mouse hotkeys based on the global hooks.
/// This handles logic like switching desktops or cycling zones.
/// </summary>
public sealed class HotkeyProcessor
{
    private GlobalHookManager? _hookManager;

    public static readonly HotkeyProcessor Instance = new();

    private HotkeyProcessor() { }

    public void Initialize(GlobalHookManager hookManager)
    {
        _hookManager = hookManager;
        
        // Connect side buttons
        _hookManager.OnX1Down += HandleX1;
        _hookManager.OnX2Down += HandleX2;
        _hookManager.OnKeyDown += HandleKeyDown;

        _hookManager.CheckXMapped = (button, alt, ctrl, shift) =>
        {
            var config = ConfigManager.Instance.Config;
            if (config.Hotkeys.DesktopCycleEnabled)
            {
                if (!alt && !ctrl && !shift) return true;
                // Also Check if mod cycle mouse is enabled
                if (IsHotKeyActive(config.Hotkeys.MouseCycleFwd, "Mouse4", alt, ctrl, shift, false)) return true;
                if (IsHotKeyActive(config.Hotkeys.MouseCycleBwd, "Mouse5", alt, ctrl, shift, false)) return true;
            }
            return false;
        };

        _hookManager.CheckKeyMapped = (vk, alt, ctrl, shift, win) =>
        {
            var config = ConfigManager.Instance.Config;
            if (IsHotKeyMatch(config.Hotkeys.CycleForward, vk, alt, ctrl, shift, win)) return true;
            if (IsHotKeyMatch(config.Hotkeys.CycleBackward, vk, alt, ctrl, shift, win)) return true;
            if (IsHotKeyMatch(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win) && config.Hotkeys.DesktopCycleEnabled) return true;
            if (IsHotKeyMatch(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win) && config.Hotkeys.DesktopCycleEnabled) return true;
            if (IsHotKeyMatch(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win)) return true;
            return false;
        };

        // In the future, we could add keyboard combinations here too
        // for "cycle forward" / "cycle backward"
    }

    private void HandleX1()
    {
        // Check configuration to see if desktop cycle is enabled
        var config = ConfigManager.Instance.Config;
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            Console.WriteLine("[HotkeyProcessor] X1 detected -> SwitchNextDesktop");
            VirtualDesktopManager.Instance.SwitchNextDesktop();
        }
    }

    private void HandleX2()
    {
        var config = ConfigManager.Instance.Config;
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            Console.WriteLine("[HotkeyProcessor] X2 detected -> SwitchPreviousDesktop");
            VirtualDesktopManager.Instance.SwitchPreviousDesktop();
        }
    }

    private void HandleKeyDown(int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        var config = ConfigManager.Instance.Config;

        if (IsHotKeyMatch(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win) && config.Hotkeys.DesktopCycleEnabled)
            VirtualDesktopManager.Instance.SwitchNextDesktop();
        else if (IsHotKeyMatch(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win) && config.Hotkeys.DesktopCycleEnabled)
            VirtualDesktopManager.Instance.SwitchPreviousDesktop();
        else if (IsHotKeyMatch(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win))
            ConfigManager.Instance.Load(); // Reload config/layouts
        // Add more handlers as needed
    }

    private bool IsHotKeyMatch(string? combo, int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo)) return false;
        var parts = combo.ToLowerInvariant().Split('+');
        
        bool reqCtrl  = parts.Contains("ctrl");
        bool reqAlt   = parts.Contains("alt");
        bool reqShift = parts.Contains("shift");
        bool reqWin   = parts.Contains("win");
        string key    = parts.Last();

        if (alt != reqAlt || ctrl != reqCtrl || shift != reqShift || win != reqWin) return false;

        return key switch
        {
            "pagedown" => vk == User32.VK_NEXT,
            "pageup"   => vk == User32.VK_PRIOR,
            "l"        => vk == 0x4C,
            "left"     => vk == User32.VK_LEFT,
            "right"    => vk == User32.VK_RIGHT,
            _          => false
        };
    }

    private bool IsHotKeyActive(string? combo, string button, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo)) return false;
        var parts = combo.ToLowerInvariant().Split('+');
        return parts.Contains(button.ToLowerInvariant()) && 
               parts.Contains("alt") == alt && 
               parts.Contains("ctrl") == ctrl && 
               parts.Contains("shift") == shift;
    }
}
