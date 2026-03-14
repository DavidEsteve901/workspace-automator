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
    private bool  _isTransitioningDesktops;

    private readonly SemaphoreSlim             _openLock     = new(1, 1);
    private readonly SemaphoreSlim             _switchLock   = new(1, 1);

    public async Task<bool> AcquireSwitchLock(int timeoutMs = 2000)
    {
        if (_switchLock == null) return false;
        return await _switchLock.WaitAsync(timeoutMs);
    }

    public void ReleaseSwitchLock()
    {
        try { _switchLock?.Release(); } catch { }
    }

    public async void ToggleManager()
    {
        if (!await _openLock.WaitAsync(0)) return;
        try
        {
            var now = DateTime.Now;
            if ((now - _lastOpenTime).TotalMilliseconds < 400) return;
            _lastOpenTime = now;

            if (_state != CzeEditorState.Closed || _manager != null || _overlaysByHandle.Count > 0)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(CloseAllInternal);
                return;
            }
            await OpenManagerInternal(false);
        }
        catch (Exception ex) { Logger.Error($"[ZoneEditorLauncher] Error in ToggleManager: {ex.Message}"); }
        finally { _openLock.Release(); }
    }

    public async Task OpenManager(bool forceOpen, Guid? targetDesktopId = null)
    {
        if (!await _openLock.WaitAsync(0)) return;
        try { await OpenManagerInternal(forceOpen, targetDesktopId); }
        finally { _openLock.Release(); }
    }

    private async Task OpenManagerInternal(bool forceOpen, Guid? targetDesktopId = null)
    {
        try
        {
            var data = await Task.Run(() =>
            {
                var monitors = MonitorManager.GetActiveMonitors();
                var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
                var availableLayouts = FancyZonesReader.GetAvailableLayouts();
                var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
                return new { monitors, appliedLayouts, availableLayouts, currentDesktopId };
            });

            if (data.monitors.Count == 0) return;

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
                        CloseManagerOnly();
                    }
                }

                if (_state == CzeEditorState.Closed || _overlaysByHandle.Count == 0)
                {
                    foreach (var ov in _overlaysByHandle.Values.ToList()) ov.Close();
                    _overlaysByHandle.Clear();

                    foreach (var mon in data.monitors)
                    {
                        var overlay = new OverlayWindow(blocking: true);
                        overlay.SetupForMonitor(mon);
                        overlay.Closed += (_, _) => OnOverlayClosed();
                        
                        if (targetDesktopId.HasValue)
                        {
                            var ovHwnd = new System.Windows.Interop.WindowInteropHelper(overlay).EnsureHandle();
                            VirtualDesktopManager.Instance.MoveWindowToDesktop(ovHwnd, targetDesktopId.Value);
                        }
                        overlay.Show();
                        _overlaysByHandle[mon.Handle] = overlay;
                    }
                }

                _manager = new ZoneEditorManagerWindow();
                var primaryIdx = data.monitors.FindIndex(m => m.IsPrimary);
                if (primaryIdx < 0) primaryIdx = 0;
                var primaryMon = data.monitors[primaryIdx];

                double scale = primaryMon.Scale / 100.0;
                _manager.Width  = Math.Clamp(primaryMon.WorkArea.Width * 0.85 / scale, 1000, 1400);
                _manager.Height = Math.Clamp(primaryMon.WorkArea.Height * 0.80 / scale, 700, 950);
                _manager.Left = (primaryMon.WorkArea.Left / scale) + (primaryMon.WorkArea.Width / scale - _manager.Width) / 2;
                _manager.Top  = (primaryMon.WorkArea.Top / scale) + (primaryMon.WorkArea.Height / scale - _manager.Height) / 2;

                _manager.Closed += (s, _) => { if (_manager == (System.Windows.Window)s!) _manager = null; if (!_isTransitioningDesktops) CloseAll(); };
                
                // Ownership ensures Z-order: Manager will always stay above the Primary Overlay
                if (_overlaysByHandle.TryGetValue(primaryMon.Handle, out var primaryOverlay))
                {
                    _manager.Owner = primaryOverlay;
                }
                if (targetDesktopId.HasValue)
                {
                    var mHwnd = new System.Windows.Interop.WindowInteropHelper(_manager).EnsureHandle();
                    VirtualDesktopManager.Instance.MoveWindowToDesktop(mHwnd, targetDesktopId.Value);
                }

                ActivateManager();
                _isTransitioningDesktops = false; 
                _lastKnownDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
                StartDesktopPolling();
                RefreshBackgroundVisuals();
                SetState(CzeEditorState.Admin);
            });
        }
        catch (Exception ex) { Logger.Error($"[ZoneEditorLauncher] Error in OpenManagerInternal: {ex.Message}"); }
    }

    public void OpenCanvas(string monitorHardwareId, string layoutId = "", bool isNew = false)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed) return;
            _lastDraftLayoutId = layoutId;
            _isNewLayout = isNew;
            _manager?.Hide();
            _editCanvas?.Close();
            foreach (var o in _overlaysByHandle.Values) o.ClearZones();

            var monitors = MonitorManager.GetActiveMonitors();
            var targetMon = monitors.FirstOrDefault(m => m.HardwareId == monitorHardwareId) ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();
            if (!_overlaysByHandle.TryGetValue(targetMon.Handle, out var targetOverlay)) targetOverlay = _overlaysByHandle.Values.FirstOrDefault();
            if (targetOverlay == null) return;

            _editCanvas = new ZoneCanvasEditorWindow(targetMon.HardwareId, layoutId, "edit") { Owner = targetOverlay };
            double scale = targetMon.Scale / 100.0;
            _editCanvas.Left = targetMon.WorkArea.Left / scale;
            _editCanvas.Top = targetMon.WorkArea.Top / scale;
            _editCanvas.Width = targetMon.WorkArea.Width / scale;
            _editCanvas.Height = targetMon.WorkArea.Height / scale;
            _editCanvas.Closed += OnEditorWindowClosed;
            _editCanvas.Show();
            _editCanvas.Activate();

            _controlDialog = new ZoneEditorControlWindow(targetMon.HardwareId, layoutId) { Owner = _editCanvas };
            _controlDialog.Closed += OnEditorWindowClosed;
            _controlDialog.Show();
            _controlDialog.Activate();
            SetState(CzeEditorState.Editing);
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

    private void OnEditorWindowClosed(object? sender, EventArgs e) => ReturnToAdmin();

    public void ReturnToAdmin() => ReturnToAdmin(false);
    public void ReturnToAdmin(bool isDiscard)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            if (_state != CzeEditorState.Editing) return;
            if (isDiscard && _isNewLayout && !string.IsNullOrEmpty(_lastDraftLayoutId))
            {
                ConfigManager.Instance.Config.CzeLayouts.Remove(_lastDraftLayoutId);
                await ConfigManager.Instance.SaveAsync();
            }

            if (_editCanvas != null) _editCanvas.Closed -= OnEditorWindowClosed;
            if (_controlDialog != null) _controlDialog.Closed -= OnEditorWindowClosed;
            try { _editCanvas?.Close(); _controlDialog?.Close(); } catch { }

            _editCanvas = null; _controlDialog = null; _lastDraftLayoutId = null; _isNewLayout = false;

            if (_state == CzeEditorState.Closed) return;
            if (_manager != null) { _manager.Show(); ActivateManager(); SetState(CzeEditorState.Admin); }
            else _ = OpenManager(true);
            RefreshBackgroundVisuals();
        });
    }

    public void CloseManagerOnly()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_manager != null) { _isTransitioningDesktops = true; _manager.Close(); _manager = null; }
        });
    }

    public void ActivateManager()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_manager == null) return;
            var helper = new System.Windows.Interop.WindowInteropHelper(_manager);
            var hwnd = helper.EnsureHandle();
            if (User32.IsIconic(hwnd)) User32.ShowWindow(hwnd, User32.SW_RESTORE);

            foreach (var overlay in _overlaysByHandle.Values) overlay.Show();

            _manager.Show();
            _manager.Activate();
            User32.SetForegroundWindow(hwnd);
            User32.SetWindowPos(hwnd, (nint)(-1), 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
        });
    }

    public void SyncDesktopState(Guid desktopId) => _lastKnownDesktopId = desktopId;
    public void SetManualSwitchInProgress(bool active) => _isManualSwitchInProgress = active;

    public async Task PrepareForSwitch()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Logger.Info("[ZoneEditorLauncher] [TRANSITION] Phase 1: Hiding windows to break focal bond.");
            _manager?.Hide();
            _editCanvas?.Hide();
            _controlDialog?.Hide();
        });
    }

    public async Task FinishSwitch(Guid targetDesktopId)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var vdm = VirtualDesktopManager.Instance;
            Logger.Info($"[ZoneEditorLauncher] [TRANSITION] Phase 2: Moving and showing windows on Desktop: {targetDesktopId}");
            
            if (_manager != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_manager).Handle;
                bool moved = vdm.MoveWindowToDesktopInternal(hwnd, targetDesktopId);
                if (!moved) vdm.MoveWindowToDesktop(hwnd, targetDesktopId);

                int verifyAttempts = 25; // Check every 15ms for max 375ms
                bool verified = false;
                while (verifyAttempts > 0)
                {
                    if (vdm.GetWindowDesktopId(hwnd) == targetDesktopId) { verified = true; break; }
                    verifyAttempts--; if (verifyAttempts > 0) await Task.Delay(15);
                }

                if (verified) Logger.Info("[ZoneEditorLauncher] [TRANSITION] Manager movement VERIFIED.");
                else Logger.Warn("[ZoneEditorLauncher] [TRANSITION] Movement FAILED verification. Showing anyway.");
                
                _manager.Show();
                ActivateManager();
            }

            if (_editCanvas != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_editCanvas).Handle;
                vdm.MoveWindowToDesktopInternal(hwnd, targetDesktopId);
                _editCanvas.Show(); _editCanvas.Activate();
            }

            if (_controlDialog != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_controlDialog).Handle;
                vdm.MoveWindowToDesktopInternal(hwnd, targetDesktopId);
                _controlDialog.Show(); _controlDialog.Activate();
            }

            _lastKnownDesktopId = targetDesktopId;
            RefreshBackgroundVisuals();
            Logger.Info("[ZoneEditorLauncher] [TRANSITION] Transition completed.");
        });
    }

    public async Task MoveAllExistingToDesktop(Guid desktopId) { await PrepareForSwitch(); await FinishSwitch(desktopId); }

    public void CloseCanvas() { System.Windows.Application.Current.Dispatcher.Invoke(() => { _editCanvas?.Close(); _editCanvas = null; }); }
    public void RestoreManager() => ReturnToAdmin();

    public void CloseAll() => CloseAllInternal();
    private void CloseAllInternal()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_state == CzeEditorState.Closed && _manager == null && _overlaysByHandle.Count == 0) return;
            SetState(CzeEditorState.Closed);
            if (_editCanvas != null) { _editCanvas.Closed -= OnEditorWindowClosed; try { _editCanvas.Close(); } catch { } _editCanvas = null; }
            if (_controlDialog != null) { _controlDialog.Closed -= OnEditorWindowClosed; try { _controlDialog.Close(); } catch { } _controlDialog = null; }
            if (_manager != null) { try { _manager.Close(); } catch { } _manager = null; }
            StopDesktopPolling();
            foreach (var o in _overlaysByHandle.Values.ToList()) o.Close();
            _overlaysByHandle.Clear();
        });
    }

    private void StartDesktopPolling()
    {
        if (_desktopPollTimer != null) return;
        _desktopPollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _desktopPollTimer.Tick += (s, e) => CheckDesktopSwitch();
        _desktopPollTimer.Start();
    }

    private void StopDesktopPolling() { _desktopPollTimer?.Stop(); _desktopPollTimer = null; }

    private async void CheckDesktopSwitch()
    {
        if (VirtualDesktopManager.Instance == null || _switchLock == null || _isManualSwitchInProgress || _switchLock.CurrentCount == 0) return;
        var currentId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (currentId != _lastKnownDesktopId)
        {
            Logger.Info($"[ZoneEditorLauncher] [POLLING] Desktop switch detected: {_lastKnownDesktopId} -> {currentId}");
            _lastKnownDesktopId = currentId;
            if (currentId.HasValue) await MoveAllExistingToDesktop(currentId.Value);
            Bridge.WebBridge.Broadcast("desktop_switched", new { desktopId = currentId?.ToString("D"), currentId = currentId });
        }
    }

    private void OnOverlayClosed() { if (_state != CzeEditorState.Closed) System.Windows.Application.Current.Dispatcher.BeginInvoke(CloseAll); }
    private void SetState(CzeEditorState state) { _state = state; Bridge.WebBridge.BroadcastCzeState(state.ToString().ToLowerInvariant()); }

    private WorkspaceLauncher.Core.CustomZoneEngine.Models.CZELayout? GetVirtualTemplate(string layoutId)
    {
        return WorkspaceLauncher.Core.CustomZoneEngine.Models.CZETemplateHelper.GetVirtualTemplate(layoutId);
    }

    public async void RefreshBackgroundVisuals()
    {
        // Gather data on background thread
        var data = await Task.Run(() => 
        {
            MonitorManager.InvalidateCache();
            var monitors = MonitorManager.GetActiveMonitors();
            FancyZonesReader.InvalidateCaches();
            var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
            var availableLayouts = FancyZonesReader.GetAvailableLayouts();
            var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
            return new { monitors, appliedLayouts, availableLayouts, currentDesktopId };
        });

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                string currentDesktopStr = data.currentDesktopId.ToString().ToLowerInvariant();
                var engine = ConfigManager.Instance.Config.ZoneEngine;

                foreach (var mon in data.monitors)
                {
                    if (!_overlaysByHandle.TryGetValue(mon.Handle, out var overlay)) continue;
                    overlay.ClearZones();
                    if (engine == "cze")
                    {
                        string key = WorkspaceLauncher.Core.CustomZoneEngine.Models.ActiveLayoutMap.MakeKey(mon.PtInstance ?? "", data.currentDesktopId);
                        if (ConfigManager.Instance.Config.CzeActiveLayouts.TryGetValue(key, out var czeLayoutId))
                        {
                            if (ConfigManager.Instance.Config.CzeLayouts.TryGetValue(czeLayoutId, out var czeLayout)) overlay.ShowCzeBackgroundPreview(czeLayout, mon);
                            else { var template = GetVirtualTemplate(czeLayoutId); if (template != null) overlay.ShowCzeBackgroundPreview(template, mon); }
                        }
                    }
                    else
                    {
                        var match = data.appliedLayouts.Where(ae => {
                            string monInstNorm = (mon.PtInstance ?? "").Trim('{', '}').ToLowerInvariant();
                            string monPtNorm = (mon.PtName ?? "").ToLowerInvariant();
                            string monSerial = (mon.SerialNumber ?? "").ToLowerInvariant();
                            string aeInstNorm = (ae.Instance ?? "").Trim('{', '}').ToLowerInvariant();
                            string aeMonNorm = (ae.MonitorName ?? "").ToLowerInvariant();
                            string aeSerial = (ae.SerialNumber ?? "").ToLowerInvariant();
                            bool isExactMatch = (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant() == currentDesktopStr;
                            bool isAllDesktops = string.IsNullOrEmpty(ae.DesktopId) || ae.DesktopId == "00000000-0000-0000-0000-000000000000";
                            return (monSerial == aeSerial || monInstNorm == aeInstNorm || monPtNorm == aeMonNorm) && (isExactMatch || isAllDesktops);
                        }).OrderByDescending(ae => (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant() == currentDesktopStr).FirstOrDefault();

                        if (match != null)
                        {
                            var layout = data.availableLayouts.FirstOrDefault(l => l.uuid.Equals(match.LayoutUuid, StringComparison.OrdinalIgnoreCase));
                            if (layout?.info is System.Text.Json.JsonElement infoElement)
                            {
                                var fzLayout = WorkspaceOrchestrator.ParseLayoutInfo(new LayoutCacheEntry { Type = layout.type, Info = infoElement });
                                if (fzLayout != null) { fzLayout.Spacing = match.Spacing; fzLayout.ShowSpacing = match.ShowSpacing; overlay.ShowBackgroundPreview(ZoneCalculator.CalculateAllZones(fzLayout, mon.WorkArea), mon); }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Error($"[ZoneEditorLauncher] RefreshBackgroundVisuals error: {ex.Message}"); }
            
            // Re-activate manager to ensure it stays on top of the newly shown overlays
            if (_state == CzeEditorState.Admin) ActivateManager();
        });
    }
}








