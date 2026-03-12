import { useState, useEffect, useRef } from 'react';
import { bridge } from '../../api/bridge';
import { useZoneEditor } from './ZoneEditorHooks';
import { ZoneCanvas } from './ZoneCanvas';
import { ZoneToolbar } from './ZoneToolbar';

export function ZoneEditorModal({ onClose }) {
  const [layouts, setLayouts] = useState([]);
  const [activeLayouts, setActiveLayouts] = useState([]);
  const [selectedLayoutId, setSelectedLayoutId] = useState(null);
  const [layoutName, setLayoutName] = useState('');
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState('');

  const { zones, setZones, selectedIds, selectZone, clearSelection, splitZone, mergeSelected, moveDivider, applyPreset, resetToFull } = useZoneEditor([]);

  useEffect(() => {
    loadAll();
  }, []);

  async function loadAll() {
    const [lRes, aRes] = await Promise.all([bridge.czeGetLayouts(), bridge.czeGetActiveLayouts()]);
    setLayouts(lRes?.layouts ?? []);
    setActiveLayouts(aRes?.entries ?? []);
  }

  function selectLayout(id) {
    const layout = layouts.find(l => l.id === id);
    if (!layout) return;
    setSelectedLayoutId(id);
    setLayoutName(layout.name);
    setZones(layout.zones ?? []);
    clearSelection();
  }

  function newLayout() {
    const id = `cze_${Date.now()}`;
    const name = 'Nuevo Layout';
    setSelectedLayoutId(id);
    setLayoutName(name);
    resetToFull();
    clearSelection();
  }

  async function saveLayout() {
    setSaving(true);
    setStatus('');
    try {
      const res = await bridge.czeSaveLayout({
        id: selectedLayoutId ?? `cze_${Date.now()}`,
        name: layoutName || 'Layout',
        zones,
      });
      if (res?.ok) {
        setStatus('Guardado.');
        await loadAll();
        setSelectedLayoutId(res.id);
      } else {
        setStatus(`Error: ${res?.error ?? 'desconocido'}`);
      }
    } finally {
      setSaving(false);
    }
  }

  async function deleteLayout(id) {
    const res = await bridge.czeDeleteLayout(id);
    if (res?.ok) {
      if (selectedLayoutId === id) {
        setSelectedLayoutId(null);
        setZones([]);
        setLayoutName('');
      }
      await loadAll();
    }
  }

  async function setActiveLayout(monitorPtInstance, desktopId, layoutId) {
    await bridge.czeSetActiveLayout(monitorPtInstance, desktopId, layoutId);
    await loadAll();
  }

  const modalStyle = {
    position: 'fixed', inset: 0, zIndex: 1000,
    background: 'rgba(0,0,0,0.65)',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
  };
  const boxStyle = {
    background: '#23262e',
    borderRadius: 10,
    border: '1px solid rgba(255,255,255,0.12)',
    width: 860,
    maxWidth: '95vw',
    maxHeight: '90vh',
    overflow: 'hidden',
    display: 'flex',
    flexDirection: 'column',
  };
  const headerStyle = {
    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
    padding: '14px 18px',
    borderBottom: '1px solid rgba(255,255,255,0.08)',
    fontSize: 14, fontWeight: 600, color: '#e8eaf0',
  };
  const bodyStyle = {
    display: 'flex', flex: 1, overflow: 'hidden',
  };
  const sidebarStyle = {
    width: 200,
    minWidth: 200,
    borderRight: '1px solid rgba(255,255,255,0.08)',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  };
  const mainStyle = {
    flex: 1,
    padding: '14px 16px',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'auto',
  };

  return (
    <div style={modalStyle} onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div style={boxStyle}>
        {/* Header */}
        <div style={headerStyle}>
          <span>Editor de Zonas Personalizadas</span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#aaa', fontSize: 18, cursor: 'pointer' }}>&#x2715;</button>
        </div>

        <div style={bodyStyle}>
          {/* Sidebar: layout list */}
          <div style={sidebarStyle}>
            <div style={{ padding: '10px 12px', borderBottom: '1px solid rgba(255,255,255,0.06)', fontSize: 11, color: 'rgba(255,255,255,0.45)' }}>LAYOUTS</div>
            <div style={{ flex: 1, overflowY: 'auto', padding: '6px 0' }}>
              {layouts.map(l => (
                <div
                  key={l.id}
                  onClick={() => selectLayout(l.id)}
                  style={{
                    padding: '7px 12px',
                    cursor: 'pointer',
                    background: selectedLayoutId === l.id ? 'rgba(109,179,242,0.15)' : 'transparent',
                    color: selectedLayoutId === l.id ? '#6db3f2' : '#c8cad0',
                    fontSize: 12,
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                  }}
                >
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{l.name}</span>
                  <button
                    onClick={(e) => { e.stopPropagation(); deleteLayout(l.id); }}
                    style={{ background: 'none', border: 'none', color: '#c06060', fontSize: 13, cursor: 'pointer', marginLeft: 4, padding: '0 2px' }}
                  >&#xD7;</button>
                </div>
              ))}
            </div>
            <div style={{ padding: '8px 12px', borderTop: '1px solid rgba(255,255,255,0.06)' }}>
              <button
                onClick={newLayout}
                style={{ width: '100%', padding: '6px 0', borderRadius: 4, border: 'none', background: '#2d3540', color: '#e8eaf0', fontSize: 12, cursor: 'pointer' }}
              >+ Nuevo layout</button>
            </div>
          </div>

          {/* Main: editor */}
          <div style={mainStyle}>
            {selectedLayoutId ? (
              <>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 10 }}>
                  <input
                    value={layoutName}
                    onChange={e => setLayoutName(e.target.value)}
                    placeholder="Nombre del layout"
                    style={{ flex: 1, padding: '5px 8px', borderRadius: 4, border: '1px solid rgba(255,255,255,0.15)', background: '#1a1d23', color: '#e8eaf0', fontSize: 12 }}
                  />
                  <button
                    onClick={saveLayout}
                    disabled={saving}
                    style={{ padding: '5px 14px', borderRadius: 4, border: 'none', background: '#3a5f8a', color: '#e8eaf0', fontSize: 12, cursor: 'pointer' }}
                  >{saving ? '...' : 'Guardar'}</button>
                  {status && <span style={{ fontSize: 11, color: status.startsWith('Error') ? '#e07070' : '#70c070' }}>{status}</span>}
                </div>

                <ZoneToolbar
                  selectedCount={selectedIds.size}
                  onPreset={applyPreset}
                  onMerge={mergeSelected}
                  onReset={resetToFull}
                />

                <ZoneCanvas
                  zones={zones}
                  selectedIds={selectedIds}
                  onSelectZone={selectZone}
                  onSplitZone={splitZone}
                  onMoveDivider={moveDivider}
                  onClearSelection={clearSelection}
                />

                {/* Active layout assignments */}
                {activeLayouts.length > 0 && (
                  <div style={{ marginTop: 14 }}>
                    <div style={{ fontSize: 11, color: 'rgba(255,255,255,0.45)', marginBottom: 8 }}>ASIGNAR A MONITOR/ESCRITORIO</div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      {activeLayouts.map(entry => (
                        <div key={`${entry.monitorPtInstance}_${entry.desktopId}`} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, color: '#c8cad0' }}>
                          <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {entry.monitorName} — {entry.desktopName}
                          </span>
                          <button
                            onClick={() => setActiveLayout(entry.monitorPtInstance, entry.desktopId, selectedLayoutId)}
                            style={{
                              padding: '3px 10px',
                              borderRadius: 4,
                              border: 'none',
                              background: entry.layoutId === selectedLayoutId ? '#3a5f8a' : '#2d3540',
                              color: entry.layoutId === selectedLayoutId ? '#6db3f2' : '#e8eaf0',
                              fontSize: 11,
                              cursor: 'pointer',
                            }}
                          >
                            {entry.layoutId === selectedLayoutId ? '\u2713 Activo' : 'Activar'}
                          </button>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'rgba(255,255,255,0.3)', fontSize: 13 }}>
                Selecciona un layout o crea uno nuevo
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
