import { useState, useEffect, useCallback, useMemo } from 'react'
import { FolderOpen, X, Globe, Terminal, Code2, FileCode, MonitorSmartphone, Cog, ArrowLeft, RotateCw, AlertCircle, CheckCircle2, Plus, Trash2, GripVertical } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import { renderZones } from '../../utils/fzUtils.jsx'
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

export default function ItemDialog({ category, index, item, validation, onSave, onClose }) {
  // Wizard state: 1 = Type selection, 2 = Form details
  const [step, setStep] = useState(item ? 2 : 1)
  // Ensure we don't pick up null values from item that might break the state
  const [form, setForm] = useState({ 
    ...DEFAULT_ITEM, 
    ...(item || {}),
    desktop: item?.desktop || 'Por defecto',
    monitor: item?.monitor || 'Por defecto'
  })

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
  const availableLayouts = useMemo(() => {
    const raw = fzStatus?.layouts || []
    return {
      custom: raw.filter(l => l.isCustom).map(l => ({ ...l })),
      templates: raw.filter(l => !l.isCustom).map(l => ({ ...l }))
    }
  }, [fzStatus])

  const flatLayouts = useMemo(() => [
    ...(availableLayouts.custom || []),
    ...(availableLayouts.templates || [])
  ], [availableLayouts])

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
    const monitor = monitors.find(m =>
      m.ptName === form.monitor ||
      m.name === form.monitor ||
      m.label === form.monitor ||
      String(m.id) === String(form.monitor) ||
      (m.displayLabel && (m.displayLabel === form.monitor || m.displayLabel.replace(' ★', '').trim() === form.monitor))
    )
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
      const layoutInfo = match.activeLayout;
      if (layoutInfo) {
        setDetectedLayout({
          uuid: layoutInfo.uuid,
          name: layoutInfo.name,
          isCustom: layoutInfo.isCustom,
          monitorLabel: match.monitorLabel,
          desktopName: match.desktopName
        })

        // Auto-select the detected layout only if it's CUSTOM and the user hasn't picked one yet
        if (layoutInfo.isCustom) {
          setForm(f => {
            if (f.fancyzone_uuid && f.fancyzone_uuid !== '') return f;
            return {
              ...f,
              fancyzone_uuid: layoutInfo.uuid,
              fancyzone: `${layoutInfo.name} - Zona 1`
            }
          })
        }
      } else {
        setDetectedLayout(null)
      }
    } else {
      setDetectedLayout(null)
    }
  }, [form.monitor, form.desktop, fzStatus, monitors, desktops, flatLayouts])

  // Trigger refresh when monitor/desktop changes
  const handleEnvChange = async (key, val) => {
    set(key, val)
    // Force fresh sync to get the latest state for the new selection
    // but don't let fzStatus refresh overwrite our manual selection
    setTimeout(() => loadFzStatus(), 0)
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

          {/* Tab Management for URL/PowerShell */}
          {(form.type === 'url' || form.type === 'powershell') && (
            <TabManager
              type={form.type}
              value={form.cmd}
              onChange={newVal => set('cmd', newVal)}
            />
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
                  <option key={m.id} value={m.ptName || m.name}>{m.displayLabel || m.label || m.name}</option>
                ))}
                {/* Fallback option if the saved monitor is completely missing from the active list */}
                {form.monitor !== 'Por defecto' && !monitors.find(m => m.ptName === form.monitor || m.name === form.monitor) && (
                  <option value={form.monitor}>{form.monitor} (Desconectado/No encontrado)</option>
                )}
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
            flatLayouts={flatLayouts}
            detectedLayout={detectedLayout}
            onRefresh={loadFzStatus}
            validation={validation}
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
            {detectedLayout.isCustom ? (
              <CheckCircle2 size={14} style={{ color: 'var(--success)' }} />
            ) : (
              <AlertCircle size={14} style={{ color: 'var(--warning)' }} />
            )}
            <span className="fz-indicator-text">
              {detectedLayout.isCustom ? (
                <>Layout activo: <strong>{detectedLayout.name}</strong></>
              ) : (
                <>Detectado layout por defecto: <strong>{detectedLayout.name}</strong>. Se recomienda asignar uno personalizado.</>
              )}
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
function FancyZonesVisualizer({ form, set, availableLayouts, flatLayouts, detectedLayout, onRefresh, validation }) {
  // Obtener el layout actual
  const currentLayout = flatLayouts.find(l => l.uuid === form.fancyzone_uuid) || null

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
    const layout = flatLayouts.find(l => l.uuid === uuid);
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
      <div className="fz-header">
        <Field label="Layout de FancyZones">
          <div className="fz-select-row">
            <select
              value={form.fancyzone_uuid || "Ninguna"}
              onChange={e => handleLayoutChange(e.target.value)}
              className={isActiveLayout ? 'fz-select-active' : ''}
            >
              <option value="Ninguna">Ninguno / Libre</option>
              
              {availableLayouts.custom.length > 0 && (
                <optgroup label="Diseños Personalizados">
                  {availableLayouts.custom.map(l => (
                    <option key={l.uuid} value={l.uuid}>
                      {l.name} {detectedLayout?.uuid === l.uuid ? '✓ ACTIVO' : ''}
                    </option>
                  ))}
                </optgroup>
              )}

              {availableLayouts.templates.length > 0 && (
                <optgroup label="Plantillas de FancyZones">
                  {availableLayouts.templates.map(l => (
                    <option key={l.uuid} value={l.uuid}>
                      {l.name} (Plantilla) {detectedLayout?.uuid === l.uuid ? '✓ ACTIVO' : ''}
                    </option>
                  ))}
                </optgroup>
              )}
            </select>
            {isActiveLayout && (
              <span className="fz-active-badge">ACTIVO</span>
            )}
          </div>
        </Field>

        {form.fancyzone && form.fancyzone !== "Ninguna" && (
          <span className="fz-status-text">
            📍 {form.fancyzone}
          </span>
        )}
      </div>

      {currentLayout && form.fancyzone_uuid && (
        <div className="fz-render-container" style={{
          width: '100%',
          height: '180px',
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
          {currentLayout.info ? (
            renderZones(currentLayout.info, activeZoneIdx, handleZoneClick)
          ) : (
            <div className="fz-no-preview">
              <AlertCircle size={24} />
              <p>Previsualización no disponible para plantillas estándar</p>
              <span>El anclaje funcionará, pero no podemos dibujar las zonas</span>
            </div>
          )}
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

      {currentLayout && currentLayout.isCustom && validation?.missingLayouts?.includes((form.fancyzone_uuid || '').replace(/\{|\}/g, '').toLowerCase()) && (
        <div className="fz-warning-bar" style={{ background: 'rgba(16, 185, 129, 0.15)', borderColor: 'rgba(16, 185, 129, 0.3)' }}>
          <AlertCircle size={14} color="#10b981" />
          <span style={{ color: '#10b981' }}>
            Este diseño existe en el workspace pero <strong>no está en este PC</strong>.
          </span>
          <button
            className="fz-warning-btn"
            style={{ background: '#10b981', color: '#000', border: 'none', fontWeight: 'bold' }}
            onClick={async () => {
              const res = await bridge.syncWorkspaceLayouts([form.fancyzone_uuid]);
              if (res?.success) {
                // Background poll will catch it up
                loadFzStatus();
              }
            }}
          >
            Importar
          </button>
        </div>
      )}

      {currentLayout && !currentLayout.isCustom && (
        <div className="fz-warning-bar template-warning">
          <AlertCircle size={14} />
          <span>
            Estás usando una <strong>plantilla estándar</strong>. 
            Para un control total, crea un "Layout Personalizado" en FancyZones.
          </span>
        </div>
      )}
    </div>
  )
}


// ── Tab Manager Component ──────────────────────────────────────────────
const TAB_SEPARATOR = '--- NUEVA PESTAÑA ---'

function TabManager({ type, value, onChange }) {
  const [draggingIdx, setDraggingIdx] = useState(null)

  const tabs = useMemo(() => {
    if (value === null || value === undefined) return []
    return value.split(TAB_SEPARATOR).map(t => t.trim())
  }, [value])

  const updateTabs = (newTabs) => {
    if (newTabs.length === 0) {
      onChange(null)
    } else {
      onChange(newTabs.join(` ${TAB_SEPARATOR} `))
    }
  }

  const handleAdd = () => {
    updateTabs([...tabs, ''])
  }

  const handleRemove = (idx) => {
    const next = [...tabs]
    next.splice(idx, 1)
    updateTabs(next)
  }

  const handleChange = (idx, val) => {
    const next = [...tabs]
    next[idx] = val
    updateTabs(next)
  }

  const handleDragStart = (e, idx) => {
    setDraggingIdx(idx)
    e.dataTransfer.effectAllowed = 'move'
    // Set a ghost image or just use default
    const row = e.target.closest('.tab-item-row')
    if (row) row.classList.add('dragging')
  }

  const handleDragOver = (e, idx) => {
    e.preventDefault()
    if (draggingIdx === null || draggingIdx === idx) return

    const next = [...tabs]
    const [movedItem] = next.splice(draggingIdx, 1)
    next.splice(idx, 0, movedItem)
    
    // We update local order during drag for smooth feel
    updateTabs(next)
    setDraggingIdx(idx)
  }

  const handleDragEnd = (e) => {
    setDraggingIdx(null)
    const row = e.target.closest('.tab-item-row')
    if (row) row.classList.remove('dragging')
  }

  const label = type === 'url' ? 'URLs Adicionales' : 'Pestañas de Terminal'
  const placeholder = type === 'url' ? 'https://...' : 'Comando o script...'

  return (
    <div className="tab-manager">
      <div className="tab-manager-header">
        <label className="dialog-label">{label}</label>
        {tabs.length > 0 && <span className="tab-count-badge">{tabs.length} pestañas</span>}
      </div>

      <div className="tab-list">
        {tabs.length === 0 && (
          <div className="tab-empty-state">
            No hay pestañas adicionales configuradas.
          </div>
        )}

        {tabs.map((tab, idx) => (
          <div 
            key={idx} 
            className={`tab-item-row ${draggingIdx === idx ? 'dragging' : ''}`}
            draggable
            onDragStart={(e) => handleDragStart(e, idx)}
            onDragOver={(e) => handleDragOver(e, idx)}
            onDragEnd={handleDragEnd}
          >
            <div className="tab-drag-handle">
              <GripVertical size={14} />
            </div>
            <div className="tab-index-badge">{idx + 1}</div>
            <input
              value={tab}
              onChange={e => handleChange(idx, e.target.value)}
              placeholder={placeholder}
              className="tab-input"
            />
            <button 
              className="tab-delete-btn" 
              onClick={() => handleRemove(idx)}
              type="button"
              title="Eliminar pestaña"
            >
              <Trash2 size={14} />
            </button>
          </div>
        ))}
      </div>

      <button className="tab-add-btn" onClick={handleAdd} type="button">
        <Plus size={16} /> Añadir nueva pestaña
      </button>
    </div>
  )
}
