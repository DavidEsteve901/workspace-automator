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

    private readonly Dictionary<string, OverlayWindow> _overlaysByHandle = [];
    private ZoneEditorManagerWindow?             _manager;
    private ZoneCanvasEditorWindow?              _editCanvas;
    private ZoneEditorControlWindow?             _controlDialog;
    private DateTime                             _lastOpenTime = DateTime.MinValue;
    private CzeEditorState _state = CzeEditorState.Closed;
    public CzeEditorState State => _state;

    private string? _lastDraftLayoutId;
    private bool    _isNewLayout;

    private System.Windows.Threading.DispatcherTimer? _desktopPollTimer;
    private Guid? _lastKnownDesktopId;
    private bool  _isManualSwitchInProgress;

    public async void ToggleManager()
    {
        // Prevent concurrent execution
        if (!await _openLock.WaitAsync(0))
        {
            Logger.Warn("[ZoneEditorLauncher] ToggleManager/OpenManager is already running.");
            return;
        }

        try
        {
            var now = DateTime.Now;
            if ((now - _lastOpenTime).TotalMilliseconds < 400)
            {
                Logger.Info("[ZoneEditorLauncher] Debouncing ToggleManager call");
                return;
            }
            _lastOpenTime = now;

            // Robust Toggle: If state is NOT closed, close everything.
            if (_state != CzeEditorState.Closed || _manager != null || _overlaysByHandle.Count > 0)
            {
                Logger.Info($"[ZoneEditorLauncher] Toggle OFF: State={_state}, Manager={(_manager != null)}, Overlays={_overlaysByHandle.Count}");
                System.Windows.Application.Current.Dispatcher.Invoke(CloseAllInternal);
                return;
            }

            Logger.Info("[ZoneEditorLauncher] Toggle ON: Starting OpenManager flow");
            await OpenManagerInternal(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneEditorLauncher] Error in ToggleManager: {ex.Message}");
        }
        finally
        {
            _openLock.Release();
        }
    }

    public async Task OpenManager(bool forceOpen)
    {
        if (!await _openLock.WaitAsync(0)) return;
        try { await OpenManagerInternal(forceOpen); }
        finally { _openLock.Release(); }
    }

    private async Task OpenManagerInternal(bool forceOpen)
    {
        try
        {
            Logger.Info("[ZoneEditorLauncher] Starting OpenManagerInternal (data gather)");

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
                if (_manager != null)
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(_manager).Handle;
                    if (VirtualDesktopManager.Instance.IsWindowOnCurrentDesktop(hwnd))
                    {
                        ActivateManager();
                        return;
                    }
                    else
                    {
                        Logger.Info("[ZoneEditorLauncher] Manager found on wrong desktop. Hard-resetting window.");
                        CloseManagerOnly();
                    }
                }

                if (_state == CzeEditorState.Closed || _overlaysByHandle.Count == 0)
                {
                    // Clean up and recreate overlays if needed
                    foreach (var ov in _overlaysByHandle.Values.ToList()) ov.Close();
                    _overlaysByHandle.Clear();

                    // One blocking overlay per monitor
                    foreach (var mon in data.monitors)
                    {
                        var overlay = new OverlayWindow(blocking: true);
                        overlay.SetupForMonitor(mon);
                        overlay.Closed += (_, _) => OnOverlayClosed();
                        overlay.Show();
                        _overlaysByHandle[mon.Handle] = overlay;
                    }
                }


                RefreshBackgroundVisuals();

                var primaryIdx = data.monitors.FindIndex(m => m.IsPrimary);
                if (primaryIdx < 0) primaryIdx = 0;
                var primaryMon     = data.monitors[primaryIdx];
                var primaryOverlay = _overlaysByHandle[primaryMon.Handle];

                _manager = new ZoneEditorManagerWindow();
                // _manager.Owner = primaryOverlay; // Removed to allow independent desktop pinning/movement

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

                _manager.Closed += (s, _) => { if (_manager == (Window)s!) _manager = null; };
                
                // Use ActivateManager to handle Show/Activate/Move logic consistently
                ActivateManager();


                // Removed PinWindow call to avoid rubber-banding focus issues.
                // Movement is now handled explicitly via Hide-Move-Show in MoveAllExistingToDesktop.


                _lastKnownDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
                StartDesktopPolling();

                SetState(CzeEditorState.Admin);
                Logger.Info("[ZoneEditorLauncher] Opened Layout Manager");
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneEditorLauncher] Error in OpenManagerInternal: {ex.Message}");
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
            foreach (var o in _overlaysByHandle.Values) o.ClearZones();

            var monitors   = MonitorManager.GetActiveMonitors();
            int targetIdx  = monitors.FindIndex(m => m.HardwareId == monitorHardwareId);
            if (targetIdx < 0) targetIdx = monitors.FindIndex(m => m.IsPrimary);
            if (targetIdx < 0) targetIdx = 0;

            var targetMon = monitors[targetIdx];
            if (!_overlaysByHandle.TryGetValue(targetMon.Handle, out var targetOverlay))
            {
                // Fallback to primary or any available if handle matching fails
                targetOverlay = _overlaysByHandle.Values.FirstOrDefault();
            }

            if (targetOverlay == null)
            {
                Logger.Error("[ZoneEditorLauncher] Cannot open canvas: No overlay window found.");
                return;
            }

            _editCanvas = new ZoneCanvasEditorWindow(targetMon.HardwareId, layoutId, "edit");
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
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            if (_state != CzeEditorState.Editing) return;

            Logger.Info($"[ZoneEditorLauncher] Returning to Admin view (discard={isDiscard})...");

            // Cleanup if it was a new draft and we are discarding
            if (isDiscard && _isNewLayout && !string.IsNullOrEmpty(_lastDraftLayoutId))
            {
                Logger.Info($"[ZoneEditorLauncher] Deleting discarded new layout: {_lastDraftLayoutId}");
                ConfigManager.Instance.Config.CzeLayouts.Remove(_lastDraftLayoutId);
                await ConfigManager.Instance.SaveAsync();
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

    /// <summary>
    /// Closes only the Manager window. Used during desktop transitions to break focal ties.
    /// Does NOT close overlays or clear the session state.
    /// </summary>
    public void CloseManagerOnly()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_manager != null)
            {
                Logger.Info("[ZoneEditorLauncher] Closing Manager window for desktop transition.");
                _manager.Close();
                _manager = null;
            }
        });
    }

    public void ActivateManager()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_manager == null) return;
            
            // Ensure the window has a handle (HWND) created
            var helper = new System.Windows.Interop.WindowInteropHelper(_manager);
            var hwnd = helper.EnsureHandle();

            // Ensure not minimized
            if (User32.IsIconic(hwnd))
                User32.ShowWindow(hwnd, User32.SW_RESTORE);

            // 1. Show all overlays first (background) and ensure they are on current desktop
            foreach (var overlay in _overlaysByHandle.Values) 
            {
                var oHelper = new System.Windows.Interop.WindowInteropHelper(overlay);
                var oHwnd = oHelper.EnsureHandle();
                if (oHwnd != nint.Zero) VirtualDesktopManager.Instance.MoveWindowToCurrentDesktop(oHwnd);
                overlay.Show();
            }

            // 2. Show and activate manager
            _manager.Show();
            _manager.Activate();
            User32.SetForegroundWindow(hwnd);

            // Re-assert TopMost just in case overlays covered it
            User32.SetWindowPos(hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);

        });
    }
    
    /// <summary>
    /// Manually updates the last known desktop ID without triggering refresh visuals.
    /// Used by the bridge during manual switches to prevent the polling timer from 
    /// detecting a "new" switch and double-firing transition logic.
    /// </summary>
    public void SyncDesktopState(Guid desktopId)
    {
        Logger.Info($"[ZoneEditorLauncher] SyncDesktopState: Manually updating lastKnownId to {desktopId}");
        _lastKnownDesktopId = desktopId;
    }

    public void SetManualSwitchInProgress(bool active)
    {
        _isManualSwitchInProgress = active;
    }

    /// <summary>
    /// Forcefully moves the Manager, Overlays, and any active Editor windows to a specific desktop.
    /// Useful when the user switches desktops via the UI and expects the editor to follow.
    /// </summary>
    public void MoveAllExistingToDesktop(Guid desktopId)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logger.Info($"[ZoneEditorLauncher] Moving all editor windows to desktop {desktopId}");
            var vdm = VirtualDesktopManager.Instance;

            // 1. Hide windows to minimize visual artifacts and avoid focus-pulling during move
            _manager?.Hide();
            foreach (var overlay in _overlaysByHandle.Values) overlay.Hide();

            // 2. Move the Manager Window
            if (_manager != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_manager).Handle;
                if (hwnd != nint.Zero) vdm.MoveWindowToDesktopInternal(hwnd, desktopId);
            }

            // 3. Move all Overlays
            foreach (var overlay in _overlaysByHandle.Values)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
                if (hwnd != nint.Zero) vdm.MoveWindowToDesktopInternal(hwnd, desktopId);
            }

            // 4. Move active editor/canvas if open
            if (_editCanvas != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_editCanvas).Handle;
                if (hwnd != nint.Zero) vdm.MoveWindowToDesktopInternal(hwnd, desktopId);
            }
            if (_controlDialog != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_controlDialog).Handle;
                if (hwnd != nint.Zero) vdm.MoveWindowToDesktopInternal(hwnd, desktopId);
            }

            // 5. DO NOT restore windows here. 
            // We wait for the desktop switch to complete and then call ActivateManager().
            // This ensures they only reappear once the new desktop is fully active.
            Logger.Info("[ZoneEditorLauncher] Windows moved and stay hidden for desktop switch transition.");


            // FINAL STEP: Ensure Manager is on TOP of all moved windows/overlays
            if (_manager != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_manager).Handle;
                if (hwnd != nint.Zero)
                {
                    User32.SetWindowPos(hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
                        User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
                }
            }
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

    // ── Internal / Lifecycle ────────────────────────────────────────────────

    private readonly SemaphoreSlim             _openLock     = new(1, 1);

    public void CloseAll() => CloseAllInternal();

    private void CloseAllInternal()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed && _manager == null && _overlaysByHandle.Count == 0) return;
            
            SetState(CzeEditorState.Closed);
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

            StopDesktopPolling();

            foreach (var o in _overlaysByHandle.Values.ToList()) o.Close();
            _overlaysByHandle.Clear();

            Logger.Info("[ZoneEditorLauncher] Closed all Zone Editor windows");
        });
    }

    private void StartDesktopPolling()
    {
        if (_desktopPollTimer != null) return;

        _desktopPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250) // Faster polling for better "following"
        };
        _desktopPollTimer.Tick += (s, e) => CheckDesktopSwitch();
        _desktopPollTimer.Start();
    }

    private void StopDesktopPolling()
    {
        _desktopPollTimer?.Stop();
        _desktopPollTimer = null;
    }

    private void CheckDesktopSwitch()
    {
        if (_isManualSwitchInProgress) return;

        var currentId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (currentId != _lastKnownDesktopId)
        {
            Logger.Info($"[ZoneEditorLauncher] Desktop switch detected: {_lastKnownDesktopId} -> {currentId}");
            _lastKnownDesktopId = currentId;
            
            // ENSURE windows follow the user to the new desktop
            if (currentId.HasValue)
            {
                MoveAllExistingToDesktop(currentId.Value);
            }

            // Refresh visuals for the new desktop FIRST
            RefreshBackgroundVisuals();
            
            // ENSURE manager is visible and on top on the new desktop
            ActivateManager();
            
            // Notify frontend
            Bridge.WebBridge.Broadcast("desktop_switched", new { 
                desktopId = currentId?.ToString("D"),
                currentId = currentId
            });
        }
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

    private WorkspaceLauncher.Core.CustomZoneEngine.Models.CZELayout? GetVirtualTemplate(string layoutId)
    {
        var id = (layoutId ?? "").ToLowerInvariant();
        var layout = new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZELayout { Id = id, Name = id };

        if (id == "foco")
        {
            layout.Name = "Foco";
            layout.Zones = [new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 1, X = 1000, Y = 1000, W = 8000, H = 8000 }];
        }
        else if (id == "columnas")
        {
            layout.Name = "Columnas";
            layout.Zones = [
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 1, X = 0,    Y = 0, W = 3333, H = 10000 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 2, X = 3333, Y = 0, W = 3334, H = 10000 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 3, X = 6667, Y = 0, W = 3333, H = 10000 }
            ];
        }
        else if (id == "filas")
        {
            layout.Name = "Filas";
            layout.Zones = [
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 1, X = 0, Y = 0,    W = 10000, H = 3333 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 2, X = 0, Y = 3333, W = 10000, H = 3334 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 3, X = 0, Y = 6667, W = 10000, H = 3333 }
            ];
        }
        else if (id == "cuadricula")
        {
            layout.Name = "Cuadrícula";
            layout.Zones = [
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 1, X = 0,    Y = 0,    W = 5000, H = 5000 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 2, X = 5000, Y = 0,    W = 5000, H = 5000 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 3, X = 0,    Y = 5000, W = 5000, H = 5000 },
                new WorkspaceLauncher.Core.CustomZoneEngine.Models.CZEZone { Id = 4, X = 5000, Y = 5000, W = 5000, H = 5000 }
            ];
        }
        else if (id == "sin")
        {
             return null;
        }
        else return null;

        return layout;
    }

    public void RefreshBackgroundVisuals()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
            Logger.Info("[RefreshVisuals] Starting background refresh...");
            MonitorManager.InvalidateCache(); // Force fresh monitor scan
            var monitors = MonitorManager.GetActiveMonitors();
            FancyZonesReader.InvalidateCaches(); // Ensure fresh data
            var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
            var availableLayouts = FancyZonesReader.GetAvailableLayouts();
            var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
            string currentDesktopStr = currentDesktopId.ToString().ToLowerInvariant();
            var engine = ConfigManager.Instance.Config.ZoneEngine;

            Logger.Info($"[RefreshVisuals] Engine={engine}, Desktop={currentDesktopStr}, Applying to {monitors.Count} monitors.");

            foreach (var mon in monitors)
            {
                Logger.Info($"[RefreshVisuals] Processing monitor: {mon.Name} ({mon.Handle})");
                if (!_overlaysByHandle.TryGetValue(mon.Handle, out var overlay))
                {
                    Logger.Warn($"[RefreshVisuals] -> SKIPPING monitor {mon.Name}: No overlay window for handle {mon.Handle}. (Available: {string.Join(", ", _overlaysByHandle.Keys)})");
                    continue;
                }
                overlay.ClearZones();

                if (engine == "cze")
                {
                    string key = WorkspaceLauncher.Core.CustomZoneEngine.Models.ActiveLayoutMap.MakeKey(mon.PtInstance ?? "", currentDesktopId);
                    
                    if (ConfigManager.Instance.Config.CzeActiveLayouts.TryGetValue(key, out var czeLayoutId))
                    {
                        Logger.Info($"[RefreshVisuals] Found active CZE layout ID {czeLayoutId} for monitor {mon.Name}");
                        if (ConfigManager.Instance.Config.CzeLayouts.TryGetValue(czeLayoutId, out var czeLayout))
                        {
                            Logger.Info($"[RefreshVisuals] Rendering custom CZE layout {czeLayoutId}");
                            overlay.ShowCzeBackgroundPreview(czeLayout, mon);
                        }
                        else
                        {
                             // Fallback to virtual templates (Columns, Row, etc.)
                             var template = GetVirtualTemplate(czeLayoutId);
                             if (template != null)
                             {
                                 Logger.Info($"[RefreshVisuals] Rendering virtual template {czeLayoutId}");
                                 overlay.ShowCzeBackgroundPreview(template, mon);
                             }
                             else
                             {
                                 Logger.Warn($"[RefreshVisuals] CZE Layout {czeLayoutId} not found in config or templates.");
                             }
                        }
                    }
                    else
                    {
                        Logger.Warn($"[RefreshVisuals] No CZE active layout found for key: {key}");
                    }
                }
                else
                {
                    Logger.Info($"[RefreshVisuals] FZ matching for {mon.Name} (PtInstance={mon.PtInstance}, PtName={mon.PtName})");
                    var match = appliedLayouts
                        .Where(ae => {
                            string monInstNorm = (mon.PtInstance ?? "").Trim('{', '}').ToLowerInvariant();
                            string monPtNorm = (mon.PtName ?? "").ToLowerInvariant();
                            string monSerial = (mon.SerialNumber ?? "").ToLowerInvariant();

                            string aeInstNorm = (ae.Instance ?? "").Trim('{', '}').ToLowerInvariant();
                            string aeMonNorm = (ae.MonitorName ?? "").ToLowerInvariant();
                            string aeSerial = (ae.SerialNumber ?? "").ToLowerInvariant();

                            bool sMatch = !string.IsNullOrEmpty(monSerial) && monSerial == aeSerial;
                            bool iMatch = !string.IsNullOrEmpty(monInstNorm) && (monInstNorm == aeInstNorm || aeInstNorm.Contains(monInstNorm));
                            bool nMatch = !string.IsNullOrEmpty(monPtNorm) && (monPtNorm == aeMonNorm || aeMonNorm.Contains(monPtNorm));

                            bool mMatch = sMatch || iMatch || nMatch;
                            if (!mMatch) return false;

                            string aeDkNorm = (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant();
                            bool isAllDesktops = string.IsNullOrEmpty(aeDkNorm) || aeDkNorm == "00000000-0000-0000-0000-000000000000";
                            bool isExactMatch = aeDkNorm == currentDesktopStr;
                            
                            bool finalMatch = isExactMatch || isAllDesktops;
                            if (finalMatch) {
                                Logger.Info($"[RefreshVisuals] -> Potential match: {ae.MonitorName} Layout={ae.LayoutUuid} Desktop={ae.DesktopId} (Exact={isExactMatch}, All={isAllDesktops})");
                            }
                            return finalMatch;
                        })
                        .OrderByDescending(ae => (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant() == currentDesktopStr)
                        .FirstOrDefault();

                    if (match != null)
                    {
                        Logger.Info($"[RefreshVisuals] Found FZ match for {mon.Name}: Layout={match.LayoutUuid}");
                        var layout = availableLayouts.FirstOrDefault(l => l.uuid.Equals(match.LayoutUuid, StringComparison.OrdinalIgnoreCase))
                                     ?? availableLayouts.FirstOrDefault(l => l.type.Equals(match.LayoutType, StringComparison.OrdinalIgnoreCase));

                        if (layout?.info is System.Text.Json.JsonElement infoElement)
                        {
                            var fzLayout = WorkspaceOrchestrator.ParseLayoutInfo(new LayoutCacheEntry { Type = layout.type, Info = infoElement });
                            if (fzLayout != null)
                            {
                                if (match.Spacing >= 0) { fzLayout.Spacing = match.Spacing; fzLayout.ShowSpacing = match.ShowSpacing; }
                                var zoneRects = ZoneCalculator.CalculateAllZones(fzLayout, mon.WorkArea);
                                overlay.ShowBackgroundPreview(zoneRects, mon);
                            }
                        }
                    }
                    else
                    {
                        Logger.Warn($"[RefreshVisuals] No FZ match for {mon.Name}");
                    }
                }
            }
        }
        catch (Exception ex) { Logger.Error($"[ZoneEditorLauncher] RefreshBackgroundVisuals error: {ex.Message}"); }
        
        // RE-ASSERT Manager Z-Order so it's not covered by the newly shown overlays
        ActivateManager();
        });
    }
}
