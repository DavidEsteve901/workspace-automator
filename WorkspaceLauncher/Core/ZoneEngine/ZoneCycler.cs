using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.ZoneEngine;

/// <summary>
/// Cycles through windows in a zone stack.
/// Port of _cycle_zone_forward / _cycle_zone_backward.
/// </summary>
public sealed class ZoneCycler
{
    public static readonly ZoneCycler Instance = new();
    private ZoneCycler() { }

    private readonly Dictionary<ZoneStack.ZoneKey, int> _positions = [];

    public void CycleForward(ZoneStack.ZoneKey key)
    {
        var stack = ZoneStack.Instance.GetStack(key);
        if (stack.Count < 2) return;

        _positions.TryGetValue(key, out int pos);
        pos = (pos + 1) % stack.Count;
        _positions[key] = pos;

        BringToFront(stack[pos]);
    }

    public void CycleBackward(ZoneStack.ZoneKey key)
    {
        var stack = ZoneStack.Instance.GetStack(key);
        if (stack.Count < 2) return;

        _positions.TryGetValue(key, out int pos);
        pos = (pos - 1 + stack.Count) % stack.Count;
        _positions[key] = pos;

        BringToFront(stack[pos]);
    }

    private static void BringToFront(nint hwnd)
    {
        User32.ShowWindow(hwnd, User32.SW_RESTORE);
        User32.SetForegroundWindow(hwnd);
    }
}
