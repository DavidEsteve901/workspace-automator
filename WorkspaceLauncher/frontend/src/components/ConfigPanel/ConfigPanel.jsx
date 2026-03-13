import { useState, useEffect, useCallback, useRef } from 'react'
import { Settings, Keyboard, Monitor, Save, Check, Folder, LayoutGrid, ChevronRight, X, ArrowRightToLine, ArrowLeftToLine, ArrowRightSquare, ArrowLeftSquare, RefreshCcw, RotateCw, Palette, Sun, Moon } from 'lucide-react'
import { bridge, onEvent, offEvent } from '../../api/bridge.js'
import './ConfigPanel.css'

const ACCENT_PALETTES = [
  { name: 'Cyan',    color: '#00D2FF' },
  { name: 'Azul',    color: '#2979FF' },
  { name: 'Verde',   color: '#00E676' },
  { name: 'Violeta', color: '#D500F9' },
  { name: 'Naranja', color: '#FF6D00' },
  { name: 'Rosa',    color: '#FF4081' },
  { name: 'Rojo',    color: '#FF1744' },
  { name: 'Oro',     color: '#FFD600' },
]

const HOTKEY_LABELS = {
  cycle_forward: { label: 'Ciclar zona →', icon: ArrowRightToLine },
  cycle_backward: { label: 'Ciclar zona ←', icon: ArrowLeftToLine },
  desktop_cycle_fwd: { label: 'Cambiar escritorio →', icon: ArrowRightSquare },
  desktop_cycle_bwd: { label: 'Cambiar escritorio ←', icon: ArrowLeftSquare },
  util_reload_layouts: { label: 'Recargar layouts', icon: RefreshCcw },
  open_zone_editor: { label: 'Abrir Editor de Zonas', icon: LayoutGrid },
}

export default function ConfigPanel({ hotkeys, pipWatcher, fzCustomPath, fzDetectedPath, fzSyncEnabled, configPath, themeMode, accentColor, onSave, onClose }) {
  const [hk, setHk] = useState({ ...hotkeys })
  const [pip, setPip] = useState(pipWatcher)
  const [fzPath, setFzPath] = useState(fzCustomPath || '')
  const [fzSync, setFzSync] = useState(fzSyncEnabled)
  const [engine, setEngine] = useState('FancyZones')
  const [saved, setSaved] = useState(false)
  const [fzModalOpen, setFzModalOpen] = useState(false)
  const [fzSectionOpen, setFzSectionOpen] = useState(true)
  const [activeRecordingKey, setActiveRecordingKey] = useState(null)
  const [theme, setTheme] = useState(themeMode || 'dark')
  const [accent, setAccent] = useState(accentColor || '')
  const [winAccent, setWinAccent] = useState('')

  useEffect(() => {
    async function loadEngine() {
      const e = await bridge.czeGetZoneEngine()
      setEngine(e.engine)
    }
    async function loadThemeConfig() {
      try {
        const t = await bridge.getThemeConfig()
        if (t?.windowsAccentColor) setWinAccent(t.windowsAccentColor)
      } catch {}
    }
    loadEngine()
    loadThemeConfig()
  }, [])

  async function handleSave() {
    onSave({ hotkeys: hk, pipWatcherEnabled: pip, fzCustomPath: fzPath, fzSyncEnabled: fzSync, themeMode: theme, accentColor: accent })
    await bridge.czeSetZoneEngine(engine)
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }
  async function handlePickPath() {
    const res = await bridge.openFileDialog({ isFolder: true })
    if (res) setFzPath(res)
  }

  async function handlePickConfigPath() {
    const res = await bridge.openFileDialog({ 
      isFolder: true, 
      title: "Seleccionar carpeta donde guardar la configuración" 
    })
    if (res) {
      await bridge.changeConfigPath(res)
    }
  }

  function handleOpenConfigFolder() {
    bridge.openConfigFolder()
  }

  function setHotkey(key, value) {
    setHk(h => ({ ...h, [key]: value }))
  }

  return (
    <div className="config-panel">
      <div className="config-header">
        <div className="config-header-left">
          <Settings size={20} className="config-header-icon" />
          <h2>Configuración</h2>
        </div>
        <button className="config-close-btn" onClick={onClose} title="Cerrar configuración">
          <X size={20} />
        </button>
      </div>

      <div className="config-body">
        <Section title="Motor de Zonas (Engine)" icon={<LayoutGrid size={14} />}>
          <div className="engine-toggle-group">
            <button 
              className={`engine-btn ${engine === 'FancyZones' ? 'active' : ''}`}
              onClick={() => setEngine('FancyZones')}
            >
              FancyZones (PowerToys)
            </button>
            <button 
              className={`engine-btn ${engine === 'CustomZoneEngine' ? 'active' : ''}`}
              onClick={() => setEngine('CustomZoneEngine')}
            >
              CustomZoneEngine (Propio)
            </button>
          </div>
          <p className="fz-path-help">
            {engine === 'CustomZoneEngine' 
              ? "Usa el motor integrado. Permite edición visual avanzada y es independiente de PowerToys."
              : "Usa el motor oficial de Microsoft PowerToys (requiere tenerlo instalado)."}
          </p>
          
          <div style={{ marginTop: '12px' }}>
            <Toggle
              label="Sincronización y Compatibilidad con FancyZones"
              value={fzSync}
              onChange={setFzSync}
            />
          </div>
        </Section>

        {/* Grouped FancyZones options (Collapsible) */}
        {fzSync && (
          <Section 
            title="FancyZones — Configuración Adicional" 
            icon={<LayoutGrid size={14} />}
            collapsible
            isOpen={fzSectionOpen}
            onToggle={() => setFzSectionOpen(!fzSectionOpen)}
          >
            <div className="fz-grouped-settings">
              <button className="fz-status-btn" onClick={() => setFzModalOpen(true)}>
                <div className="fz-status-btn-content">
                  <LayoutGrid size={16} />
                  <div>
                    <span className="fz-status-btn-title">Gestionar Layouts por Monitor</span>
                    <span className="fz-status-btn-desc">Ver y cambiar qué layout está activo en cada monitor</span>
                  </div>
                </div>
                <ChevronRight size={16} className="fz-status-btn-arrow" />
              </button>

              <div className="fz-path-section" style={{ marginTop: '12px' }}>
                <span className="config-sub-label">Ruta de PowerToys / FancyZones:</span>
                <div className="fz-path-row">
                  <input
                    className="fz-path-input"
                    type="text"
                    placeholder="Autodetección..."
                    value={fzPath}
                    onChange={e => setFzPath(e.target.value)}
                  />
                  <button className="fz-path-btn" onClick={handlePickPath} title="Seleccionar">
                    <Folder size={14} />
                  </button>
                </div>
                {fzDetectedPath && (
                  <div className="fz-detected-path">
                    <span>Detectado:</span> <code>{fzDetectedPath}</code>
                  </div>
                )}
              </div>
            </div>
          </Section>
        )}

        {/* System & Global Toggles */}
        <Section title="Sistema y Opciones Globales" icon={<Monitor size={14} />}>
          <Toggle
            label="Anclar ventanas Picture-in-Picture a todos los escritorios"
            value={pip}
            onChange={setPip}
          />
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
          <Toggle
            label="Mostrar consola de depuración"
            value={hk.show_system_console}
            onChange={v => setHotkey('show_system_console', v)}
          />
        </Section>

        {/* Config file location */}
        <Section title="Ubicación de Configuración (JSON)" icon={<Folder size={14} />}>
          <div className="fz-path-row">
            <input
              className="fz-path-input"
              type="text"
              readOnly
              value={configPath || ''}
            />
            <button className="fz-path-btn" onClick={handlePickConfigPath} title="Seleccionar nueva carpeta de configuración">
              <Folder size={14} />
            </button>
            <button className="fz-path-btn" onClick={handleOpenConfigFolder} title="Abrir carpeta actual en el Explorador">
              <ChevronRight size={14} />
            </button>
          </div>
          <p className="fz-path-help">
            Ruta actual del archivo <code>mis_apps_config_v2.json</code>. Puedes cambiarla para sincronizar entre equipos.
          </p>
        </Section>

        {/* Apariencia */}
        <Section title="Apariencia" icon={<Palette size={14} />}>
          <div className="appearance-theme-row">
            <span className="config-sub-label">Tema de la interfaz</span>
            <div className="theme-toggle-group">
              <button
                className={`theme-mode-btn ${theme === 'dark' ? 'active' : ''}`}
                onClick={() => setTheme('dark')}
              >
                <Moon size={13} /> Oscuro
              </button>
              <button
                className={`theme-mode-btn ${theme === 'light' ? 'active' : ''}`}
                onClick={() => setTheme('light')}
              >
                <Sun size={13} /> Claro
              </button>
            </div>
          </div>

          <div style={{ marginTop: '14px' }}>
            <span className="config-sub-label">Color de acento</span>
            <div className="accent-palette">
              {ACCENT_PALETTES.map(p => (
                <button
                  key={p.color}
                  className={`accent-swatch ${accent === p.color ? 'active' : ''}`}
                  style={{ '--swatch-color': p.color }}
                  onClick={() => setAccent(p.color)}
                  title={p.name}
                />
              ))}
              {winAccent && (
                <button
                  className={`accent-swatch accent-swatch-win ${accent === winAccent ? 'active' : ''}`}
                  style={{ '--swatch-color': winAccent }}
                  onClick={() => setAccent(winAccent)}
                  title={`Color de Windows (${winAccent})`}
                >
                  <span className="accent-swatch-win-label">W</span>
                </button>
              )}
              <button
                className={`accent-swatch accent-swatch-reset ${!accent ? 'active' : ''}`}
                onClick={() => setAccent('')}
                title="Cyan por defecto"
              >
                <span style={{ fontSize: '9px', fontWeight: 700 }}>DEF</span>
              </button>
            </div>
            <div className="accent-preview">
              <div
                className="accent-preview-dot"
                style={{ background: accent || '#00D2FF' }}
              />
              <code className="accent-preview-hex">{accent || '#00D2FF (por defecto)'}</code>
            </div>
          </div>
        </Section>

        {/* Hotkeys — keybinding recorder */}
        <Section title="Atajos de teclado / ratón" icon={<Keyboard size={14} />}>
          <div className="hotkey-table">
            {Object.entries(HOTKEY_LABELS).map(([key, data]) => {
              const Icon = data.icon;
              return (
              <div key={key} className="hotkey-row">
                <span className="hotkey-label"><Icon size={14} className="hotkey-icon" /> {data.label}</span>
                <KeybindRecorder
                  value={hk[key] || ''}
                  isRecording={activeRecordingKey === key}
                  onStartRecording={() => setActiveRecordingKey(key)}
                  onStopRecording={() => setActiveRecordingKey(null)}
                  onChange={val => setHotkey(key, val)}
                />
              </div>
            )})}
          </div>
        </Section>
      </div>

      <div className="config-footer">
        <button 
          className={`btn-launch ${saved ? 'btn-success' : ''}`} 
          onClick={handleSave}
          disabled={saved}
        >
          {saved ? (
            <>
              <Check size={14} /> Aplicado correctamente
            </>
          ) : (
            <>
              <Save size={14} /> Guardar configuración
            </>
          )}
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
    console.log("[FzModal] Requesting FZ status...");
    try {
      const data = await bridge.getFzStatus()
      console.log("[FzModal] FZ status received:", data);
      setFzStatus(data)
    } catch (err) {
      console.error("[FzModal] Error loading FZ status:", err)
      bridge.setHotkeysEnabled(true) // Just in case
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    loadStatus()
    
    onEvent('desktop_switched', loadStatus)
    return () => offEvent('desktop_switched', loadStatus)
  }, [loadStatus])

  const handleChangeLayout = async (entry, newLayoutUuid) => {
    setChangingEntry(entry)
    try {
      const res = await bridge.changeLayoutAssignment(
        entry.monitorPtInstance,
        entry.monitorPtName,
        entry.monitorSerial,
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
                      <div 
                        key={i} 
                        className={`fz-modal-desktop-row ${entry.desktopIsCurrent ? 'current' : ''}`}
                        onDoubleClick={() => handleChangeLayout(entry, entry.activeLayoutUuid)}
                        title="Doble clic para forzar reaplicación"
                      >
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
                          onClick={e => e.stopPropagation()}
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

function Section({ title, icon, children, collapsible, isOpen, onToggle }) {
  return (
    <div className={`config-section ${collapsible ? 'collapsible' : ''} ${isOpen === false ? 'collapsed' : ''}`}>
      <div className="config-section-title" onClick={collapsible ? onToggle : undefined} style={{ cursor: collapsible ? 'pointer' : 'default' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          {icon}
          <span>{title}</span>
        </div>
        {collapsible && (
          <ChevronRight 
            size={14} 
            style={{ 
              transform: isOpen ? 'rotate(90deg)' : 'rotate(0deg)',
              transition: 'transform 0.2s ease',
              marginLeft: 'auto'
            }} 
          />
        )}
      </div>
      {(!collapsible || isOpen) && (
        <div className="config-section-content">
          {children}
        </div>
      )}
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
function KeybindRecorder({ value, isRecording, onStartRecording, onStopRecording, onChange }) {
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
      onStopRecording()
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
      onStopRecording()
      ref.current?.blur()
    }
  }, [onChange, onStopRecording, value])

  const handleMouseDown = useCallback((e) => {
    if (!isRecording) return
    // Capture mouse buttons: middle click (1), X1/Back (3), X2/Forward (4)
    if (e.button === 1 || e.button === 3 || e.button === 4) {
      e.preventDefault()
      e.stopPropagation()

      const parts = []
      if (e.ctrlKey) parts.push('Ctrl')
      if (e.altKey) parts.push('Alt')
      if (e.shiftKey) parts.push('Shift')
      if (e.metaKey) parts.push('Win')

      let btnName = 'mbutton'
      if (e.button === 3) btnName = 'x1'
      else if (e.button === 4) btnName = 'x2'

      parts.push(btnName)

      const combo = parts.join('+')
      setDisplay(combo)
      onChange(combo.toLowerCase())
      onStopRecording()
    }
  }, [isRecording, onChange, onStopRecording])

  useEffect(() => {
    if (isRecording) {
      bridge.setHotkeysEnabled(false)
      window.addEventListener('mousedown', handleMouseDown, true)
      return () => {
        bridge.setHotkeysEnabled(true)
        window.removeEventListener('mousedown', handleMouseDown, true)
      }
    }
  }, [isRecording, handleMouseDown])

  return (
    <button
      ref={ref}
      className={`keybind-btn ${isRecording ? 'recording' : ''}`}
      onClick={() => { if (!isRecording) onStartRecording() }}
      onKeyDown={isRecording ? handleKeyDown : undefined}
    >
      {isRecording ? (
        <span className="keybind-recording">Presiona atajo (Esc cancelar)...</span>
      ) : (
        <span className="keybind-value">{display || '—'}</span>
      )}
    </button>
  )
}
