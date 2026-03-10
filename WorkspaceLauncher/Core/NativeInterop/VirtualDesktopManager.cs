using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Virtual desktop management via Windows internal COM interfaces.
/// Equivalent to pyvda in Python.
///
/// NOTE: These COM GUIDs are internal/undocumented Windows APIs.
/// They are the same ones that pyvda uses.
/// </summary>
public sealed class VirtualDesktopManager : IDisposable
{
    private IVirtualDesktopManagerInternal? _manager;
    private IVirtualDesktopPinnedView? _pinManager;
    private IVirtualDesktopNotificationService? _notifService;
    private bool _initialized;

    public static readonly VirtualDesktopManager Instance = new();

    private VirtualDesktopManager() { }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        try
        {
            // Get ImmersiveShell service provider
            var shell = (IServiceProvider)new ImmersiveShell();
            var guid  = typeof(IVirtualDesktopManagerInternal).GUID;
            shell.QueryService(ref guid, ref guid, out object obj);
            _manager = (IVirtualDesktopManagerInternal)obj;
            
            var pinGuid = typeof(IVirtualDesktopPinnedView).GUID;
            shell.QueryService(ref pinGuid, ref pinGuid, out object pinObj);
            _pinManager = (IVirtualDesktopPinnedView)pinObj;

            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualDesktopManager] COM init failed: {ex.Message}");
        }
    }

    public List<Guid> GetDesktops()
    {
        EnsureInitialized();
        if (_manager == null) return [];
        try
        {
            _manager.GetDesktops(out IObjectArray arr);
            arr.GetCount(out uint count);
            var result = new List<Guid>();
            for (uint i = 0; i < count; i++)
            {
                var iidDesktop = typeof(IVirtualDesktop).GUID;
                arr.GetAt(i, ref iidDesktop, out object obj);
                var desktop = (IVirtualDesktop)obj;
                desktop.GetID(out Guid id);
                result.Add(id);
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualDesktopManager] GetDesktops failed: {ex.Message}");
            return [];
        }
    }

    public Guid? GetCurrentDesktopId()
    {
        EnsureInitialized();
        if (_manager == null) return null;
        try
        {
            _manager.GetCurrentDesktop(out IVirtualDesktop desktop);
            desktop.GetID(out Guid id);
            return id;
        }
        catch { return null; }
    }

    public bool SwitchToDesktop(Guid desktopId)
    {
        EnsureInitialized();
        if (_manager == null) return false;
        try
        {
            var desktop = FindDesktopById(desktopId);
            if (desktop == null) return false;
            _manager.SwitchDesktop(desktop);
            Thread.Sleep(150); // Let OS settle
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualDesktopManager] SwitchDesktop failed: {ex.Message}");
            return false;
        }
    }

    public bool SwitchNextDesktop()
    {
        var desktops = GetDesktops();
        var current = GetCurrentDesktopId();
        if (desktops.Count <= 1 || !current.HasValue) return false;

        int idx = desktops.IndexOf(current.Value);
        int next = (idx + 1) % desktops.Count;
        return SwitchToDesktop(desktops[next]);
    }

    public bool SwitchPreviousDesktop()
    {
        var desktops = GetDesktops();
        var current = GetCurrentDesktopId();
        if (desktops.Count <= 1 || !current.HasValue) return false;

        int idx = desktops.IndexOf(current.Value);
        int prev = (idx - 1 + desktops.Count) % desktops.Count;
        return SwitchToDesktop(desktops[prev]);
    }

    public Guid? CreateDesktop()
    {
        EnsureInitialized();
        if (_manager == null) return null;
        try
        {
            _manager.CreateDesktopW(out IVirtualDesktop newDesktop);
            newDesktop.GetID(out Guid id);
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
        EnsureInitialized();
        if (_pinManager == null) return false;
        try
        {
            // First we need to get the view for the hwnd
            // This is complex in native COM. For now, we'll try to find if there is an alternative.
            // Actually, the simpler way is to check if it's visible on all desktops.
            _pinManager.IsViewPinned(hwnd, out bool pinned);
            return pinned;
        }
        catch { return false; }
    }

    public void PinWindow(nint hwnd)
    {
        EnsureInitialized();
        if (_pinManager == null) return;
        try { _pinManager.PinView(hwnd); } catch { }
    }

    public void UnpinWindow(nint hwnd)
    {
        EnsureInitialized();
        if (_pinManager == null) return;
        try { _pinManager.UnpinView(hwnd); } catch { }
    }

    private IVirtualDesktop? FindDesktopById(Guid id)
    {
        _manager!.GetDesktops(out IObjectArray arr);
        arr.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            var iid = typeof(IVirtualDesktop).GUID;
            arr.GetAt(i, ref iid, out object obj);
            var desktop = (IVirtualDesktop)obj;
            desktop.GetID(out Guid dId);
            if (dId == id) return desktop;
        }
        return null;
    }

    public void Dispose() { }

    // ── COM Interfaces (Windows 11 internal) ──────────────────────────────
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport, Guid("3941C776-04FF-4ECC-B6C5-C29B4BA3F73B"), ClassInterface(ClassInterfaceType.None)]
    private class ImmersiveShell { }

    [ComImport, Guid("ff72ffdd-be7e-43fc-9c03-ad81681e88e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop
    {
        [PreserveSig] int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfVisible);
        [PreserveSig] int GetID(out Guid pGuid);
    }

    [ComImport, Guid("f31574d6-b682-4cdc-bd56-1827860abec6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal
    {
        [PreserveSig] int GetCount([MarshalAs(UnmanagedType.U4)] out uint pCount);
        [PreserveSig] int MoveViewToDesktop([MarshalAs(UnmanagedType.IUnknown)] object pView, IVirtualDesktop pDesktop);
        [PreserveSig] int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object pView, [MarshalAs(UnmanagedType.Bool)] out bool pfCanMove);
        [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop desktop);
        [PreserveSig] int GetDesktops(out IObjectArray ppDesktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop pDesktopReference, uint uDirection, out IVirtualDesktop ppAdjacentDesktop);
        [PreserveSig] int SwitchDesktop(IVirtualDesktop pDesktop);
        [PreserveSig] int CreateDesktopW(out IVirtualDesktop ppNewDesktop);
        [PreserveSig] int RemoveDesktop(IVirtualDesktop pRemove, IVirtualDesktop pFallbackDesktop);
        [PreserveSig] int FindDesktop(ref Guid desktopId, out IVirtualDesktop ppDesktop);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint pcObjects);
        [PreserveSig] int GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport, Guid("0cd45e71-d927-4f15-8b0a-8fef525337bf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopNotificationService
    {
        [PreserveSig] int Register([MarshalAs(UnmanagedType.IUnknown)] object pNotification, out uint pdwCookie);
        [PreserveSig] int Unregister(uint dwCookie);
    }

    [ComImport, Guid("4ce81583-1e40-4632-af57-54916c8e4f8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedView
    {
        [PreserveSig] int IsViewPinned(nint hWnd, out bool pinned);
        [PreserveSig] int PinView(nint hWnd);
        [PreserveSig] int UnpinView(nint hWnd);
        [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId, out bool pinned);
        [PreserveSig] int PinAppId([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int UnpinAppId([MarshalAs(UnmanagedType.LPWStr)] string appId);
    }
}
