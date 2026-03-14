using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.CustomZoneEngine.Models;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class OverlayWindow : System.Windows.Window
{
    private readonly bool _blocking;

    // ── Visual constants ──────────────────────────────────────────────────────
    // Normal zone fill: subtle but clearly visible
    private const byte FILL_ALPHA_NORMAL    = 55;
    // Normal zone border: semi-opaque accent
    private const byte STROKE_ALPHA_NORMAL  = 160;
    // Highlighted zone fill: strong visible feedback
    private const byte FILL_ALPHA_HIT       = 155;
    // Highlighted zone border: full-opacity white for snapping cue
    private const byte STROKE_ALPHA_HIT     = 255;

    private const double RADIUS_NORMAL      = 12.0;
    private const double RADIUS_HIT         = 14.0;
    private const double STROKE_NORMAL      = 2.0;
    private const double STROKE_HIT         = 3.5;
    private const double FONT_NUMBER        = 48.0;
    private const double FONT_DIMS          = 12.0;

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
            // Semi-dark glass: dark enough to indicate "editor active" without totally obscuring desktop
            RootGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(145, 8, 8, 12));
        }
    }

    // ── Accent System.Windows.Media.Color helper ───────────────────────────────────────────────────

    /// <summary>Reads the user's chosen accent System.Windows.Media.Color from config and applies the given alpha.</summary>
    private System.Windows.Media.Color GetAccentColor(byte alpha = 255)
    {
        try
        {
            string hex = ConfigManager.Instance.Config.AccentColor;
            if (string.IsNullOrWhiteSpace(hex))
                return System.Windows.Media.Color.FromArgb(alpha, 0, 210, 255); // fallback cyan

            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);
        }
        catch { return System.Windows.Media.Color.FromArgb(alpha, 0, 210, 255); }
    }

    /// <summary>Returns a slightly brighter/lighter variant of the accent for glow effects.</summary>
    private System.Windows.Media.Color GetAccentGlow(byte alpha = 200)
    {
        var c = GetAccentColor(255);
        byte r = (byte)Math.Min(255, c.R + 40);
        byte g = (byte)Math.Min(255, c.G + 40);
        byte b = (byte)Math.Min(255, c.B + 40);
        return System.Windows.Media.Color.FromArgb(alpha, r, g, b);
    }

    // ── Zone visual builder ───────────────────────────────────────────────────

    private void AddZoneVisual(int index, double x, double y, double w, double h,
                               double scale, int spacing, bool isHighlighted = false)
    {
        double margin = spacing / 2.0;
        var grid = new Grid
        {
            Width  = Math.Max(1, w),
            Height = Math.Max(1, h),
            Tag    = index,
            // Subtle scale-in from center — start at 0.94, animate to 1
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform        = new ScaleTransform(0.94, 0.94),
            // Opacity                = 0.0,
        };

        // ── Rectangle fill ────────────────────────────────────────────────────
        byte fillAlpha   = isHighlighted ? FILL_ALPHA_HIT    : FILL_ALPHA_NORMAL;
        byte strokeAlpha = isHighlighted ? STROKE_ALPHA_HIT  : STROKE_ALPHA_NORMAL;

        // Gradient fill: accent center, darker edges — more depth than flat System.Windows.Media.Color
        var fillBrush = new RadialGradientBrush
        {
            Center          = new System.Windows.Point(0.5, 0.45),
            GradientOrigin  = new System.Windows.Point(0.5, 0.35),
            RadiusX         = 0.7,
            RadiusY         = 0.7,
            GradientStops   =
            {
                new GradientStop(GetAccentColor(fillAlpha), 0.0),
                new GradientStop(GetAccentColor((byte)(fillAlpha / 2)), 1.0),
            }
        };

        System.Windows.Media.Color strokeColor = isHighlighted
            ? System.Windows.Media.Color.FromArgb(STROKE_ALPHA_HIT, 255, 255, 255)
            : GetAccentColor(strokeAlpha);

        var rect = new System.Windows.Shapes.Rectangle
        {
            Fill            = fillBrush,
            Stroke          = new SolidColorBrush(strokeColor),
            StrokeThickness = isHighlighted ? STROKE_HIT : STROKE_NORMAL,
            RadiusX         = isHighlighted ? RADIUS_HIT : RADIUS_NORMAL,
            RadiusY         = isHighlighted ? RADIUS_HIT : RADIUS_NORMAL,
            Margin          = new Thickness(margin),
        };

        if (isHighlighted)
        {
            // Outer glow for the snapping zone
            rect.Effect = new DropShadowEffect
            {
                BlurRadius  = 28,
                ShadowDepth = 0,
                Color = GetAccentGlow(230),
                Opacity     = 0.75,
            };
        }

        grid.Children.Add(rect);

        // ── Top-edge accent line (premium detail) ─────────────────────────────
        // A thin bright line at the top of the zone suggests a light source and adds depth.
        if (!isHighlighted)
        {
            var topLine = new System.Windows.Shapes.Rectangle
            {
                Height          = 1.5,
                RadiusX         = 1,
                RadiusY         = 1,
                Margin          = new Thickness(margin + 12, margin + 1, margin + 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Fill            = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 255)),
                IsHitTestVisible = false,
            };
            grid.Children.Add(topLine);
        }

        // ── Text labels ───────────────────────────────────────────────────────
        var labelStack = new StackPanel
        {
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            IsHitTestVisible    = false,
        };

        // Zone number
        var numberBlock = new TextBlock
        {
            Text      = (index + 1).ToString(),
            FontSize  = FONT_NUMBER,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                isHighlighted ? (byte)255 : (byte)220, 255, 255, 255)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius  = isHighlighted ? 20 : 10,
                ShadowDepth = 0,
                Opacity     = isHighlighted ? 0.9 : 0.6,
                Color = isHighlighted ? GetAccentColor(255) : Colors.Black,
            }
        };
        labelStack.Children.Add(numberBlock);

        // Pixel dimensions chip
        int pixW = (int)Math.Round(w * scale);
        int pixH = (int)Math.Round(h * scale);
        var dimsBlock = new TextBlock
        {
            Text      = $"{pixW} × {pixH}",
            FontSize  = FONT_DIMS,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin    = new Thickness(0, 6, 0, 0),
        };
        // Small pill background around the dims text
        var dimsBorder = new Border
        {
            CornerRadius    = new CornerRadius(5),
            Padding         = new Thickness(8, 3, 8, 3),
            Margin          = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        dimsBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0, 0, 0));
        dimsBorder.Child = new TextBlock
        {
            Text      = $"{pixW} × {pixH}",
            FontSize  = FONT_DIMS,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        labelStack.Children.Add(dimsBorder);

        grid.Children.Add(labelStack);

        Canvas.SetLeft(grid, x);
        Canvas.SetTop(grid, y);
        OverlayCanvas.Children.Add(grid);

        // ── Entrance animation ────────────────────────────────────────────────
        // Stagger by index: each zone fades in slightly after the previous
        double staggerMs = index * 35.0;
        AnimateZoneIn(grid, staggerMs);
    }

    /// <summary>Applies a fade + scale-in animation to a zone grid element.</summary>
    private static void AnimateZoneIn(Grid grid, double delayMs)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        var delay    = TimeSpan.FromMilliseconds(delayMs);
        var ease     = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Fade in
        var fadeAnim = new DoubleAnimation(0, 1, duration)
        {
            BeginTime      = delay,
            EasingFunction = ease,
        };

        // Scale from 0.94 → 1.0
        var scaleXAnim = new DoubleAnimation(0.94, 1.0, duration)
        {
            BeginTime      = delay,
            EasingFunction = ease,
        };
        var scaleYAnim = new DoubleAnimation(0.94, 1.0, duration)
        {
            BeginTime      = delay,
            EasingFunction = ease,
        };

        grid.BeginAnimation(OpacityProperty, fadeAnim);
        if (grid.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }
    }

    // ── Win32 System.Windows.Window setup ────────────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        nint exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);

        if (_blocking)
        {
            // WS_EX_LAYERED for transparency + WS_EX_TOOLWINDOW to hide from taskbar/Alt-Tab.
            // NO WS_EX_TRANSPARENT — captures mouse events to block desktop access.
            User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW);
        }
        else
        {
            // Drag-preview: click-through at Win32 level
            User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW);
        }

        VirtualDesktopManager.Instance.PinWindow(hwnd);
    }

    // ── Public interface ──────────────────────────────────────────────────────

    public string MonitorHandle { get; private set; } = "";

    /// <summary>
    /// Position this overlay to exactly cover the given WorkArea (rcWork from GetMonitorInfo).
    /// Call BEFORE Show().
    /// </summary>
    public void SetupForMonitor(MonitorInfo mon)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        MonitorHandle = mon.Handle;

        var HWND_TOPMOST = new nint(-1);
        User32.SetWindowPos(hwnd, HWND_TOPMOST,
            mon.WorkArea.Left, mon.WorkArea.Top, mon.WorkArea.Width, mon.WorkArea.Height,
            User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW);

        double scale = mon.Scale / 100.0;
        Left   = mon.WorkArea.Left   / scale;
        Top    = mon.WorkArea.Top    / scale;
        Width  = mon.WorkArea.Width  / scale;
        Height = mon.WorkArea.Height / scale;
    }

    /// <summary>
    /// Render zone rectangles (drag-preview or editor background preview).
    /// Zones use base-10000 integer units.
    /// </summary>
    public void ShowLayout(CzeLayoutEntry layout, MonitorInfo mon)
    {
        NoLayoutMessage.Visibility = Visibility.Collapsed;
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            double x = (double)ze.X / 10000 * mon.WorkArea.Width  / scale;
            double y = (double)ze.Y / 10000 * mon.WorkArea.Height / scale;
            double w = (double)ze.W / 10000 * mon.WorkArea.Width  / scale;
            double h = (double)ze.H / 10000 * mon.WorkArea.Height / scale;
            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
        }

        this.Show();
    }

    /// <summary>Shows a message indicating no layout is assigned to the monitor.</summary>
    public void ShowNoLayoutMessage(MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();
        NoLayoutMessage.Visibility = Visibility.Visible;

        // Animate the message panel in
        // NoLayoutMessage// .Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(320)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        NoLayoutMessage.BeginAnimation(OpacityProperty, fadeIn);

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
            double x = (z.Left - mon.WorkArea.Left) / scale;
            double y = (z.Top  - mon.WorkArea.Top)  / scale;
            double w = z.Width  / scale;
            double h = z.Height / scale;
            AddZoneVisual(i, x, y, w, h, scale, 0);
        }

        this.Show();
    }

    /// <summary>Render a CZE layout as a static background preview.</summary>
    public void ShowCzeBackgroundPreview(CzeLayoutEntry layout, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width)  / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width)  / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;
            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
        }

        this.Show();
    }

    /// <summary>Render a CZE internal layout model as a static background preview.</summary>
    public void ShowCzeBackgroundPreview(CZELayout layout, MonitorInfo mon)
    {
        SetupForMonitor(mon);
        OverlayCanvas.Children.Clear();

        double scale = mon.Scale / 100.0;
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            double x = ((double)ze.X / 10000 * mon.WorkArea.Width)  / scale;
            double y = ((double)ze.Y / 10000 * mon.WorkArea.Height) / scale;
            double w = ((double)ze.W / 10000 * mon.WorkArea.Width)  / scale;
            double h = ((double)ze.H / 10000 * mon.WorkArea.Height) / scale;
            AddZoneVisual(i, x, y, w, h, scale, layout.Spacing);
        }

        this.Show();
    }

    /// <summary>
    /// Highlight the zone under the cursor during Shift-drag snap.
    /// Updates fill, stroke, glow and thickness to give strong visual feedback.
    /// </summary>
    public void HighlightZone(int index)
    {
        foreach (var child in OverlayCanvas.Children)
        {
            if (child is not Grid zoneGrid || zoneGrid.Tag is not int zoneIdx) continue;

            var rect = zoneGrid.Children.OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault();
            if (rect == null) continue;

            bool isHit = (zoneIdx == index);

            // ── Fill ──────────────────────────────────────────────────────────
            rect.Fill = new RadialGradientBrush
            {
                Center         = new System.Windows.Point(0.5, 0.45),
                GradientOrigin = new System.Windows.Point(0.5, 0.35),
                RadiusX        = 0.7,
                RadiusY        = 0.7,
                GradientStops  =
                {
                    new GradientStop(GetAccentColor(isHit ? FILL_ALPHA_HIT : FILL_ALPHA_NORMAL), 0.0),
                    new GradientStop(GetAccentColor(isHit ? (byte)(FILL_ALPHA_HIT / 2) : (byte)(FILL_ALPHA_NORMAL / 2)), 1.0),
                }
            };

            // ── Stroke ────────────────────────────────────────────────────────
            rect.Stroke = new SolidColorBrush(
                isHit ? System.Windows.Media.Color.FromArgb(STROKE_ALPHA_HIT, 255, 255, 255)
                       : GetAccentColor(STROKE_ALPHA_NORMAL));
            rect.StrokeThickness = STROKE_NORMAL;
            rect.RadiusX         = RADIUS_NORMAL;
            rect.RadiusY         = RADIUS_NORMAL;

            // ── Glow effect ───────────────────────────────────────────────────
            rect.Effect = isHit
                ? new DropShadowEffect
                  {
                      BlurRadius  = 32,
                      ShadowDepth = 0,
                      Color = GetAccentGlow(240),
                      Opacity     = 0.85,
                  }
                : null;
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








