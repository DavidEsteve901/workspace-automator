import { useState, useEffect, useCallback, useMemo } from 'react'
import { FolderOpen, X, Globe, Terminal, Code2, FileCode, MonitorSmartphone, Cog, ArrowLeft } from 'lucide-react'
import { bridge, onEvent, offEvent } from '../../api/bridge.js'
import './ItemDialog.css'

const ITEM_TYPES = [
  { value: 'exe',        label: 'Ejecutable (.exe)',         desc: 'Aplicación nativa de Windows', icon: Cog, color: 'var(--cat-exe)' },
  { value: 'url',        label: 'Web / URL',                 desc: 'Páginas web en el navegador',  icon: Globe, color: 'var(--cat-web)' },
  { value: 'ide',        label: 'IDE Personalizado',         desc: 'IntelliJ, Cursor, Android Studio...', icon: Code2, color: 'var(--cat-ide)' },
  { value: 'vscode',     label: 'VS Code',                   desc: 'Abre un proyecto en VS Code',  icon: FileCode, color: 'var(--cat-ide)' },
  { value: 'powershell', label: 'Terminal',                  desc: 'PowerShell o Windows Terminal',icon: Terminal, color: 'var(--cat-terminal)' },
  { value: 'obsidian',   label: 'Obsidian',                  desc: 'Abre un vault específico',     icon: MonitorSmartphone, color: 'var(--cat-obsidian)' },
]

const BROWSERS = [
  { value: 'default', label: 'Por defecto del sistema' },
  { value: 'msedge',  label: 'Microsoft Edge' },
  { value: 'chrome',  label: 'Google Chrome' },
  { value: 'firefox', label: 'Mozilla Firefox' },
  { value: 'brave',   label: 'Brave' },
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
  
  // Data states
  const [desktops, setDesktops] = useState([])
  const [monitors, setMonitors] = useState([])
  const [layoutsCache, setLayoutsCache] = useState({}) // Datos de FancyZones
  
  // Load initial environment data
  useEffect(() => {
    async function loadData() {
      try {
        const dkList = await bridge.listDesktops()
        setDesktops(dkList || [])
        const mList = await bridge.listMonitors()
        setMonitors(mList || [])
      } catch {}
      
      try {
        const handleUpdate = (data) => {
          if (data && data.fzLayoutsCache) setLayoutsCache(data.fzLayoutsCache);
        };
        onEvent('state_update', handleUpdate);
        bridge.getState(); // Trigger state update to get cache
        return () => offEvent('state_update', handleUpdate);
      } catch (e) {}
    }
    loadData()
  }, [])

  function set(key, value) {
    setForm(f => ({ ...f, [key]: value }))
  }

  const handleBrowsePath = useCallback(async () => {
    const isFolder = ['ide', 'vscode', 'powershell', 'obsidian'].includes(form.type)
    const filters = form.type === 'exe' 
        ? [{ name: 'Ejecutables', extensions: ['exe'] }] 
        : [{ name: 'Todos', extensions: ['*'] }]
        
    try {
      const result = await bridge.openFileDialog({ filters, isFolder })
      if (result) set('path', result)
    } catch (err) {}
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
                <div key={t.value} className="type-card" style={{'--card-color': t.color}} 
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
              <select value={form.monitor} onChange={e => set('monitor', e.target.value)}>
                <option value="Por defecto">Por defecto</option>
                {monitors.map(m => <option key={m.id} value={m.label}>{m.label}</option>)}
              </select>
            </Field>
            <Field label="Escritorio Virtual">
              <select value={form.desktop} onChange={e => set('desktop', e.target.value)}>
                <option value="Por defecto">Por defecto</option>
                {desktops.map((dk, i) => <option key={dk.id || i} value={dk.name}>{dk.name}</option>)}
              </select>
            </Field>
          </div>

          {/* FancyZones Interactive Render */}
          <FancyZonesVisualizer form={form} set={set} layoutsCache={layoutsCache} />

          {/* Delay */}
          <Field label="Retardo antes de lanzar (Milisegundos)">
            <input type="number" min="0" value={form.delay} onChange={e => set('delay', e.target.value)} style={{ maxWidth: 150 }} />
          </Field>
        </div>

        <div className="dialog-footer">
          {!item ? (
            <button className="btn-secondary" onClick={() => setStep(1)}>
               <ArrowLeft size={16}/> Volver
            </button>
          ) : <div></div>}
          
          <div className="footer-right">
            <button className="btn-secondary" onClick={onClose} style={{border: 'none', background: 'transparent'}}>Cancelar</button>
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

// ── Mini-Motor Renderizador de FancyZones ──────────────────────────────────
function FancyZonesVisualizer({ form, set, layoutsCache }) {
  // Convierte el diccionario de PowerToys a una lista para el selector
  const availableLayouts = useMemo(() => {
    return Object.values(layoutsCache || {}).map(l => ({
      uuid: l.uuid,
      name: l.name,
      info: l.info
    }))
  }, [layoutsCache])

  // Obtener el layout actual
  const currentLayout = availableLayouts.find(l => l.uuid === form.fancyzone_uuid) || availableLayouts[0]

  const handleZoneClick = (idx) => {
    if (!currentLayout) return;
    set('fancyzone', `${currentLayout.name} - Zona ${idx + 1}`);
    set('fancyzone_uuid', currentLayout.uuid);
  }

  const handleLayoutChange = (uuid) => {
     if(uuid === "Ninguna") {
         set('fancyzone', "Ninguna");
         set('fancyzone_uuid', "");
         return;
     }
     const layout = availableLayouts.find(l => l.uuid === uuid);
     if(layout) {
         set('fancyzone_uuid', layout.uuid);
         // Resetea a la zona 1 por defecto al cambiar de layout
         set('fancyzone', `${layout.name} - Zona 1`);
     }
  }

  // Parsear el número de zona actual del string "Nombre Layout - Zona X"
  const activeZoneIdx = useMemo(() => {
    if(form.fancyzone === "Ninguna" || !form.fancyzone) return -1;
    const parts = form.fancyzone.split("Zona ");
    if(parts.length > 1) return parseInt(parts[1]) - 1;
    return -1;
  }, [form.fancyzone])

  return (
    <div className="fz-visualizer">
      <div className="fz-header">
         <Field label="Layout de FancyZones">
            <select value={form.fancyzone_uuid || "Ninguna"} onChange={e => handleLayoutChange(e.target.value)} style={{width: '250px'}}>
              <option value="Ninguna">Ninguno / Libre</option>
              {availableLayouts.map(l => <option key={l.uuid} value={l.uuid}>{l.name}</option>)}
            </select>
         </Field>
         {form.fancyzone && form.fancyzone !== "Ninguna" && (
             <span className="fz-status">📍 Asignado: {form.fancyzone}</span>
         )}
      </div>

      {currentLayout && form.fancyzone_uuid && (
        <div className="fz-grid-container">
           {renderZones(currentLayout.info, activeZoneIdx, handleZoneClick)}
        </div>
      )}
    </div>
  )
}

// Lógica para dibujar CSS Grid basado en la configuración real de PowerToys
function renderZones(info, activeIdx, onClick) {
  if (!info) return null;
  const type = info.type || "grid";

  if (type === "grid") {
    // PowerToys guarda la estructura en cell-child-map
    const rowsMap = info["cell-child-map"] || [[0]];
    const rowsPerc = info["rows-percentage"] || [10000];
    const colsPerc = info["columns-percentage"] || [10000];

    // Encontrar posiciones min/max para cada zona (para hacer Grid Spans)
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
      <div style={{...gridStyle, width: '100%', height: '100%', display: 'grid', gap: info.spacing ? '4px' : '0px'}}>
         {Object.entries(zones).map(([zId, span]) => {
            const id = parseInt(zId);
            const isSelected = activeIdx === id;
            return (
              <button 
                key={id}
                className={`fz-zone-btn ${isSelected ? 'selected' : ''}`}
                style={{
                   gridRow: `${span.minR + 1} / ${span.maxR + 2}`,
                   gridColumn: `${span.minC + 1} / ${span.maxC + 2}`
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
            // Canvas values are usually based on a reference width/height
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
                   opacity: 0.8
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
