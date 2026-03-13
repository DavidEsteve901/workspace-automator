import { useState, useEffect, useRef } from 'react';
import { bridge, onEvent, offEvent } from '../../api/bridge';
import { useZoneEditor, gridToZones } from './ZoneEditorHooks';
import { ZoneCanvas } from './ZoneCanvas';
import { ZoneToolbar } from './ZoneToolbar';
import { 
  Info, Save, Trash2, Plus, Monitor, Layout, X, 
  Settings, Edit3, Trash, Copy, MoreVertical, ChevronRight,
  Maximize2, MousePointer2, Keyboard
} from 'lucide-react';

export function ZoneEditorModal({ onClose, standalone = false, canvasOnly = false, controlOnly = false, canvasMode = 'edit', initialMonitorId = null, initialLayoutId = null }) {
  const [monitors, setMonitors] = useState([]);
  const [layouts, setLayouts] = useState([]);
  const [activeLayouts, setActiveLayouts] = useState([]);
  const [currentDesktopId, setCurrentDesktopId] = useState(null);
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
    // Close menu when clicking outside
    const handleClick = () => setMenuOpenId(null);
    window.addEventListener('click', handleClick);
    return () => window.removeEventListener('click', handleClick);
  }, []);

  async function loadAll() {
    try {
      const [lRes, aRes, mRes] = await Promise.all([
        bridge.czeGetLayouts(),
        bridge.czeGetActiveLayouts(),
        bridge.listMonitors()
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
      setCurrentDesktopId(aRes?.currentDesktopId);

      const monitorId = activeMonitorId || (mRes?.length > 0 ? mRes[0].hardwareId : null);
      if (!activeMonitorId) setActiveMonitorId(monitorId);

      const activeMon = mRes.find(m => m.hardwareId === monitorId);
      const activeEntry = aRes?.entries?.find(a => a.monitorPtInstance === activeMon?.ptInstance && a.isCurrentDesktop);
      if (activeEntry?.layoutId) setSelectedLayoutId(activeEntry.layoutId);

      if ((canvasOnly || controlOnly) && monitorId) {
        let targetLayout = null;

        if (initialLayoutId) {
          targetLayout = normalizedLayouts.find(l => l.id === initialLayoutId);
        }

        if (!targetLayout) {
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

      const res = await bridge.czeSaveLayout({
        ...editingLayout,
        zones:     intZones,
        spacing:   spacing,
        gridState: JSON.stringify(grid),
        refWidth,
        refHeight,
      });

      if (res?.ok) {
        if (closeAfterSave) {
          setEditingLayout(null);
          if (canvasOnly) {
            window.chrome.webview.postMessage({ action: 'cze_canvas_saved' });
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
      const newLayout = { ...layout, id: `layout_${Date.now()}`, name: `${layout.name} (Copia)`, isTemplate: false };
      const res = await bridge.czeSaveLayout(newLayout);
      if (res?.ok) await loadAll();
    } finally {
      setSaving(false);
    }
  }

  async function createNewLayout() {
    const newId = `layout_${Date.now()}`;
    const newLayout = { id: newId, name: `Nuevo diseño ${layouts.length + 1}`, spacing: 8, zones: [{ id: 1, x: 0, y: 0, w: 1, h: 1 }] };
    const res = await bridge.czeSaveLayout(newLayout);
    if (res?.ok) {
      await loadAll();
      openCanvasEditor(activeMonitorId, newId, true);
    }
  }

  async function deleteLayout(id) {
    if (confirm('¿Eliminar este diseño de forma permanente?')) {
      const res = await bridge.czeDeleteLayout(id);
      if (res?.ok) {
        setEditingLayout(null);
        await loadAll();
      }
    }
  }

  const activeMonitor = monitors.find(m => m.hardwareId === activeMonitorId);
  const activeEntryForMonitor = activeLayouts.find(a => a.monitorPtInstance === activeMonitor?.ptInstance && a.isCurrentDesktop);
  const activeLayoutIdForMonitor = activeEntryForMonitor?.layoutId;

  const templates = [
    { id: 'sin', name: 'Sin diseño', zones: [], isTemplate: true },
    { id: 'foco', name: 'Foco', zones: [{ id: 1, x: 0.1, y: 0.1, w: 0.8, h: 0.8 }], isTemplate: true },
    { id: 'columnas', name: 'Columnas', zones: [{ x: 0, y: 0, w: 0.33, h: 1 }, { x: 0.33, y: 0, w: 0.34, h: 1 }, { x: 0.67, y: 0, w: 0.33, h: 1 }], isTemplate: true },
    { id: 'filas', name: 'Filas', zones: [{ x: 0, y: 0, w: 1, h: 0.33 }, { x: 0, y: 0.33, w: 1, h: 0.34 }, { x: 0, y: 0.67, w: 1, h: 0.33 }], isTemplate: true },
    { id: 'cuadricula', name: 'Cuadrícula', zones: [{ x: 0, y: 0, w: 0.5, h: 0.5 }, { x: 0.5, y: 0, w: 0.5, h: 0.5 }, { x: 0, y: 0.5, w: 0.5, h: 0.5 }, { x: 0.5, y: 0.5, w: 0.5, h: 0.5 }], isTemplate: true }
  ];

  const customLayouts = layouts.filter(l => !l.isTemplate);

  const modalStyle = standalone ? {
    position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', background: 'var(--fz-bg)', color: 'var(--fz-text)', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif'
  } : {
    position: 'fixed', inset: 0, zIndex: 1000,
    background: 'rgba(0,0,0,0.85)',
    backdropFilter: 'blur(12px)',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif'
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
            {editingLayout?.name || 'Diseño Pro'}
          </span>
          <div style={{ background: 'var(--fz-accent)', color: '#000', padding: '4px 10px', borderRadius: 6, fontSize: 10, fontWeight: 900 }}>
            EDITOR ACTIVO
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto' }}>
          <div style={{ marginBottom: 12, fontSize: 11, fontWeight: 700, color: 'var(--fz-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
             Atajos Rápidos
          </div>
          <div style={{ marginBottom: 32, fontSize: 13, lineHeight: '2' }}>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Shift + Clic</strong> — dividir zona</span>
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Doble Clic</strong> — seleccionar/foco</span>
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Tab</strong> — cambiar dirección (V/H)</span>
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Suprimir</strong> — eliminar línea resaltada</span>
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Ctrl + Clic</strong> — selección múltiple</span>
              </li>
              <li style={{ display: 'flex', gap: 12, marginBottom: 10 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Enter</strong> — guardar cambios</span>
              </li>
              <li style={{ display: 'flex', gap: 12 }}>
                <span style={{ color: 'var(--fz-accent)', fontWeight: 700, minWidth: 20 }}>•</span>
                <span><strong>Escape</strong> — cancelar</span>
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
            {saving ? 'Guardando...' : 'Guardar Diseño'}
          </button>
          <button
            onClick={() => window.chrome.webview.postMessage({ type: 'cze_canvas_discard' })}
            className="fz-btn-secondary"
            style={{ flex: 1, height: 48, borderRadius: 12 }}
          >
            Cancelar
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
        <ZoneCanvas grid={grid} zones={zones} spacing={spacing} selectedIds={selectedIds} onSelectZone={selectZone} onSplitZone={splitZone} onMoveDivider={moveDivider} onRemoveDivider={removeDivider} onClearSelection={clearSelection} onCommit={() => saveLayoutProperties(false)} />
      </div>
    );
  }

  if (loading && !canvasOnly) {
    return (
      <div style={modalStyle}>
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 14 }}>
          <div style={{ position: 'relative', width: 42, height: 42 }}>
            <div style={{ position: 'absolute', inset: 0, border: '2px solid rgba(255,255,255,0.05)', borderRadius: '50%' }} />
            <div style={{ position: 'absolute', inset: 0, border: '2px solid transparent', borderTopColor: 'var(--fz-accent)', borderRadius: '50%', animation: 'fzSpin 0.75s linear infinite' }} />
            <div style={{ position: 'absolute', inset: 7, border: '1.5px solid transparent', borderTopColor: 'var(--fz-accent-dim)', borderRadius: '50%', animation: 'fzSpin 1.3s linear infinite reverse' }} />
          </div>
          <div style={{ color: 'rgba(255,255,255,0.2)', fontSize: 9, fontWeight: 700, letterSpacing: '0.16em', textTransform: 'uppercase' }}>Preparando</div>
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
        <div className="fz-monitor-bar">
          {monitors.map(mon => {
            const isActiveMon = activeMonitorId === mon.hardwareId;
            return (
              <div
                key={mon.hardwareId}
                className={`fz-monitor-item ${isActiveMon ? 'active' : ''}`}
                onClick={() => setActiveMonitorId(mon.hardwareId)}
              >
                {/* Monitor silhouette icon */}
                <div style={{ position: 'relative', width: 38, height: 26 }}>
                  <div style={{
                    width: '100%', height: '100%',
                    border: `1.5px solid ${isActiveMon ? 'var(--fz-accent)' : 'var(--fz-border)'}`,
                    borderRadius: 4,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    transition: 'border-color 0.25s ease',
                    background: isActiveMon ? 'var(--fz-accent-low)' : 'transparent',
                  }}>
                    <span style={{ fontSize: 10, fontWeight: 800, color: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-text-muted)', transition: 'color 0.25s ease', letterSpacing: '-0.02em' }}>
                      {mon.monitorNumber}
                    </span>
                  </div>
                  {/* Stand */}
                  <div style={{
                    position: 'absolute', bottom: -4, left: '50%', transform: 'translateX(-50%)',
                    width: 10, height: 3,
                    background: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-border)',
                    borderRadius: '0 0 3px 3px',
                    transition: 'background 0.25s ease',
                  }} />
                </div>
                <div style={{ fontSize: 9.5, fontWeight: 600, color: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-text-muted)', letterSpacing: '0.02em', marginTop: 4, transition: 'color 0.25s ease' }}>
                  {mon.bounds.width}×{mon.bounds.height}
                </div>
                {mon.isPrimary && (
                  <div style={{ fontSize: 8, fontWeight: 700, color: isActiveMon ? 'var(--fz-accent)' : 'var(--fz-text-muted)', letterSpacing: '0.08em', textTransform: 'uppercase', transition: 'color 0.25s ease' }}>
                    Principal
                  </div>
                )}
              </div>
            );
          })}
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '0 60px 60px' }}>
          <div className="fz-section-title">Plantillas del Sistema</div>
          <div className="fz-grid">
            {templates.map((t, idx) => (
              <LayoutCard 
                key={t.id} 
                layout={{...t, staggerIndex: idx}} 
                isActive={activeLayoutIdForMonitor === t.id} 
                isSelected={selectedLayoutId === t.id}
                onSelect={() => setSelectedLayoutId(t.id)}
                onActivate={() => setActiveLayout(activeMonitor?.ptInstance, currentDesktopId || activeEntryForMonitor?.desktopId || '00000000-0000-0000-0000-000000000000', t.id)}
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
            ))}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 48, marginBottom: 14 }}>
            <div className="fz-section-title" style={{ margin: 0, flex: 1, marginRight: 16 }}>Mis Diseños Personalizados</div>
            <button className="fz-btn-primary" onClick={createNewLayout} style={{ padding: '7px 14px', borderRadius: 9, display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, flexShrink: 0 }}>
              <Plus size={13} /> Nuevo
            </button>
          </div>
          
          {customLayouts.length > 0 ? (
            <div className="fz-grid">
              {customLayouts.map((l, idx) => (
                <LayoutCard
                  key={l.id}
                  layout={{...l, staggerIndex: templates.length + idx}}
                  isActive={activeLayoutIdForMonitor === l.id}
                  isSelected={selectedLayoutId === l.id}
                  onSelect={() => setSelectedLayoutId(l.id)}
                  onActivate={() => setActiveLayout(activeMonitor?.ptInstance, currentDesktopId || activeEntryForMonitor?.desktopId || '00000000-0000-0000-0000-000000000000', l.id)}
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
              ))}
            </div>
          ) : (
            <div style={{ padding: '52px 28px', textAlign: 'center', background: 'rgba(255,255,255,0.018)', borderRadius: 18, border: '1px dashed rgba(255,255,255,0.07)', position: 'relative', overflow: 'hidden' }}>
              <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(ellipse at 50% 100%, var(--fz-accent-low) 0%, transparent 65%)', pointerEvents: 'none' }} />
              <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 14, opacity: 0.14 }}>
                <Layout size={42} />
              </div>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--fz-text-muted)', marginBottom: 6 }}>Sin diseños personalizados</div>
              <div style={{ fontSize: 11.5, color: 'var(--fz-text-muted)', opacity: 0.6, lineHeight: 1.65 }}>
                Crea uno nuevo o duplica una plantilla<br/>para empezar
              </div>
            </div>
          )}
        </div>

        {editingLayout && (
          <div className="fz-dialog-overlay">
            <div className="fz-dialog">
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 32 }}>
                <span style={{ fontSize: 20, fontWeight: 800, letterSpacing: '-0.02em' }}>Personalizar Diseño</span>
                {!editingLayout.isTemplate && (
                  <button 
                    onClick={() => deleteLayout(editingLayout.id)}
                    style={{ background: 'var(--danger-dim)', color: 'var(--danger)', border: 'none', padding: 8, borderRadius: 10, cursor: 'pointer' }}
                  >
                    <Trash2 size={20} />
                  </button>
                )}
              </div>

              <div style={{ marginBottom: 24 }}>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 700, color: 'var(--fz-text-muted)', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Nombre del diseño</label>
                <div style={{ position: 'relative' }}>
                  <Edit3 size={18} style={{ position: 'absolute', left: 14, top: 13, opacity: 0.4 }} />
                  <input 
                    value={editingLayout.name} 
                    onChange={e => setEditingLayout({...editingLayout, name: e.target.value})}
                    placeholder="Ej: Multitarea, Gaming..."
                    style={{ width: '100%', background: 'rgba(128,128,128,0.08)', border: '1px solid var(--fz-border)', color: 'var(--fz-text)', padding: '12px 16px 12px 42px', borderRadius: 12, fontSize: 14, fontWeight: 500, outline: 'none', transition: 'border-color 0.2s' }}
                    onFocus={e => e.target.style.borderColor = 'var(--fz-accent)'}
                    onBlur={e => e.target.style.borderColor = 'rgba(255,255,255,0.1)'}
                  />
                </div>
              </div>

              <div style={{ marginBottom: 32 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
                   <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <Settings size={18} style={{ opacity: 0.5 }} />
                      <span style={{ fontSize: 14, fontWeight: 600 }}>Espaciado entre zonas</span>
                   </div>
                   <div style={{ background: 'var(--accent-low)', color: 'var(--fz-accent)', padding: '4px 10px', borderRadius: 8, fontSize: 12, fontWeight: 700 }}>
                    {spacing}px
                   </div>
                </div>
                <input 
                  type="range" min="0" max="64" step="2"
                  value={spacing} 
                  onChange={e => setSpacing(parseInt(e.target.value))} 
                  style={{ width: '100%', accentColor: 'var(--fz-accent)', cursor: 'pointer' }} 
                />
              </div>

              <div style={{ display: 'flex', gap: 12 }}>
                <button onClick={saveLayoutProperties} className="fz-btn-primary" style={{ flex: 1.5, height: 48 }}>Guardar Configuración</button>
                <button onClick={() => setEditingLayout(null)} className="fz-btn-secondary" style={{ flex: 1, height: 48 }}>Cancelar</button>
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
        input[type="range"] {
          -webkit-appearance: none;
          height: 5px;
          background: rgba(255,255,255,0.09);
          border-radius: 3px;
        }
        input[type="range"]::-webkit-slider-thumb {
          -webkit-appearance: none;
          width: 16px;
          height: 16px;
          background: #fff;
          border: 2px solid var(--fz-accent);
          border-radius: 50%;
          cursor: pointer;
          box-shadow: 0 2px 8px rgba(0,0,0,0.35), 0 0 10px var(--fz-accent-dim);
          transition: transform 0.15s ease;
        }
        input[type="range"]::-webkit-slider-thumb:hover {
          transform: scale(1.15);
        }
      `}</style>
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
            <div style={{ width: 7, height: 7, borderRadius: '50%', background: 'var(--fz-accent)', flexShrink: 0, animation: 'fzActivePing 2.2s ease-in-out infinite' }} />
          )}
          {isAdapted && (
            <span title={`Diseñado para ${layout.refWidth}×${layout.refHeight}, monitor actual ${monW}×${monH}`}
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
              <Edit3 size={14} style={{ opacity: 0.7, color: 'var(--fz-accent)' }} /> Editar Propiedades
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
                <Maximize2 size={14} style={{ opacity: 0.7, color: 'var(--fz-accent)' }} /> Editor Interactivo
              </button>
            )}
            <div style={{ height: 1, background: 'rgba(255,255,255,0.08)', margin: '4px 8px' }} />
            <button 
              style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
              className="menu-item-hover"
              onClick={() => { duplicateLayout(layout); setMenuOpenId(null); }}
            >
              <Copy size={14} style={{ opacity: 0.7 }} /> Duplicar
            </button>
            {!layout.isTemplate && (
              <button 
                style={{ width: '100%', padding: '8px 12px', textAlign: 'left', background: 'none', border: 'none', color: '#ff4d4d', fontSize: 13, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 8, transition: 'all 0.15s' }}
                className="menu-item-hover"
                onClick={() => { deleteLayout(layout.id); setMenuOpenId(null); }}
              >
                <Trash2 size={14} /> Eliminar
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
