import { useState, useEffect, useCallback } from 'react'
import { RotateCw, AlertTriangle } from 'lucide-react'
import { bridge, onEvent, offEvent } from './api/bridge.js'
import Sidebar from './components/Sidebar/Sidebar.jsx'
import AppList from './components/AppList/AppList.jsx'
import ItemDialog from './components/AppList/ItemDialog.jsx'
import CleanModal from './components/AppList/CleanModal.jsx'
import SyncModal from './components/AppList/SyncModal.jsx'
import ConfigPanel from './components/ConfigPanel/ConfigPanel.jsx'
import TitleBar from './components/TitleBar/TitleBar.jsx'
import LogConsole from './components/LogConsole/LogConsole.jsx'
import ConfirmModal from './components/AppList/ConfirmModal.jsx'
import { ZoneEditorModal } from './components/ZoneEditor/ZoneEditorModal.jsx'
import { ErrorBoundary } from './components/ErrorBoundary.jsx'
import './App.css'

export default function App() {
  const getRoute = () => {
    const hash = window.location.hash || ''
    // Remove hash and leading slash to get a clean route name
    const base = hash.split('?')[0].replace(/^#\/?/, '')
    return base || 'main'
  }
  const [route, setRoute] = useState(getRoute())
  const [state, setState] = useState(null)
  const [activeCategory, setActiveCategory] = useState(null)
  const [view, setView] = useState('main') // 'main' | 'config'
  const [itemDialog, setItemDialog] = useState(null)   // { category, index, item } | null
  const [cleanModal, setCleanModal] = useState(null)   // category | null
  const [syncModal, setSyncModal] = useState(null)     // { category, validation } | null
  const [validation, setValidation] = useState(null)   // { valid, warnings, missingLayouts } | null
  const [launchStatus, setLaunchStatus] = useState(null)   // { message, progress } | null
  const [confirmAction, setConfirmAction] = useState(null) // { title, message, onConfirm } | null

  // ── Bootstrap ────────────────────────────────────────────────────────
  const load = useCallback(async () => {
    await bridge.getState()
  }, [])

  useEffect(() => {
    const handler = (data) => {
      setState(data)
      if (!activeCategory) setActiveCategory(data.lastCategory || Object.keys(data.categories)[0])
    }
    const hashHandler = () => setRoute(getRoute())
    
    onEvent('state_update', handler)
    window.addEventListener('hashchange', hashHandler)
    load()
    
    return () => {
      offEvent('state_update', handler)
      window.removeEventListener('hashchange', hashHandler)
    }
  }, [load])

  useEffect(() => {
    if (activeCategory) {
      bridge.validateWorkspace(activeCategory).then(setValidation)
    } else {
      setValidation(null)
    }
  }, [activeCategory])

  const handleRefresh = useCallback(() => {
    load()
    if (activeCategory) {
      bridge.validateWorkspace(activeCategory).then(setValidation)
    }
  }, [activeCategory, load])

  // ── Launch progress listener ─────────────────────────────────────────
  useEffect(() => {
    const handler = (data) => {
      setLaunchStatus(data)
      if (data.progress >= 100) {
        setTimeout(() => setLaunchStatus(null), 2000)
      }
    }
    onEvent('launch_progress', handler)
    return () => offEvent('launch_progress', handler)
  }, [])


  // ── Category switch ──────────────────────────────────────────────────
  const handleCategorySelect = useCallback((cat) => {
    if (view === 'config') return // Bloquear cambio si estamos en configuración
    setActiveCategory(cat)
    bridge.setLastCategory(cat)
  }, [view])

  // ── Launch ───────────────────────────────────────────────────────────
  const handleLaunch = useCallback(() => {
    if (!activeCategory) return
    if (validation && !validation.valid) {
      setSyncModal({ category: activeCategory, validation })
      return
    }
    setLaunchStatus({ message: 'Iniciando...', progress: 0 })
    bridge.launchWorkspace(activeCategory)
  }, [activeCategory, validation])

  // ── Item CRUD ────────────────────────────────────────────────────────
  const handleSaveItem = useCallback((category, index, item) => {
    bridge.saveItem(category, index, item)
    setItemDialog(null)
    // Optimistic local update; backend will also send state_update
    setState(prev => {
      const apps = { ...prev.categories }
      const list = [...(apps[category] || [])]
      if (index >= 0 && index < list.length) list[index] = item
      else list.push(item)
      apps[category] = list
      return { ...prev, categories: apps }
    })
  }, [])

  const handleDeleteItem = useCallback((category, index) => {
    const item = state?.categories[category]?.[index]
    const name = item?.path?.split(/[/\\]/).pop() || 'esta aplicación'
    
    setConfirmAction({
      title: 'Eliminar Aplicación',
      message: `¿Estás seguro de que quieres eliminar "${name}" del workspace?`,
      onConfirm: () => {
        bridge.deleteItem(category, index)
        setState(prev => {
          const apps = { ...prev.categories }
          apps[category] = apps[category].filter((_, i) => i !== index)
          return { ...prev, categories: apps }
        })
        setConfirmAction(null)
      }
    })
  }, [state])

  const handleMoveItem = useCallback((category, from, to) => {
    bridge.moveItem(category, from, to)
    setState(prev => {
      const apps = { ...prev.categories }
      const list = [...apps[category]]
      const [item] = list.splice(from, 1)
      list.splice(to, 0, item)
      apps[category] = list
      return { ...prev, categories: apps }
    })
  }, [])

  const handleAddCategory = useCallback((name) => {
    bridge.addCategory(name)
    setState(prev => ({
      ...prev,
      categories: { ...prev.categories, [name]: [] },
      categoryOrder: [...(prev.categoryOrder || Object.keys(prev.categories)), name] // Add to order
    }))
    setActiveCategory(name)
  }, [])

  const handleDeleteCategory = useCallback(async (name) => {
    setConfirmAction({
      title: 'Eliminar Workspace',
      message: `¿Estás seguro de que quieres eliminar el workspace "${name}"? Esta acción no se puede deshacer.`,
      onConfirm: async () => {
        await bridge.deleteCategory(name)
        setState(prev => {
          const cats = { ...prev.categories }
          delete cats[name]
          const newCategoryOrder = prev.categoryOrder?.filter(cat => cat !== name) || Object.keys(cats)
          return { ...prev, categories: cats, categoryOrder: newCategoryOrder }
        })
        setActiveCategory(prev => Object.keys(state?.categories || {}).find(c => c !== name) || null)
        setConfirmAction(null)
      }
    })
  }, [state])

  const handleMoveCategory = useCallback((from, to) => {
    bridge.moveCategory(from, to)
    setState(prev => {
      const order = [...(prev.categoryOrder || Object.keys(prev.categories))]
      const [item] = order.splice(from, 1)
      order.splice(to, 0, item)
      return { ...prev, categoryOrder: order }
    })
  }, [])

  const handleRenameCategory = useCallback((oldName, newName) => {
    bridge.renameCategory(oldName, newName)
    setState(prev => {
      const categories = { ...prev.categories }
      categories[newName] = categories[oldName]
      delete categories[oldName]

      const categoryOrder = (prev.categoryOrder || Object.keys(prev.categories)).map(c => 
        c === oldName ? newName : c
      )

      return { ...prev, categories, categoryOrder, lastCategory: prev.lastCategory === oldName ? newName : prev.lastCategory }
    })
    if (activeCategory === oldName) setActiveCategory(newName)
  }, [activeCategory])

  // ── Theme application ────────────────────────────────────────────────
  const applyTheme = useCallback((themeMode, accentHex) => {
    const root = document.documentElement
    const isDark = themeMode !== 'light'
    root.setAttribute('data-theme', isDark ? 'dark' : 'light')

    // Notify the WPF host window to update its title bar and background color
    bridge.updateWindowTheme(isDark)

    if (accentHex && /^#[0-9A-Fa-f]{6}$/.test(accentHex)) {
      const r = parseInt(accentHex.slice(1, 3), 16)
      const g = parseInt(accentHex.slice(3, 5), 16)
      const b = parseInt(accentHex.slice(5, 7), 16)

      const darken  = (v, pct) => Math.max(0, Math.round(v * (1 - pct)))
      const lighten = (v, pct) => Math.min(255, Math.round(v + (255 - v) * pct))
      const toHex   = (r, g, b) => `#${r.toString(16).padStart(2,'0')}${g.toString(16).padStart(2,'0')}${b.toString(16).padStart(2,'0')}`

      root.style.setProperty('--accent',        accentHex)
      root.style.setProperty('--accent-hover',  toHex(darken(r,.10), darken(g,.10), darken(b,.10)))
      root.style.setProperty('--accent-light',  toHex(lighten(r,.40), lighten(g,.40), lighten(b,.40)))
      root.style.setProperty('--accent-dim',    `rgba(${r},${g},${b},0.12)`)
      root.style.setProperty('--accent-glow',   `rgba(${r},${g},${b},0.25)`)
      root.style.setProperty('--accent-rgb',    `${r},${g},${b}`)
      root.style.setProperty('--border-accent', `rgba(${r},${g},${b},0.30)`)
      root.style.setProperty('--shadow-accent', `0 0 20px rgba(${r},${g},${b},0.15)`)
      root.style.setProperty('--shadow-glow',   `0 0 40px rgba(${r},${g},${b},0.08)`)
      // Also sync --fz-accent* so ZoneEditorModal inline styles pick up the custom accent
      root.style.setProperty('--fz-accent',      accentHex)
      root.style.setProperty('--fz-accent-hover',toHex(darken(r,.10), darken(g,.10), darken(b,.10)))
      root.style.setProperty('--fz-accent-dim',  `rgba(${r},${g},${b},0.15)`)
      root.style.setProperty('--fz-accent-glow', `rgba(${r},${g},${b},0.35)`)
      root.style.setProperty('--fz-accent-low',  `rgba(${r},${g},${b},0.05)`)

      // Dynamic SVG Select Arrow with the current accent color
      const svgArrow = `<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='rgb(${r},${g},${b})' stroke-width='3' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='m6 9 6 6 6-6'/%3E%3C/svg%3E`;
      const arrowDataUri = `url("data:image/svg+xml,${svgArrow.replace(/#/g, '%23').replace(/</g, '%3C').replace(/>/g, '%3E')}")`;
      root.style.setProperty('--fz-select-arrow', arrowDataUri)
    } else {
      ;['--accent','--accent-hover','--accent-light','--accent-dim','--accent-glow',
        '--border-accent','--shadow-accent','--shadow-glow',
        '--fz-accent','--fz-accent-hover','--fz-accent-dim','--fz-accent-glow','--fz-accent-low',
        '--fz-select-arrow'
      ].forEach(v => root.style.removeProperty(v))
    }
  }, [])

  useEffect(() => {
    if (state) applyTheme(state.themeMode, state.accentColor)
  }, [state?.themeMode, state?.accentColor, applyTheme])

  const handleSaveConfig = useCallback((config) => {
    bridge.saveConfig(config)
    if (config.fzCustomPath !== undefined) {
      bridge.saveFzPath(config.fzCustomPath)
    }
    setState(prev => ({ ...prev, ...config }))
  }, [])

  const handleRestore = useCallback(() => {
    if (!activeCategory) return
    if (validation && !validation.valid) {
      setSyncModal({ category: activeCategory, validation })
      return
    }
    bridge.restoreWorkspace(activeCategory)
  }, [activeCategory, validation])

  const handleClean = useCallback(() => {
    if (!activeCategory) return
    setCleanModal(activeCategory)
  }, [activeCategory])

  // Standalone Routing (Handled before state block to preserve transparency of modals and canvas)
  if (route === 'zone-editor') {
    return <ZoneEditorModal standalone onClose={() => window.close()} />
  }

  if (route === 'zone-canvas') {
    const params = new URLSearchParams(window.location.hash.split('?')[1]);
    const monitorId = params.get('monitor');
    const layoutId = params.get('layout');
    const canvasMode = params.get('mode') || 'preview';
    return (
      <div style={{ background: 'transparent', width: '100vw', height: '100vh', overflow: 'hidden' }}>
        <style>{`
          html, body, #root { background: transparent !important; }
        `}</style>
        <ZoneEditorModal standalone canvasOnly canvasMode={canvasMode} initialMonitorId={monitorId} initialLayoutId={layoutId} onClose={() => window.close()} />
      </div>
    )
  }

  if (route === 'zone-control') {
    const params = new URLSearchParams(window.location.hash.split('?')[1]);
    const monitorId = params.get('monitor');
    const layoutId = params.get('layout');
    return (
      <div style={{ background: 'transparent', width: '100vw', height: '100vh', overflow: 'hidden' }}>
        <style>{`
          html, body, #root { background: transparent !important; }
        `}</style>
        <ZoneEditorModal standalone controlOnly initialMonitorId={monitorId} initialLayoutId={layoutId} onClose={() => window.close()} />
      </div>
    )
  }

  if (!state) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100vh', background: 'var(--fz-bg, #0A0A0A)' }}>
        <div style={{ color: 'var(--fz-text-muted)' }}>Cargando...</div>
      </div>
    )
  }

  const currentItems = state.categories[activeCategory] || []

  return (
    <div className="app-layout">
      <TitleBar />
      
      <div className="app-body">
        <Sidebar
          categories={state.categoryOrder || Object.keys(state.categories)}
          activeCategory={activeCategory}
          onSelect={handleCategorySelect}
          onAddCategory={handleAddCategory}
          onDeleteCategory={handleDeleteCategory}
          onMoveCategory={handleMoveCategory}
          onRenameCategory={handleRenameCategory}
          onOpenConfig={() => setView(v => v === 'config' ? 'main' : 'config')}
          configActive={view === 'config'}
          disabled={view === 'config'}
        />

        <div className="main-content">
          {view === 'config' ? (
            <ConfigPanel
              hotkeys={state.hotkeys}
              pipWatcher={state.pipWatcher}
              fzCustomPath={state.fzCustomPath}
              fzDetectedPath={state.fzDetectedPath}
              fzSyncEnabled={state.fzSyncEnabled}
              configPath={state.configPath}
              themeMode={state.themeMode}
              accentColor={state.accentColor}
              desktopAnimationsEnabled={state.desktopAnimationsEnabled}
              onSave={handleSaveConfig}
              onClose={() => setView('main')}
            />
          ) : (
            <>
              <AppList
                category={activeCategory}
                items={currentItems}
                onAddItem={() => setItemDialog({ category: activeCategory, index: -1, item: null })}
                onEditItem={(idx) => setItemDialog({ category: activeCategory, index: idx, item: currentItems[idx] })}
                onDeleteItem={(idx) => handleDeleteItem(activeCategory, idx)}
                onMoveItem={(from, to) => handleMoveItem(activeCategory, from, to)}
                onLaunch={handleLaunch}
                onRestore={handleRestore}
                onClean={handleClean}
                launchStatus={launchStatus}
              />

              {validation && !validation.valid && !itemDialog && !cleanModal && !syncModal && !confirmAction && (
                <div
                  className="sync-alert"
                  onClick={() => setSyncModal({ category: activeCategory, validation })}
                  title="Ver conflictos y soluciones"
                >
                  <AlertTriangle size={18} />
                  <div className="sync-alert-text">
                    <strong>Conflictos detectados</strong>
                    <span>Configuración no coincide con el equipo</span>
                  </div>
                  <div className="sync-alert-badge">!</div>
                </div>
              )}
            </>
          )}

          {state.hotkeys?.show_system_console && <LogConsole />}
        </div>

        {itemDialog && (
          <ErrorBoundary>
            <ItemDialog
              category={itemDialog.category}
              index={itemDialog.index}
              item={itemDialog.item}
              validation={validation}
              fzSyncEnabled={state.fzSyncEnabled}
              czeActiveLayouts={state.czeActiveLayouts}
              currentDesktopId={state.currentDesktopId}
              hotkeys={state.hotkeys}
              categories={state.categories}
              onSave={(item) => handleSaveItem(itemDialog.category, itemDialog.index, item)}
              onClose={() => setItemDialog(null)}
            />
          </ErrorBoundary>
        )}

        {cleanModal && (
          <CleanModal 
            category={cleanModal} 
            onClose={() => setCleanModal(null)} 
          />
        )}

        {syncModal && (
          <SyncModal
            category={syncModal.category}
            validation={syncModal.validation}
            fzSyncEnabled={state.fzSyncEnabled}
            onClose={() => setSyncModal(null)}
            onSynced={() => {
              if (activeCategory) bridge.validateWorkspace(activeCategory).then(setValidation)
              handleRefresh()
            }}
          />
        )}

        {confirmAction && (
          <ConfirmModal
            title={confirmAction.title}
            message={confirmAction.message}
            onConfirm={confirmAction.onConfirm}
            onCancel={() => setConfirmAction(null)}
          />
        )}

      </div>
    </div>
  )
}
