import { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { FolderOpen, X, Globe, Terminal, Code2, FileCode, MonitorSmartphone, Cog, ArrowLeft, RotateCw, AlertCircle, CheckCircle2, Plus, Trash2, GripVertical, ChevronDown } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import { renderZones } from '../../utils/fzUtils.jsx'
import { ErrorBoundary } from '../ErrorBoundary.jsx'
import PremiumSelect from '../PremiumSelect.jsx'
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
  monitor: '',
  desktop: '',
  fancyzone: 'Ninguna',
  fancyzone_uuid: '',
  cze_layout_id: '',
  cze_zone_index: null,
  delay: '0',
  is_enabled: true
}

export default function ItemDialog({ category, index, item, validation, fzSyncEnabled, czeActiveLayouts, currentDesktopId, hotkeys, categories, onSave, onClose }) {
  // Wizard state: 1 = Type selection, 2 = Form details
  const [step, setStep] = useState(item ? 2 : 1)
  // Ensure we don't pick up null values from item that might break the state
  const [form, setForm] = useState({ 
    ...DEFAULT_ITEM, 
    ...(item || {}),
    desktop: item?.desktop || '',
    monitor: item?.monitor || ''
  })

  // Data states
  const [fzStatus, setFzStatus] = useState(null)    // Active FZ layout detection — only when fzSyncEnabled
  const [fzLayouts, setFzLayouts] = useState([])    // FZ layout list — always loaded (independent of sync toggle)
  const [czeLayouts, setCzeLayouts] = useState([])  // CZE layout list — always loaded
  const [rawMonitors, setRawMonitors] = useState([]) // Monitors — always loaded
  const [rawDesktops, setRawDesktops] = useState([]) // Desktops — always loaded
  const monitors = useMemo(() => rawMonitors, [rawMonitors])
  const desktops = useMemo(() => rawDesktops, [rawDesktops])
  const [syncState, setSyncState] = useState('idle') // 'idle' | 'syncing' | 'synced' | 'error'
  const [detectedLayout, setDetectedLayout] = useState(null)

  // Always load monitors + desktops (never gated by fzSyncEnabled)
  const loadRawEnv = useCallback(async () => {
    try {
      const [mons, dks] = await Promise.all([bridge.listMonitors(), bridge.listDesktops()])
      if (Array.isArray(mons)) setRawMonitors(mons)
      if (Array.isArray(dks)) setRawDesktops(dks)
    } catch (err) {
      console.error("Error loading monitors/desktops:", err)
    }
  }, [])

  // Always load FZ layout list (never gated by fzSyncEnabled — FZ layouts are always selectable)
  const loadFzLayouts = useCallback(async () => {
    try {
      const layouts = await bridge.listFancyZones()
      if (Array.isArray(layouts)) setFzLayouts(layouts)
    } catch (_) {
      setFzLayouts([]) // FZ not installed or files missing — silently show empty
    }
  }, [])

  // Load CZE layouts (always)
  const loadCzeLayouts = useCallback(async () => {
    try {
      const res = await bridge.czeGetLayouts()
      if (res?.layouts) setCzeLayouts(res.layouts)
    } catch (err) {
      console.error("Error loading CZE layouts:", err)
    }
  }, [])

  // Load FZ active-layout detection status — ONLY when fzSyncEnabled
  // (reads applied-layouts.json and detects which layout is active per monitor+desktop)
  const loadFzStatus = useCallback(async () => {
    if (!fzSyncEnabled) {
      setSyncState('idle')
      return
    }
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
  }, [fzSyncEnabled])

  // On mount: load everything. When fzSyncEnabled changes: reload all.
  useEffect(() => {
    loadRawEnv()
    loadFzLayouts()
    loadCzeLayouts()
    if (fzSyncEnabled) loadFzStatus()
    else setFzStatus(null) // Clear stale active-layout data when sync is disabled
  }, [fzSyncEnabled, loadFzStatus, loadCzeLayouts, loadRawEnv, loadFzLayouts])

  // Default assignments for NEW items
  useEffect(() => {
    if (index === -1) {
      setForm(f => {
        let changed = false;
        const updates = { ...f };
        if (!f.monitor && monitors.length > 0) {
          const primary = monitors.find(m => m.isPrimary) || monitors[0];
          if (primary) { updates.monitor = primary.ptName || primary.name; changed = true; }
        }
        if (!f.desktop && desktops.length > 0) {
          const current = desktops.find(d => d.isCurrent) || desktops[0];
          if (current) { updates.desktop = current.name; changed = true; }
        }
        return changed ? updates : f;
      });
    }
  }, [index, monitors, desktops])

  // Background polling: refresh active-layout status + env periodically
  useEffect(() => {
    const timer = setInterval(() => {
      loadRawEnv()
      loadFzLayouts()
      loadFzStatus()
    }, 8000)
    return () => clearInterval(timer)
  }, [loadRawEnv, loadFzLayouts, loadFzStatus])



  // Layouts: always expose BOTH CZE (native) and FZ layouts regardless of fzSyncEnabled.
  const availableLayouts = useMemo(() => ({
    native: czeLayouts.map(l => ({
      uuid: l.id,
      name: l.name,
      isCustom: !l.isTemplate,
      isNative: true,
      isTemplate: l.isTemplate,
      info: { 
        type: 'canvas', 
        zones: (l.zones || l.Zones || []).map(z => ({
          x: z.x ?? z.X ?? 0,
          y: z.y ?? z.Y ?? 0,
          w: z.w ?? z.W ?? 10000,
          h: z.h ?? z.H ?? 10000
        })),
        spacing: l.spacing || 0, 
        refWidth: 10000, 
        refHeight: 10000 
      }
    })),
    // Filter out FZ layouts if sync is disabled — show only native CZE layouts
    custom: fzSyncEnabled ? fzLayouts.filter(l => l.isCustom).map(l => ({ ...l })) : [],
    templates: fzSyncEnabled ? fzLayouts.filter(l => !l.isCustom).map(l => ({ ...l })) : []
  }), [czeLayouts, fzLayouts, fzSyncEnabled])

  // flatLayouts: all layouts together — no fzSyncEnabled gate
  const flatLayouts = useMemo(() => [
    ...(availableLayouts.native || []),
    ...(availableLayouts.custom || []),
    ...(availableLayouts.templates || [])
  ], [availableLayouts])

  function set(key, value) {
    setForm(f => ({ ...f, [key]: value }))
  }

  // ── Auto-detect active layout when monitor/desktop changes ──────────
  useEffect(() => {
    if (!form.monitor || form.monitor === 'Por defecto') {
      setDetectedLayout(null)
      return
    }

    const monitor = monitors.find(m =>
      (m.ptName && m.ptName === form.monitor) ||
      (m.name && m.name === form.monitor) ||
      (m.label && m.label === form.monitor) ||
      String(m.id) === String(form.monitor) ||
      (typeof m.displayLabel === 'string' && (m.displayLabel === form.monitor || m.displayLabel.replace(' ★', '').trim() === form.monitor)) ||
      (m.name && typeof form.monitor === 'string' && m.name.includes(form.monitor)) ||
      (m.label && typeof form.monitor === 'string' && m.label.includes(form.monitor))
    )
    if (!monitor) {
      setDetectedLayout(null)
      return
    }

    const desktop = desktops.find(d => d.name === form.desktop)
    const desktopId = desktop?.id

    // ── PATH A: FancyZones Sync is ENABLED ──
    if (fzSyncEnabled && fzStatus) {
      const entries = fzStatus.entries || []
      let match = entries.find(e => {
        const monMatch = e.monitorId === monitor.id ||
          e.monitorPtName === monitor.ptName ||
          e.monitorPtInstance === monitor.ptInstance
        const dkMatch = desktopId ? (e.desktopId === desktopId) : e.desktopIsCurrent
        return monMatch && dkMatch
      })

      if (!match?.activeLayoutUuid && desktopId) {
        match = entries.find(e => {
          const monMatch = e.monitorId === monitor.id ||
            e.monitorPtName === monitor.ptName ||
            e.monitorPtInstance === monitor.ptInstance
          return monMatch && e.activeLayoutUuid
        })
      }

      if (match?.activeLayoutUuid && match.activeLayout) {
        const layoutInfo = match.activeLayout;
        setDetectedLayout({
          uuid: layoutInfo.uuid,
          name: layoutInfo.name,
          isCustom: layoutInfo.isCustom,
          info: layoutInfo.info || layoutInfo, // Capturamos la info para la previsualización
          monitorLabel: match.monitorLabel,
          desktopName: match.desktopName
        })

        if (layoutInfo.isCustom) {
          setForm(f => (f.fancyzone_uuid ? f : { ...f, fancyzone_uuid: layoutInfo.uuid, fancyzone: `${layoutInfo.name} - Zona 1` }))
        }
        return
      }
    }

    // ── PATH B: CustomZoneEngine (Fallback/Sync Disabled) ──
    if (!fzSyncEnabled && czeActiveLayouts && monitor.ptInstance) {
      // Si el escritorio es "Por defecto", usamos el ID del escritorio actual detectado por el bridge
      const dId = desktopId || currentDesktopId || "00000000-0000-0000-0000-000000000000"
      
      // Normalized as per ActiveLayoutMap.cs: normalizedPt|desktopId (lowercase)
      const normPt = (monitor.ptInstance || "").trim().replace(/^\{|\}$/g, '').toLowerCase()
      if (!normPt) {
        setDetectedLayout(null)
        return
      }
      const key = `${normPt}|${dId.toLowerCase()}`
      const activeLayoutId = czeActiveLayouts[key]

      if (activeLayoutId) {
        const layout = availableLayouts.native.find(l => String(l.uuid).toLowerCase() === String(activeLayoutId).toLowerCase())
        if (layout) {
          setDetectedLayout({
            uuid: layout.uuid,
            name: layout.name,
            isCustom: layout.isCustom,
            isNative: true,
            isTemplate: layout.isTemplate,
            info: layout.info, // Añadido para que renderZones funcione
            monitorLabel: monitor.displayLabel || monitor.name,
            desktopName: form.desktop
          })
          
          setForm(f => (f.fancyzone_uuid ? f : { ...f, fancyzone_uuid: layout.uuid, fancyzone: `${layout.name} - Zona 1` }))
          return
        }
      }
    }

    setDetectedLayout(null)
  }, [form.monitor, form.desktop, fzSyncEnabled, fzStatus, czeActiveLayouts, currentDesktopId, monitors, desktops, availableLayouts.native])

  // Trigger refresh when monitor/desktop changes
  const handleEnvChange = (key, val) => {
    set(key, val)
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
              <PremiumSelect 
                value={form.browser || 'default'} 
                options={BROWSERS}
                onChange={val => {
                  const b = BROWSERS.find(br => br.value === val)
                  set('browser', val); set('browser_display', b?.label || val)
                }}
              />
            </Field>
          )}

          {/* Monitor & Desktop */}
          <div className="field-row">
            <Field label="Monitor">
              <PremiumSelect 
                value={form.monitor} 
                options={[
                  ...monitors.map(m => ({ value: m.ptName || m.name, label: m.displayLabel || m.label || m.name })),
                  ...(form.monitor && !monitors.find(m => m.ptName === form.monitor || m.name === form.monitor || m.displayLabel === form.monitor || m.label === form.monitor) 
                    ? [{ value: form.monitor, label: `${form.monitor} (Desconectado)` }] : [])
                ]}
                onChange={val => handleEnvChange('monitor', val)}
              />
            </Field>
            <Field label="Escritorio Virtual">
              <PremiumSelect 
                value={form.desktop} 
                options={desktops.map(dk => ({ value: dk.name, label: `${dk.name}${dk.isCurrent ? ' (Actual)' : ''}` }))}
                onChange={val => handleEnvChange('desktop', val)}
              />
            </Field>
          </div>

          {/* Active layout detection indicator */}
          {form.monitor && (
            <ActiveLayoutIndicator
              fzStatus={fzStatus}
              syncState={syncState}
              onRefresh={loadFzStatus}
              fzSyncEnabled={fzSyncEnabled}
            />
          )}

          {/* Layout Interaction - Conditional Component */}
          <ErrorBoundary>
            {fzSyncEnabled ? (
              <FancyZonesVisualizer
                form={form}
                set={set}
                availableLayouts={availableLayouts}
                flatLayouts={flatLayouts}
                detectedLayout={detectedLayout}
                onRefresh={loadFzStatus}
                validation={validation}
                categories={categories}
                currentCategory={category}
                currentIndex={index}
              />
            ) : (
              <CzeActiveVisualizer
                form={form}
                set={set}
                detectedLayout={detectedLayout}
                hotkeys={hotkeys}
                categories={categories}
                currentCategory={category}
                currentIndex={index}
                validation={validation}
                onRefresh={loadFzStatus}
              />
            )}
          </ErrorBoundary>

          {/* Delay */}
          <Field label="Retardo antes de lanzar (Milisegundos)">
            <input type="number" min="0" value={form.delay} onChange={e => set('delay', e.target.value)} style={{ maxWidth: 150 }} />
          </Field>

          {/* Status Toggle */}
          <div style={{ 
            display: 'flex', 
            justifyContent: 'space-between', 
            alignItems: 'center', 
            padding: '12px 14px',
            background: form.is_enabled ? 'rgba(0, 230, 118, 0.05)' : 'rgba(255, 255, 255, 0.03)',
            borderRadius: '12px',
            marginTop: '24px',
            border: form.is_enabled ? '1px solid rgba(0, 230, 118, 0.2)' : '1px solid rgba(255, 255, 255, 0.08)',
            transition: 'all 0.2s ease'
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <div style={{ 
                width: '8px', 
                height: '8px', 
                borderRadius: '50%', 
                background: form.is_enabled ? '#00e676' : '#9e9e9e',
                boxShadow: form.is_enabled ? '0 0 8px #00e676' : 'none'
              }} />
              <span style={{ fontSize: '14px', fontWeight: 700, color: form.is_enabled ? 'white' : 'var(--fz-text-muted)' }}>
                {form.is_enabled ? 'Ventana Habilitada' : 'Ventana Deshabilitada'}
              </span>
            </div>
            <button 
              type="button"
              onClick={() => set('is_enabled', !form.is_enabled)}
              style={{
                background: form.is_enabled ? '#00e676' : 'rgba(255, 255, 255, 0.1)',
                border: 'none',
                width: '40px',
                height: '22px',
                borderRadius: '11px',
                position: 'relative',
                cursor: 'pointer',
                transition: 'background 0.2s ease'
              }}
            >
              <div style={{
                position: 'absolute',
                top: '2px',
                left: form.is_enabled ? '20px' : '2px',
                width: '18px',
                height: '18px',
                background: 'white',
                borderRadius: '50%',
                transition: 'left 0.2s ease',
                boxShadow: '0 1px 3px rgba(0,0,0,0.3)'
              }} />
            </button>
          </div>
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
function ActiveLayoutIndicator({ fzStatus, syncState, onRefresh, fzSyncEnabled }) {
  if (!fzSyncEnabled) return null;

  const isRunning = fzStatus?.isFzRunning;
  const detectedLayout = fzStatus?.entries?.length > 0 ? fzStatus.entries[0].activeLayout : null;

  return (
    <div className={`fz-active-indicator ${!isRunning ? 'warning-critical' : ''}`}>
      <div className="fz-active-indicator-content">
        {!isRunning ? (
          <>
            <AlertCircle size={14} style={{ color: 'var(--cat-error)' }} />
            <span className="fz-indicator-text" style={{ color: 'var(--cat-error)' }}>
              <strong>FancyZones no está ejecutándose.</strong> La sincronización no funcionará hasta que abras PowerToys.
            </span>
          </>
        ) : syncState === 'syncing' ? (
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
            </span>
          </>
        ) : (
          <>
            <AlertCircle size={14} style={{ color: 'var(--warning)' }} />
            <span className="fz-indicator-text">No se detectó un layout activo en FancyZones</span>
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

// ── Componente A: Sincronización con FancyZones ACTIVADA ───────────────────
// Permite elegir layouts, sincronizar si hay desajuste, etc.
function FancyZonesVisualizer({ 
  form, set, availableLayouts, flatLayouts, detectedLayout, 
  onRefresh, validation, categories, currentCategory, currentIndex 
}) {
  const currentLayout = flatLayouts.find(l => l.uuid === form.fancyzone_uuid) || null

  const occupancyMap = useMemo(() => {
    if (!form.monitor || !currentLayout) return {};
    const map = {};
    const apps = categories[currentCategory] || [];
    apps.forEach((app, idx) => {
      if (!app) return;
      if (idx === currentIndex) return;
      if (app.monitor === form.monitor && app.desktop === form.desktop && app.fancyzone_uuid === form.fancyzone_uuid && app.is_enabled !== false) {
        const zoneMatch = String(app.fancyzone || "").match(/Zona (\d+)/);
        if (zoneMatch) {
          const zIdx = parseInt(zoneMatch[1]) - 1;
          const appPath = String(app.path || "");
          const appName = appPath ? appPath.split(/[\\/]/).pop().replace('.exe', '') : 'App';
          if (!map[zIdx]) map[zIdx] = appName;
          else map[zIdx] += `, ${appName}`;
        }
      }
    });
    return map;
  }, [form.monitor, form.desktop, form.fancyzone_uuid, currentLayout, categories, currentCategory, currentIndex])

  const handleZoneClick = (idx) => {
    if (!currentLayout) return;
    set('fancyzone', `${currentLayout.name} - Zona ${idx + 1}`);
    set('fancyzone_uuid', currentLayout.uuid);
    
    // Sync CZE specific fields if it's a native layout
    if (currentLayout.isNative) {
      set('cze_layout_id', currentLayout.uuid);
      set('cze_zone_index', idx);
    } else {
      set('cze_layout_id', '');
      set('cze_zone_index', null);
    }
  }

  const handleLayoutChange = (uuid) => {
    if (uuid === "Ninguna") {
      set('fancyzone', "Ninguna");
      set('fancyzone_uuid', "");
      set('cze_layout_id', "");
      set('cze_zone_index', null);
      return;
    }
    const layout = flatLayouts.find(l => l.uuid === uuid);
    if (layout) {
      set('fancyzone_uuid', layout.uuid);
      set('fancyzone', `${layout.name} - Zona 1`);
      
      if (layout.isNative) {
        set('cze_layout_id', layout.uuid);
        set('cze_zone_index', 0);
      } else {
        set('cze_layout_id', "");
        set('cze_zone_index', null);
      }
    }
  }

  const activeZoneIdx = useMemo(() => {
    if (form.fancyzone === "Ninguna" || !form.fancyzone) return -1;
    const parts = form.fancyzone.split("Zona ");
    if (parts.length > 1) return parseInt(parts[1]) - 1;
    return -1;
  }, [form.fancyzone])

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
              {availableLayouts.native.length > 0 && (
                <optgroup label="Diseños Propios (CZE)">
                  {availableLayouts.native.map(l => (
                    <option key={l.uuid} value={l.uuid}>{l.name}</option>
                  ))}
                </optgroup>
              )}
              {availableLayouts.custom.length > 0 && (
                <optgroup label="Diseños Personalizados FancyZones">
                  {availableLayouts.custom.map(l => (
                    <option key={l.uuid} value={l.uuid}>{l.name} {detectedLayout?.uuid === l.uuid ? '✓ ACTIVO' : ''}</option>
                  ))}
                </optgroup>
              )}
              {availableLayouts.templates.length > 0 && (
                <optgroup label="Plantillas de FancyZones">
                  {availableLayouts.templates.map(l => (
                    <option key={l.uuid} value={l.uuid}>{l.name} (Plantilla) {detectedLayout?.uuid === l.uuid ? '✓ ACTIVO' : ''}</option>
                  ))}
                </optgroup>
              )}
            </select>
            {isActiveLayout && <span className="fz-active-badge">✓ DISEÑO ACTIVO</span>}
          </div>
        </Field>
      </div>

      {currentLayout && form.fancyzone_uuid && (
        <div className="fz-render-container" style={{
          width: '100%', height: '180px', background: 'rgba(0,0,0,0.45)', borderRadius: '12px',
          border: isActiveLayout ? '1px solid rgba(0, 230, 118, 0.3)' : '1px solid rgba(255,255,255,0.08)',
          overflow: 'hidden', position: 'relative', padding: '12px', boxSizing: 'border-box',
          boxShadow: isActiveLayout ? 'inset 0 0 30px rgba(0,0,0,0.6), 0 0 15px rgba(0, 230, 118, 0.1)' : 'inset 0 0 30px rgba(0,0,0,0.6)'
        }}>
          {currentLayout.info ? renderZones(currentLayout.info, activeZoneIdx, handleZoneClick, occupancyMap) : (
            <div className="fz-no-preview">
              <AlertCircle size={24} />
              <p>Previsualización no disponible para plantillas estándar</p>
            </div>
          )}
        </div>
      )}

      {!isActiveLayout && form.fancyzone_uuid && detectedLayout && (
        <div className="fz-warning-bar">
          <AlertCircle size={14} />
          <span>Este activo en {detectedLayout.isNative ? 'CZE' : 'FancyZones'} es: <strong>{detectedLayout.name}</strong></span>
          <button className="fz-warning-btn" onClick={() => handleLayoutChange(detectedLayout.uuid)}>Usar activo</button>
        </div>
      )}
    </div>
  )
}

// ── Componente B: Modo Nativo (Sincronización DESACTIVADA) ───────────────────
// Fijado al layout activo del monitor. Solo permite elegir zona.
function CzeActiveVisualizer({ 
  form, set, detectedLayout, hotkeys, categories, 
  currentCategory, currentIndex, validation, onRefresh 
}) {
  const currentLayout = detectedLayout;

  // Actualizar automáticamente si el layout configurado no es el detectado
  useEffect(() => {
    if (detectedLayout && form.fancyzone_uuid !== detectedLayout.uuid) {
      set('fancyzone_uuid', detectedLayout.uuid);
      set('cze_layout_id', detectedLayout.uuid);
      
      // No reseteamos la zona forzadamente para no molestar si el usuario está eligiendo,
      // pero si no hay nada configurado, ponemos Zona 1
      if (!form.fancyzone || form.fancyzone === "Ninguna") {
        set('fancyzone', `${detectedLayout.name} - Zona 1`);
        set('cze_zone_index', 0);
      }
    }
  }, [detectedLayout, form.fancyzone_uuid, form.fancyzone, set])

  const occupancyMap = useMemo(() => {
    if (!form.monitor || !currentLayout) return {};
    const map = {};
    const apps = categories[currentCategory] || [];
    apps.forEach((app, idx) => {
      if (!app) return;
      if (idx === currentIndex) return;
      if (app.monitor === form.monitor && app.desktop === form.desktop && app.fancyzone_uuid === currentLayout.uuid && app.is_enabled !== false) {
        const zoneMatch = String(app.fancyzone || "").match(/Zona (\d+)/);
        if (zoneMatch) {
          const zIdx = parseInt(zoneMatch[1]) - 1;
          const appPath = String(app.path || "");
          const appName = appPath ? appPath.split(/[\\/]/).pop().replace('.exe', '') : 'App';
          if (!map[zIdx]) map[zIdx] = appName;
          else map[zIdx] += `, ${appName}`;
        }
      }
    });
    return map;
  }, [form.monitor, form.desktop, currentLayout, categories, currentCategory, currentIndex])

  const handleZoneClick = (idx) => {
    if (!currentLayout) return;
    set('fancyzone', `${currentLayout.name} - Zona ${idx + 1}`);
    set('fancyzone_uuid', currentLayout.uuid);
    set('cze_layout_id', currentLayout.uuid);
    set('cze_zone_index', idx);
  }

  const activeZoneIdx = useMemo(() => {
    if (form.fancyzone === "Ninguna" || !form.fancyzone) return -1;
    const parts = form.fancyzone.split("Zona ");
    if (parts.length > 1) return parseInt(parts[1]) - 1;
    return -1;
  }, [form.fancyzone])

  return (
    <div className="fz-visualizer cze-native-mode">
      <div className="fz-header">
        <Field label="Layout Activo (Lectura)">
          <div className="fz-select-row">
            {detectedLayout ? (
               <div className="fz-layout-badge active">
                 <CheckCircle2 size={14} />
                 <span><strong>{detectedLayout.name}</strong></span>
               </div>
            ) : (
              <div className="fz-layout-badge missing">
                <AlertCircle size={14} />
                <span>Sin diseño activo</span>
              </div>
            )}
          </div>
        </Field>
      </div>

      {currentLayout ? (
        <div className="fz-render-container" style={{
          width: '100%', height: '180px', background: 'rgba(0,0,0,0.5)', borderRadius: '12px',
          border: '1px solid var(--accent-low)', overflow: 'hidden', position: 'relative',
          padding: '12px', boxSizing: 'border-box', boxShadow: 'inset 0 0 30px rgba(0,0,0,0.7)'
        }}>
          {currentLayout.info ? renderZones(currentLayout.info, activeZoneIdx, handleZoneClick, occupancyMap) : (
            <div className="fz-no-preview">
              <AlertCircle size={24} />
              <p>Previsualización no disponible</p>
            </div>
          )}
        </div>
      ) : (
        <div className="fz-warning-bar critical">
          <AlertCircle size={20} />
          <div style={{ flex: 1 }}>
            <span>No hay un diseño activo para este monitor y escritorio.</span>
            <div style={{ fontSize: '11px', marginTop: '4px', opacity: 0.9 }}>
              Usa <strong>{hotkeys?.open_zone_editor || 'Ctrl + Espacio'}</strong> para abrir el Gestor y asignar uno.
            </div>
          </div>
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
                // El polling de fondo lo actualizará, pero forzamos uno ahora
                onRefresh?.();
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
    return String(value).split(TAB_SEPARATOR).map(t => t.trim())
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
