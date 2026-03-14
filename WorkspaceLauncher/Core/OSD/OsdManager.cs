using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkspaceLauncher.Core.OSD;

/// <summary>
/// Shows brief on-screen-display notifications.
/// Auto-hides after 2.5s with fade animation.
/// </summary>
public static class OsdManager
{
    private static OsdWindow? _window;

    public static void Show(string text, string icon = "ℹ️")
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _window ??= new OsdWindow();
            _window.ShowMessage($"{icon} {text}");
        });
    }
}


