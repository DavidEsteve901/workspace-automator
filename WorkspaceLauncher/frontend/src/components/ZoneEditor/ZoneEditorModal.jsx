import { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { bridge, onEvent, offEvent } from '../../api/bridge.js';
import PremiumSelect from '../PremiumSelect.jsx';
import ConfirmModal from '../AppList/ConfirmModal.jsx';
import { ErrorBoundary } from '../ErrorBoundary.jsx';
import '../../App.css';
import { useZoneEditor, gridToZones } from './ZoneEditorHooks';
import { ZoneCanvas } from './ZoneCanvas';
import { ZoneToolbar } from './ZoneToolbar';
import { 
  Info, Save, Trash2, Plus, Monitor, Layout, X, 
  Settings, Edit3, Trash, Copy, MoreVertical, ChevronRight,
  Maximize2, MousePointer2, Keyboard, Layers, Crown
} from 'lucide-react';

export function ZoneEditorModal(props) {
  return (
    <ErrorBoundary>
      <ZoneEditorModalInner {...props} />
    </ErrorBoundary>
  );
}

function ZoneEditorModalInner({ onClose, standalone = false, canvasOnly = false, controlOnly = false, canvasMode = 'edit', initialMonitorId = null, initialLayoutId = null, isNew = false }) {
  const { t } = useTranslation();
  const [monitors, setMonitors] = useState([]);
  const [layouts, setLayouts] = useState([]);
  const [searchQuery, setSearchQuery] = useState('');
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [layoutToDelete, setLayoutToDelete] = useState(null);
  const [activeLayouts, setActiveLayouts] = useState([]);
  const [currentDesktopId, setCurrentDesktopId] = useState(null);
  const [desktops, setDesktops] = useState([]);
  const [selectedDesktopId, setSelectedDesktopId] = useState(null);
  
  const handleSwitchDesktop = async (id, isManual = false) => {
    setSelectedDesktopId(id);
    if (!id) return;
    
    if (!isManual) {
      await bridge.czeSwitchToDesktop(id);
    } else {
      // If manual (from backend event), just refresh everything for that desktop
      await loadAll();
    }
  };
  const [activeMonitorId, setActiveMonitorId] = useState(initialMonitorId);
  const [editingLayout, setEditingLayout] = useState(null);
  const [menuOpenId, setMenuOpenId] = useState(null);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [isInitialLoad, setIsInitialLoad] = useState(true);

  const { grid, zones, spacing, setSpacing, selectedIds, selectZone, clearSelection, splitZone, mergeSelected, moveDivider, removeDivider, applyPreset, resetToFull, setGridFromZones, setGridFromGridState } = useZoneEditor([]);

  const [selectedLayoutId, setSelectedLayoutId] = useState(null);

  useEffect(() => {
    loadAll().finally(() => {
      setLoading(false);
      // Wait for cards to animate in, then disable initial load class to prevent re-triggering
      setTimeout(() => setIsInitialLoad(false), 2000);
    });
  }, []);

  useEffect(() => {
    if (controlOnly) return; // Controls handled in the instruction dialog
    if (!canvasOnly || canvasMode !== 'edit') return;
    const handleKeys = (e) => {
      if (e.key === 's' || e.key === 'S') {
        const selectedId = Array.from(selectedIds)[0];
        if (selectedId) splitZone(selectedId, 0.5, 0.5);
      } else if (e.key === 'Enter') {
        saveLayoutProperties();
      } else if (e.key === 'Escape') {
        window.chrome.webview.postMessage({ action: 'cze_canvas_discard' });
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [canvasOnly, canvasMode, selectedIds, splitZone, controlOnly]);

  useEffect(() => {
    // Escuchar eventos globales del bridge (para sincronizar entre ventanas)
    const handleCanvasAction = (data) => {
        if (data && data.action === 'save') {
           saveLayoutProperties();
        }
        if (data && data.action === 'discard') {
           window.chrome.webview.postMessage({ action: 'cze_canvas_discard' });
        }
    };

    if (canvasOnly) {
        onEvent('cze_remote_action', handleCanvasAction);
        return () => offEvent('cze_remote_action', handleCanvasAction);
    }
  }, [canvasOnly, editingLayout, zones, grid, spacing]);

  useEffect(() => {
    // Escuchar actualizaciones globales para refrescar la lista de diseños y estados
    const handleRefresh = () => {
      console.log("[CZE] Refreshing due to bridge event");
      loadAll();
    };

    const handleDesktopSwitched = async (data) => {
      console.log("[CZE] Manual desktop switch detected:", data);
      const newId = data.desktopId;
      if (newId) {
        handleSwitchDesktop(newId, true);
      }
    };

    const handleCancelled = () => {
      console.log("[CZE] Edit session cancelled, clearing modal");
      setEditingLayout(null);
    };

    const handleFinished = (data) => {
      console.log("[CZE] Edit session finished, focusing layout:", data.layoutId);
      if (data.layoutId && !canvasOnly) {
         // Find the saved layout in the current list (refreshed by loadAll)
         bridge.czeGetLayouts().then(res => {
            const layouts = res?.layouts || [];
            const saved = layouts.find(l => l.id === data.layoutId);
            if (saved) {
               setEditingLayout(saved);
               if (saved.gridState) {
                  setGridFromGridState(saved.gridState, saved.spacing || 0);
               } else {
                  setGridFromZones(saved.zones || [], saved.spacing || 0);
               }
            }
         });
      }
    };

    onEvent('state_update', handleRefresh);
    onEvent('cze_state_changed', handleRefresh);
    onEvent('desktop_switched', handleDesktopSwitched);
    onEvent('cze_operation_cancelled', handleCancelled);
    onEvent('cze_editor_finished', handleFinished);

    // Close menu when clicking outside
    const handleClick = () => setMenuOpenId(null);
    window.addEventListener('click', handleClick);
    
    return () => {
      offEvent('state_update', handleRefresh);
      offEvent('cze_state_changed', handleRefresh);
      offEvent('desktop_switched', handleDesktopSwitched);
      offEvent('cze_operation_cancelled', handleCancelled);
      offEvent('cze_editor_finished', handleFinished);
      window.removeEventListener('click', handleClick);
    };
  }, []);

  async function loadAll() {
    try {
      const [lRes, aRes, mRes, dRes] = await Promise.all([
        bridge.czeGetLayouts(),
        bridge.czeGetActiveLayouts(),
        bridge.listMonitors(),
        bridge.listDesktops()
      ]);

      // Normalize zones from int units (0–10000) to fractions (0.0–1.0)
      const normZones = (zones) => (zones || []).map(z => ({
        ...z,
        x: z.x > 1 ? z.x / 10000 : z.x,
        y: z.y > 1 ? z.y / 10000 : z.y,
        w: z.w > 1 ? z.w / 10000 : z.w,
        h: z.h > 1 ? z.h / 10000 : z.h,
      }));

      const rawLayouts = lRes?.layouts ?? [];
      // Attach normalized zones so all consumers get fractions
      const normalizedLayouts = rawLayouts.map(l => ({ ...l, zones: normZones(l.zones) }));

      setLayouts(normalizedLayouts);
      setActiveLayouts(aRes?.entries ?? []);
      setMonitors(mRes ?? []);
      setDesktops(dRes ?? []);
      setCurrentDesktopId(aRes?.currentDesktopId);
      
      // If no desktop is selected yet, use the current active one
      if (!selectedDesktopId) {
        setSelectedDesktopId(aRes?.currentDesktopId);
      }

      const monitorId = activeMonitorId || (mRes?.length > 0 ? mRes[0].hardwareId : null);
      if (!activeMonitorId) setActiveMonitorId(monitorId);

      const activeMon = mRes.find(m => m.hardwareId === monitorId);
      const targetDkId = String(selectedDesktopId || aRes?.currentDesktopId || "").toLowerCase();
      const activeEntry = aRes?.entries?.find(a => 
                a.monitorPtInstance === activeMon?.ptInstance && 
                String(a.desktopId).toLowerCase() === targetDkId
      );
      if (activeEntry?.layoutId) setSelectedLayoutId(activeEntry.layoutId);

      if ((canvasOnly || controlOnly) && monitorId) {
        let targetLayout = null;

        if (initialLayoutId) {
          targetLayout = normalizedLayouts.find(l => l.id === initialLayoutId);
        }

        if (!targetLayout && !isNew) {
          const mon = mRes.find(m => m.hardwareId === monitorId);
          const activeEntryFiltered = aRes?.entries?.find(a => a.monitorPtInstance === mon?.ptInstance);
          targetLayout = normalizedLayouts.find(l => l.id === activeEntryFiltered?.layoutId);
        }

        if (targetLayout) {
          setEditingLayout(targetLayout);
          if (targetLayout.gridState) {
            setGridFromGridState(targetLayout.gridState, targetLayout.spacing || 0);
          } else {
            setGridFromZones(targetLayout.zones || [], targetLayout.spacing || 0);
          }
        } else if (isNew) {
          // Initialize for new layout: 1x1 full screen
          const newLayout = { id: '', name: `${t('zone_editor.new_layout')} ${normalizedLayouts.filter(l => !l.isTemplate).length + 1}`, spacing: 8, zones: [{ id: 0, x: 0, y: 0, w: 1, h: 1 }] };
          setEditingLayout(newLayout);
          resetToFull();
        }
      }
    } catch (err) {
      console.error("[CZE] loadAll failed:", err);
    }
  }

  async function setActiveLayout(monitorPtInstance, desktopId, layoutId, autoClose = false) {
    setSelectedLayoutId(layoutId);
    await bridge.czeSetActiveLayout(monitorPtInstance, desktopId, layoutId);
    if (autoClose) {
      window.chrome.webview.postMessage({ type: 'window_close' });
    } else {
      await loadAll();
    }
  }

  function openCanvasEditor(monitorHardwareId, layoutId = '', isNew = false) {
    bridge.czeOpenCanvas(monitorHardwareId, layoutId, isNew);
  }

  async function saveLayoutProperties(closeAfterSave = true) {
    if (controlOnly) {
      window.chrome.webview.postMessage({ action: 'cze_request_save' });
      return;
    }
    setSaving(true);
    try {
      const currentZones = zones.length > 0 ? zones : (editingLayout?.zones || []);

      // Find current monitor's workArea for refWidth/refHeight
      const activeMon = monitors.find(m => m.hardwareId === activeMonitorId);
      const refWidth  = activeMon?.workArea?.width  || activeMon?.bounds?.width  || 0;
      const refHeight = activeMon?.workArea?.height || activeMon?.bounds?.height || 0;

      // Convert fraction coords → int units (0–10000)
      const intZones = currentZones.map(z => ({
        id: z.id,
        x: Math.round(z.x * 10000),
        y: Math.round(z.y * 10000),
        w: Math.round(z.w * 10000),
        h: Math.round(z.h * 10000),
      }));

      // If it's a new layout (no ID yet), ensure name is set
      const layoutToSave = {
        ...editingLayout,
        zones:     intZones,
        spacing:   spacing,
        gridState: JSON.stringify(grid),
        refWidth,
        refHeight,
      };

      if (!layoutToSave.id) {
          layoutToSave.id = `layout_${Date.now()}`;
          if (!layoutToSave.name) layoutToSave.name = `${t('zone_editor.new_layout')} ${layouts.length + 1}`;
      }

      const res = await bridge.czeSaveLayout(layoutToSave);

      if (res?.ok) {
        if (closeAfterSave) {
          setEditingLayout(null);
          if (canvasOnly) {
            window.chrome.webview.postMessage({ action: 'cze_canvas_saved', layoutId: res.id });
          }
        }
        
        if (!canvasOnly) {
          await loadAll();
        }
      }
    } finally {
      setSaving(false);
    }
  }

  async function duplicateLayout(layout) {
    setSaving(true);
    try {
      const newLayout = { ...layout, id: `layout_${Date.now()}`, name: `${layout.name} (${t('common.copy')})`, isTemplate: false };
      const res = await bridge.czeSaveLayout(newLayout);
      if (res?.ok) await loadAll();
    } finally {
      setSaving(false);
    }
  }

  async function createNewLayout() {
    openCanvasEditor(activeMonitorId, '', true);
  }

  function deleteLayout(layout) {
    setLayoutToDelete(layout);
    setShowDeleteConfirm(true);
  }

  async function handleConfirmDelete() {
    if (!layoutToDelete) return;
    
    const res = await bridge.czeDeleteLayout(layoutToDelete.id);
    if (res?.ok) {
      if (editingLayout?.id === layoutToDelete.id) {
        setEditingLayout(null);
      }
      await loadAll();
    }
    setShowDeleteConfirm(false);
    setLayoutToDelete(null);
  }

  const activeMonitor = monitors.find(m => m.hardwareId === activeMonitorId);

  const templates = [
    { id: 'sin', name: t('common.none'), zones: [], isTemplate: true },
    { id: 'foco', name: t('item_dialog.presets.foco'), zones: [{ id: 1, x: 0.1, y: 0.1, w: 0.8, h: 0.8 }], isTemplate: true },
    { id: 'columnas', name: t('item_dialog.presets.3col'), zones: [{ x: 0, y: 0, w: 0.33, h: 1 }, { x: 0.33, y: 0, w: 0.34, h: 1 }, { x: 0.67, y: 0, w: 0.33, h: 1 }], isTemplate: true },
    { id: 'filas', name: t('item_dialog.presets.2row'), zones: [{ x: 0, y: 0, w: 1, h: 0.33 }, { x: 0, y: 0.33, w: 1, h: 0.34 }, { x: 0, y: 0.67, w: 1, h: 0.33 }], isTemplate: true },
    { id: 'cuadricula', name: t('item_dialog.presets.grid'), zones: [{ x: 0, y: 0, w: 0.5, h: 0.5 }, { x: 0.5, y: 0, w: 0.5, h: 0.5 }, { x: 0, y: 0.5, w: 0.5, h: 0.5 }, { x: 0.5, y: 0.5, w: 0.5, h: 0.5 }], isTemplate: true }
  ];

  const customLayouts = layouts.filter(l => !l.isTemplate);

  const modalStyle = standalone ? {
    position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', background: 'var(--fz-bg)', color: 'var(--fz-text)', fontFamily: "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
  } : {
    position: 'fixed', inset: 0, zIndex: 1000,
    background: 'rgba(0,0,0,0.88)',
    backdropFilter: 'blur(20px) saturate(140%)',
    WebkitBackdropFilter: 'blur(20px) saturate(140%)',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    fontFamily: "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
    animation: 'fzOverlayIn 0.22s ease'
  };

  if (controlOnly) {
    return (
      <div style={{
        height: '100vh',
        width: '100vw',
        display: 'flex', flexDirection: 'column',
        background: 'var(--fz-bg)',
        color: 'var(--fz-text)',
        padding: '24px 32px'
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
          <span style={{ fontSize: 16, fontWeight: 800, letterSpacing: '-0.01em' }}>
            {editingLayout?.name || t('item_dialog.new_layout')}
          </span>
          <div style={{ background: 'var(--fz-accent)', color: '#000', padding: '4px 10px', borderRadius: 6, fontSize: 10, fontWeight: 900 }}>
            {t('zone_editor.editor_active')}
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto' }}>
          <div style={{ marginBottom: 12, fontSize: 11, fontWeight: 700, color: 'var(--fz-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
             {t('zone_editor.shortcuts_title')}
          </div>
          <div style={{ marginBottom: 32, fontSize: 13, lineHeight: '2' }}>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              <li style={{ display: 'flex', gap: 12, marginBottom: 14 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span dangerouslySetInnerHTML={{ __html: t('zone_editor.shortcut_split') }} />
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 14 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span dangerouslySetInnerHTML={{ __html: t('zone_editor.shortcut_delete') }} />
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 14 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span dangerouslySetInnerHTML={{ __html: t('zone_editor.shortcut_tab') }} />
              </li>
            </ul>
          </div>
        </div>

        <div style={{ display: 'flex', gap: 12, paddingTop: 20, borderTop: '1px solid rgba(255,255,255,0.05)' }}>
          <button
            onClick={saveLayoutProperties}
            disabled={saving}
            className="fz-btn-primary"
            style={{ flex: 1.5, height: 48, borderRadius: 12 }}
          >
            {saving ? t('common.loading') : t('zone_editor.save_config')}
          </button>
          <button
            onClick={() => window.chrome.webview.postMessage({ type: 'cze_canvas_discard' })}
            className="fz-btn-secondary"
            style={{ flex: 1, height: 48, borderRadius: 12 }}
          >
            {t('common.cancel')}
          </button>
        </div>
      </div>
    );
  }

  if (canvasOnly) {
    if (canvasMode === 'preview') {
      const redirectToManager = () => window.chrome?.webview?.postMessage({ type: 'cze_activate_manager' });
      return (
        <div style={{ position: 'fixed', inset: 0, background: 'transparent', overflow: 'hidden' }} onClick={redirectToManager}>
          <ZoneCanvas grid={grid} zones={zones} spacing={spacing} selectedIds={new Set()} onSelectZone={() => {}} onSplitZone={() => {}} onMoveDivider={() => {}} onRemoveDivider={removeDivider} onClearSelection={() => {}} onCommit={() => saveLayoutProperties(false)} />
        </div>
      );
    }

    return (
      <div style={{ position: 'fixed', inset: 0, background: 'transparent', overflow: 'hidden' }}>
        <style>{`
          html, body, #root { background: transparent !important; background-color: transparent !important; }
        `}</style>
        <ZoneCanvas grid={grid} zones={zones} spacing={spacing} selectedIds={selectedIds} onSelectZone={selectZone} onSplitZone={splitZone} onMoveDivider={moveDivider} onRemoveDivider={removeDivider} onClearSelection={clearSelection} onCommit={null} />
      </div>
    );
  }

  if (loading && !canvasOnly) {
    return (
      <div style={modalStyle}>
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 14 }}>
          <div style={{ position: 'relative', width: 42, height: 42 }}>
            <div style={{ position: 'absolute', inset: 0, border: '2px solid var(--fz-border)', borderRadius: '50%' }} />
            <div style={{ position: 'absolute', inset: 0, border: '2px solid transparent', borderTopColor: 'var(--fz-accent)', borderRadius: '50%', animation: 'fzSpin 0.75s linear infinite' }} />
            <div style={{ position: 'absolute', inset: 7, border: '1.5px solid transparent', borderTopColor: 'var(--fz-accent-dim)', borderRadius: '50%', animation: 'fzSpin 1.3s linear infinite reverse' }} />
          </div>
          <div style={{ color: 'var(--fz-text-muted)', fontSize: 9, fontWeight: 700, letterSpacing: '0.16em', textTransform: 'uppercase' }}>{t('common.loading')}</div>
        </div>
        <style>{`@keyframes fzSpin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }

  return (
    <div style={modalStyle}>
        <div 
          className="fz-editor-container"
          onClick={() => setSelectedLayoutId(null)}
          style={{
            width: '100%',
            height: '100%',
            display: 'flex',
            flexDirection: 'column',
            position: 'relative',
            background: standalone ? 'transparent' : 'var(--fz-bg)',
            boxShadow: standalone ? 'none' : '0 50px 100px rgba(0,0,0,0.8)',
            borderRadius: standalone ? 0 : 24,
            overflow: 'hidden',
            border: standalone ? 'none' : '1px solid var(--fz-border)'
          }}
        >
        <div className="fz-monitor-bar" style={{ 
          display: 'flex', 
          flexDirection: 'column', 
          alignItems: 'center', 
          paddingTop: 32,
          position: 'relative',
          minHeight: 120
        }}>
          {/* Desktop Switcher - Positioned Top Right */}
          <div style={{ 
            position: 'absolute', 
            top: 24, 
            right: 24, 
            display: 'flex', 
            alignItems: 'center', 
            gap: 12, 
            padding: '8px 16px',
            background: 'var(--fz-bg-alt)',
            borderRadius: 12,
            border: '1px solid var(--fz-border)',
            boxShadow: 'var(--fz-shadow)'
          }}>
            <div style={{ color: 'var(--fz-text-muted)', display: 'flex', alignItems: 'center', gap: 8 }}>
              <Layers size={14} />
              <span style={{ fontSize: 10, fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em' }}>{t('zone_editor.desktop')}</span>
            </div>
            <div className="fz-toolbar-select-wrapper" style={{ minWidth: 150 }}>
              <PremiumSelect 
                value={selectedDesktopId || ''} 
                options={desktops.map(d => ({ 
                  id: d.id, 
                  name: `${d.name} ${d.id === currentDesktopId ? `(${t('common.actual')})` : ''}` 
                }))}
                onChange={val => handleSwitchDesktop(val)}
                valueKey="id"
                labelKey="name"
              />
            </div>
          </div>

          {/* Centered Monitors */}
          <div style={{ display: 'flex', gap: 16, justifyContent: 'center' }}>
            {monitors.map(mon => {
              const isActiveMon = activeMonitorId === mon.hardwareId;
              return (
                <div
                  key={mon.hardwareId}
                  className={`fz-monitor-item ${isActiveMon ? 'active' : ''}`}
                  onClick={() => setActiveMonitorId(mon.hardwareId)}
                  style={{ width: 100, cursor: 'pointer' }}
                >
                  <div style={{ position: 'relative', width: 60, height: 40, margin: '0 auto' }}>
                    <div style={{
                      width: '100%', height: '100%',
                       border: `2px solid ${isActiveMon ? 'var(--fz-accent)' : 'var(--fz-border)'}`,
                      borderRadius: 6,
                      display: 'flex', alignItems: 'center', justifyContent: 'center',
                      transition: 'all 0.25s ease',
                      background: isActiveMon ? 'var(--fz-accent-low)' : 'var(--fz-bg-alt)',
                      boxShadow: isActiveMon ? '0 0 15px var(--fz-accent-dim)' : 'none'
                    }}>
                      <span style={{ fontSize: 14, fontWeight: 900, color: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-text-muted)', transition: 'color 0.25s ease' }}>
                        {mon.monitorNumber}
                      </span>
                    </div>
                    <div style={{
                      position: 'absolute', bottom: -6, left: '50%', transform: 'translateX(-50%)',
                      width: 16, height: 4,
                      background: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-border)',
                      borderRadius: '0 0 4px 4px',
                      transition: 'background 0.25s ease',
                    }} />
                  </div>
                  <div style={{ fontSize: 10, fontWeight: 700, textAlign: 'center', color: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-text-muted)', letterSpacing: '0.02em', marginTop: 12, transition: 'color 0.25s ease' }}>
                    {mon.bounds.width}×{mon.bounds.height}
                  </div>
                  
                  {mon.isPrimary && (
                    <div 
                      title={t('zone_editor.main_monitor')}
                      style={{ 
                        display: 'flex', 
                        justifyContent: 'center', 
                        marginTop: 6, 
                        color: 'var(--fz-accent)',
                        animation: 'fzFadeIn 0.3s ease-out'
                      }}
                    >
                      <Crown size={12} fill="currentColor" fillOpacity={0.2} />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '0 60px 60px' }}>
          <div className="fz-section-title">{t('zone_editor.system_templates')}</div>
          <div className="fz-grid">
            {templates.map((t, idx) => {
              const targetDkId = String(selectedDesktopId || "").toLowerCase();
              const isActiveForSelected = activeLayouts.find(a => 
                a.monitorPtInstance === activeMonitor?.ptInstance && 
                String(a.desktopId).toLowerCase() === targetDkId &&
                String(a.layoutId).toLowerCase() === String(t.id).toLowerCase()
              );

              return (
                <LayoutCard 
                  key={t.id} 
                  layout={{...t, staggerIndex: idx}} 
                  isActive={!!isActiveForSelected} 
                  isSelected={selectedLayoutId === t.id}
                  onSelect={() => setSelectedLayoutId(t.id)}
                  onActivate={() => setActiveLayout(activeMonitor?.ptInstance, selectedDesktopId, t.id)}
                  isInitialLoad={isInitialLoad}
                  menuOpenId={menuOpenId}
                  setMenuOpenId={setMenuOpenId}
                  monitors={monitors}
                  activeMonitorId={activeMonitorId}
                  setEditingLayout={setEditingLayout}
                  setGridFromGridState={setGridFromGridState}
                  setGridFromZones={setGridFromZones}
                  openCanvasEditor={openCanvasEditor}
                  duplicateLayout={duplicateLayout}
                  deleteLayout={deleteLayout}
                />
              );
            })}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 48, marginBottom: 14 }}>
            <div className="fz-section-title" style={{ margin: 0, flex: 1, marginRight: 16 }}>{t('zone_editor.my_layouts')}</div>
            <button className="fz-btn-primary" onClick={createNewLayout} style={{ padding: '7px 14px', borderRadius: 9, display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, flexShrink: 0 }}>
              <Plus size={13} /> {t('zone_editor.new_layout')}
            </button>
          </div>
          
          {customLayouts.length > 0 ? (
            <div className="fz-grid">
              {customLayouts.map((l, idx) => {
                const targetDkId = String(selectedDesktopId || "").toLowerCase();
                const isActiveForSelected = activeLayouts.find(a => 
                  a.monitorPtInstance === activeMonitor?.ptInstance && 
                  String(a.desktopId).toLowerCase() === targetDkId &&
                  String(a.layoutId).toLowerCase() === String(l.id).toLowerCase()
                );

                return (
                  <LayoutCard
                    key={l.id}
                    layout={{...l, staggerIndex: templates.length + idx}}
                    isActive={!!isActiveForSelected}
                    isSelected={selectedLayoutId === l.id}
                    onSelect={() => setSelectedLayoutId(l.id)}
                    onActivate={() => setActiveLayout(activeMonitor?.ptInstance, selectedDesktopId, l.id)}
                    isInitialLoad={isInitialLoad}
                    menuOpenId={menuOpenId}
                    setMenuOpenId={setMenuOpenId}
                    monitors={monitors}
                    activeMonitorId={activeMonitorId}
                    setEditingLayout={setEditingLayout}
                    setGridFromGridState={setGridFromGridState}
                    setGridFromZones={setGridFromZones}
                    openCanvasEditor={openCanvasEditor}
                    duplicateLayout={duplicateLayout}
                    deleteLayout={deleteLayout}
                  />
                );
              })}
            </div>
          ) : (
            <div style={{ padding: '52px 28px', textAlign: 'center', background: 'var(--fz-bg-alt)', borderRadius: 18, border: '1px dashed var(--fz-border)', position: 'relative', overflow: 'hidden' }}>
              <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(ellipse at 50% 100%, var(--fz-accent-low) 0%, transparent 65%)', pointerEvents: 'none' }} />
              <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 14, opacity: 0.14 }}>
                <Layout size={42} />
              </div>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--fz-text-muted)', marginBottom: 6 }}>{t('zone_editor.no_custom_layouts')}</div>
              <div style={{ fontSize: 11.5, color: 'var(--fz-text-muted)', opacity: 0.6, lineHeight: 1.65 }}>
                {t('zone_editor.no_custom_layouts_hint')}
              </div>
            </div>
          )}
        </div>

        {editingLayout && (
          <div className="fz-dialog-overlay" style={{ animation: 'fzOverlayIn 0.2s ease' }}>
            <div className="fz-dialog" style={{ animation: 'fzDialogIn 0.32s cubic-bezier(0.34, 1.56, 0.64, 1)' }}>
              {/* Header */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 28 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <div style={{
                    width: 38, height: 38, borderRadius: 11,
                    background: 'var(--fz-accent-low)',
                    border: '1px solid var(--fz-accent-dim)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center'
                  }}>
                    <Layout size={18} color="var(--fz-accent)" />
                  </div>
                  <div>
                    <div style={{ fontSize: 18, fontWeight: 800, letterSpacing: '-0.02em', color: 'var(--fz-text)' }}>{t('zone_editor.customize_title')}</div>
                    <div style={{ fontSize: 11, color: 'var(--fz-text-muted)', marginTop: 2, letterSpacing: '0.02em' }}>{t('zone_editor.customize_subtitle')}</div>
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  {!editingLayout.isTemplate && (
                    <button 
                      onClick={() => deleteLayout(editingLayout.id)}
                      style={{ 
                        background: 'rgba(255,23,68,0.08)', color: 'var(--danger)', 
                        border: '1px solid rgba(255,23,68,0.2)', padding: '8px 10px', 
                        borderRadius: 10, cursor: 'pointer',
                        transition: 'all 0.18s ease',
                        display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, fontWeight: 600
                      }}
                      onMouseEnter={e => { e.currentTarget.style.background = 'rgba(255,23,68,0.15)'; e.currentTarget.style.transform = 'translateY(-1px)'; }}
                      onMouseLeave={e => { e.currentTarget.style.background = 'rgba(255,23,68,0.08)'; e.currentTarget.style.transform = 'translateY(0)'; }}
                    >
                      <Trash2 size={15} />
                    </button>
                  )}
                </div>
              </div>

              {/* Name field */}
              <div style={{ marginBottom: 22 }}>
                <label style={{ 
                  display: 'flex', alignItems: 'center', gap: 6,
                  fontSize: 10, fontWeight: 800, color: 'var(--fz-text-muted)', 
                  marginBottom: 10, textTransform: 'uppercase', letterSpacing: '0.1em' 
                }}>
                  <div style={{ width: 3, height: 10, background: 'var(--fz-accent)', borderRadius: 2 }} />
                  {t('zone_editor.layout_name')}
                </label>
                <div style={{ position: 'relative' }}>
                  <Edit3 size={16} style={{ position: 'absolute', left: 14, top: '50%', transform: 'translateY(-50%)', opacity: 0.35, pointerEvents: 'none' }} />
                  <input 
                    value={editingLayout.name} 
                    onChange={e => setEditingLayout({...editingLayout, name: e.target.value})}
                    placeholder={t('zone_editor.layout_name_placeholder')}
                    style={{ 
                      width: '100%', 
                      background: 'var(--fz-bg-alt)', 
                      border: '1px solid var(--fz-border)', 
                      color: 'var(--fz-text)', 
                      padding: '13px 16px 13px 40px', 
                      borderRadius: 13, 
                      fontSize: 14, 
                      fontWeight: 600, 
                      outline: 'none', 
                      transition: 'all 0.2s ease',
                      boxSizing: 'border-box'
                    }}
                     onFocus={e => { e.target.style.borderColor = 'var(--fz-accent)'; e.target.style.boxShadow = '0 0 0 3px var(--fz-accent-dim)'; e.target.style.background = 'var(--fz-accent-low)'; }}
                    onBlur={e => { e.target.style.borderColor = 'var(--fz-border)'; e.target.style.boxShadow = 'none'; e.target.style.background = 'var(--fz-bg-alt)'; }}
                  />
                </div>
              </div>

              {/* Spacing field */}
              <div style={{ marginBottom: 32 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
                   <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <div style={{ width: 3, height: 10, background: 'var(--fz-accent)', borderRadius: 2, opacity: 0.6 }} />
                      <span style={{ fontSize: 10, fontWeight: 800, color: 'var(--fz-text-muted)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>{t('zone_editor.spacing_label')}</span>
                   </div>
                   <div style={{ 
                     background: 'rgba(var(--accent-rgb, 0, 210, 255), 0.1)', 
                     color: 'var(--fz-accent)', 
                     padding: '2px 8px',
                     borderRadius: 4,
                     border: '1px solid rgba(var(--accent-rgb, 0, 210, 255), 0.2)',
                     fontFamily: 'monospace'
                   }}>
                    {spacing}px
                   </div>
                </div>
                 <div style={{ 
                  background: 'var(--fz-bg-alt)', 
                  border: '1px solid var(--fz-border)', 
                  borderRadius: 12, 
                  padding: '14px 16px'
                }}>
                  <input 
                    type="range" min="0" max="64" step="2"
                    value={spacing} 
                    onChange={e => setSpacing(parseInt(e.target.value))} 
                    style={{ 
                      width: '100%', 
                      accentColor: 'var(--fz-accent)', 
                      cursor: 'pointer',
                      '--val': `${(spacing / 64) * 100}%`
                    }} 
                  />
                </div>
              </div>

              {/* Buttons */}
              <div style={{ display: 'flex', gap: 10 }}>
                <button 
                  onClick={saveLayoutProperties} 
                  className="fz-btn-primary" 
                  style={{ 
                    flex: 1.5, 
                    height: 48,
                    borderRadius: 13,
                    fontSize: 13,
                    letterSpacing: '0.04em'
                  }}
                >
                  {saving ? t('common.loading') : t('zone_editor.save_config')}
                </button>
                <button 
                  onClick={() => setEditingLayout(null)} 
                  className="fz-btn-secondary" 
                  style={{ flex: 1, height: 48, borderRadius: 13 }}
                >
                  {t('common.cancel')}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
      
      <style>{`
        .menu-item-hover:hover {
          background: var(--fz-accent-low) !important;
          color: var(--fz-accent) !important;
        }
        @keyframes fadeInScale {
          from { opacity: 0; transform: scale(0.93) translateY(-8px); }
          to   { opacity: 1; transform: scale(1)    translateY(0); }
        }
        @keyframes fzActivePing {
          0%, 100% { box-shadow: 0 0 0 2px var(--fz-accent-dim), 0 0 8px var(--fz-accent-glow); }
          50%       { box-shadow: 0 0 0 4px var(--fz-accent-low), 0 0 16px var(--fz-accent); }
        }
        @keyframes fzOverlayIn {
          from { opacity: 0; backdrop-filter: blur(0px); }
          to   { opacity: 1; backdrop-filter: blur(20px); }
        }
        @keyframes fzDialogIn {
          from { opacity: 0; transform: scale(0.88) translateY(20px); filter: blur(5px); }
          to   { opacity: 1; transform: scale(1)    translateY(0);    filter: blur(0); }
        }
        .fz-dialog {
          background: var(--fz-card) !important;
          border: 1px solid var(--fz-border) !important;
          border-radius: 22px !important;
          padding: 30px !important;
          box-shadow: var(--fz-shadow), 0 0 0 1px rgba(255,255,255,0.04) inset, 0 1px 0 rgba(255,255,255,0.06) inset !important;
          position: relative;
          overflow: hidden;
          color: var(--fz-text) !important;
        }
        .fz-dialog::before {
          content: '';
          position: absolute;
          top: 0;
          left: 40px;
          right: 40px;
          height: 1px;
          background: linear-gradient(90deg, transparent, rgba(var(--accent-rgb, 0, 210, 255), 0.3) 50%, transparent);
          pointer-events: none;
        }
        input[type="range"] {
          -webkit-appearance: none;
          height: 5px;
          background: rgba(255,255,255,0.09);
          border-radius: 99px;
        }
        input[type="range"]::-webkit-slider-thumb {
          -webkit-appearance: none;
          width: 18px;
          height: 18px;
          background: #fff;
          border: 2.5px solid var(--fz-accent);
          border-radius: 50%;
          cursor: pointer;
          margin-top: -7px; /* Centers the thumb vertically on a 5px track */
          box-shadow: 0 2px 8px rgba(0,0,0,0.4), 0 0 12px var(--fz-accent-dim);
          transition: transform 0.18s cubic-bezier(0.34,1.56,0.64,1), box-shadow 0.18s ease;
        }
        input[type="range"]::-webkit-slider-thumb:hover {
          transform: scale(1.2);
          box-shadow: 0 2px 12px rgba(0,0,0,0.5), 0 0 18px var(--fz-accent-glow);
        }
        input[type="range"]::-webkit-slider-runnable-track {
          height: 5px;
          border-radius: 99px;
          background: linear-gradient(to right, var(--fz-accent) 0%, var(--fz-accent) var(--val, 50%), rgba(255,255,255,0.09) var(--val, 50%));
        }
      `}</style>
      {/* Confirm Deletion Modal */}
      {showDeleteConfirm && (
        <ConfirmModal 
          title={t('modals.delete_layout_title')}
          message={t('modals.delete_layout_msg', { name: layoutToDelete?.name })}
          onConfirm={handleConfirmDelete}
          onCancel={() => {
            setShowDeleteConfirm(false);
            setLayoutToDelete(null);
          }}
          confirmText={t('common.delete_permanently')}
          isDanger={true}
        />
      )}
    </div>
  );
}

// --------------------------------------------------------------------------------
// LayoutCard Component (Extracted for stability and animation control)
// --------------------------------------------------------------------------------
const LayoutCard = ({ 
  layout, isActive, isSelected, onSelect, onActivate, 
  isInitialLoad, menuOpenId, setMenuOpenId,
  monitors, activeMonitorId, setEditingLayout,
  setGridFromGridState, setGridFromZones,
  openCanvasEditor, duplicateLayout, deleteLayout
}) => {
  const { t } = useTranslation();
  const [preselectAnim, setPreselectAnim] = useState(false);
  const [activateAnim, setActivateAnim] = useState(false);
  const prevIsSelected = useRef(isSelected);
  const prevIsActive = useRef(isActive);
  const isMenuOpen = menuOpenId === layout.id;
  
  // Trigger pre-selection pulse ONLY when isSelected transitions from false to true
  useEffect(() => {
    if (!isSelected) {
      setPreselectAnim(false);
      return;
    }
    if (isSelected && !prevIsSelected.current && !isActive) {
      setPreselectAnim(true);
      const timer = setTimeout(() => setPreselectAnim(false), 600);
      return () => clearTimeout(timer);
    }
    prevIsSelected.current = isSelected;
  }, [isSelected, isActive]);

  // Trigger activation sonar ONLY when isActive transitions from false to true
  useEffect(() => {
    if (isActive && !prevIsActive.current) {
      setActivateAnim(true);
      const timer = setTimeout(() => setActivateAnim(false), 800);
      return () => clearTimeout(timer);
    }
    prevIsActive.current = isActive;
  }, [isActive]);

  // Resolution mismatch detection
  const activeMon = monitors.find(m => m.hardwareId === activeMonitorId);
  const monW = activeMon?.workArea?.width || activeMon?.bounds?.width || 0;
  const monH = activeMon?.workArea?.height || activeMon?.bounds?.height || 0;
  const isAdapted = layout.refWidth > 0 && layout.refHeight > 0
    && (layout.refWidth !== monW || layout.refHeight !== monH);

  return (
    <div 
      className={`fz-layout-card ${isActive ? 'active' : ''} ${isSelected && !isActive ? 'selected' : ''} ${isInitialLoad ? 'fz-card-entrance' : ''} ${preselectAnim ? 'fz-card-preselect' : ''} ${activateAnim ? 'fz-card-activated' : ''} ${isMenuOpen ? 'menu-open' : ''}`}
      style={{ animationDelay: isInitialLoad ? `${layout.staggerIndex * 50}ms` : '0ms' }}
      onClick={(e) => {
        e.stopPropagation();
        onSelect();
      }}
      onDoubleClick={(e) => {
        e.stopPropagation();
        onActivate();
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 10 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0, flex: 1, paddingRight: 28 }}>
          {isActive && (
            <div style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--fz-accent)', flexShrink: 0, boxShadow: '0 0 10px var(--fz-accent-glow)' }} />
          )}
          {isAdapted && (
            <span title={t('modals.sync_portability_details_short', { refW: layout.refWidth, refH: layout.refHeight, monW, monH })}
              style={{ fontSize: 9, background: 'rgba(255,200,0,0.12)', color: 'rgba(255,215,0,0.75)', padding: '1px 5px', borderRadius: 3, fontWeight: 700, flexShrink: 0, letterSpacing: '0.02em' }}>
              ∝
            </span>
          )}
          <span style={{ fontSize: 12.5, fontWeight: 700, color: isActive ? 'var(--fz-accent)' : 'var(--fz-text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            {layout.name}
          </span>
        </div>
        <div className="fz-card-actions">
          <button
            className="fz-icon-btn"
            onClick={(e) => { e.stopPropagation(); setMenuOpenId(isMenuOpen ? null : layout.id); }}
          >
            <MoreVertical size={13} />
          </button>
        </div>
        
        {isMenuOpen && (
          <div 
            style={{ 
              position: 'absolute', 
              top: 40, 
              right: 0, 
              background: 'rgba(28, 28, 30, 0.94)', 
              backdropFilter: 'blur(16px) saturate(180%)',
              WebkitBackdropFilter: 'blur(16px) saturate(180%)',
              border: '1px solid rgba(255, 255, 255, 0.12)', 
              borderRadius: 12, 
              padding: 6, 
              zIndex: 1000, 
              boxShadow: '0 15px 35px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.05)', 
              minWidth: 180,
              animation: 'fadeInScale 0.2s cubic-bezier(0.16, 1, 0.3, 1)'
            }}
            onClick={e => e.stopPropagation()}
          >
            <button 
              style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
              className="menu-item-hover"
              onClick={() => {
                setEditingLayout(layout);
                if (layout.gridState) {
                  setGridFromGridState(layout.gridState, layout.spacing || 0);
                } else {
                  setGridFromZones(layout.zones || [], layout.spacing || 0);
                }
                setMenuOpenId(null);
              }}
            >
              <Edit3 size={14} style={{ opacity: 0.7, color: 'var(--fz-accent)' }} /> {t('zone_editor.edit_props')}
            </button>
            {!layout.isTemplate && (
              <button 
                style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
                className="menu-item-hover"
                onClick={() => {
                  setMenuOpenId(null);
                  openCanvasEditor(activeMonitorId, layout.id);
                }}
              >
                <Maximize2 size={14} style={{ opacity: 0.7, color: 'var(--fz-accent)' }} /> {t('zone_editor.interactive_editor')}
              </button>
            )}
            <div style={{ height: 1, background: 'rgba(255,255,255,0.08)', margin: '4px 8px' }} />
            <button 
              style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
              className="menu-item-hover"
              onClick={() => { duplicateLayout(layout); setMenuOpenId(null); }}
            >
              <Copy size={14} style={{ opacity: 0.7 }} /> {t('common.duplicate')}
            </button>
            {!layout.isTemplate && (
              <button 
                style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#ff4d4d', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
                className="menu-item-hover"
                onClick={() => { deleteLayout(layout); setMenuOpenId(null); }}
              >
                <Trash2 size={14} /> {t('common.delete')}
              </button>
            )}
          </div>
        )}
      </div>
      
      <div style={{
        height: 104,
        background: 'rgba(0,0,0,0.42)',
        position: 'relative',
        borderRadius: 10,
        overflow: 'hidden',
        border: isActive
          ? '1px solid var(--fz-accent-dim)'
          : isSelected
            ? '1px solid var(--fz-accent-low)'
            : '1px solid rgba(255,255,255,0.055)',
        transition: 'border-color 0.28s ease, box-shadow 0.28s ease',
        boxShadow: isActive ? 'inset 0 0 24px var(--fz-accent-low)' : 'none',
      }}>
        {/* Subtle grid dots background */}
        <div style={{
          position: 'absolute', inset: 0,
          backgroundImage: 'radial-gradient(circle, rgba(255,255,255,0.045) 1px, transparent 1px)',
          backgroundSize: '20px 20px',
          pointerEvents: 'none',
        }} />

        {layout.zones?.length === 0 ? (
          <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <div style={{ width: 22, height: 22, borderRadius: '50%', border: '1.5px solid rgba(255,255,255,0.1)' }} />
          </div>
        ) : layout.zones?.map((z, i) => (
          <div key={i} style={{
            position: 'absolute',
            left: `calc(${z.x*100}% + 8px)`,
            top: `calc(${z.y*100}% + 8px)`,
            width: `calc(${z.w*100}% - 16px)`,
            height: `calc(${z.h*100}% - 16px)`,
            borderRadius: 4,
            border: isActive
              ? '1px solid var(--fz-accent-dim)'
              : isSelected
                ? '1px solid var(--fz-accent-low)'
                : '1px solid rgba(255,255,255,0.1)',
            background: isActive
              ? 'linear-gradient(135deg, var(--fz-accent-dim) 0%, var(--fz-accent-low) 100%)'
              : isSelected
                ? 'linear-gradient(135deg, var(--fz-accent-low) 0%, var(--fz-accent-low) 100%)'
                : 'linear-gradient(135deg, rgba(255,255,255,0.045) 0%, rgba(255,255,255,0.01) 100%)',
            transition: 'background 0.28s ease, border-color 0.28s ease',
          }} />
        ))}

        {/* Zone count badge */}
        {(layout.zones?.length || 0) > 0 && (
          <div style={{
            position: 'absolute', bottom: 5, right: 6,
            fontSize: 8.5, fontWeight: 700,
            color: isActive ? 'var(--fz-accent)' : 'var(--fz-text-muted)',
            letterSpacing: '0.04em',
            transition: 'color 0.28s ease',
          }}>
            {layout.zones.length}Z
          </div>
        )}

        {/* Active inset ring */}
        {isActive && (
          <div style={{
            position: 'absolute', inset: 0, borderRadius: 10,
            boxShadow: 'inset 0 0 0 1.5px var(--fz-accent)',
            pointerEvents: 'none', zIndex: 10,
          }} />
        )}
      </div>
    </div>
  );
};
