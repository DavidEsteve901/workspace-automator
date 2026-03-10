import { useState, useEffect, useCallback, useRef } from 'react'
import { Settings, Keyboard, Monitor, Save, Check, Folder, LayoutGrid, RotateCw, X, ChevronRight } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import './ConfigPanel.css'

const HOTKEY_LABELS = {
  cycle_forward: 'Ciclar zona →',
  cycle_backward: 'Ciclar zona ←',
  desktop_cycle_fwd: 'Cambiar escritorio →',
  desktop_cycle_bwd: 'Cambiar escritorio ←',
  util_reload_layouts: 'Recargar layouts FancyZones',
}

export default function ConfigPanel({ hotkeys, pipWatcher, fzCustomPath, fzDetectedPath, onSave }) {
  const [hk, setHk] = useState({ ...hotkeys })
  const [pip, setPip] = useState(pipWatcher)
  const [fzPath, setFzPath] = useState(fzCustomPath || '')
  const [saved, setSaved] = useState(false)
  const [fzModalOpen, setFzModalOpen] = useState(false)

  function handleSave() {
    onSave({ hotkeys: hk, pipWatcherEnabled: pip, fzCustomPath: fzPath })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  async function handlePickPath() {
    const res = await bridge.openFileDialog({ isFolder: true })
    if (res) setFzPath(res)
  }

  function setHotkey(key, value) {
    setHk(h => ({ ...h, [key]: value }))
  }

  return (
    <div className="config-panel">
      <div className="config-header">
        <Settings size={20} className="config-header-icon" />
        <h2>Configuración</h2>
      </div>

      <div className="config-body">
        {/* Zone cycling toggles */}
        <Section title="Cycling de zonas y escritorios" icon={<Monitor size={14} />}>
          <Toggle
            label="Habilitar cycling de zonas"
            value={hk._zone_cycle_enabled}
            onChange={v => setHotkey('_zone_cycle_enabled', v)}
          />
          <Toggle
            label="Habilitar cycling de escritorios virtuales"
            value={hk._desktop_cycle_enabled}
            onChange={v => setHotkey('_desktop_cycle_enabled', v)}
          />
        </Section>

        {/* FancyZones Status Manager */}
        <Section title="FancyZones — Estado y Layouts" icon={<LayoutGrid size={14} />}>
          <button className="fz-status-btn" onClick={() => setFzModalOpen(true)}>
            <div className="fz-status-btn-content">
              <LayoutGrid size={16} />
              <div>
                <span className="fz-status-btn-title">Gestionar Layouts por Monitor</span>
                <span className="fz-status-btn-desc">Ver y cambiar qué layout está activo en cada monitor y escritorio</span>
              </div>
            </div>
            <ChevronRight size={16} className="fz-status-btn-arrow" />
          </button>
        </Section>

        {/* PowerToys path overrides */}
        <Section title="PowerToys / FancyZones Path" icon={<Folder size={14} />}>
          <div className="fz-path-row">
            <input
              className="fz-path-input"
              type="text"
              placeholder="Por defecto: Autodetección"
              value={fzPath}
              onChange={e => setFzPath(e.target.value)}
            />
            <button className="fz-path-btn" onClick={handlePickPath} title="Seleccionar">
              <Folder size={14} />
            </button>
          </div>
          <p className="fz-path-help">
            Modifica esta ruta solo si PowerToys está instalado en una ubicación no estándar.
          </p>
          {fzDetectedPath && (
            <div className="fz-detected-path">
              <span>Detectado:</span> <code>{fzDetectedPath}</code>
            </div>
          )}
        </Section>

        <Section title="Sistema" icon={<Monitor size={14} />}>
          <Toggle
            label="Anclar ventanas Picture-in-Picture a todos los escritorios"
            value={pip}
            onChange={setPip}
          />
          <Toggle
            label="Mostrar consola de depuración"
            value={hk.show_system_console}
            onChange={v => setHotkey('show_system_console', v)}
          />
        </Section>

        {/* Hotkeys — keybinding recorder */}
        <Section title="Atajos de teclado / ratón" icon={<Keyboard size={14} />}>
          <div className="hotkey-table">
            {Object.entries(HOTKEY_LABELS).map(([key, label]) => (
              <div key={key} className="hotkey-row">
                <span className="hotkey-label">{label}</span>
                <KeybindRecorder
                  value={hk[key] || ''}
                  onChange={val => setHotkey(key, val)}
                />
              </div>
            ))}
          </div>
        </Section>
      </div>

      <div className="config-footer">
        {saved && (
          <span className="config-saved">
            <Check size={14} /> Guardado
          </span>
        )}
        <button className="btn-launch" onClick={handleSave}>
          <Save size={14} /> Guardar configuración
        </button>
      </div>

      {fzModalOpen && (
        <FzStatusModal onClose={() => setFzModalOpen(false)} />
      )}
    </div>
  )
}

// ── FancyZones Status Modal ──────────────────────────────────────────────
function FzStatusModal({ onClose }) {
  const [fzStatus, setFzStatus] = useState(null)
  const [loading, setLoading] = useState(true)
  const [changingEntry, setChangingEntry] = useState(null) // entry being changed
  const [savingMsg, setSavingMsg] = useState('')

  const loadStatus = useCallback(async () => {
    setLoading(true)
    try {
      const data = await bridge.getFzStatus()
      setFzStatus(data)
    } catch (err) {
      console.error("Error loading FZ status:", err)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    loadStatus()
  }, [loadStatus])

  const handleChangeLayout = async (entry, newLayoutUuid) => {
    setChangingEntry(entry)
    try {
      const res = await bridge.changeLayoutAssignment(
        entry.monitorPtInstance,
        entry.monitorPtName,
        entry.desktopId,
        newLayoutUuid
      )
      if (res?.success) {
        setSavingMsg('✓ Layout cambiado correctamente')
        setTimeout(() => setSavingMsg(''), 2000)
        await loadStatus()
      } else {
        setSavingMsg('✗ Error al cambiar layout')
        setTimeout(() => setSavingMsg(''), 3000)
      }
    } catch (err) {
      setSavingMsg('✗ Error: ' + (err.message || 'desconocido'))
      setTimeout(() => setSavingMsg(''), 3000)
    }
    setChangingEntry(null)
  }

  // Group entries by monitor
  const groupedEntries = {}
  if (fzStatus?.entries) {
    for (const entry of fzStatus.entries) {
      if (!groupedEntries[entry.monitorLabel]) {
        groupedEntries[entry.monitorLabel] = []
      }
      groupedEntries[entry.monitorLabel].push(entry)
    }
  }

  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="dialog fz-modal">
        <div className="dialog-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--accent)' }}>
            <LayoutGrid size={20} />
            <h2>Estado de FancyZones</h2>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <button
              className="btn-icon-small fz-modal-refresh"
              onClick={loadStatus}
              title="Sincronizar"
              disabled={loading}
            >
              <RotateCw size={14} className={loading ? 'fz-spin' : ''} />
            </button>
            <button className="dialog-close" onClick={onClose}><X size={20} /></button>
          </div>
        </div>

        <div className="fz-modal-body">
          {loading && !fzStatus ? (
            <div className="fz-modal-loading">
              <RotateCw size={20} className="fz-spin" />
              <span>Leyendo configuración de FancyZones...</span>
            </div>
          ) : Object.keys(groupedEntries).length === 0 ? (
            <div className="fz-modal-empty">
              <Monitor size={32} style={{ opacity: 0.3 }} />
              <p>No se encontraron monitores o layouts aplicados</p>
            </div>
          ) : (
            <div className="fz-modal-monitors">
              {Object.entries(groupedEntries).map(([monLabel, entries]) => (
                <div key={monLabel} className="fz-modal-monitor-group">
                  <div className="fz-modal-monitor-header">
                    <Monitor size={14} />
                    <span>{monLabel}</span>
                  </div>
                  <div className="fz-modal-desktop-list">
                    {entries.map((entry, i) => (
                      <div key={i} className={`fz-modal-desktop-row ${entry.desktopIsCurrent ? 'current' : ''}`}>
                        <div className="fz-modal-desktop-info">
                          <span className="fz-modal-desktop-name">
                            {entry.desktopName}
                            {entry.desktopIsCurrent && (
                              <span className="fz-modal-current-badge">ACTUAL</span>
                            )}
                          </span>
                          <span className="fz-modal-layout-display">
                            {entry.activeLayout ? (
                              <span className="fz-modal-layout-active">
                                <LayoutGrid size={12} />
                                {entry.activeLayout.name}
                              </span>
                            ) : (
                              <span className="fz-modal-layout-none">Sin layout</span>
                            )}
                          </span>
                        </div>
                        <select
                          className="fz-modal-layout-select"
                          value={entry.activeLayoutUuid || ''}
                          onChange={e => handleChangeLayout(entry, e.target.value)}
                          disabled={changingEntry === entry}
                        >
                          <option value="">Sin layout</option>
                          {(fzStatus?.layouts || []).map(l => (
                            <option key={l.uuid} value={l.uuid}>{l.name}</option>
                          ))}
                        </select>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}

          {savingMsg && (
            <div className={`fz-modal-toast ${savingMsg.startsWith('✓') ? 'success' : 'error'}`}>
              {savingMsg}
            </div>
          )}
        </div>

        <div className="dialog-footer">
          <div></div>
          <button className="btn-secondary" onClick={onClose} style={{ border: 'none', background: 'transparent' }}>
            Cerrar
          </button>
        </div>
      </div>
    </div>
  )
}

function Section({ title, icon, children }) {
  return (
    <div className="config-section">
      <div className="config-section-title">
        {icon}
        <span>{title}</span>
      </div>
      {children}
    </div>
  )
}

function Toggle({ label, value, onChange }) {
  return (
    <label className="toggle-row">
      <span>{label}</span>
      <div
        className={`toggle ${value ? 'on' : ''}`}
        onClick={() => onChange(!value)}
      >
        <div className="toggle-thumb" />
      </div>
    </label>
  )
}

/**
 * KeybindRecorder — captures real keyboard events instead of manual text input.
 * Click "Presiona la combinación..." → press keys → combo is saved automatically.
 */
function KeybindRecorder({ value, onChange }) {
  const [recording, setRecording] = useState(false)
  const [display, setDisplay] = useState(value)
  const ref = useRef(null)

  useEffect(() => {
    setDisplay(value)
  }, [value])

  const handleKeyDown = useCallback((e) => {
    e.preventDefault()
    e.stopPropagation()

    const parts = []
    if (e.ctrlKey) parts.push('Ctrl')
    if (e.altKey) parts.push('Alt')
    if (e.shiftKey) parts.push('Shift')
    if (e.metaKey) parts.push('Win')

    if (e.key === 'Escape') {
      setRecording(false)
      setDisplay(value)
      return
    }

    // Ignore if only modifier keys are pressed
    const ignoredKeys = ['Control', 'Alt', 'Shift', 'Meta']
    if (!ignoredKeys.includes(e.key)) {
      // Normalize key names
      let keyName = e.key
      if (keyName === ' ') keyName = 'Space'
      else if (keyName.length === 1) keyName = keyName.toUpperCase()
      else if (keyName === 'ArrowLeft') keyName = 'Left'
      else if (keyName === 'ArrowRight') keyName = 'Right'
      else if (keyName === 'ArrowUp') keyName = 'Up'
      else if (keyName === 'ArrowDown') keyName = 'Down'

      parts.push(keyName)
      const combo = parts.join('+')
      setDisplay(combo)
      onChange(combo.toLowerCase())
      setRecording(false)
      ref.current?.blur()
    }
  }, [onChange])

  const handleMouseDown = useCallback((e) => {
    if (!recording) return
    // Capture mouse X buttons (button 3 = X1/Back, button 4 = X2/Forward)
    if (e.button === 3 || e.button === 4) {
      e.preventDefault()
      e.stopPropagation()

      const parts = []
      if (e.ctrlKey) parts.push('Ctrl')
      if (e.altKey) parts.push('Alt')
      if (e.shiftKey) parts.push('Shift')
      if (e.metaKey) parts.push('Win')

      parts.push(e.button === 3 ? 'x1' : 'x2')

      const combo = parts.join('+')
      setDisplay(combo)
      onChange(combo.toLowerCase())
      setRecording(false)
    }
  }, [recording, onChange])

  useEffect(() => {
    if (recording) {
      bridge.setHotkeysEnabled(false)
      window.addEventListener('mousedown', handleMouseDown, true)
      window.addEventListener('auxclick', handleMouseDown, true)
      return () => {
        bridge.setHotkeysEnabled(true)
        window.removeEventListener('mousedown', handleMouseDown, true)
        window.removeEventListener('auxclick', handleMouseDown, true)
      }
    }
  }, [recording, handleMouseDown])

  return (
    <button
      ref={ref}
      className={`keybind-btn ${recording ? 'recording' : ''}`}
      onClick={() => setRecording(true)}
      onKeyDown={recording ? handleKeyDown : undefined}
    >
      {recording ? (
        <span className="keybind-recording">Presiona la combinación (Esc para cancelar)...</span>
      ) : (
        <span className="keybind-value">{display || '—'}</span>
      )}
    </button>
  )
}
