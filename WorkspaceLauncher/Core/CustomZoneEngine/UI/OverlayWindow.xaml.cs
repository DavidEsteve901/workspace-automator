using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
            // Dimmer effect: dark, semi-transparent background - follow user's expectation of premium glass
            RootGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 10, 10, 12));
        }
    }

    private System.Windows.Media.Color GetAccentColor(byte alphaInt = 255)
    {
        try
        {
            string hex = ConfigManager.Instance.Config.AccentColor;
            if (string.IsNullOrEmpty(hex)) return System.Windows.Media.Color.FromArgb(alphaInt, 0, 120, 215);

            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return System.Windows.Media.Color.FromArgb(alphaInt, color.R, color.G, color.B);
        }
        catch { return System.Windows.Media.Color.FromArgb(alphaInt, 0, 120, 215); }
    }

    private void AddZoneVisual(int index, double x, double y, double w, double h, double scale, int spacing, bool isHighlighted = false)
    {
        var grid = new Grid { Width = Math.Max(1, w), Height = Math.Max(1, h), Tag = index };
        
        byte fillAlpha = isHighlighted ? (byte)140 : (byte)80;
        byte strokeAlpha = isHighlighted ? (byte)255 : (byte)180;
        double thickness = isHighlighted ? 3.5 : 1.5;

        double margin = spacing / 2.0;

        var rect = new System.Windows.Shapes.Rectangle
        {
            Fill            = new SolidColorBrush(GetAccentColor(fillAlpha)),
            Stroke          = new SolidColorBrush(GetAccentColor(strokeAlpha)),
            StrokeThickness = thickness,
            RadiusX         = 8,
            RadiusY         = 8,
            Margin          = new Thickness(margin)
        };
        grid.Children.Add(rect);

        var labelStack = new StackPanel
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            IsHitTestVisible = false
        };

        labelStack.Children.Add(new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 42,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6 }
        });

        labelStack.Children.Add(new TextBlock
        {
            Text = $"{(int)Math.Round(w * scale)} x {(int)Math.Round(h * scale)}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Opacity = 0.8,
            Margin = new Thickness(0, 2, 0, 0)
        });

        grid.Children.Add(labelStack);

        Canvas.SetLeft(grid, x);
        Canvas.SetTop(grid, y);
        OverlayCanvas.Children.Add(grid);
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

        double scale = mon.Scale / 100.0;
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            double x = (double)ze.X / 10000 * mon.WorkArea.Width / scale;
            double y = (double)ze.Y / 10000 * mon.WorkArea.Height / scale;
            double w = (double)ze.W / 10000 * mon.WorkArea.Width / scale;
            double h = (double)ze.H / 10000 * mon.WorkArea.Height / scale;

            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
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

        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            // Convert Absolute RECT to WorkArea-Relative dimensions and adjust for DPI scale
            double x = (z.Left - mon.WorkArea.Left) / scale;
            double y = (z.Top - mon.WorkArea.Top) / scale;
            double w = z.Width / scale;
            double h = z.Height / scale;

            AddZoneVisual(i, x, y, w, h, scale, 0);
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

        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            // CZE layout units (0-10000) mapped to WorkArea, then adjusted for DPI scale
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width) / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width) / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;

            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
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

        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width) / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width) / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;

            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
        }

        this.Show();
    }

    public void HighlightZone(int index)
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is Grid zoneGrid && zoneGrid.Tag is int zoneIdx)
            {
                var rect = zoneGrid.Children.OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault();
                if (rect != null)
                {
                    bool isHit = (zoneIdx == index);
                    rect.Fill = new SolidColorBrush(GetAccentColor(isHit ? (byte)150 : (byte)80));
                    rect.StrokeThickness = isHit ? 3.5 : 1.5;
                }
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
