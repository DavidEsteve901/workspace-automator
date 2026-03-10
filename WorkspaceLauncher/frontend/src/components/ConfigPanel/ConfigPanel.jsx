import { useState, useEffect, useCallback, useRef } from 'react'
import { Settings, Keyboard, Monitor, Save, Check } from 'lucide-react'
import './ConfigPanel.css'

const HOTKEY_LABELS = {
  cycle_forward:      'Ciclar zona → (adelante)',
  cycle_backward:     'Ciclar zona ← (atrás)',
  mouse_cycle_fwd:    'Ratón: ciclar zona adelante',
  mouse_cycle_bwd:    'Ratón: ciclar zona atrás',
  desktop_cycle_fwd:  'Cambiar escritorio →',
  desktop_cycle_bwd:  'Cambiar escritorio ←',
  util_reload_layouts:'Recargar layouts FancyZones',
}

export default function ConfigPanel({ hotkeys, pipWatcher, onSave }) {
  const [hk, setHk]     = useState({ ...hotkeys })
  const [pip, setPip]   = useState(pipWatcher)
  const [saved, setSaved] = useState(false)

  function handleSave() {
    onSave({ hotkeys: hk, pipWatcherEnabled: pip })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
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

        {/* PiP watcher */}
        <Section title="PiP Watcher" icon={<Monitor size={14} />}>
          <Toggle
            label="Anclar ventanas Picture-in-Picture a todos los escritorios"
            value={pip}
            onChange={setPip}
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
    if (e.ctrlKey)  parts.push('Ctrl')
    if (e.altKey)   parts.push('Alt')
    if (e.shiftKey) parts.push('Shift')
    if (e.metaKey)  parts.push('Win')

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
    // Capture mouse X buttons (button 3 = X1, button 4 = X2)
    if (e.button === 3 || e.button === 4) {
      e.preventDefault()
      const parts = []
      if (e.ctrlKey)  parts.push('Ctrl')
      if (e.altKey)   parts.push('Alt')
      if (e.shiftKey) parts.push('Shift')
      parts.push(e.button === 3 ? 'Mouse4' : 'Mouse5')

      const combo = parts.join('+')
      setDisplay(combo)
      onChange(combo.toLowerCase())
      setRecording(false)
    }
  }, [recording, onChange])

  useEffect(() => {
    if (recording) {
      window.addEventListener('mousedown', handleMouseDown)
      return () => window.removeEventListener('mousedown', handleMouseDown)
    }
  }, [recording, handleMouseDown])

  return (
    <button
      ref={ref}
      className={`keybind-btn ${recording ? 'recording' : ''}`}
      onClick={() => setRecording(true)}
      onKeyDown={recording ? handleKeyDown : undefined}
      onBlur={() => setRecording(false)}
    >
      {recording ? (
        <span className="keybind-recording">Presiona la combinación...</span>
      ) : (
        <span className="keybind-value">{display || '—'}</span>
      )}
    </button>
  )
}
