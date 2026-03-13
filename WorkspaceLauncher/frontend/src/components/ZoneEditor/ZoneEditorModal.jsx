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
  const [activeMonitorId, setActiveMonitorId] = useState(initialMonitorId);
  const [editingLayout, setEditingLayout] = useState(null);
  const [menuOpenId, setMenuOpenId] = useState(null);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);

  const { grid, zones, spacing, setSpacing, selectedIds, selectZone, clearSelection, splitZone, mergeSelected, moveDivider, removeDivider, applyPreset, resetToFull, setGridFromZones, setGridFromGridState } = useZoneEditor([]);

  const [selectedLayoutId, setSelectedLayoutId] = useState(null);

  useEffect(() => {
    loadAll().finally(() => setLoading(false));
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

      const monitorId = activeMonitorId || (mRes?.length > 0 ? mRes[0].hardwareId : null);
      if (!activeMonitorId) setActiveMonitorId(monitorId);

      const activeMon = mRes.find(m => m.hardwareId === monitorId);
      const activeEntry = aRes?.entries?.find(a => a.monitorPtInstance === activeMon?.ptInstance);
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

  async function saveLayoutProperties() {
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
        setEditingLayout(null);
        if (canvasOnly) {
          window.chrome.webview.postMessage({ action: 'cze_canvas_saved' });
        } else {
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
  const activeEntryForMonitor = activeLayouts.find(a => a.monitorPtInstance === activeMonitor?.ptInstance);
  const activeLayoutIdForMonitor = activeEntryForMonitor?.layoutId;

  const templates = [
    { id: 'sin', name: 'Sin diseño', zones: [], isTemplate: true },
    { id: 'foco', name: 'Foco', zones: [{ id: 1, x: 0.1, y: 0.1, w: 0.8, h: 0.8 }], isTemplate: true },
    { id: 'columnas', name: 'Columnas', zones: [{ x: 0, y: 0, w: 0.33, h: 1 }, { x: 0.33, y: 0, w: 0.34, h: 1 }, { x: 0.67, y: 0, w: 0.33, h: 1 }], isTemplate: true },
    { id: 'filas', name: 'Filas', zones: [{ x: 0, y: 0, w: 1, h: 0.33 }, { x: 0, y: 0.33, w: 1, h: 0.34 }, { x: 0, y: 0.67, w: 1, h: 0.33 }], isTemplate: true },
    { id: 'cuadricula', name: 'Cuadrícula', zones: [{ x: 0, y: 0, w: 0.5, h: 0.5 }, { x: 0.5, y: 0, w: 0.5, h: 0.5 }, { x: 0, y: 0.5, w: 0.5, h: 0.5 }, { x: 0.5, y: 0.5, w: 0.5, h: 0.5 }], isTemplate: true },
  ];

  const customLayouts = layouts.filter(l => !l.isTemplate);

  const LayoutCard = ({ layout, isActive }) => {
    const isMenuOpen = menuOpenId === layout.id;
    const isSelected = selectedLayoutId === layout.id;

    // Detect resolution mismatch (layout was designed on a different resolution)
    const activeMon = monitors.find(m => m.hardwareId === activeMonitorId);
    const monW = activeMon?.workArea?.width || activeMon?.bounds?.width || 0;
    const monH = activeMon?.workArea?.height || activeMon?.bounds?.height || 0;
    const isAdapted = layout.refWidth > 0 && layout.refHeight > 0
      && (layout.refWidth !== monW || layout.refHeight !== monH);

    return (
      <div 
        className={`fz-layout-card ${isActive ? 'active' : ''} ${isSelected && !isActive ? 'selected' : ''}`}
        onClick={() => setSelectedLayoutId(layout.id)}
        onDoubleClick={() => setActiveLayout(activeMonitor?.ptInstance, activeEntryForMonitor?.desktopId || '00000000-0000-0000-0000-000000000000', layout.id)}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <div style={{ fontSize: 13, fontWeight: 700, color: isActive ? 'var(--fz-accent)' : 'var(--fz-text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', display: 'flex', alignItems: 'center', gap: 6 }}>
            {isActive && <div style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--fz-accent)', boxShadow: '0 0 8px var(--fz-accent)' }} />}
            {isAdapted && (
              <span title={`Diseñado para ${layout.refWidth}×${layout.refHeight}, monitor actual ${monW}×${monH} — escala proporcional aplicada`}
                style={{ fontSize: 10, background: 'rgba(255,200,0,0.15)', color: '#ffd700', padding: '1px 5px', borderRadius: 4, fontWeight: 600 }}>
                ∝
              </span>
            )}
            {layout.name}
          </div>
          <div className="fz-card-actions">
            <button 
              className="fz-icon-btn" 
              onClick={(e) => { e.stopPropagation(); setMenuOpenId(isMenuOpen ? null : layout.id); }}
            >
              <MoreVertical size={14} />
            </button>
          </div>
          
          {isMenuOpen && (
            <div 
              style={{ position: 'absolute', top: 44, right: 12, background: '#1c1c1e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: 10, padding: 6, zIndex: 100, boxShadow: '0 10px 30px rgba(0,0,0,0.5)', minWidth: 160 }}
              onClick={e => e.stopPropagation()}
            >
              <button 
                style={{ width: '100%', padding: '10px 14px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 12, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 6, transition: 'background 0.2s' }}
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
                <Edit3 size={14} style={{ opacity: 0.6 }} /> Editar Propiedades
              </button>
              {!layout.isTemplate && (
                <button 
                  style={{ width: '100%', padding: '10px 14px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 12, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 6, transition: 'background 0.2s' }}
                  className="menu-item-hover"
                  onClick={() => {
                    setMenuOpenId(null);
                    openCanvasEditor(activeMonitorId, layout.id);
                  }}
                >
                  <Maximize2 size={14} style={{ opacity: 0.6 }} /> Editor Interactivo
                </button>
              )}
              <button 
                style={{ width: '100%', padding: '10px 14px', textAlign: 'left', background: 'none', border: 'none', color: '#fff', fontSize: 12, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 6, transition: 'background 0.2s' }}
                className="menu-item-hover"
                onClick={() => { duplicateLayout(layout); setMenuOpenId(null); }}
              >
                <Copy size={14} style={{ opacity: 0.6 }} /> Duplicar
              </button>
              {!layout.isTemplate && (
                <button 
                  style={{ width: '100%', padding: '10px 14px', textAlign: 'left', background: 'none', border: 'none', color: '#ff4d4d', fontSize: 12, display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer', borderRadius: 6, transition: 'background 0.2s' }}
                  className="menu-item-hover"
                  onClick={() => { deleteLayout(layout.id); setMenuOpenId(null); }}
                >
                  <Trash2 size={14} /> Eliminar
                </button>
              )}
            </div>
          )}
        </div>
        
        <div style={{ height: 84, background: 'rgba(0,0,0,0.5)', position: 'relative', borderRadius: 8, overflow: 'hidden', border: '1px solid rgba(255,255,255,0.06)', transition: 'border-color 0.2s' }}>
          {layout.zones?.map((z, i) => (
            <div key={i} style={{ 
              position: 'absolute', 
              left: `${z.x*100}%`, 
              top: `${z.y*100}%`, 
              width: `${z.w*100}%`, 
              height: `${z.h*100}%`, 
              border: isActive ? '1px solid rgba(0, 210, 255, 0.25)' : '1px solid rgba(255,255,255,0.1)', 
              background: isActive ? 'rgba(0, 210, 255, 0.05)' : 'rgba(255,255,255,0.02)' 
            }} />
          ))}
          {isActive && <div style={{ position: 'absolute', inset: 0, border: '2px solid var(--fz-accent)', background: 'rgba(0, 210, 255, 0.08)', pointerEvents: 'none' }} />}
          {isSelected && !isActive && <div style={{ position: 'absolute', inset: 0, border: '2px solid rgba(0, 210, 255, 0.3)', background: 'rgba(0, 210, 255, 0.03)', pointerEvents: 'none' }} />}
        </div>
      </div>
    );
  };

  const modalStyle = standalone ? {
    position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', background: 'var(--fz-bg)', color: 'white', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif'
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
        color: '#e2e2e2',
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
        <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.25)', overflow: 'hidden' }} onClick={redirectToManager}>
          <ZoneCanvas grid={grid} zones={zones} spacing={spacing} selectedIds={new Set()} onSelectZone={() => {}} onSplitZone={() => {}} onMoveDivider={() => {}} onRemoveDivider={removeDivider} onClearSelection={() => {}} />
        </div>
      );
    }

    return (
      <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.3)', overflow: 'hidden' }}>
        <style>{`
          html, body, #root { background: transparent !important; }
        `}</style>
        <ZoneCanvas grid={grid} zones={zones} spacing={spacing} selectedIds={selectedIds} onSelectZone={selectZone} onSplitZone={splitZone} onMoveDivider={moveDivider} onRemoveDivider={removeDivider} onClearSelection={clearSelection} />
      </div>
    );
  }

  if (loading && !canvasOnly) {
    return (
      <div style={modalStyle}>
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 20 }}>
          <div style={{ width: 40, height: 40, border: '3px solid rgba(255,255,255,0.1)', borderTopColor: 'var(--fz-accent)', borderRadius: '50%', animation: 'spin 1s linear infinite' }} />
          <div style={{ color: 'var(--fz-text-muted)', fontSize: 13, fontWeight: 500, letterSpacing: '0.05em' }}>PREPARANDO ENTORNO...</div>
        </div>
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }

  return (
    <div style={modalStyle}>
      <div 
        className="fz-editor-container"
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
          border: standalone ? 'none' : '1px solid rgba(255,255,255,0.1)'
        }}
      >
        <div className="fz-monitor-bar" style={{ paddingTop: 40 }}>
          {monitors.map(mon => (
            <div 
              key={mon.hardwareId}
              className={`fz-monitor-item ${activeMonitorId === mon.hardwareId ? 'active' : ''}`}
              onClick={() => setActiveMonitorId(mon.hardwareId)}
            >
              <div className="fz-monitor-number">{mon.monitorNumber}</div>
              <div style={{ fontSize: 11, opacity: 0.6, fontWeight: 600, marginTop: 4 }}>
                {mon.bounds.width} x {mon.bounds.height}
              </div>
            </div>
          ))}
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '0 60px 60px' }}>
          <div className="fz-section-title">Plantillas del Sistema</div>
          <div className="fz-grid">
            {templates.map(t => (
              <LayoutCard key={t.id} layout={t} isActive={activeLayoutIdForMonitor === t.id} />
            ))}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 48, marginBottom: 16 }}>
            <div className="fz-section-title" style={{ margin: 0 }}>Mis Diseños Personalizados</div>
            <button className="fz-btn-primary" onClick={createNewLayout} style={{ padding: '8px 16px', borderRadius: 8, display: 'flex', alignItems: 'center', gap: 8 }}>
              <Plus size={16} /> Nuevo
            </button>
          </div>
          
          {customLayouts.length > 0 ? (
            <div className="fz-grid">
              {customLayouts.map(l => (
                <LayoutCard key={l.id} layout={l} isActive={activeLayoutIdForMonitor === l.id} />
              ))}
            </div>
          ) : (
            <div style={{ padding: 60, textAlign: 'center', background: 'rgba(255,255,255,0.02)', borderRadius: 20, border: '2px dashed rgba(255,255,255,0.05)' }}>
              <div style={{ opacity: 0.3, marginBottom: 12 }}><Layout size={48} style={{ margin: '0 auto' }} /></div>
              <div style={{ fontSize: 14, fontWeight: 600, opacity: 0.5 }}>No tienes diseños personalizados todavía</div>
              <div style={{ fontSize: 12, opacity: 0.3, marginTop: 4 }}>Crea uno nuevo o duplica una plantilla para empezar.</div>
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
                    style={{ width: '100%', background: 'rgba(255,255,255,0.05)', border: '1px solid rgba(255,255,255,0.1)', color: 'white', padding: '12px 16px 12px 42px', borderRadius: 12, fontSize: 14, fontWeight: 500, outline: 'none', transition: 'border-color 0.2s' }}
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
          background: rgba(255,255,255,0.05) !important;
        }
        input[type="range"] {
          -webkit-appearance: none;
          height: 6px;
          background: rgba(255,255,255,0.1);
          border-radius: 3px;
        }
        input[type="range"]::-webkit-slider-thumb {
          -webkit-appearance: none;
          width: 18px;
          height: 18px;
          background: #fff;
          border: 2px solid var(--fz-accent);
          border-radius: 50%;
          cursor: pointer;
          box-shadow: 0 0 10px rgba(0,0,0,0.3);
        }
      `}</style>
    </div>
  );
}
