using System.Runtime.InteropServices;
using System.Diagnostics;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Virtual desktop management via Windows internal COM interfaces.
/// Supports multiple Windows 11 builds (22H2, 23H2, 24H2+) with automatic
/// detection and keyboard-simulation fallback.
/// </summary>
public sealed class VirtualDesktopManager : IDisposable
{
    private object? _manager;    // Typed as object since the interface varies by build
    private IServiceProvider? _shell; // Shell reference for querying additional services
    private bool _initialized;
    private bool _initAttempted;
    private BuildVariant _variant = BuildVariant.Unknown;
    private List<Guid> _cachedDesktops = new();
    private DateTime _desktopsCacheTime = DateTime.MinValue;
    private IApplicationViewCollection? _viewCollection;
    private IVirtualDesktopPinnedApps? _pinnedApps;
    private readonly object _pinLock = new();
    // Cache desktop list for 600ms — avoids repeated COM round-trips during validation/launch
    // (ResolveDesktopGuid is called per item and would otherwise do a COM call each time).
    // Invalidated on CreateDesktop/SwitchToDesktop calls.
    private static readonly TimeSpan DesktopsCacheTtl = TimeSpan.FromMilliseconds(600);

    public static readonly VirtualDesktopManager Instance = new();

    public bool IsAvailable => _initialized;
    public string? InitError { get; private set; }

    private enum BuildVariant { Unknown, Legacy, Modern, KeyboardFallback }

    private VirtualDesktopManager() { }

    public void ReportStatus()
    {
        EnsureInitialized();
        if (_initialized)
            Logger.Success($"[VirtualDesktopManager] Operativo ({_variant})");
        else
            Logger.Warn($"[VirtualDesktopManager] {InitError ?? "No disponible"}");
    }

    private static int GetWindowsBuild()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("CurrentBuild") is string buildStr && int.TryParse(buildStr, out int build))
                return build;
        }
        catch { }
        return 0;
    }

    private void EnsureInitialized()
    {
        if (_initialized || _initAttempted) return;
        _initAttempted = true;

        try
        {
            var shell = (IServiceProvider)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"))!)!;
            _shell = shell;

            var mgrGuid = new Guid("A2000A25-C6C3-43D4-9C78-577F32D065C8");
            int hr = shell.QueryService(ref mgrGuid, ref mgrGuid, out object mgrObj);
            
            if (hr == 0 && mgrObj != null)
            {
                _manager = mgrObj;
                int build = GetWindowsBuild();
                
                // Modern: vtable with GetAllCurrentDesktops (24H2+, build 25309+)
                // Legacy: vtable for standard 22H2/23H2 (no GetAllCurrentDesktops)
                if (build >= 25309)
                {
                    _variant = BuildVariant.Modern;
                }
                else
                {
                    _variant = BuildVariant.Legacy;
                }

                _initialized = true;
                Logger.Success($"[VirtualDesktopManager] COM initialized successfully (variant: {_variant}, build: {build})");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] COM Init Failed: {ex.Message}");
        }

        _variant = BuildVariant.KeyboardFallback;
        InitError = "COM initialization failed. Using keyboard simulation.";
        Logger.Warn($"[VirtualDesktopManager] Fallback: {InitError}");
    }

    /// <summary>
    /// Lazily initializes the IApplicationViewCollection and IVirtualDesktopPinnedApps
    /// COM services needed for window pinning. Stable across all Windows 11 builds.
    /// </summary>
    private bool EnsurePinningServices()
    {
        if (_viewCollection != null && _pinnedApps != null) return true;
        EnsureInitialized();
        if (_shell == null) return false;

        lock (_pinLock)
        {
            if (_viewCollection == null)
            {
                try
                {
                    var vcGuid = typeof(IApplicationViewCollection).GUID;
                    _shell.QueryService(ref vcGuid, ref vcGuid, out object vcObj);
                    _viewCollection = (IApplicationViewCollection)vcObj;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[VDM] IApplicationViewCollection init failed: {ex.Message}");
                    return false;
                }
            }
            if (_pinnedApps == null)
            {
                try
                {
                    var paGuid = typeof(IVirtualDesktopPinnedApps).GUID;
                    _shell.QueryService(ref paGuid, ref paGuid, out object paObj);
                    _pinnedApps = (IVirtualDesktopPinnedApps)paObj;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[VDM] IVirtualDesktopPinnedApps init failed: {ex.Message}");
                    return false;
                }
            }
            return _viewCollection != null && _pinnedApps != null;
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public List<Guid> GetDesktops()
    {
        EnsureInitialized();

        // Return cached list if still fresh — avoids COM round-trips on repeated calls
        if (_cachedDesktops.Count > 0 && (DateTime.UtcNow - _desktopsCacheTime) < DesktopsCacheTtl)
            return new List<Guid>(_cachedDesktops);

        var result = new List<Guid>();
        try
        {
            if (_manager != null)
            {
                IObjectArray? arr = null;
                if (_variant == BuildVariant.Modern)
                {
                    int hr = ((IVirtualDesktopManagerInternal_Modern)_manager).GetDesktops(out arr);
                    if (hr != 0 || arr == null) hr = ((IVirtualDesktopManagerInternal_Modern)_manager).GetAllCurrentDesktops(out arr);
                }
                else if (_variant == BuildVariant.Legacy)
                {
                    int hr = ((IVirtualDesktopManagerInternal_Legacy)_manager).GetDesktops(out arr);
                    if (hr != 0) Logger.Error($"[VirtualDesktopManager] GetDesktops COM returned HR: 0x{hr:X}");
                }

                if (arr != null)
                {
                    arr.GetCount(out uint count);
                    var iidDesktop = _variant == BuildVariant.Modern ? typeof(IVirtualDesktop_Modern).GUID : typeof(IVirtualDesktop_Legacy).GUID;
                    for (uint i = 0; i < count; i++)
                    {
                        int hrAt = arr.GetAt(i, ref iidDesktop, out object obj);
                        if (hrAt != 0 || obj == null) continue;
                        GetDesktopId(obj, out Guid id);
                        result.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] GetDesktops COM path failed: {ex.Message}");
        }

        // Registry fallback
        var regDesktops = GetDesktopIDsFromRegistry();
        if (regDesktops.Count > result.Count)
        {
            if (result.Count > 0)
                Logger.Info($"[VirtualDesktopManager] Registry has more desktops ({regDesktops.Count}) than COM ({result.Count}). Using Registry.");
            result = regDesktops;
        }
        
        if (result.Count == 0) result = regDesktops;

        _cachedDesktops = result;
        _desktopsCacheTime = DateTime.UtcNow;
        Logger.Info($"[VirtualDesktopManager] Found {result.Count} desktops total.");
        return result;
    }

    public Guid? GetCurrentDesktopId()
    {
        EnsureInitialized();
        Guid? id = null;
        if (_manager != null)
        {
            try
            {
                if (_variant == BuildVariant.Modern)
                {
                    ((IVirtualDesktopManagerInternal_Modern)_manager).GetCurrentDesktop(out IVirtualDesktop_Modern d);
                    GetDesktopId(d, out Guid g);
                    id = g;
                }
                else if (_variant == BuildVariant.Legacy)
                {
                    ((IVirtualDesktopManagerInternal_Legacy)_manager).GetCurrentDesktop(out IVirtualDesktop_Legacy d);
                    GetDesktopId(d, out Guid g);
                    id = g;
                }
            }
            catch { }
        }

        if (id == null)
        {
            id = GetCurrentDesktopIdFromRegistry();
        }
        return id;
    }

    public async Task<bool> WaitForDesktopSwitchAsync(Guid targetId, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var current = GetCurrentDesktopId();
            if (current == targetId) return true;
            await Task.Delay(100);
        }
        return false;
    }

    public bool IsWindowOnCurrentDesktop(nint hwnd)
    {
        try
        {
            var desktopId = GetWindowDesktopId(hwnd);
            if (desktopId == null) return true; // Fail safe

            var current = GetCurrentDesktopId();
            return desktopId == current;
        }
        catch { return true; }
    }

    public Guid? GetWindowDesktopId(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return null;
        
        try
        {
            // The standard IVirtualDesktopManager works for this across all builds
            var standardMgr = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
            standardMgr.GetWindowDesktopId(hwnd, out Guid id);
            return id;
        }
        catch { return null; }
    }

    /// <summary>
    /// Forces a window to the current active desktop if it's not already there.
    /// This is the key to preventing "rubber-banding" jump-backs.
    /// </summary>
    public bool MoveWindowToCurrentDesktop(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return false;
        
        var current = GetCurrentDesktopId();
        if (!current.HasValue) return false;

        var windowDesktop = GetWindowDesktopId(hwnd);
        if (windowDesktop == current.Value) return true;

        Logger.Info($"[VDM] MoveWindowToCurrentDesktop: Moving {hwnd} from {windowDesktop} to {current.Value}");
        return MoveWindowToDesktop(hwnd, current.Value);
    }

    public bool MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        if (!User32.IsWindow(hwnd)) return false;
        
        try
        {
            // The standard official IVirtualDesktopManager is often not registered on Win11 24H2
            // We'll try it, but if it fails we don't log it as an error if we have an internal manager.
            var standardMgr = (IVirtualDesktopManager?)null;
            try { standardMgr = (IVirtualDesktopManager)new VirtualDesktopManagerClass(); } catch {}

            if (standardMgr != null)
            {
                int hr = standardMgr.MoveWindowToDesktop(hwnd, ref desktopId);
                if (hr == 0)
                {
                    Logger.Success($"[VirtualDesktopManager] MoveWindowToDesktop: Ventana {hwnd} movida vía API estándar.");
                    return true;
                }
            }
            
            // If standard fails or is missing, use the internal one
            return MoveWindowToDesktopInternal(hwnd, desktopId);
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] MoveWindowToDesktop Global Exception: {ex.Message}");
            return false;
        }
    }

    public bool MoveWindowToDesktopInternal(nint hwnd, Guid desktopId)
    {
        if (!User32.IsWindow(hwnd)) return false;
        
        // Then try the internal one which is more robust for modern Windows
        if (!EnsurePinningServices()) return false;
        
        try
        {
            var desktop = FindDesktopById(desktopId);
            if (desktop == null) return false;

            int hr = _viewCollection!.GetViewForHwnd(hwnd, out object view);
            if (hr != 0 || view == null) return false;

            if (_variant == BuildVariant.Modern)
                hr = ((IVirtualDesktopManagerInternal_Modern)_manager!).MoveViewToDesktop(view, (IVirtualDesktop_Modern)desktop);
            else if (_variant == BuildVariant.Legacy)
                hr = ((IVirtualDesktopManagerInternal_Legacy)_manager!).MoveViewToDesktop(view, (IVirtualDesktop_Legacy)desktop);

            if (hr == 0)
            {
                Logger.Success($"[VirtualDesktopManager] MoveWindowToDesktopInternal: Ventana {hwnd} movida a {desktopId}");
                return true;
            }

            Logger.Warn($"[VirtualDesktopManager] MoveInternal falló con HR: 0x{hr:X}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] MoveInternal Exception: {ex.Message}");
        }
        return false;
    }

    private Guid? GetCurrentDesktopIdFromRegistry()
    {
        try
        {
            string sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString();
            string[] paths = {
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{sessionId}\VirtualDesktops",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops"
            };

            foreach (var path in paths)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
                var val = key?.GetValue("CurrentVirtualDesktop");
                if (val is byte[] b && b.Length == 16) return new Guid(b);
            }
        }
        catch { }
        return null;
    }

    public bool SwitchToDesktop(Guid desktopId)
    {
        EnsureInitialized();
        if (_manager == null) return FallbackSwitch(desktopId);

        try
        {
            var desktop = FindDesktopById(desktopId);
            if (desktop == null || _manager == null) 
            {
                Logger.Info($"[VirtualDesktopManager] No COM desktop object for {desktopId}. Using keyboard fallback.");
                return FallbackSwitch(desktopId);
            }

            try
            {
                int hr = 0;
                if (_variant == BuildVariant.Modern)
                {
                    Logger.Info($"[VirtualDesktopManager] Calling SwitchDesktop (Modern) for {desktopId}");
                    hr = ((IVirtualDesktopManagerInternal_Modern)_manager).SwitchDesktop((IVirtualDesktop_Modern)desktop);
                }
                else if (_variant == BuildVariant.Legacy)
                {
                    Logger.Info($"[VirtualDesktopManager] Calling SwitchDesktop (Legacy) for {desktopId}");
                    hr = ((IVirtualDesktopManagerInternal_Legacy)_manager).SwitchDesktop((IVirtualDesktop_Legacy)desktop);
                }

                if (hr != 0)
                {
                    Logger.Error($"[VirtualDesktopManager] SwitchDesktop COM returned HR: 0x{hr:X}. Falling back to keyboard simulation.");
                    return FallbackSwitch(desktopId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[VirtualDesktopManager] SwitchDesktop COM exception: {ex.Message}. Falling back to keyboard simulation.");
                return FallbackSwitch(desktopId);
            }

            Thread.Sleep(150);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] SwitchToDesktop failed: {ex.Message}");
            return false;
        }
    }

    private bool FallbackSwitch(Guid targetId)
    {
        var desktops = GetDesktops();
        var current = GetCurrentDesktopId();
        
        if (desktops.Count <= 1 || !current.HasValue)
        {
             // If we really don't know where we are, just send one direction
             return SimulateDesktopSwitch(forward: true);
        }

        int fromIdx = desktops.IndexOf(current.Value);
        int toIdx = desktops.IndexOf(targetId);
        
        if (fromIdx == -1 || toIdx == -1) return false;
        if (fromIdx == toIdx) return true;

        int diff = toIdx - fromIdx;
        bool forward = diff > 0;
        int steps = Math.Abs(diff);

        // Circular optimization for simulation:
        // If we are at 1 (idx 0) and want to go to 3 (idx 2) in a 3-desktop setup,
        // it's 2 steps Forward OR 1 step Backward.
        // Standard Windows doesn't wrap around, so we usually HAVE to step.
        // However, if the user describes 1 -> 2 -> 3, they want to reach it.
        // If 'steps' is large, we check if going the 'other way' would be shorter
        // BUT only if we assume some local wrapping. Since we don't, we stick to the direct path.

        Logger.Info($"[VirtualDesktopManager] FallbackSwitch: From {fromIdx} to {toIdx} ({steps} steps)");
        
        if (steps > 1)
        {
            return SimulateMultipleDesktopSwitches(forward, steps);
        }
        else
        {
            return SimulateDesktopSwitch(forward);
        }
    }

    private static bool SimulateMultipleDesktopSwitches(bool forward, int steps)
    {
        try
        {
            byte vkDirection = forward ? (byte)User32.VK_RIGHT : (byte)User32.VK_LEFT;
            
            // Sequence: [Win Down, Ctrl Down], [Arrow Down, Arrow Up] x N, [Ctrl Up, Win Up]
            var inputs = new List<INPUT>();

            // Key down: Win + Ctrl
            inputs.Add(MakeKeyInput(User32.VK_LWIN, 0));
            inputs.Add(MakeKeyInput(User32.VK_CTRL, 0));

            for (int i = 0; i < steps; i++)
            {
                inputs.Add(MakeKeyInput(vkDirection, 0));
                inputs.Add(MakeKeyInput(vkDirection, KEYEVENTF_KEYUP));
            }

            // Key up: Ctrl + Win
            inputs.Add(MakeKeyInput(User32.VK_CTRL, KEYEVENTF_KEYUP));
            inputs.Add(MakeKeyInput(User32.VK_LWIN, KEYEVENTF_KEYUP));

            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            if (sent != inputs.Count)
            {
                Logger.Error($"[VirtualDesktopManager] SimulateMultiple: SendInput sent {sent}/{inputs.Count}.");
                return false;
            }

            Logger.Info($"[VirtualDesktopManager] SimulateMultiple ({steps} steps {(forward ? "Forward" : "Backward")}) sequence sent.");
            Thread.Sleep(150 + (steps * 50));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] Multiple switch failed: {ex.Message}");
            return false;
        }
    }

    public bool SwitchNextDesktop()
    {
        EnsureInitialized();
        var desktops = GetDesktops();
        var current = GetCurrentDesktopId();
        
        if (desktops.Count <= 1 || !current.HasValue) 
        {
            Logger.Warn("[VirtualDesktopManager] SwitchNext: Limited desktop info. Sending bare simulation.");
            return SimulateDesktopSwitch(forward: true);
        }

        int idx = desktops.IndexOf(current.Value);
        int next = (idx + 1) % desktops.Count;
        Logger.Info($"[VirtualDesktopManager] SwitchNext: Index {idx} -> {next} (Total: {desktops.Count})");
        return SwitchToDesktop(desktops[next]);
    }

    public bool SwitchPreviousDesktop()
    {
        EnsureInitialized();
        var desktops = GetDesktops();
        var current = GetCurrentDesktopId();
        
        if (desktops.Count <= 1 || !current.HasValue)
        {
            Logger.Warn("[VirtualDesktopManager] SwitchPrev: Limited desktop info. Sending bare simulation.");
            return SimulateDesktopSwitch(forward: false);
        }

        int idx = desktops.IndexOf(current.Value);
        int prev = (idx - 1 + desktops.Count) % desktops.Count;
        Logger.Info($"[VirtualDesktopManager] SwitchPrev: Index {idx} -> {prev} (Total: {desktops.Count})");
        return SwitchToDesktop(desktops[prev]);
    }

    public Guid? CreateDesktop()
    {
        EnsureInitialized();
        if (_manager == null) return null;
        _desktopsCacheTime = DateTime.MinValue; // Invalidate cache — a new desktop was created
        try
        {
            if (_variant == BuildVariant.Modern)
            {
                ((IVirtualDesktopManagerInternal_Modern)_manager!).CreateDesktopW(out IVirtualDesktop_Modern d);
                GetDesktopId(d, out Guid id);
                return id;
            }
            else if (_variant == BuildVariant.Legacy)
            {
                ((IVirtualDesktopManagerInternal_Legacy)_manager!).CreateDesktopW(out IVirtualDesktop_Legacy d);
                GetDesktopId(d, out Guid id);
                return id;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualDesktopManager] CreateDesktop failed: {ex.Message}");
            return null;
        }
    }

    public bool IsWindowPinned(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return false;
        if (!EnsurePinningServices()) return false;
        try
        {
            int hr = _viewCollection!.GetViewForHwnd(hwnd, out object view);
            if (hr != 0 || view == null) return false;
            _pinnedApps!.IsPinnedView(view, out bool isPinned);
            return isPinned;
        }
        catch { return false; }
    }

    public void PinWindow(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return;
        if (!EnsurePinningServices()) return;
        
        // Retry logic: sometimes the window isn't immediately "ready" in the shell's view collection
        Task.Run(async () => {
            int attempts = 3;
            while (attempts > 0)
            {
                bool success = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    try {
                        int hr = _viewCollection!.GetViewForHwnd(hwnd, out object view);
                        if (hr == 0 && view != null)
                        {
                            hr = _pinnedApps!.PinView(view);
                            if (hr == 0) {
                                Logger.Success($"[VDM] Window {hwnd} pinned successfully (stickied).");
                                success = true;
                            } else {
                                Logger.Warn($"[VDM] PinView returned hr=0x{hr:X} for HWND {hwnd}");
                            }
                        } else {
                            Logger.Warn($"[VDM] PinWindow: no IApplicationView yet for HWND {hwnd} (hr=0x{hr:X})");
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"[VDM] PinWindow attempt error: {ex.Message}");
                    }
                });

                if (success) break;
                attempts--;
                if (attempts > 0) await Task.Delay(500);
            }
        });
    }

    public void UnpinWindow(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return;
        if (!EnsurePinningServices()) return;
        try
        {
            int hr = _viewCollection!.GetViewForHwnd(hwnd, out object view);
            if (hr != 0 || view == null) return;
            _pinnedApps!.UnpinView(view);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[VDM] UnpinWindow failed for HWND {hwnd}: {ex.Message}");
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private Guid GetDesktopIID()
    {
        return _variant == BuildVariant.Modern ? typeof(IVirtualDesktop_Modern).GUID : typeof(IVirtualDesktop_Legacy).GUID;
    }

    private void GetDesktopId(object desktop, out Guid id)
    {
        if (_variant == BuildVariant.Modern)
            ((IVirtualDesktop_Modern)desktop).GetID(out id);
        else if (_variant == BuildVariant.Legacy)
            ((IVirtualDesktop_Legacy)desktop).GetID(out id);
        else
            id = Guid.Empty;
    }

    private object? FindDesktopById(Guid id)
    {
        if (_manager == null) return null;

        try
        {
            int hr = 0;
            object? result = null;

            if (_variant == BuildVariant.Modern)
            {
                hr = ((IVirtualDesktopManagerInternal_Modern)_manager).FindDesktop(ref id, out IVirtualDesktop_Modern desktop);
                result = desktop;
            }
            else if (_variant == BuildVariant.Legacy)
            {
                hr = ((IVirtualDesktopManagerInternal_Legacy)_manager).FindDesktop(ref id, out IVirtualDesktop_Legacy desktop);
                result = desktop;
            }

            if (hr == 0 && result != null)
            {
                return result;
            }

            Logger.Warn($"[VirtualDesktopManager] FindDesktop COM failed for {id} (HR: 0x{hr:X}). Falling back to list iteration...");

            IObjectArray? arr = null;
            if (_variant == BuildVariant.Modern)
            {
                hr = ((IVirtualDesktopManagerInternal_Modern)_manager).GetDesktops(out arr);
                if (hr != 0 || arr == null) hr = ((IVirtualDesktopManagerInternal_Modern)_manager).GetAllCurrentDesktops(out arr);
            }
            else if (_variant == BuildVariant.Legacy)
            {
                hr = ((IVirtualDesktopManagerInternal_Legacy)_manager).GetDesktops(out arr);
            }

            if (arr != null)
            {
                arr.GetCount(out uint count);
                var iidDesktop = GetDesktopIID();
                for (uint i = 0; i < count; i++)
                {
                    int hrAt = arr.GetAt(i, ref iidDesktop, out object obj);
                    if (hrAt != 0 || obj == null) continue;
                    GetDesktopId(obj, out Guid dId);
                    Logger.Info($"[VirtualDesktopManager] FindDesktopById: Comparando {dId} vs {id}");
                    if (dId == id) return obj;
                }
            }
            
            Logger.Warn($"[VirtualDesktopManager] FindDesktopById: Desktop {id} not found after all attempts.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] FindDesktopById exception: {ex.Message}");
        }
        return null;
    }

    private List<Guid> GetDesktopIDsFromRegistry()
    {
        var result = new List<Guid>();
        try
        {
            string sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString();
            string[] paths = {
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{sessionId}\VirtualDesktops",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops"
            };

            foreach (var path in paths)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
                if (key?.GetValue("VirtualDesktopIDs") is byte[] ids && ids.Length >= 16)
                {
                    for (int i = 0; i < ids.Length; i += 16)
                    {
                        if (i + 16 > ids.Length) break;
                        byte[] guidBytes = new byte[16];
                        Array.Copy(ids, i, guidBytes, 0, 16);
                        result.Add(new Guid(guidBytes));
                    }
                    if (result.Count > 0) break;
                }
            }
        }
        catch { }
        return result;
    }

    // ── Keyboard simulation fallback ─────────────────────────────────────────

    private static bool SimulateDesktopSwitch(bool forward)
    {
        try
        {
            byte vkDirection = forward ? (byte)User32.VK_RIGHT : (byte)User32.VK_LEFT;

            // Press Win + Ctrl + Arrow
            var inputs = new INPUT[6];

            // Key down: Win
            inputs[0] = MakeKeyInput(User32.VK_LWIN, 0);
            // Key down: Ctrl
            inputs[1] = MakeKeyInput(User32.VK_CTRL, 0);
            // Key down: Arrow
            inputs[2] = MakeKeyInput(vkDirection, 0);
            // Key up: Arrow
            inputs[3] = MakeKeyInput(vkDirection, KEYEVENTF_KEYUP);
            // Key up: Ctrl
            inputs[4] = MakeKeyInput(User32.VK_CTRL, KEYEVENTF_KEYUP);
            // Key up: Win
            inputs[5] = MakeKeyInput(User32.VK_LWIN, KEYEVENTF_KEYUP);

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                Logger.Error($"[VirtualDesktopManager] SimulateDesktopSwitch: SendInput only sent {sent} of {inputs.Length} inputs. Error: {Marshal.GetLastWin32Error()}");
                return false;
            }
            Logger.Info($"[VirtualDesktopManager] SimulateDesktopSwitch ({ (forward ? "Next" : "Prev") }) sequence sent successfully.");
            Thread.Sleep(150);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualDesktopManager] Keyboard fallback failed: {ex.Message}");
            return false;
        }
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static INPUT MakeKeyInput(int vk, uint flags)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki = new KEYBDINPUT
        {
            wVk = (ushort)vk,
            wScan = 0,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = 0
        };
        return input;
    }

    public void Dispose() { }

    // ══════════════════════════════════════════════════════════════════════════
    // COM Interfaces - shared
    // ══════════════════════════════════════════════════════════════════════════

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint pcObjects);
        [PreserveSig] int GetAt(uint uiIndex, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COM Interfaces - Windows 11 Legacy (builds < 25309)
    // ══════════════════════════════════════════════════════════════════════════

    [ComImport, Guid("ff72ffdd-be7e-43fc-9c03-ad81681e88e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop_Legacy
    {
        [PreserveSig] int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int GetID(out Guid pGuid);
    }

    [ComImport, Guid("f31574d6-b682-4cdc-bd56-1827860abec6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal_Legacy
    {
        [PreserveSig] int GetCount(out uint pCount);
        [PreserveSig] int MoveViewToDesktop([MarshalAs(UnmanagedType.IUnknown)] object pView, IVirtualDesktop_Legacy pDesktop);
        [PreserveSig] int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfCanMove);
        [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop_Legacy desktop);
        [PreserveSig] int GetDesktops(out IObjectArray ppDesktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop_Legacy pDesktopReference, uint uDirection, out IVirtualDesktop_Legacy ppAdjacentDesktop);
        [PreserveSig] int SwitchDesktop(IVirtualDesktop_Legacy pDesktop);
        [PreserveSig] int CreateDesktopW(out IVirtualDesktop_Legacy ppNewDesktop);
        [PreserveSig] int RemoveDesktop(IVirtualDesktop_Legacy pRemove, IVirtualDesktop_Legacy pFallbackDesktop);
        [PreserveSig] int FindDesktop(ref Guid desktopId, out IVirtualDesktop_Legacy ppDesktop);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COM Interfaces - Windows 11 Modern (builds >= 25309, 24H2+)
    // ══════════════════════════════════════════════════════════════════════════

    [ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop_Modern
    {
        [PreserveSig] int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int GetID(out Guid pGuid);
    }

    [ComImport, Guid("536d3495-b208-4cc9-ae26-7df56efda23a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal_Modern
    {
        [PreserveSig] int GetCount(out uint pCount);
        [PreserveSig] int MoveViewToDesktop([MarshalAs(UnmanagedType.IUnknown)] object pView, IVirtualDesktop_Modern pDesktop);
        [PreserveSig] int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfCanMove);
        [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop_Modern desktop);
        [PreserveSig] int GetAllCurrentDesktops(out IObjectArray ppDesktops); // Added in build 25309+
        [PreserveSig] int GetDesktops(out IObjectArray ppDesktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop_Modern pDesktopReference, uint uDirection, out IVirtualDesktop_Modern ppAdjacentDesktop);
        [PreserveSig] int SwitchDesktop(IVirtualDesktop_Modern pDesktop);
        [PreserveSig] int CreateDesktopW(out IVirtualDesktop_Modern ppNewDesktop);
        [PreserveSig] int MoveDesktop(IVirtualDesktop_Modern pDesktop, uint nIndex);
        [PreserveSig] int RemoveDesktop(IVirtualDesktop_Modern pRemove, IVirtualDesktop_Modern pFallbackDesktop);
        [PreserveSig] int FindDesktop(ref Guid desktopId, out IVirtualDesktop_Modern ppDesktop);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Standard IVirtualDesktopManager (Public COM Interface)
    // ══════════════════════════════════════════════════════════════════════════

    [ComImport, Guid("aa509086-5d76-480e-80f2-19352c335693")]
    private class VirtualDesktopManagerClass { }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Window Pinning COM Interfaces (stable across all Windows 11 builds)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieves the IApplicationView (shell view object) for any top-level HWND.
    /// Required to call IVirtualDesktopPinnedApps.PinView().
    /// </summary>
    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int GetViews(out IObjectArray views);
        [PreserveSig] int GetViewsByZOrder(out IObjectArray views);
        [PreserveSig] int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IObjectArray views);
        [PreserveSig] int GetViewForHwnd(nint hwnd, [MarshalAs(UnmanagedType.IUnknown)] out object view);
        [PreserveSig] int GetViewForApplication([MarshalAs(UnmanagedType.IUnknown)] object application, [MarshalAs(UnmanagedType.IUnknown)] out object view);
        [PreserveSig] int GetViewForAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.IUnknown)] out object view);
        [PreserveSig] int GetViewInFocus([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    /// <summary>
    /// Pins/unpins IApplicationView objects to all virtual desktops (PiP, sticky windows).
    /// Vtable: IsPinnedView, PinView, UnpinView, IsPinnedApp, PinApp, UnpinApp.
    /// </summary>
    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedApps
    {
        [PreserveSig] int IsPinnedView([MarshalAs(UnmanagedType.IUnknown)] object view, [MarshalAs(UnmanagedType.Bool)] out bool isPinned);
        [PreserveSig] int PinView([MarshalAs(UnmanagedType.IUnknown)] object view);
        [PreserveSig] int UnpinView([MarshalAs(UnmanagedType.IUnknown)] object view);
        [PreserveSig] int IsPinnedApp([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.Bool)] out bool isPinned);
        [PreserveSig] int PinApp([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int UnpinApp([MarshalAs(UnmanagedType.LPWStr)] string appId);
    }
}
