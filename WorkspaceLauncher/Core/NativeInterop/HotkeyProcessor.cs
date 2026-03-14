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
    
    public event Action? OnOpenZoneEditorRequested;
    
    public bool Enabled { get; set; } = true;

    private HotkeyProcessor() { }
    
    private struct ParsedHotkey
    {
        public int Vk;
        public bool Alt, Ctrl, Shift, Win;
        public string? Button; // for mouse buttons
    }

    private readonly Dictionary<string, ParsedHotkey> _parsedCache = [];
    private string _lastConfigHash = "";

    private void RefreshCacheIfNeeded()
    {
        var config = ConfigManager.Instance.Config;
        // Simple hash of current hotkey strings to detect changes
        var currentHash = $"{config.Hotkeys.CycleForward}|{config.Hotkeys.CycleBackward}|{config.Hotkeys.DesktopCycleFwd}|{config.Hotkeys.DesktopCycleBwd}|{config.Hotkeys.UtilReloadLayouts}|{config.Hotkeys.OpenZoneEditor}";
        
        if (currentHash == _lastConfigHash) return;
        
        _parsedCache.Clear();
        ParseAndCache(config.Hotkeys.CycleForward);
        ParseAndCache(config.Hotkeys.CycleBackward);
        ParseAndCache(config.Hotkeys.DesktopCycleFwd);
        ParseAndCache(config.Hotkeys.DesktopCycleBwd);
        ParseAndCache(config.Hotkeys.UtilReloadLayouts);
        ParseAndCache(config.Hotkeys.OpenZoneEditor);
        
        _lastConfigHash = currentHash;
    }

    private void ParseAndCache(string? combo)
    {
        if (string.IsNullOrEmpty(combo) || _parsedCache.ContainsKey(combo)) return;
        
        var parts = combo.ToLowerInvariant().Split('+').Select(p => p.Trim()).ToArray();
        var parsed = new ParsedHotkey
        {
            Alt = parts.Contains("alt"),
            Ctrl = parts.Contains("ctrl"),
            Shift = parts.Contains("shift"),
            Win = parts.Contains("win")
        };

        string key = parts.Last();
        if (key is "x1" or "x2" or "mbutton" or "mouse_left" or "mouse_right")
        {
            parsed.Button = key;
        }
        else
        {
            parsed.Vk = key switch
            {
                "pagedown" => User32.VK_NEXT,
                "pageup" => User32.VK_PRIOR,
                "l" => 0x4C,
                "left" => User32.VK_LEFT,
                "right" => User32.VK_RIGHT,
                "up" => 0x26,
                "down" => 0x28,
                "tab" => 0x09,
                "space" => 0x20,
                "z" => 0x5A,
                "enter" or "return" => 0x0D,
                _ when key.Length == 1 && char.IsLetterOrDigit(key[0]) => char.ToUpper(key[0]),
                _ => 0
            };
        }
        _parsedCache[combo] = parsed;
    }

    public void Initialize(GlobalHookManager hookManager)
    {
        _hookManager = hookManager;

        // Connect side buttons (modifiers captured at hook time)
        _hookManager.OnX1Down += HandleX1;
        _hookManager.OnX2Down += HandleX2;
        _hookManager.OnMiddleDown += HandleMiddle;
        _hookManager.OnKeyDown += HandleKeyDown;

        _hookManager.CheckXMapped = (button, alt, ctrl, shift, win) =>
        {
            if (!Enabled) return false;
            var config = ConfigManager.Instance.Config;

            // Ctrl-only = browser back/forward passthrough (don't suppress)
            if (ctrl && !alt && !shift && !win) return false;

            RefreshCacheIfNeeded();

            // Check Desktop Cycle
            if (config.Hotkeys.DesktopCycleEnabled)
            {
                if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleFwd, button, alt, ctrl, shift, win)) return true;
                if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleBwd, button, alt, ctrl, shift, win)) return true;
            }

            // Alt-only + X = hover zone cycling
            if (config.Hotkeys.ZoneCycleEnabled && alt && !ctrl && !shift && !win)
                return true;

            // Any other configured combo for zone cycling
            if (config.Hotkeys.ZoneCycleEnabled)
            {
                if (IsHotKeyActiveCached(config.Hotkeys.CycleForward, button, alt, ctrl, shift, win)) return true;
                if (IsHotKeyActiveCached(config.Hotkeys.CycleBackward, button, alt, ctrl, shift, win)) return true;
            }
            return false;
        };

        _hookManager.CheckKeyMapped = (vk, alt, ctrl, shift, win) =>
        {
            if (!Enabled) return false;
            RefreshCacheIfNeeded();
            
            var config = ConfigManager.Instance.Config;
            if (config.Hotkeys.ZoneCycleEnabled)
            {
                if (IsHotKeyMatchCached(config.Hotkeys.CycleForward, vk, alt, ctrl, shift, win)) return true;
                if (IsHotKeyMatchCached(config.Hotkeys.CycleBackward, vk, alt, ctrl, shift, win)) return true;
            }
            if (config.Hotkeys.DesktopCycleEnabled)
            {
                if (IsHotKeyMatchCached(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win)) return true;
                if (IsHotKeyMatchCached(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win)) return true;
            }
            if (IsHotKeyMatchCached(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win)) return true;
            if (IsHotKeyMatchCached(config.Hotkeys.OpenZoneEditor, vk, alt, ctrl, shift, win)) return true;
            return false;
        };
    }

    private bool IsHotKeyMatchCached(string? combo, int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo) || !_parsedCache.TryGetValue(combo, out var p)) return false;
        if (p.Button != null) return false; // Keyboard match only
        
        return p.Vk == vk && p.Alt == alt && p.Ctrl == ctrl && p.Shift == shift && p.Win == win;
    }

    private bool IsHotKeyActiveCached(string? combo, string button, bool alt, bool ctrl, bool shift, bool win)
    {
        if (string.IsNullOrEmpty(combo) || !_parsedCache.TryGetValue(combo, out var p)) return false;
        if (p.Button == null) return false; // Mouse match only
        
        return p.Button == button.ToLowerInvariant() && p.Alt == alt && p.Ctrl == ctrl && p.Shift == shift && p.Win == win;
    }

    private void HandleKeyDown(int vk, bool alt, bool ctrl, bool shift, bool win)
    {
        if (!Enabled) return;
        RefreshCacheIfNeeded();
        
        var config = ConfigManager.Instance.Config;

        // Zone cycling (keyboard)
        if (config.Hotkeys.ZoneCycleEnabled)
        {
            if (IsHotKeyMatchCached(config.Hotkeys.CycleForward, vk, alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] CycleForward hotkey detected");
                var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
                if (key != null) ZoneCycler.Instance.CycleForward(key);
                return;
            }
            if (IsHotKeyMatchCached(config.Hotkeys.CycleBackward, vk, alt, ctrl, shift, win))
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
            if (IsHotKeyMatchCached(config.Hotkeys.DesktopCycleFwd, vk, alt, ctrl, shift, win))
            {
                VirtualDesktopManager.Instance.SwitchNextDesktop();
                return;
            }
            if (IsHotKeyMatchCached(config.Hotkeys.DesktopCycleBwd, vk, alt, ctrl, shift, win))
            {
                VirtualDesktopManager.Instance.SwitchPreviousDesktop();
                return;
            }
        }

        // Utility: reload layouts
        if (IsHotKeyMatchCached(config.Hotkeys.UtilReloadLayouts, vk, alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] Reloading layouts...");
            FancyZones.FancyZonesReader.SyncCacheFromDisk();
            ConfigManager.Instance.Load();
        }

        // Utility: open zone editor
        if (IsHotKeyMatchCached(config.Hotkeys.OpenZoneEditor, vk, alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] Opening Zone Editor...");
            OnOpenZoneEditorRequested?.Invoke();
        }
    }

    private void HandleX1(bool alt, bool ctrl, bool shift, bool win)
    {
        if (!Enabled) return;
        RefreshCacheIfNeeded();
        var config = ConfigManager.Instance.Config;

        // Alt-only + X1 → hover zone cycling (window under cursor)
        if (config.Hotkeys.ZoneCycleEnabled && alt && !ctrl && !shift && !win)
        {
            Console.WriteLine("[HotkeyProcessor] Alt+X1 -> CycleZoneForward (hover)");
            var key = GetZoneKeyUnderCursor() ?? ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }

        // Configured modifier combos (keyboard-style zone cycling via x1)
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleForward, "x1", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X1+mod -> CycleZoneForward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleBackward, "x1", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X1+mod -> CycleZoneBackward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Desktop cycling
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleFwd, "x1", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] X1 -> SwitchNextDesktop");
                VirtualDesktopManager.Instance.SwitchNextDesktop();
                return;
            }
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleBwd, "x1", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] X1 -> SwitchPreviousDesktop");
                VirtualDesktopManager.Instance.SwitchPreviousDesktop();
                return;
            }
        }
    }

    private void HandleX2(bool alt, bool ctrl, bool shift, bool win)
    {
        if (!Enabled) return;
        RefreshCacheIfNeeded();
        var config = ConfigManager.Instance.Config;

        // Alt-only + X2 → hover zone cycling backward (window under cursor)
        if (config.Hotkeys.ZoneCycleEnabled && alt && !ctrl && !shift && !win)
        {
            Console.WriteLine("[HotkeyProcessor] Alt+X2 -> CycleZoneBackward (hover)");
            var key = GetZoneKeyUnderCursor() ?? ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Configured modifier combos
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleForward, "x2", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X2+mod -> CycleZoneForward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleBackward, "x2", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] X2+mod -> CycleZoneBackward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Desktop cycling
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleFwd, "x2", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] X2 -> SwitchNextDesktop");
                VirtualDesktopManager.Instance.SwitchNextDesktop();
                return;
            }
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleBwd, "x2", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] X2 -> SwitchPreviousDesktop");
                VirtualDesktopManager.Instance.SwitchPreviousDesktop();
                return;
            }
        }
    }

    private void HandleMiddle(bool alt, bool ctrl, bool shift, bool win)
    {
        if (!Enabled) return;
        RefreshCacheIfNeeded();
        var config = ConfigManager.Instance.Config;

        // Hover zone cycling
        if (config.Hotkeys.ZoneCycleEnabled && alt && !ctrl && !shift && !win)
        {
            Console.WriteLine("[HotkeyProcessor] Alt+MButton -> CycleZoneForward (hover)");
            var key = GetZoneKeyUnderCursor() ?? ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }

        // Configured modifier combos
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleForward, "mbutton", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] mbutton -> CycleZoneForward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleForward(key);
            return;
        }
        if (config.Hotkeys.ZoneCycleEnabled && IsHotKeyActiveCached(config.Hotkeys.CycleBackward, "mbutton", alt, ctrl, shift, win))
        {
            Console.WriteLine("[HotkeyProcessor] mbutton -> CycleZoneBackward");
            var key = ZoneCycler.Instance.DetectActiveWindowZoneKey();
            if (key != null) ZoneCycler.Instance.CycleBackward(key);
            return;
        }

        // Desktop cycling
        if (config.Hotkeys.DesktopCycleEnabled)
        {
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleFwd, "mbutton", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] mbutton -> SwitchNextDesktop");
                VirtualDesktopManager.Instance.SwitchNextDesktop();
                return;
            }
            if (IsHotKeyActiveCached(config.Hotkeys.DesktopCycleBwd, "mbutton", alt, ctrl, shift, win))
            {
                Console.WriteLine("[HotkeyProcessor] mbutton -> SwitchPreviousDesktop");
                VirtualDesktopManager.Instance.SwitchPreviousDesktop();
                return;
            }
        }
    }

    /// <summary>
    /// Returns the zone key of the window currently under the mouse cursor.
    /// First checks the registered zone stack (fast), then falls back to
    /// position-based detection (so newly-moved windows are found immediately).
    /// </summary>
    private static ZoneStack.ZoneKey? GetZoneKeyUnderCursor()
    {
        if (!User32.GetCursorPos(out POINT pt)) return null;
        nint hwnd = User32.WindowFromPoint(pt);
        if (hwnd == 0) return null;
        // Normalize to top-level window — WindowFromPoint returns child windows
        // (e.g. browser tab content, VS Code editor pane) which aren't in the stack
        nint root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        if (root != 0) hwnd = root;
        // Fast path: already registered in a zone stack
        var registered = ZoneStack.Instance.FindKeyForHwnd(hwnd);
        if (registered != null) return registered;
        // Slow path: detect by position (catches windows dragged to a zone but not yet in stack)
        return ZoneCycler.DetectZoneByPosition(hwnd);
    }
}


