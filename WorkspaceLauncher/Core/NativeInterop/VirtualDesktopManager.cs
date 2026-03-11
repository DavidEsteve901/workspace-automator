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

    private enum BuildVariant { Unknown, Build22H2, Build24H2, KeyboardFallback }

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

        int build = GetWindowsBuild();
        string arch = RuntimeInformation.OSArchitecture.ToString();
        int inputSize = Marshal.SizeOf<INPUT>();
        Logger.Info($"[VirtualDesktopManager] Windows build: {build}, Arch: {arch}, InputSize: {inputSize}");

        // Try build-specific COM initialization
        if (build >= 26000)
            _initialized = TryInit24H2();

        if (!_initialized && build >= 22000)
            _initialized = TryInit22H2();

        // Try all variants as fallback
        if (!_initialized) _initialized = TryInit24H2();
        if (!_initialized) _initialized = TryInit22H2();

        if (_initialized)
        {
            Logger.Info($"[VirtualDesktopManager] COM initialized (variant: {_variant})");
        }
        else
        {
            _variant = BuildVariant.KeyboardFallback;
            InitError = "COM init failed for all known interface versions. Using keyboard simulation fallback.";
            Logger.Warn($"[VirtualDesktopManager] {InitError}");
        }
    }

    // ── COM Initialization Strategies ────────────────────────────────────────

    private bool TryInit22H2()
    {
        try
        {
            var shell = (IServiceProvider)new ImmersiveShell22H2();
            _shell = shell;
            var mgrGuid = typeof(IVirtualDesktopManagerInternal_22H2).GUID;
            shell.QueryService(ref mgrGuid, ref mgrGuid, out object mgrObj);
            _manager = (IVirtualDesktopManagerInternal_22H2)mgrObj;
            _variant = BuildVariant.Build22H2;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] 22H2 init failed: {ex.Message}");
            return false;
        }
    }

    private bool TryInit24H2()
    {
        try
        {
            var shell = (IServiceProvider)new ImmersiveShell24H2();
            _shell = shell;
            var mgrGuid = typeof(IVirtualDesktopManagerInternal_24H2).GUID;
            shell.QueryService(ref mgrGuid, ref mgrGuid, out object mgrObj);
            _manager = (IVirtualDesktopManagerInternal_24H2)mgrObj;
            _variant = BuildVariant.Build24H2;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] 24H2 init failed: {ex.Message}");
            return false;
        }
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
                if (_variant == BuildVariant.Build24H2)
                {
                    int hr = ((IVirtualDesktopManagerInternal_24H2)_manager).GetDesktops(out arr);
                    if (hr != 0 || arr == null)
                    {
                        Logger.Warn($"[VirtualDesktopManager] GetDesktops failed (HR: 0x{hr:X}). Trying GetAllCurrentDesktops...");
                        hr = ((IVirtualDesktopManagerInternal_24H2)_manager).GetAllCurrentDesktops(out arr);
                    }
                    
                    if (hr != 0) Logger.Error($"[VirtualDesktopManager] GetDesktops/GetAll COM returned HR: 0x{hr:X}");
                }
                else
                {
                    int hr = ((IVirtualDesktopManagerInternal_22H2)_manager).GetDesktops(out arr);
                    if (hr != 0) Logger.Error($"[VirtualDesktopManager] GetDesktops COM returned HR: 0x{hr:X}");
                }

                if (arr != null)
                {
                    arr.GetCount(out uint count);
                    var iidDesktop = GetDesktopIID();
                    for (uint i = 0; i < count; i++)
                    {
                        arr.GetAt(i, ref iidDesktop, out object obj);
                        if (obj == null) continue;
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
                object? desktop = null;
                if (_variant == BuildVariant.Build24H2)
                {
                    ((IVirtualDesktopManagerInternal_24H2)_manager).GetCurrentDesktop(out var d);
                    desktop = d;
                }
                else
                {
                    ((IVirtualDesktopManagerInternal_22H2)_manager).GetCurrentDesktop(out var d);
                    desktop = d;
                }
                
                if (desktop != null)
                {
                    GetDesktopId(desktop, out Guid g);
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

    public bool MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        if (!User32.IsWindow(hwnd)) return false;
        
        try
        {
            var standardMgr = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
            int hr = standardMgr.MoveWindowToDesktop(hwnd, ref desktopId);
            
            if (hr != 0)
            {
                Logger.Error($"[VirtualDesktopManager] MoveWindowToDesktop COM error: 0x{hr:X} for HWND {hwnd}");
                return false;
            }

            // Verification Pulse
            var actual = GetWindowDesktopId(hwnd);
            if (actual == desktopId)
            {
                Logger.Success($"[VirtualDesktopManager] Ventana {hwnd} movida correctamente a {desktopId}");
                return true;
            }
            
            Logger.Warn($"[VirtualDesktopManager] El movimiento de {hwnd} no se reflejó. Reintentando vía COM...");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"[VirtualDesktopManager] MoveWindowToDesktop Exception: {ex.Message}");
            return false;
        }
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
                if (_variant == BuildVariant.Build24H2)
                {
                    Logger.Info($"[VirtualDesktopManager] Calling SwitchDesktop (Build24H2) for {desktopId}");
                    int hr = ((IVirtualDesktopManagerInternal_24H2)_manager).SwitchDesktop((IVirtualDesktop_24H2)desktop);
                    if (hr != 0) 
                    {
                        Logger.Error($"[VirtualDesktopManager] SwitchDesktop COM returned HR: 0x{hr:X}. Falling back to keyboard simulation.");
                        return FallbackSwitch(desktopId);
                    }
                }
                else
                {
                    Logger.Info($"[VirtualDesktopManager] Calling SwitchDesktop (Build22H2) for {desktopId}");
                    int hr = ((IVirtualDesktopManagerInternal_22H2)_manager).SwitchDesktop((IVirtualDesktop_22H2)desktop);
                    if (hr != 0)
                    {
                        Logger.Error($"[VirtualDesktopManager] SwitchDesktop COM returned HR: 0x{hr:X}. Falling back to keyboard simulation.");
                        return FallbackSwitch(desktopId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[VirtualDesktopManager] SwitchDesktop COM failed: {ex.Message}. Falling back to keyboard simulation.");
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

        Logger.Info($"[VirtualDesktopManager] FallbackSwitch: From {fromIdx} to {toIdx} ({steps} steps)");
        
        for (int i = 0; i < steps; i++)
        {
            if (!SimulateDesktopSwitch(forward)) return false;
            Thread.Sleep(100);
        }
        return true;
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
            object newDesktop;
            if (_variant == BuildVariant.Build24H2)
            {
                ((IVirtualDesktopManagerInternal_24H2)_manager).CreateDesktopW(out var d);
                newDesktop = d;
            }
            else
            {
                ((IVirtualDesktopManagerInternal_22H2)_manager).CreateDesktopW(out var d);
                newDesktop = d;
            }
            GetDesktopId(newDesktop, out Guid id);
            return id;
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
        try
        {
            int hr = _viewCollection!.GetViewForHwnd(hwnd, out object view);
            if (hr != 0 || view == null)
            {
                Logger.Warn($"[VDM] PinWindow: no IApplicationView for HWND {hwnd} (hr=0x{hr:X})");
                return;
            }
            hr = _pinnedApps!.PinView(view);
            if (hr == 0)
                Logger.Success($"[VDM] Window {hwnd} pinned to all desktops.");
            else
                Logger.Warn($"[VDM] PinView returned hr=0x{hr:X} for HWND {hwnd}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[VDM] PinWindow failed for HWND {hwnd}: {ex.Message}");
        }
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
        return _variant == BuildVariant.Build24H2
            ? typeof(IVirtualDesktop_24H2).GUID
            : typeof(IVirtualDesktop_22H2).GUID;
    }

    private void GetDesktopId(object desktop, out Guid id)
    {
        if (_variant == BuildVariant.Build24H2)
            ((IVirtualDesktop_24H2)desktop).GetID(out id);
        else
            ((IVirtualDesktop_22H2)desktop).GetID(out id);
    }

    private object? FindDesktopById(Guid id)
    {
        IObjectArray arr;
        if (_variant == BuildVariant.Build24H2)
            ((IVirtualDesktopManagerInternal_24H2)_manager!).GetDesktops(out arr);
        else
            ((IVirtualDesktopManagerInternal_22H2)_manager!).GetDesktops(out arr);

        arr.GetCount(out uint count);
        var iidDesktop = GetDesktopIID();
        for (uint i = 0; i < count; i++)
        {
            arr.GetAt(i, ref iidDesktop, out object obj);
            GetDesktopId(obj, out Guid dId);
            if (dId == id) return obj;
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
    // COM Interfaces - Windows 11 22H2 (builds 22621-22631)
    // ══════════════════════════════════════════════════════════════════════════

    // ImmersiveShell CLSID for 22H2
    [ComImport, Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"), ClassInterface(ClassInterfaceType.None)]
    private class ImmersiveShell22H2 { }

    [ComImport, Guid("ff72ffdd-be7e-43fc-9c03-ad81681e88e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop_22H2
    {
        [PreserveSig] int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object pView,
            [MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int GetID(out Guid pGuid);
    }

    [ComImport, Guid("f31574d6-b682-4cdc-bd56-1827860abec6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal_22H2
    {
        [PreserveSig] int GetCount(out uint pCount);
        [PreserveSig] int MoveViewToDesktop([MarshalAs(UnmanagedType.IUnknown)] object pView, IVirtualDesktop_22H2 pDesktop);
        [PreserveSig] int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfCanMove);
        [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop_22H2 desktop);
        [PreserveSig] int GetDesktops(out IObjectArray ppDesktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop_22H2 pDesktopReference, uint uDirection, out IVirtualDesktop_22H2 ppAdjacentDesktop);
        [PreserveSig] int SwitchDesktop(IVirtualDesktop_22H2 pDesktop);
        [PreserveSig] int CreateDesktopW(out IVirtualDesktop_22H2 ppNewDesktop);
        [PreserveSig] int RemoveDesktop(IVirtualDesktop_22H2 pRemove, IVirtualDesktop_22H2 pFallbackDesktop);
        [PreserveSig] int FindDesktop(ref Guid desktopId, out IVirtualDesktop_22H2 ppDesktop);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COM Interfaces - Windows 11 24H2 (builds 26100+)
    // Note: ImmersiveShell uses same CLSID, but internal interfaces differ.
    //       24H2 added GetAllCurrentDesktops() which shifts the vtable.
    // ══════════════════════════════════════════════════════════════════════════

    // ImmersiveShell CLSID for 24H2 (same as 22H2)
    [ComImport, Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"), ClassInterface(ClassInterfaceType.None)]
    private class ImmersiveShell24H2 { }

    [ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop_24H2
    {
        [PreserveSig] int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object pView,
            [MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int GetID(out Guid pGuid);
    }

    // 24H2 vtable: GetCount, MoveViewToDesktop, CanViewMoveDesktops, GetCurrentDesktop,
    //              GetAllCurrentDesktops (NEW), GetDesktops, GetAdjacentDesktop,
    //              SwitchDesktop, CreateDesktopW, MoveDesktop, RemoveDesktop, FindDesktop
    [ComImport, Guid("88846798-1611-4BBB-8BFA-8D1C1AD0A48B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal_24H2
    {
        [PreserveSig] int GetCount(out uint pCount);
        [PreserveSig] int MoveViewToDesktop([MarshalAs(UnmanagedType.IUnknown)] object pView, IVirtualDesktop_24H2 pDesktop);
        [PreserveSig] int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfCanMove);
        [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop_24H2 desktop);
        [PreserveSig] int GetAllCurrentDesktops(out IObjectArray ppDesktops); // NEW in 24H2!
        [PreserveSig] int GetDesktops(out IObjectArray ppDesktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop_24H2 pDesktopReference, uint uDirection, out IVirtualDesktop_24H2 ppAdjacentDesktop);
        [PreserveSig] int SwitchDesktop(IVirtualDesktop_24H2 pDesktop);
        [PreserveSig] int CreateDesktopW(out IVirtualDesktop_24H2 ppNewDesktop);
        [PreserveSig] int MoveDesktop(IVirtualDesktop_24H2 pDesktop, uint nIndex);
        [PreserveSig] int RemoveDesktop(IVirtualDesktop_24H2 pRemove, IVirtualDesktop_24H2 pFallbackDesktop);
        [PreserveSig] int FindDesktop(ref Guid desktopId, out IVirtualDesktop_24H2 ppDesktop);
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
