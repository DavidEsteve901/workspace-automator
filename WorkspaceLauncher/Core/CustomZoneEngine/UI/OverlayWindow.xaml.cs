using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.CustomZoneEngine.Models;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class OverlayWindow : Window
{
    private readonly bool _blocking;

    /// <param name="blocking">
    /// true  = editor overlay: nearly-transparent background (#01000000) that blocks
    ///         mouse interaction with the desktop (no WS_EX_TRANSPARENT).
    /// false = drag-preview overlay: fully click-through (WS_EX_TRANSPARENT).
    /// </param>
    public OverlayWindow(bool blocking = false)
    {
        _blocking = blocking;
        InitializeComponent();

        if (blocking)
        {
            // Dimmer effect: dark, semi-transparent background (alpha 180 out of 255)
            RootGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0));
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        nint exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);

        if (_blocking)
        {
            // WS_EX_LAYERED for transparency + WS_EX_TOOLWINDOW to hide from taskbar/Alt-Tab.
            // NO WS_EX_TRANSPARENT — we want to capture mouse events to block desktop access.
            User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW);
        }
        else
        {
            // Classic drag-preview: click-through at Win32 level
            User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW);
        }
    }

    public string MonitorHandle { get; private set; } = "";

    /// <summary>
    /// Position this overlay to exactly cover the given WorkArea (rcWork from GetMonitorInfo).
    /// Call BEFORE Show().
    /// </summary>
    public void SetupForMonitor(MonitorInfo mon)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        this.MonitorHandle = mon.Handle;
        
        // Match HWND_TOPMOST (or HWND_TOP if we want to be less aggressive)
        var HWND_TOP = new nint(0);
        var HWND_TOPMOST = new nint(-1);

        User32.SetWindowPos(hwnd, HWND_TOPMOST, 
            mon.WorkArea.Left, mon.WorkArea.Top, mon.WorkArea.Width, mon.WorkArea.Height,
            User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW);

        double scale = mon.Scale / 100.0;
        
        Left   = mon.WorkArea.Left / scale;
        Top    = mon.WorkArea.Top  / scale;
        Width  = mon.WorkArea.Width / scale;
        Height = mon.WorkArea.Height / scale;
    }

    /// <summary>
    /// Render zone rectangles (drag-preview or editor background preview).
    /// Zones use base-10000 integer units.
    /// </summary>
    public void ShowLayout(CzeLayoutEntry layout, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        foreach (var ze in layout.Zones)
        {
            double x = (double)ze.X / 10000 * mon.WorkArea.Width;
            double y = (double)ze.Y / 10000 * mon.WorkArea.Height;
            double w = (double)ze.W / 10000 * mon.WorkArea.Width;
            double h = (double)ze.H / 10000 * mon.WorkArea.Height;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60,  88, 166, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 88, 166, 255)),
                StrokeThickness = 1.5,
                RadiusX         = 8,
                RadiusY         = 8,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);
        }

        this.Show();
    }

    /// <summary>
    /// Render a set of zones as a static background preview (non-interactive).
    /// </summary>
    public void ShowBackgroundPreview(List<RECT> zones, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;

        foreach (var z in zones)
        {
            // Convert Absolute RECT to WorkArea-Relative dimensions and adjust for DPI scale
            double x = (z.Left - mon.WorkArea.Left) / scale;
            double y = (z.Top - mon.WorkArea.Top) / scale;
            double w = z.Width / scale;
            double h = z.Height / scale;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                // Grayish blue with higher opacity for better visibility
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 88, 166, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 88, 166, 255)),
                StrokeThickness = 3,
                RadiusX         = 6,
                RadiusY         = 6,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);
        }

        this.Show();
    }

    /// <summary>
    /// Render a CZE layout as a static background preview.
    /// </summary>
    public void ShowCzeBackgroundPreview(CzeLayoutEntry layout, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;

        foreach (var ze in layout.Zones)
        {
            // CZE layout units (0-10000) mapped to WorkArea, then adjusted for DPI scale
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width) / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width) / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                // Slightly more opaque and thick for "Premium" visibility
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 88, 166, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 88, 166, 255)),
                StrokeThickness = 3,
                RadiusX         = 10,
                RadiusY         = 10,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);
        }

        this.Show();
    }

    /// <summary>
    /// Render a CZE internal layout model as a static background preview.
    /// Support for virtual templates (CS1503 fix).
    /// </summary>
    public void ShowCzeBackgroundPreview(CZELayout layout, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;

        foreach (var ze in layout.Zones)
        {
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width) / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width) / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 88, 166, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 88, 166, 255)),
                StrokeThickness = 3,
                RadiusX         = 10,
                RadiusY         = 10,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);
        }

        this.Show();
    }

    public void HighlightZone(int index)
    {
        if (index < 0 || index >= OverlayCanvas.Children.Count) return;
        for (int i = 0; i < OverlayCanvas.Children.Count; i++)
        {
            if (OverlayCanvas.Children[i] is System.Windows.Shapes.Rectangle rect)
            {
                rect.Fill = (i == index)
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 88, 166, 255))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(60,  88, 166, 255));
                rect.StrokeThickness = (i == index) ? 3 : 1.5;
            }
        }
    }

    public void ClearZones()
    {
        OverlayCanvas.Children.Clear();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Logger.Info("[OverlayWindow] Emergency Escape triggered");
            ZoneEditorLauncher.Instance.CloseAll();
        }
    }
}
