using System.Runtime.InteropServices;
using System.Text;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.Utils;

public class MonitorInfo
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";         // Human-readable display name
    public string DeviceName { get; set; } = "";    // e.g. \\.\DISPLAY1
    public string HardwareId { get; set; } = "";    // Full DeviceID path
    public string PtName { get; set; } = "";        // What PT calls "monitor" (e.g. SDC41B6)
    public string PtInstance { get; set; } = "";    // What PT calls "monitor-instance" (e.g. 4&1d653659&0&UID8388688)
    public int    MonitorNumber { get; set; }       // What PT uses as "monitor-number"
    public string SerialNumber { get; set; } = "";  // Serial from PT if available
    public RECT Bounds { get; set; }
    public RECT WorkArea { get; set; }
    public int  Scale { get; set; }                // DPI Scale percentage (e.g. 100, 125, 175)
    public bool IsPrimary { get; set; }
}

public static class MonitorManager
{
    private static List<MonitorInfo> _cache = [];
    private static DateTime _lastRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public static void InvalidateCache() => _lastRefresh = DateTime.MinValue;

    public static List<MonitorInfo> GetActiveMonitors()
    {
        if (_cache.Count > 0 && (DateTime.Now - _lastRefresh) < CacheDuration)
            return _cache;

        var monitors = new List<MonitorInfo>();
        var wmiData = GetMonitorMetadataWmi();

        // Ensure physical pixel coordinates
        nint prevCtx = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        User32.EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (User32.GetMonitorInfoW(hMonitor, ref mi))
            {
                string deviceName = GetDeviceName(hMonitor);

                var info = new MonitorInfo
                {
                    Handle = hMonitor.ToString(),
                    Bounds = lprcMonitor,
                    WorkArea = mi.rcWork,
                    IsPrimary = (mi.dwFlags & 1) != 0,
                    DeviceName = deviceName,
                    Name = "",                             // filled below: WMI → DeviceString → PtName → fallback
                    MonitorNumber = monitors.Count + 1
                };

                // Get Hardware Metadata from the display devices
                var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (User32.EnumDisplayDevicesW(deviceName, 0, ref dd, 0))
                {
                    // Adapter info...
                }

                var md = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                for (uint i = 0; User32.EnumDisplayDevicesW(deviceName, i, ref md, 1); i++)
                {
                    if (!string.IsNullOrEmpty(md.DeviceID))
                    {
                        info.HardwareId = md.DeviceID;
                        var hashParts = md.DeviceID.Split('#');
                        if (hashParts.Length >= 3)
                        {
                            info.PtName = hashParts[1];
                            info.PtInstance = hashParts[2];
                        }
                        
                        // Try to find matching WMI data to get Serial Number
                        // DeviceID looks like: \\?\DISPLAY#SDC41B6#4&1d653659&0&UID8388688#{...}
                        // InstanceName looks like: DISPLAY\SDC41B6\4&1d653659&0&UID8388688_0
                        string? matchKey = null;
                        if (hashParts.Length >= 3) matchKey = $"{hashParts[0].Replace(@"\\?\", "")}\\{hashParts[1]}\\{hashParts[2]}".ToUpperInvariant();
                        
                        if (matchKey != null)
                        {
                            foreach (var kvp in wmiData)
                            {
                                if (kvp.Key.ToUpperInvariant().Contains(matchKey))
                                {
                                    info.SerialNumber = kvp.Value.Serial;
                                    if (!string.IsNullOrEmpty(kvp.Value.Name)) info.Name = kvp.Value.Name;
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(md.DeviceString) && string.IsNullOrEmpty(info.Name))
                        {
                            info.Name = md.DeviceString;
                        }
                    }
                    if (!string.IsNullOrEmpty(info.HardwareId)) break;
                    md = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                }

                int w = lprcMonitor.Right - lprcMonitor.Left;
                int h = lprcMonitor.Bottom - lprcMonitor.Top;

                // Fallback chain: WMI UserFriendlyName → DeviceString (already applied above)
                //   → PtName (hardware model code, e.g. "AUS2723") → deviceName → "Pantalla N"
                if (string.IsNullOrEmpty(info.Name) && !string.IsNullOrEmpty(info.PtName))
                    info.Name = info.PtName;
                if (string.IsNullOrEmpty(info.Name) && !string.IsNullOrEmpty(deviceName))
                    info.Name = deviceName;
                if (string.IsNullOrEmpty(info.Name))
                    info.Name = $"Pantalla {monitors.Count + 1}";

                GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                info.Scale = (int)Math.Round(dpiX * 100.0 / 96.0);

                Console.WriteLine($"[MonitorManager] Monitor: {info.Name} | Scale={info.Scale}% | PtName={info.PtName} | Serial={info.SerialNumber} | PtInst={info.PtInstance}");
                monitors.Add(info);
            }
            return true;
        }, nint.Zero);

        if (prevCtx != nint.Zero) SetThreadDpiAwarenessContext(prevCtx);
        
        _cache = monitors;
        _lastRefresh = DateTime.Now;
        return monitors;
    }

    private class WmiModData { public string Name = ""; public string Serial = ""; }
    private static Dictionary<string, WmiModData> GetMonitorMetadataWmi()
    {
        var result = new Dictionary<string, WmiModData>();
        try
        {
            // Note: System.Management requires a reference to System.Management.dll
            using var searcher = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");
            foreach (System.Management.ManagementBaseObject queryObj in searcher.Get())
            {
                var data = new WmiModData();
                var nameArr = (ushort[])queryObj["UserFriendlyName"];
                if (nameArr != null) foreach (var c in nameArr) { if (c == 0) break; data.Name += (char)c; }
                
                var serialArr = (ushort[])queryObj["SerialNumberID"];
                if (serialArr != null) foreach (var c in serialArr) { if (c == 0) break; data.Serial += (char)c; }

                string inst = queryObj["InstanceName"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(inst)) result[inst] = data;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MonitorManager] WMI Error: {ex.Message}"); }
        return result;
    }

    private static string GetDeviceName(nint hMonitor)
    {
        var mi = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        if (GetMonitorInfoW_Ex(hMonitor, ref mi))
        {
            return mi.szDevice;
        }
        return "";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW_Ex(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}


