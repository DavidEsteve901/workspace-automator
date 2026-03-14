using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WorkspaceLauncher.Core.OSD;

public partial class OsdWindow : Window
{
    private DispatcherTimer? _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        PositionBottomRight();
    }

    public void ShowMessage(string text)
    {
        MessageText.Text = text;

        // Cancel existing hide timer
        _hideTimer?.Stop();

        // Position bottom-right of primary screen
        PositionBottomRight();

        // Fade in
        Show();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fadeIn);

        // Auto-hide after 2.5s
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _hideTimer.Tick += (_, _) => FadeOut();
        _hideTimer.Start();
    }

    private void FadeOut()
    {
        _hideTimer?.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right  - Width  - 24;
        Top  = workArea.Bottom - ActualHeight - 24;
    }
}


