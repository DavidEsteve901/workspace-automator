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
    private CzeEditorState _state = CzeEditorState.Closed;
    public CzeEditorState State => _state;

    private string? _lastDraftLayoutId;
    private bool    _isNewLayout;

    // ── Public API ────────────────────────────────────────────────────────────

    private readonly SemaphoreSlim             _openLock     = new(1, 1);

    public async void OpenManager() => await OpenManager(false);

    public async Task OpenManager(bool forceOpen)
    {
        // Prevent concurrent execution of OpenManager
        if (!await _openLock.WaitAsync(0))
        {
            Logger.Warn("[ZoneEditorLauncher] OpenManager is already running/starting.");
            return;
        }

        try
        {
            var now = DateTime.Now;
            if (!forceOpen && (now - _lastOpenTime).TotalMilliseconds < 500)
            {
                Logger.Info("[ZoneEditorLauncher] Debouncing OpenManager call (too rapid)");
                return;
            }
            _lastOpenTime = now;

            // Robust Toggle: If state is NOT closed, or any window exists, close everything first
            // Only if NOT forceOpen (restoration flow)
            if (!forceOpen && (_state != CzeEditorState.Closed || _manager != null || _overlays.Count > 0))
            {
                Logger.Info($"[ZoneEditorLauncher] Toggle OFF: State={_state}, Manager={(_manager != null)}, Overlays={_overlays.Count}");
                System.Windows.Application.Current.Dispatcher.Invoke(CloseAll);
                return;
            }

            Logger.Info("[ZoneEditorLauncher] Starting OpenManager (async data gather)");

            // Gather heavy data on a background thread
            var data = await Task.Run(() =>
            {
                var monitors = MonitorManager.GetActiveMonitors();
                var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
                var availableLayouts = FancyZonesReader.GetAvailableLayouts();
                var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
                
                return new { monitors, appliedLayouts, availableLayouts, currentDesktopId };
            });

            if (data.monitors.Count == 0) return;

            // Create UI elements on the Dispatcher thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // One blocking overlay per monitor, covering exactly the WorkArea
                foreach (var mon in data.monitors)
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
                var primaryIdx = data.monitors.FindIndex(m => m.IsPrimary);
                if (primaryIdx < 0) primaryIdx = 0;
                var primaryOverlay = _overlays[primaryIdx];
                var primaryMon     = data.monitors[primaryIdx];

                _manager = new ZoneEditorManagerWindow();
                _manager.Owner = primaryOverlay;

                // Size and center over the primary monitor's work area
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
                Logger.Info("[ZoneEditorLauncher] Opened Layout Manager (Async Flow Complete)");
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneEditorLauncher] Error in OpenManager: {ex.Message}");
        }
        finally
        {
            _openLock.Release();
        }
    }

    /// <summary>
    /// Enter Editing state: hide manager, open interactive canvas owned by the target overlay.
    /// </summary>
    public void OpenCanvas(string monitorHardwareId, string layoutId = "", bool isNew = false)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed) return;

            _lastDraftLayoutId = layoutId;
            _isNewLayout = isNew;

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

            _editCanvas.Closed += OnEditorWindowClosed;
            _editCanvas.Show();
            _editCanvas.Activate();

            _controlDialog = new ZoneEditorControlWindow(targetMon.HardwareId, layoutId);
            _controlDialog.Owner = _editCanvas;
            _controlDialog.Closed += OnEditorWindowClosed;
            _controlDialog.Show();
            _controlDialog.Activate();

            SetState(CzeEditorState.Editing);
            Logger.Info($"[ZoneEditorLauncher] Double-window editor on monitor: {monitorHardwareId}");
        });
    }

    private void OnEditorWindowClosed(object? sender, EventArgs e)
    {
        // One of the editor windows (canvas or control) closed, handle return flow
        ReturnToAdmin();
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
    public void ReturnToAdmin() => ReturnToAdmin(false);

    public void ReturnToAdmin(bool isDiscard)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state != CzeEditorState.Editing) return;

            Logger.Info($"[ZoneEditorLauncher] Returning to Admin view (discard={isDiscard})...");

            // Cleanup if it was a new draft and we are discarding
            if (isDiscard && _isNewLayout && !string.IsNullOrEmpty(_lastDraftLayoutId))
            {
                Logger.Info($"[ZoneEditorLauncher] Deleting discarded new layout: {_lastDraftLayoutId}");
                ConfigManager.Instance.Config.CzeLayouts.Remove(_lastDraftLayoutId);
                ConfigManager.Instance.Save();
            }

            // Detach handlers before closing to prevent recursion/crash
            if (_editCanvas != null) _editCanvas.Closed -= OnEditorWindowClosed;
            if (_controlDialog != null) _controlDialog.Closed -= OnEditorWindowClosed;

            try 
            {
                if (_editCanvas != null && _editCanvas.IsLoaded) _editCanvas.Close();
                if (_controlDialog != null && _controlDialog.IsLoaded) _controlDialog.Close();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ZoneEditorLauncher] Error closing editor windows: {ex.Message}");
            }

            _editCanvas = null;
            _controlDialog = null;
            _lastDraftLayoutId = null;
            _isNewLayout = false;

            if (_state == CzeEditorState.Closed) return;

            if (_manager != null && _manager.IsLoaded)
            {
                _manager.Show();
                ActivateManager();
                SetState(CzeEditorState.Admin);
            }
            else 
            {
                // Manager was closed or lost, recreate it or force show
                Logger.Info("[ZoneEditorLauncher] Manager window missing or closed, recreating...");
                _ = OpenManager(true); 
            }

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

            Logger.Info("[ZoneEditorLauncher] Closing all Zone Editor windows.");

            if (_editCanvas != null)
            {
                _editCanvas.Closed -= OnEditorWindowClosed;
                try { if (_editCanvas.IsLoaded) _editCanvas.Close(); } catch { }
                _editCanvas = null;
            }

            if (_controlDialog != null)
            {
                _controlDialog.Closed -= OnEditorWindowClosed;
                try { if (_controlDialog.IsLoaded) _controlDialog.Close(); } catch { }
                _controlDialog = null;
            }

            if (_manager != null)
            {
                try { if (_manager.IsLoaded) _manager.Close(); } catch { }
                _manager = null;
            }

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
