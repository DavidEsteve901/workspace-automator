namespace WorkspaceLauncher.Core.ZoneEngine;

/// <summary>
/// Manages stacks of HWNDs per zone.
/// Port of zone_stacks in Python DevLauncherApp.
/// </summary>
public sealed class ZoneStack
{
    public static readonly ZoneStack Instance = new();

    private readonly Dictionary<ZoneKey, List<nint>> _stacks = [];
    private readonly object _lock = new();

    private ZoneStack() { }

    public record ZoneKey(Guid Desktop, string Monitor, string Layout, int Zone);

    public void Register(ZoneKey key, nint hwnd)
    {
        lock (_lock)
        {
            if (!_stacks.TryGetValue(key, out var list))
            {
                list = [];
                _stacks[key] = list;
            }
            if (!list.Contains(hwnd)) list.Add(hwnd);
        }
    }

    public void Unregister(nint hwnd)
    {
        lock (_lock)
        {
            foreach (var list in _stacks.Values) list.Remove(hwnd);
        }
    }

    public IReadOnlyList<nint> GetStack(ZoneKey key)
    {
        lock (_lock)
        {
            return _stacks.TryGetValue(key, out var list) ? list.AsReadOnly() : Array.Empty<nint>();
        }
    }

    /// <summary>
    /// Find the ZoneKey that contains the given hwnd.
    /// Returns null if the hwnd is not registered in any zone.
    /// </summary>
    public ZoneKey? FindKeyForHwnd(nint hwnd)
    {
        lock (_lock)
        {
            foreach (var (key, list) in _stacks)
            {
                if (list.Contains(hwnd)) return key;
            }
            return null;
        }
    }

    public void Clear()
    {
        lock (_lock) { _stacks.Clear(); }
    }
}
