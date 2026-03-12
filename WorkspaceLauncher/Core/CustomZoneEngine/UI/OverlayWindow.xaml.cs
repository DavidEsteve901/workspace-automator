using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;

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
            // Alpha = 1 (0x01) — visually near-invisible but captures Win32 hit-tests
            RootGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
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

    /// <summary>
    /// Position this overlay to exactly cover the given WorkArea (rcWork from GetMonitorInfo).
    /// Call BEFORE Show().
    /// </summary>
    public void SetupForMonitor(RECT workArea)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        
        // Use SetWindowPos with SWP_NOACTIVATE to position physically without stealing focus.
        // We use the raw physical RECT from MonitorManager/GetMonitorInfo.
        User32.SetWindowPos(hwnd, nint.Zero, 
            workArea.Left, workArea.Top, workArea.Width, workArea.Height,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW);

        // Update WPF properties just in case, though SetWindowPos takes precedence for the OS.
        var monitor = MonitorManager.GetActiveMonitors().FirstOrDefault(m => 
            m.WorkArea.Left == workArea.Left && m.WorkArea.Top == workArea.Top);
        double scale = (monitor?.Scale ?? 100) / 100.0;
        
        Left   = workArea.Left / scale;
        Top    = workArea.Top  / scale;
        Width  = workArea.Width / scale;
        Height = workArea.Height / scale;
    }

    /// <summary>
    /// Render zone rectangles (drag-preview or editor background preview).
    /// Zones use base-10000 integer units.
    /// </summary>
    public void ShowLayout(CzeLayoutEntry layout, RECT workArea)
    {
        // Re-position to work area every time in case monitor config changed
        SetupForMonitor(workArea);
        OverlayCanvas.Children.Clear();

        foreach (var ze in layout.Zones)
        {
            double x = (double)ze.X / 10000 * workArea.Width;
            double y = (double)ze.Y / 10000 * workArea.Height;
            double w = (double)ze.W / 10000 * workArea.Width;
            double h = (double)ze.H / 10000 * workArea.Height;

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
    public void ShowBackgroundPreview(List<RECT> zones, RECT workArea)
    {
        SetupForMonitor(workArea);
        OverlayCanvas.Children.Clear();

        // Get monitor scale for coordinate conversion (Physical -> DIPs)
        var monitor = MonitorManager.GetActiveMonitors().FirstOrDefault(m => 
            m.WorkArea.Left == workArea.Left && m.WorkArea.Top == workArea.Top);
        double scale = (monitor?.Scale ?? 100) / 100.0;

        foreach (var z in zones)
        {
            // Convert Absolute RECT to WorkArea-Relative dimensions and adjust for DPI scale
            double x = (z.Left - workArea.Left) / scale;
            double y = (z.Top - workArea.Top) / scale;
            double w = z.Width / scale;
            double h = z.Height / scale;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                // Subtle grayish blue with very low opacity
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 100, 150, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70,  100, 150, 255)),
                StrokeThickness = 1,
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
    public void ShowCzeBackgroundPreview(CzeLayoutEntry layout, RECT workArea)
    {
        SetupForMonitor(workArea);
        OverlayCanvas.Children.Clear();

        // Get monitor scale for coordinate conversion (Physical -> DIPs)
        var monitor = MonitorManager.GetActiveMonitors().FirstOrDefault(m => 
            m.WorkArea.Left == workArea.Left && m.WorkArea.Top == workArea.Top);
        double scale = (monitor?.Scale ?? 100) / 100.0;

        foreach (var ze in layout.Zones)
        {
            // CZE layout units (0-10000) mapped to WorkArea, then adjusted for DPI scale
            double x = ((double)ze.X / 10000 * workArea.Width) / scale;
            double y = ((double)ze.Y / 10000 * workArea.Height) / scale;
            double w = ((double)ze.W / 10000 * workArea.Width) / scale;
            double h = ((double)ze.H / 10000 * workArea.Height) / scale;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = Math.Max(1, w),
                Height          = Math.Max(1, h),
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 100, 150, 255)),
                Stroke          = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70,  100, 150, 255)),
                StrokeThickness = 1,
                RadiusX         = 6,
                RadiusY         = 6,
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
