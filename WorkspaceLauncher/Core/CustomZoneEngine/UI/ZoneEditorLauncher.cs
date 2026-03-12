using System.Windows;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.Launcher;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public enum CzeEditorState { Closed, Admin, Editing }

/// <summary>
/// Controls the Zone Editor UI lifecycle with FancyZones-style ownership:
///   OverlayWindow (per monitor, blocks desktop)
///     └── ZoneEditorManagerWindow  (Admin state)
///     └── ZoneCanvasEditorWindow   (Editing state, replaces manager)
///
/// Overlays stay alive from OpenManager() until CloseAll(), ensuring the
/// desktop is never accessible while the editor is active.
/// </summary>
public sealed class ZoneEditorLauncher
{
    public static readonly ZoneEditorLauncher Instance = new();
    private ZoneEditorLauncher() { }

    private readonly List<OverlayWindow>         _overlays     = [];
    private ZoneEditorManagerWindow?             _manager;
    private ZoneCanvasEditorWindow?              _editCanvas;
    private ZoneEditorControlWindow?             _controlDialog;
    private DateTime                             _lastOpenTime = DateTime.MinValue;
    private CzeEditorState                       _state        = CzeEditorState.Closed;

    public CzeEditorState State => _state;

    // ── Public API ────────────────────────────────────────────────────────────

    public void OpenManager()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var now = DateTime.Now;
            if ((now - _lastOpenTime).TotalMilliseconds < 500)
            {
                Logger.Info("[ZoneEditorLauncher] Debouncing OpenManager call (too rapid)");
                return;
            }
            _lastOpenTime = now;

            // Robust Toggle: If state is NOT closed, or any window exists, close everything first
            if (_state != CzeEditorState.Closed || _manager != null || _overlays.Count > 0)
            {
                Logger.Info($"[ZoneEditorLauncher] Toggle OFF: State={_state}, Manager={(_manager != null)}, Overlays={_overlays.Count}");
                CloseAll();
                return;
            }

            var monitors = MonitorManager.GetActiveMonitors();
            if (monitors.Count == 0) return;

            // Fetch current FancyZones state for background visualization
            var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
            var availableLayouts = FancyZonesReader.GetAvailableLayouts();
            var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
            string currentDesktopStr = currentDesktopId.ToString().ToLowerInvariant();
            var engine = ConfigManager.Instance.Config.ZoneEngine;

            // One blocking overlay per monitor, covering exactly the WorkArea
            foreach (var mon in monitors)
            {
                var overlay = new OverlayWindow(blocking: true);
                overlay.SetupForMonitor(mon.WorkArea);

                overlay.Closed += (_, _) => OnOverlayClosed();
                overlay.Show();
                _overlays.Add(overlay);
            }

            // --- Background visualization: show current zones as static preview ---
            RefreshBackgroundVisuals();

            // Manager window owned by the primary overlay → always on top of it

            // Manager window owned by the primary overlay → always on top of it
            var primaryIdx = monitors.FindIndex(m => m.IsPrimary);
            if (primaryIdx < 0) primaryIdx = 0;
            var primaryOverlay = _overlays[primaryIdx];
            var primaryMon     = monitors[primaryIdx];

            _manager = new ZoneEditorManagerWindow();
            _manager.Owner = primaryOverlay;

            // Size and center over the primary monitor's work area
            // MonitorManager.GetActiveMonitors() returns physical units, but WPF uses DIPs.
            double scale = primaryMon.Scale / 100.0;
            double workLeft = primaryMon.WorkArea.Left / scale;
            double workTop  = primaryMon.WorkArea.Top  / scale;
            double workWidth = primaryMon.WorkArea.Width / scale;
            double workHeight = primaryMon.WorkArea.Height / scale;

            double targetW = workWidth  * 0.85;
            double targetH = workHeight * 0.80;
            _manager.Width  = Math.Clamp(targetW, 1000, 1400);
            _manager.Height = Math.Clamp(targetH,  700,  950);

            _manager.Left = workLeft + (workWidth  - _manager.Width)  / 2;
            _manager.Top  = workTop  + (workHeight - _manager.Height) / 2;

            _manager.Closed += (_, _) => CloseAll();
            _manager.Show();
            _manager.Activate();

            SetState(CzeEditorState.Admin);
            Logger.Info("[ZoneEditorLauncher] Opened Layout Manager");
        });
    }

    /// <summary>
    /// Enter Editing state: hide manager, open interactive canvas owned by the target overlay.
    /// </summary>
    public void OpenCanvas(string monitorHardwareId, string layoutId = "")
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed) return;

            // Hide the manager to prevent interaction while editing zones
            _manager?.Hide();
            _editCanvas?.Close();

            // Clear background visuals while editing
            foreach (var o in _overlays) o.ClearZones();

            var monitors   = MonitorManager.GetActiveMonitors();
            int targetIdx  = monitors.FindIndex(m => m.HardwareId == monitorHardwareId);
            if (targetIdx < 0) targetIdx = monitors.FindIndex(m => m.IsPrimary);
            if (targetIdx < 0) targetIdx = 0;

            var targetMon     = monitors[targetIdx];
            var targetOverlay = targetIdx < _overlays.Count ? _overlays[targetIdx] : _overlays[0];

            _editCanvas    = new ZoneCanvasEditorWindow(targetMon.HardwareId, layoutId, "edit");
            _editCanvas.Owner = targetOverlay;

            double scale = targetMon.Scale / 100.0;
            _editCanvas.Left   = targetMon.WorkArea.Left / scale;
            _editCanvas.Top    = targetMon.WorkArea.Top  / scale;
            _editCanvas.Width  = targetMon.WorkArea.Width / scale;
            _editCanvas.Height = targetMon.WorkArea.Height / scale;

            _editCanvas.Closed += (_, _) => ReturnToAdmin();
            
            _editCanvas.Show();
            _editCanvas.Activate();

            _controlDialog = new ZoneEditorControlWindow(targetMon.HardwareId, layoutId);
            _controlDialog.Owner = _editCanvas;
            _controlDialog.Closed += (_, _) => ReturnToAdmin();
            _controlDialog.Show();
            _controlDialog.Activate();

            SetState(CzeEditorState.Editing);
            Logger.Info($"[ZoneEditorLauncher] Double-window editor on monitor: {monitorHardwareId}");
        });
    }

    public void RequestCanvasSave()
    {
        _editCanvas?.Dispatcher.Invoke(() => _editCanvas.ExecuteRemoteAction("save"));
    }

    public void RequestCanvasDiscard()
    {
        _editCanvas?.Dispatcher.Invoke(() => _editCanvas.ExecuteRemoteAction("discard"));
    }

    /// <summary>
    /// Return from Editing → Admin: close canvas, restore manager.
    /// Called after save or discard.
    /// </summary>
    public void ReturnToAdmin()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Detach Closed handler before closing to avoid re-entrancy
            if (_editCanvas != null)
            {
                _editCanvas.Closed -= (_, _) => ReturnToAdmin();
                if (_editCanvas.IsLoaded) _editCanvas.Close();
                _editCanvas = null;
            }

            if (_controlDialog != null)
            {
                _controlDialog.Closed -= (_, _) => ReturnToAdmin();
                if (_controlDialog.IsLoaded) _controlDialog.Close();
                _controlDialog = null;
            }

            if (_state == CzeEditorState.Closed) return;  // CloseAll already ran

            _manager?.Show();
            ActivateManager();
            SetState(CzeEditorState.Admin);

            // Restore background visuals
            RefreshBackgroundVisuals();
        });
    }

    public void ActivateManager()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_manager == null) return;
            
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_manager).Handle;
            if (hwnd == nint.Zero) return;

            // Ensure not minimized
            if (User32.IsIconic(hwnd))
                User32.ShowWindow(hwnd, User32.SW_RESTORE);

            _manager.Show();
            _manager.Activate();
            User32.SetForegroundWindow(hwnd);
        });
    }

    /// <summary>Legacy alias kept for WebBridge backward compat.</summary>
    public void CloseCanvas()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _editCanvas?.Close();
            _editCanvas = null;
        });
    }

    /// <summary>Legacy alias — RestoreManager now maps to ReturnToAdmin.</summary>
    public void RestoreManager() => ReturnToAdmin();

    public void CloseAll()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed) return;
            SetState(CzeEditorState.Closed);  // Set first to prevent re-entry

            _editCanvas?.Close();
            _editCanvas = null;

            _controlDialog?.Close();
            _controlDialog = null;

            _manager?.Close();
            _manager = null;

            foreach (var o in _overlays.ToList()) o.Close();
            _overlays.Clear();

            Logger.Info("[ZoneEditorLauncher] Closed all Zone Editor windows");
        });
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void OnOverlayClosed()
    {
        // If an overlay is closed externally (e.g. alt-F4 at Win32 level), close everything
        if (_state != CzeEditorState.Closed)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(CloseAll);
    }

    private void SetState(CzeEditorState state)
    {
        _state = state;
        Bridge.WebBridge.BroadcastCzeState(state.ToString().ToLowerInvariant());
    }

    private void RefreshBackgroundVisuals()
    {
        try
        {
            var monitors = MonitorManager.GetActiveMonitors();
            var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
            var availableLayouts = FancyZonesReader.GetAvailableLayouts();
            var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
            string currentDesktopStr = currentDesktopId.ToString().ToLowerInvariant();
            var engine = ConfigManager.Instance.Config.ZoneEngine;

            for (int i = 0; i < monitors.Count && i < _overlays.Count; i++)
            {
                var mon = monitors[i];
                var overlay = _overlays[i];
                overlay.ClearZones();

                if (engine == "cze")
                {
                    if (ConfigManager.Instance.Config.CzeActiveLayouts.TryGetValue(mon.HardwareId, out var czeLayoutId))
                    {
                        if (ConfigManager.Instance.Config.CzeLayouts.TryGetValue(czeLayoutId, out var czeLayout))
                        {
                            overlay.ShowCzeBackgroundPreview(czeLayout, mon.WorkArea);
                        }
                    }
                }
                else
                {
                    var match = appliedLayouts
                        .Where(ae => {
                            string monInstNorm = (mon.PtInstance ?? "").Trim('{', '}').ToLowerInvariant();
                            string monPtNorm = (mon.PtName ?? "").ToLowerInvariant();
                            string monSerial = (mon.SerialNumber ?? "").ToLowerInvariant();

                            string aeInstNorm = (ae.Instance ?? "").Trim('{', '}').ToLowerInvariant();
                            string aeMonNorm = (ae.MonitorName ?? "").ToLowerInvariant();
                            string aeSerial = (ae.SerialNumber ?? "").ToLowerInvariant();

                            bool mMatch = (!string.IsNullOrEmpty(monSerial) && monSerial == aeSerial) ||
                                          (!string.IsNullOrEmpty(aeInstNorm) && aeInstNorm == monInstNorm) ||
                                          (!string.IsNullOrEmpty(aeMonNorm) && (aeMonNorm == monPtNorm || aeMonNorm.Contains(monPtNorm)));

                            if (!mMatch) return false;

                            string aeDkNorm = (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant();
                            bool isAllDesktops = string.IsNullOrEmpty(aeDkNorm) || aeDkNorm == "00000000-0000-0000-0000-000000000000";
                            bool isExactMatch = aeDkNorm == currentDesktopStr;
                            return isExactMatch || isAllDesktops;
                        })
                        .OrderByDescending(ae => ae.DesktopId == currentDesktopStr)
                        .FirstOrDefault();

                    if (match != null)
                    {
                        var layout = availableLayouts.FirstOrDefault(l => l.uuid.Equals(match.LayoutUuid, StringComparison.OrdinalIgnoreCase))
                                     ?? availableLayouts.FirstOrDefault(l => l.type.Equals(match.LayoutType, StringComparison.OrdinalIgnoreCase));

                        if (layout?.info is System.Text.Json.JsonElement infoElement)
                        {
                            var fzLayout = WorkspaceOrchestrator.ParseLayoutInfo(new LayoutCacheEntry { Type = layout.type, Info = infoElement });
                            if (fzLayout != null)
                            {
                                if (match.Spacing >= 0) { fzLayout.Spacing = match.Spacing; fzLayout.ShowSpacing = match.ShowSpacing; }
                                var zoneRects = ZoneCalculator.CalculateAllZones(fzLayout, mon.WorkArea);
                                overlay.ShowBackgroundPreview(zoneRects, mon.WorkArea);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Logger.Error($"[ZoneEditorLauncher] RefreshBackgroundVisuals error: {ex.Message}"); }
    }
}
