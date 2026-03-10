import { useState, useEffect, useCallback, useMemo } from 'react'
import { FolderOpen, X, Globe, Terminal, Code2, FileCode, MonitorSmartphone, Cog, ArrowLeft, RotateCw, AlertCircle, CheckCircle2 } from 'lucide-react'
import { bridge, onEvent, offEvent } from '../../api/bridge.js'
import './ItemDialog.css'

const ITEM_TYPES = [
  { value: 'exe', label: 'Ejecutable (.exe)', desc: 'Aplicación nativa de Windows', icon: Cog, color: 'var(--cat-exe)' },
  { value: 'url', label: 'Web / URL', desc: 'Páginas web en el navegador', icon: Globe, color: 'var(--cat-web)' },
  { value: 'ide', label: 'IDE Personalizado', desc: 'IntelliJ, Cursor, Android Studio...', icon: Code2, color: 'var(--cat-ide)' },
  { value: 'vscode', label: 'VS Code', desc: 'Abre un proyecto en VS Code', icon: FileCode, color: 'var(--cat-ide)' },
  { value: 'powershell', label: 'Terminal', desc: 'PowerShell o Windows Terminal', icon: Terminal, color: 'var(--cat-terminal)' },
  { value: 'obsidian', label: 'Obsidian', desc: 'Abre un vault específico', icon: MonitorSmartphone, color: 'var(--cat-obsidian)' },
]

const BROWSERS = [
  { value: 'default', label: 'Por defecto del sistema' },
  { value: 'msedge', label: 'Microsoft Edge' },
  { value: 'chrome', label: 'Google Chrome' },
  { value: 'firefox', label: 'Mozilla Firefox' },
  { value: 'brave', label: 'Brave' },
]

const DEFAULT_ITEM = {
  type: '',
  path: '',
  cmd: '',
  ide_cmd: '',
  browser: 'default',
  browser_display: 'Por defecto del sistema',
  monitor: 'Por defecto',
  desktop: 'Por defecto',
  fancyzone: 'Ninguna',
  fancyzone_uuid: '',
  delay: '0'
}

export default function ItemDialog({ category, index, item, onSave, onClose }) {
  // Wizard state: 1 = Type selection, 2 = Form details
  const [step, setStep] = useState(item ? 2 : 1)
  const [form, setForm] = useState({ ...DEFAULT_ITEM, ...(item || {}) })

  // Data states - use the unified fzStatus endpoint
  const [fzStatus, setFzStatus] = useState(null) // { entries, layouts, monitors, desktops }
  const [syncState, setSyncState] = useState('idle') // 'idle' | 'syncing' | 'synced' | 'error'
  const [detectedLayout, setDetectedLayout] = useState(null) // The auto-detected active layout

  // Load initial data using the unified endpoint
  const loadFzStatus = useCallback(async () => {
    setSyncState('syncing')
    try {
      const data = await bridge.getFzStatus()
      if (data) {
        setFzStatus(data)
        setSyncState('synced')
        setTimeout(() => setSyncState('idle'), 1500)
      }
    } catch (err) {
      console.error("Error loading FZ status:", err)
      setSyncState('error')
    }
  }, [])

  useEffect(() => {
    loadFzStatus()
  }, [loadFzStatus])

  // Background polling to keep sync
  useEffect(() => {
    const timer = setInterval(loadFzStatus, 8000)
    return () => clearInterval(timer)
  }, [loadFzStatus])

  // Derived data from fzStatus
  const monitors = useMemo(() => fzStatus?.monitors || [], [fzStatus])
  const desktops = useMemo(() => fzStatus?.desktops || [], [fzStatus])
  const availableLayouts = useMemo(() => (fzStatus?.layouts || []).map(l => ({
    uuid: l.uuid,
    name: l.name,
    zoneCount: l.zoneCount,
    type: l.type,
    info: l.info
  })), [fzStatus])

  function set(key, value) {
    setForm(f => ({ ...f, [key]: value }))
  }

  // ── Auto-detect active layout when monitor/desktop changes ──────────
  useEffect(() => {
    if (!fzStatus || form.monitor === 'Por defecto') {
      setDetectedLayout(null)
      return
    }

    const entries = fzStatus.entries || []
    const monitor = monitors.find(m => m.label === form.monitor || m.id === form.monitor)
    if (!monitor) {
      setDetectedLayout(null)
      return
    }

    const desktop = desktops.find(d => d.name === form.desktop)
    const desktopId = desktop?.id

    // Find the matching entry from the pre-resolved status
    let match = entries.find(e => {
      const monMatch = e.monitorId === monitor.id ||
        e.monitorPtName === monitor.ptName ||
        e.monitorPtInstance === monitor.ptInstance
      const dkMatch = desktopId
        ? (e.desktopId === desktopId)
        : e.desktopIsCurrent
      return monMatch && dkMatch
    })

    // Fallback: try any desktop for this monitor
    if (!match?.activeLayoutUuid && desktopId) {
      match = entries.find(e => {
        const monMatch = e.monitorId === monitor.id ||
          e.monitorPtName === monitor.ptName ||
          e.monitorPtInstance === monitor.ptInstance
        return monMatch && e.activeLayoutUuid
      })
    }

    if (match?.activeLayoutUuid) {
      const layout = availableLayouts.find(l =>
        l.uuid.replace(/[{}]/g, '').toLowerCase() === match.activeLayoutUuid.replace(/[{}]/g, '').toLowerCase()
      )
      if (layout) {
        setDetectedLayout({
          uuid: layout.uuid,
          name: layout.name,
          monitorLabel: match.monitorLabel,
          desktopName: match.desktopName
        })

        // Auto-select the detected layout and reset to zone 1
        // Only auto-update if the user hasn't manually picked a different layout
        // or this is the initial load
        setForm(f => {
          // If form already has a layout selected that matches, keep the zone
          if (f.fancyzone_uuid === layout.uuid) return f
          return {
            ...f,
            fancyzone_uuid: layout.uuid,
            fancyzone: `${layout.name} - Zona 1`
          }
        })
      } else {
        setDetectedLayout(null)
      }
    } else {
      setDetectedLayout(null)
    }
  }, [form.monitor, form.desktop, fzStatus, monitors, desktops, availableLayouts])

  // Trigger refresh when monitor/desktop changes
  const handleEnvChange = async (key, val) => {
    set(key, val)
    // Force fresh sync to get the latest state for the new selection
    await loadFzStatus()
  }

  const handleBrowsePath = useCallback(async () => {
    const isFolder = ['ide', 'vscode', 'powershell', 'obsidian'].includes(form.type)
    const filters = form.type === 'exe'
      ? [{ name: 'Ejecutables', extensions: ['exe'] }]
      : [{ name: 'Todos', extensions: ['*'] }]

    try {
      const result = await bridge.openFileDialog({ filters, isFolder })
      if (result) set('path', result)
    } catch (err) { }
  }, [form.type])

  function handleSave() {
    if (!form.path.trim() && form.type !== 'url') return
    const out = { ...form }
    // Clean up irrelevant fields
    if (out.type !== 'url') { delete out.cmd; delete out.browser; delete out.browser_display }
    if (out.type !== 'ide') delete out.ide_cmd
    if (out.type !== 'powershell') { if (!out.cmd) delete out.cmd }
    onSave(out)
  }

  // Render Step 1: Type Selection (Visual Grid)
  if (step === 1) {
    return (
      <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
        <div className="dialog">
          <div className="dialog-header">
            <h2>¿Qué tipo de aplicación quieres añadir?</h2>
            <button className="dialog-close" onClick={onClose}><X size={20} /></button>
          </div>
          <div className="type-grid">
            {ITEM_TYPES.map(t => {
              const Icon = t.icon
              return (
                <div key={t.value} className="type-card" style={{ '--card-color': t.color }}
                  onClick={() => { set('type', t.value); setStep(2); }}>
                  <div className="type-card-icon"><Icon size={24} /></div>
                  <div className="type-card-text">
                    <h3>{t.label}</h3>
                    <p>{t.desc}</p>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      </div>
    )
  }

  // Render Step 2: Details Form
  const selectedTypeConfig = ITEM_TYPES.find(t => t.value === form.type) || ITEM_TYPES[0]
  const TypeIcon = selectedTypeConfig.icon

  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <div className="dialog-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: selectedTypeConfig.color }}>
            <TypeIcon size={20} />
            <h2>Configurar {selectedTypeConfig.label}</h2>
          </div>
          <button className="dialog-close" onClick={onClose}><X size={20} /></button>
        </div>

        <div className="dialog-body">
          {/* Path with file picker */}
          <Field label={form.type === 'url' ? 'URL principal' : 'Ruta / Directorio'}>
            <div className="field-with-btn">
              <input
                value={form.path}
                onChange={e => set('path', e.target.value)}
                placeholder={form.type === 'url' ? 'https://...' : 'Selecciona o pega la ruta...'}
                autoFocus
              />
              {form.type !== 'url' && (
                <button className="btn-browse" onClick={handleBrowsePath} type="button">
                  <FolderOpen size={16} /> Examinar
                </button>
              )}
            </div>
          </Field>

          {/* IDE command */}
          {form.type === 'ide' && (
            <Field label="Comando de Terminal para el IDE (Ej: cursor, webstorm, phpstorm)">
              <input value={form.ide_cmd || ''} onChange={e => set('ide_cmd', e.target.value)} placeholder="Ej: cursor" />
            </Field>
          )}

          {/* PowerShell / URL Tabs */}
          {(form.type === 'url' || form.type === 'powershell') && (
            <Field label={form.type === 'url' ? "URLs adicionales (separadas por  --- NUEVA PESTAÑA ---)" : "Comandos en pestañas (separadas por  --- NUEVA PESTAÑA ---)"}>
              <textarea rows={2} value={form.cmd || ''} onChange={e => set('cmd', e.target.value)} />
            </Field>
          )}

          {/* URL: browser */}
          {form.type === 'url' && (
            <Field label="Navegador preferido">
              <select value={form.browser || 'default'} onChange={e => {
                const b = BROWSERS.find(br => br.value === e.target.value)
                set('browser', e.target.value); set('browser_display', b?.label || e.target.value)
              }}>
                {BROWSERS.map(b => <option key={b.value} value={b.value}>{b.label}</option>)}
              </select>
            </Field>
          )}

          {/* Monitor & Desktop */}
          <div className="field-row">
            <Field label="Monitor">
              <select value={form.monitor} onChange={e => handleEnvChange('monitor', e.target.value)}>
                <option value="Por defecto">Por defecto</option>
                {monitors.map(m => (
                  <option key={m.id} value={m.label}>{m.label}</option>
                ))}
              </select>
            </Field>
            <Field label="Escritorio Virtual">
              <select value={form.desktop} onChange={e => handleEnvChange('desktop', e.target.value)}>
                <option value="Por defecto">Por defecto</option>
                {desktops.map((dk, i) => (
                  <option key={dk.id || i} value={dk.name}>
                    {dk.name} {dk.isCurrent ? ' (Actual)' : ''}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          {/* Active layout detection indicator */}
          {form.monitor !== 'Por defecto' && (
            <ActiveLayoutIndicator
              detectedLayout={detectedLayout}
              syncState={syncState}
              onRefresh={loadFzStatus}
            />
          )}

          {/* FancyZones Interactive Render */}
          <FancyZonesVisualizer
            form={form}
            set={set}
            availableLayouts={availableLayouts}
            detectedLayout={detectedLayout}
            onRefresh={loadFzStatus}
          />

          {/* Delay */}
          <Field label="Retardo antes de lanzar (Milisegundos)">
            <input type="number" min="0" value={form.delay} onChange={e => set('delay', e.target.value)} style={{ maxWidth: 150 }} />
          </Field>
        </div>

        <div className="dialog-footer">
          {!item ? (
            <button className="btn-secondary" onClick={() => setStep(1)}>
              <ArrowLeft size={16} /> Volver
            </button>
          ) : <div></div>}

          <div className="footer-right">
            <button className="btn-secondary" onClick={onClose} style={{ border: 'none', background: 'transparent' }}>Cancelar</button>
            <button className="btn-launch" onClick={handleSave} disabled={!form.path.trim() && form.type !== 'url'}>
              {index >= 0 ? 'Guardar cambios' : 'Añadir al Workspace'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

function Field({ label, children }) {
  return (
    <div className="dialog-field">
      <label className="dialog-label">{label}</label>
      {children}
    </div>
  )
}

// ── Active Layout Detection Indicator ───────────────────────────────────
function ActiveLayoutIndicator({ detectedLayout, syncState, onRefresh }) {
  return (
    <div className="fz-active-indicator">
      <div className="fz-active-indicator-content">
        {syncState === 'syncing' ? (
          <>
            <RotateCw size={14} className="fz-spin" />
            <span className="fz-indicator-text">Sincronizando con PowerToys...</span>
          </>
        ) : detectedLayout ? (
          <>
            <CheckCircle2 size={14} style={{ color: 'var(--success)' }} />
            <span className="fz-indicator-text">
              Layout activo: <strong>{detectedLayout.name}</strong>
              <span className="fz-indicator-sub">
                {detectedLayout.monitorLabel} · {detectedLayout.desktopName}
              </span>
            </span>
          </>
        ) : (
          <>
            <AlertCircle size={14} style={{ color: 'var(--warning)' }} />
            <span className="fz-indicator-text">No se detectó un layout activo para esta posición</span>
          </>
        )}
      </div>
      <button
        className="btn-icon-small fz-refresh-btn"
        onClick={onRefresh}
        title="Sincronizar con PowerToys"
      >
        <RotateCw size={13} color="var(--accent)" />
      </button>
    </div>
  )
}

// ── Mini-Motor Renderizador de FancyZones ──────────────────────────────────
function FancyZonesVisualizer({ form, set, availableLayouts, detectedLayout, onRefresh }) {
  // Obtener el layout actual
  const currentLayout = availableLayouts.find(l => l.uuid === form.fancyzone_uuid) || null

  const handleZoneClick = (idx) => {
    if (!currentLayout) return;
    set('fancyzone', `${currentLayout.name} - Zona ${idx + 1}`);
    set('fancyzone_uuid', currentLayout.uuid);
  }

  const handleLayoutChange = (uuid) => {
    if (uuid === "Ninguna") {
      set('fancyzone', "Ninguna");
      set('fancyzone_uuid', "");
      return;
    }
    const layout = availableLayouts.find(l => l.uuid === uuid);
    if (layout) {
      set('fancyzone_uuid', layout.uuid);
      set('fancyzone', `${layout.name} - Zona 1`);
    }
  }

  // Parsear el número de zona actual del string "Nombre Layout - Zona X"
  const activeZoneIdx = useMemo(() => {
    if (form.fancyzone === "Ninguna" || !form.fancyzone) return -1;
    const parts = form.fancyzone.split("Zona ");
    if (parts.length > 1) return parseInt(parts[1]) - 1;
    return -1;
  }, [form.fancyzone])

  // Check if current selection matches the detected (active) layout
  const isActiveLayout = detectedLayout && form.fancyzone_uuid === detectedLayout.uuid

  return (
    <div className="fz-visualizer">
      <div className="fz-header" style={{ display: 'flex', alignItems: 'flex-end', gap: '15px', marginBottom: '10px' }}>
        <Field label="Layout de FancyZones">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <select
              value={form.fancyzone_uuid || "Ninguna"}
              onChange={e => handleLayoutChange(e.target.value)}
              style={{ width: '250px' }}
              className={isActiveLayout ? 'fz-select-active' : ''}
            >
              <option value="Ninguna">Ninguno / Libre</option>
              {availableLayouts.map(l => (
                <option key={l.uuid} value={l.uuid}>
                  {l.name} {detectedLayout?.uuid === l.uuid ? '✓ ACTIVO' : ''}
                </option>
              ))}
            </select>
            {isActiveLayout && (
              <span className="fz-active-badge">ACTIVO</span>
            )}
          </div>
        </Field>

        {form.fancyzone && form.fancyzone !== "Ninguna" && (
          <span className="fz-status" style={{ fontSize: '0.85rem', color: 'var(--accent)', opacity: 0.9, paddingBottom: '8px' }}>
            📍 Asignado: {form.fancyzone}
          </span>
        )}
      </div>

      {currentLayout && form.fancyzone_uuid && (
        <div className="fz-render-container" style={{
          width: '100%',
          height: '240px',
          background: 'rgba(0,0,0,0.45)',
          borderRadius: '12px',
          border: isActiveLayout ? '1px solid rgba(0, 230, 118, 0.3)' : '1px solid rgba(255,255,255,0.08)',
          overflow: 'hidden',
          position: 'relative',
          padding: '12px',
          boxShadow: isActiveLayout
            ? 'inset 0 0 30px rgba(0,0,0,0.6), 0 0 15px rgba(0, 230, 118, 0.1)'
            : 'inset 0 0 30px rgba(0,0,0,0.6)',
          boxSizing: 'border-box'
        }}>
          {renderZones(currentLayout.info, activeZoneIdx, handleZoneClick)}
        </div>
      )}

      {!isActiveLayout && form.fancyzone_uuid && detectedLayout && (
        <div className="fz-warning-bar">
          <AlertCircle size={14} />
          <span>
            Este layout <strong>no</strong> es el activo en FancyZones para esta posición.
            El activo es: <strong>{detectedLayout.name}</strong>
          </span>
          <button
            className="fz-warning-btn"
            onClick={() => handleLayoutChange(detectedLayout.uuid)}
          >
            Usar activo
          </button>
        </div>
      )}
    </div>
  )
}

function renderZones(info, activeIdx, onClick) {
  if (!info) return null;
  const type = info.type || "grid";

  if (type === "grid") {
    const rowsMap = info["cell-child-map"] || [[0]];
    const rowsPerc = info["rows-percentage"] || [10000];
    const colsPerc = info["columns-percentage"] || [10000];

    const zones = {};
    rowsMap.forEach((row, rIdx) => {
      row.forEach((zId, cIdx) => {
        if (!zones[zId]) zones[zId] = { minR: rIdx, maxR: rIdx, minC: cIdx, maxC: cIdx };
        else {
          zones[zId].minR = Math.min(zones[zId].minR, rIdx);
          zones[zId].maxR = Math.max(zones[zId].maxR, rIdx);
          zones[zId].minC = Math.min(zones[zId].minC, cIdx);
          zones[zId].maxC = Math.max(zones[zId].maxC, cIdx);
        }
      })
    });

    const gridStyle = {
      gridTemplateRows: rowsPerc.map(p => `${p}fr`).join(' '),
      gridTemplateColumns: colsPerc.map(p => `${p}fr`).join(' ')
    };

    return (
      <div style={{ ...gridStyle, width: '100%', height: '100%', display: 'grid', gap: info.spacing ? '4px' : '2px' }}>
        {Object.entries(zones).map(([zId, span]) => {
          const id = parseInt(zId);
          const isSelected = activeIdx === id;
          return (
            <button
              key={id}
              className={`fz-zone-btn ${isSelected ? 'selected' : ''}`}
              style={{
                gridRow: `${span.minR + 1} / ${span.maxR + 2}`,
                gridColumn: `${span.minC + 1} / ${span.maxC + 2}`,
                border: isSelected ? '2px solid var(--accent)' : '1px solid rgba(255,255,255,0.1)',
                background: isSelected ? 'var(--accent-low)' : 'rgba(255,255,255,0.05)',
                color: isSelected ? 'var(--accent)' : '#888',
                cursor: 'pointer',
                fontWeight: 'bold',
                transition: 'all 0.2s',
                borderRadius: '4px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: '1rem'
              }}
              onClick={() => onClick(id)}
            >
              {id + 1}
            </button>
          )
        })}
      </div>
    )
  }

  if (type === "canvas") {
    const zones = info.zones || [];
    return (
      <div style={{ position: 'relative', width: '100%', height: '100%' }}>
        {zones.map((z, idx) => {
          const isSelected = activeIdx === idx;
          const refW = info["ref-width"] || 10000;
          const refH = info["ref-height"] || 10000;
          const x = (z.X / refW) * 100;
          const y = (z.Y / refH) * 100;
          const w = (z.width / refW) * 100;
          const h = (z.height / refH) * 100;

          return (
            <button
              key={idx}
              className={`fz-zone-btn ${isSelected ? 'selected' : ''}`}
              style={{
                position: 'absolute',
                left: `${x}%`, top: `${y}%`, width: `${w}%`, height: `${h}%`,
                border: isSelected ? '2px solid var(--accent)' : '1px solid rgba(255,255,255,0.2)',
                background: isSelected ? 'var(--accent-low)' : 'rgba(255,255,255,0.1)',
                color: isSelected ? 'var(--accent)' : '#fff',
                opacity: 0.9,
                borderRadius: '4px',
                cursor: 'pointer',
                fontWeight: 'bold',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
              }}
              onClick={() => onClick(idx)}
            >
              {idx + 1}
            </button>
          )
        })}
      </div>
    )
  }
  return null;
}
