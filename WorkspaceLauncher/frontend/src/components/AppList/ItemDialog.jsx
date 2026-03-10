import { useState, useEffect, useCallback } from 'react'
import { FolderOpen, X, Globe, Terminal, Code2, FileCode, MonitorSmartphone, Cog } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import './ItemDialog.css'

const ITEM_TYPES = [
  { value: 'exe',        label: 'Ejecutable (.exe)',         icon: Cog },
  { value: 'url',        label: 'URL / Navegador',           icon: Globe },
  { value: 'ide',        label: 'IDE',                       icon: Code2 },
  { value: 'vscode',     label: 'VS Code',                   icon: FileCode },
  { value: 'powershell', label: 'Terminal (PowerShell/WT)',   icon: Terminal },
  { value: 'obsidian',   label: 'Obsidian',                  icon: MonitorSmartphone },
]

const BROWSERS = [
  { value: 'default', label: 'Por defecto del sistema' },
  { value: 'msedge',  label: 'Microsoft Edge' },
  { value: 'chrome',  label: 'Google Chrome' },
  { value: 'firefox', label: 'Mozilla Firefox' },
  { value: 'brave',   label: 'Brave' },
]

const DEFAULT_ITEM = {
  type: 'exe',
  path: '',
  cmd: '',
  ide_cmd: '',
  browser: 'default',
  browser_display: 'Por defecto del sistema',
  monitor: 'Por defecto',
  desktop: 'Por defecto',
  fancyzone: 'Ninguna',
  delay: '0',
  fancyzone_uuid: '',
}

export default function ItemDialog({ category, index, item, onSave, onClose }) {
  const [form, setForm] = useState({ ...DEFAULT_ITEM, ...(item || {}) })
  const [desktops, setDesktops] = useState([])
  const [windows, setWindows] = useState([])
  const [loadingDesktops, setLoadingDesktops] = useState(false)
  const [loadingWindows, setLoadingWindows] = useState(false)

  // Load available desktops and windows on dialog open
  useEffect(() => {
    async function loadData() {
      setLoadingDesktops(true)
      setLoadingWindows(true)
      try {
        const dkList = await bridge.listDesktops()
        setDesktops(dkList || [])
      } catch { setDesktops([]) }
      setLoadingDesktops(false)

      try {
        const winList = await bridge.listWindows()
        setWindows(winList || [])
      } catch { setWindows([]) }
      setLoadingWindows(false)
    }
    loadData()
  }, [])

  function set(key, value) {
    setForm(f => ({ ...f, [key]: value }))
  }

  const handleBrowsePath = useCallback(async () => {
    const isExe = form.type === 'exe'
    const filters = isExe
      ? [{ name: 'Ejecutables', extensions: ['exe'] }, { name: 'Todos', extensions: ['*'] }]
      : [{ name: 'Todos', extensions: ['*'] }]
    try {
      const result = await bridge.openFileDialog({
        filters,
        isFolder: form.type === 'ide' || form.type === 'vscode' || form.type === 'powershell' || form.type === 'obsidian',
      })
      if (result) set('path', result)
    } catch (err) {
      console.warn('[ItemDialog] File dialog error:', err)
    }
  }, [form.type])

  function handleSave() {
    if (!form.path.trim()) return
    const out = { ...form }
    if (form.type !== 'url') { delete out.cmd; delete out.browser; delete out.browser_display }
    if (form.type !== 'ide') delete out.ide_cmd
    if (form.type !== 'powershell') { if (!out.cmd) delete out.cmd }
    onSave(out)
  }

  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <div className="dialog-header">
          <h2>{index >= 0 ? 'Editar app' : 'Añadir app'}</h2>
          <span className="dialog-category">{category}</span>
          <button className="dialog-close" onClick={onClose}><X size={16} /></button>
        </div>

        <div className="dialog-body">
          {/* Type */}
          <Field label="Tipo">
            <select value={form.type} onChange={e => set('type', e.target.value)}>
              {ITEM_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </Field>

          {/* Path with file picker */}
          <Field label={form.type === 'url' ? 'URL principal' : 'Ruta'}>
            <div className="field-with-btn">
              <input
                value={form.path}
                onChange={e => set('path', e.target.value)}
                placeholder={form.type === 'url' ? 'https://...' : 'Selecciona con Examinar...'}
              />
              {form.type !== 'url' && (
                <button className="btn-browse" onClick={handleBrowsePath} type="button">
                  <FolderOpen size={14} /> Examinar...
                </button>
              )}
            </div>
          </Field>

          {/* URL: multi-tab cmd */}
          {form.type === 'url' && (
            <Field label="URLs adicionales (separadas por  --- NUEVA PESTAÑA ---)">
              <textarea
                rows={3}
                value={form.cmd || form.path}
                onChange={e => set('cmd', e.target.value)}
                placeholder="url1 --- NUEVA PESTAÑA --- url2"
              />
            </Field>
          )}

          {/* URL: browser */}
          {form.type === 'url' && (
            <Field label="Navegador">
              <select
                value={form.browser || 'default'}
                onChange={e => {
                  const b = BROWSERS.find(br => br.value === e.target.value)
                  set('browser', e.target.value)
                  set('browser_display', b?.label || e.target.value)
                }}
              >
                {BROWSERS.map(b => <option key={b.value} value={b.value}>{b.label}</option>)}
              </select>
            </Field>
          )}

          {/* IDE command */}
          {form.type === 'ide' && (
            <Field label="Comando IDE">
              <input
                value={form.ide_cmd || ''}
                onChange={e => set('ide_cmd', e.target.value)}
                placeholder="antigravity / cursor / etc."
              />
            </Field>
          )}

          {/* PowerShell: cmd */}
          {form.type === 'powershell' && (
            <Field label="Comandos (separados por  --- NUEVA PESTAÑA ---)">
              <textarea
                rows={3}
                value={form.cmd || ''}
                onChange={e => set('cmd', e.target.value)}
                placeholder="npm run dev --- NUEVA PESTAÑA --- npm run db:migrate"
              />
            </Field>
          )}

          {/* Monitor (from open windows list) + Desktop row */}
          <div className="field-row">
            <Field label="Monitor">
              <input
                value={form.monitor}
                onChange={e => set('monitor', e.target.value)}
                placeholder="Pantalla 1 [SDC41B6] / Por defecto"
              />
            </Field>
            <Field label="Escritorio virtual">
              <select
                value={form.desktop}
                onChange={e => set('desktop', e.target.value)}
              >
                <option value="Por defecto">Por defecto</option>
                {loadingDesktops ? (
                  <option disabled>Cargando escritorios...</option>
                ) : (
                  desktops.map((dk, i) => (
                    <option key={dk.id || i} value={dk.name}>{dk.name}</option>
                  ))
                )}
              </select>
            </Field>
          </div>

          {/* FancyZone + UUID row */}
          <div className="field-row">
            <Field label="FancyZone">
              <input
                value={form.fancyzone}
                onChange={e => set('fancyzone', e.target.value)}
                placeholder="Entera - Zona 1 / Ninguna"
              />
            </Field>
            <Field label="UUID de layout">
              <input
                value={form.fancyzone_uuid || ''}
                onChange={e => set('fancyzone_uuid', e.target.value)}
                placeholder="a01ecfa2-3683-47fb-..."
                className="mono"
              />
            </Field>
          </div>

          {/* PiP Window Selector */}
          <Field label="Ventana para PiP / Anclar (opcional)">
            <select
              value={form.pip_window || ''}
              onChange={e => set('pip_window', e.target.value)}
            >
              <option value="">Ninguna</option>
              {loadingWindows ? (
                <option disabled>Cargando ventanas...</option>
              ) : (
                windows.map((w, i) => (
                  <option key={w.hwnd || i} value={w.title}>{w.title}{w.processName ? ` (${w.processName})` : ''}</option>
                ))
              )}
            </select>
          </Field>

          {/* Delay */}
          <Field label="Retardo de lanzamiento (ms)">
            <input
              type="number"
              min="0"
              value={form.delay}
              onChange={e => set('delay', e.target.value)}
              style={{ maxWidth: 140 }}
            />
          </Field>
        </div>

        <div className="dialog-footer">
          <button className="btn-secondary" onClick={onClose}>Cancelar</button>
          <button className="btn-launch" onClick={handleSave} disabled={!form.path.trim()}>
            {index >= 0 ? 'Guardar cambios' : 'Añadir app'}
          </button>
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
