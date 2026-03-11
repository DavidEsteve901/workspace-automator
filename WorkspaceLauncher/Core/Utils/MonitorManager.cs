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
    public bool IsPrimary { get; set; }
}

public static class MonitorManager
{
    public static List<MonitorInfo> GetActiveMonitors()
    {
        var monitors = new List<MonitorInfo>();

        // Ensure physical pixel coordinates regardless of which thread calls this.
        // Without this, background threads may get DPI-scaled logical coordinates instead
        // of physical pixels, causing zone calculations to be off on high-DPI monitors.
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
                    Name = "Pantalla " + (monitors.Count + 1),
                    MonitorNumber = monitors.Count + 1
                };

                // Get Hardware Metadata from the display devices
                string adapterName = "";
                var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                
                // First call: Get the adapter device string (GPU name)
                if (User32.EnumDisplayDevicesW(deviceName, 0, ref dd, 0))
                {
                    adapterName = dd.DeviceString ?? "";
                }

                // Second call: Get the specific monitor device attached to it (with EDD_GET_DEVICE_INTERFACE_NAME flag = 1)
                var md = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                for (uint i = 0; User32.EnumDisplayDevicesW(deviceName, i, ref md, 1); i++)
                {
                    if (!string.IsNullOrEmpty(md.DeviceID))
                    {
                        info.HardwareId = md.DeviceID;
                        
                        // DeviceID format: \\?\DISPLAY#SDC41B6#4&1d653659&0&UID8388688#{e6f07b5f-...}
                        // We need to split by '#' to get:
                        //   [0] = \\?\DISPLAY   (or MONITOR\ prefix in some cases)
                        //   [1] = SDC41B6        => PtName (what PowerToys calls "monitor")
                        //   [2] = 4&1d653659&0&UID8388688  => PtInstance (what PowerToys calls "monitor-instance")
                        //   [3] = {guid}         => interface GUID
                        var hashParts = md.DeviceID.Split('#');
                        if (hashParts.Length >= 3)
                        {
                            info.PtName = hashParts[1];       // e.g. "SDC41B6"
                            info.PtInstance = hashParts[2];   // e.g. "4&1d653659&0&UID8388688"
                        }
                        else
                        {
                            // Fallback: try backslash split for older formats like MONITOR\SDC41B6\{...}\0001
                            var bsParts = md.DeviceID.Split('\\');
                            if (bsParts.Length >= 2)
                            {
                                // Find the part that looks like a model ID (no special chars except &)
                                foreach (var part in bsParts)
                                {
                                    if (string.IsNullOrEmpty(part) || part == "?" || part == "MONITOR" || part == "DISPLAY") continue;
                                    if (part.StartsWith("{")) continue; // GUID
                                    
                                    if (!part.Contains("&"))
                                    {
                                        if (string.IsNullOrEmpty(info.PtName)) info.PtName = part;
                                    }
                                    else
                                    {
                                        if (string.IsNullOrEmpty(info.PtInstance)) info.PtInstance = part;
                                    }
                                }
                            }
                        }
                        
                        // Use monitor DeviceString for name if it's not "Generic"
                        if (!string.IsNullOrEmpty(md.DeviceString) && !md.DeviceString.Contains("Generic"))
                        {
                            info.Name = md.DeviceString;
                        }
                    }
                    
                    // Most monitors only have one entry here
                    if (!string.IsNullOrEmpty(info.HardwareId)) break;
                    md = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                }

                // Build a useful display name
                int w = lprcMonitor.Right - lprcMonitor.Left;
                int h = lprcMonitor.Bottom - lprcMonitor.Top;
                string resolution = $"{w}x{h}";
                
                // If the name is still generic, use the model + device name
                if (info.Name.StartsWith("Pantalla") || info.Name.Contains("Generic"))
                {
                    if (!string.IsNullOrEmpty(info.PtName))
                    {
                        info.Name = $"{info.PtName} ({resolution})";
                    }
                    else
                    {
                        info.Name = $"{deviceName} ({resolution})";
                    }
                }
                else
                {
                    // Include resolution alongside the real name
                    info.Name = $"{info.Name} ({resolution})";
                }

                GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out uint dpiY);
                Console.WriteLine($"[MonitorManager] Monitor: {info.Name} | PtName={info.PtName} | DPI={dpiX} | Bounds=[{lprcMonitor.Left},{lprcMonitor.Top},{lprcMonitor.Width}x{lprcMonitor.Height}] | WorkArea=[{mi.rcWork.Left},{mi.rcWork.Top},{mi.rcWork.Width}x{mi.rcWork.Height}] | Primary={info.IsPrimary}");
                monitors.Add(info);
            }
            return true;
        }, nint.Zero);

        // Restore previous DPI context
        if (prevCtx != nint.Zero) SetThreadDpiAwarenessContext(prevCtx);

        foreach (var m in monitors)
            Console.WriteLine($"[MonitorManager] Confirmed: {m.Name} WorkArea=[{m.WorkArea.Left},{m.WorkArea.Top},{m.WorkArea.Right},{m.WorkArea.Bottom}] Size={m.WorkArea.Width}x{m.WorkArea.Height} Primary={m.IsPrimary}");

        return monitors;
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
