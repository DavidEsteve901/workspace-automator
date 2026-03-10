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
import './App.css'

export default function App() {
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
    onEvent('state_update', handler)
    load()
    return () => offEvent('state_update', handler)
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
    setActiveCategory(cat)
    bridge.setLastCategory(cat)
  }, [])

  // ── Launch ───────────────────────────────────────────────────────────
  const handleLaunch = useCallback(() => {
    if (!activeCategory) return
    setLaunchStatus({ message: 'Iniciando...', progress: 0 })
    bridge.launchWorkspace(activeCategory)
  }, [activeCategory])

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

  const handleSaveConfig = useCallback((config) => {
    bridge.saveConfig(config)
    if (config.fzCustomPath !== undefined) {
      bridge.saveFzPath(config.fzCustomPath)
    }
    setState(prev => ({ ...prev, ...config }))
  }, [])

  const handleRestore = useCallback(() => {
    if (!activeCategory) return
    bridge.restoreWorkspace(activeCategory)
  }, [activeCategory])

  const handleClean = useCallback(() => {
    if (!activeCategory) return
    setCleanModal(activeCategory)
  }, [activeCategory])

  if (!state) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
        <div style={{ color: 'var(--text-muted)' }}>Cargando...</div>
      </div>
    )
  }

  const currentItems = state.categories[activeCategory] || []

  return (
    <div className="app-layout" style={{ flexDirection: 'column' }}>
      <TitleBar />
      
      <div className="layout-body" style={{ display: 'flex', flexGrow: 1, overflow: 'hidden' }}>
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
        />

        <div className="main-content">
          {view === 'config' ? (
            <ConfigPanel
              pipWatcher={state.pipWatcher}
              fzCustomPath={state.fzCustomPath}
              fzDetectedPath={state.fzDetectedPath}
              configPath={state.configPath}
              onSave={handleSaveConfig}
              onClose={() => setView('main')}
            />
          ) : (
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
          )}
        </div>

        {itemDialog && (
          <ItemDialog
            category={itemDialog.category}
            index={itemDialog.index}
            item={itemDialog.item}
            onSave={(item) => handleSaveItem(itemDialog.category, itemDialog.index, item)}
            onClose={() => setItemDialog(null)}
          />
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
            onClose={() => setSyncModal(null)}
            onSynced={() => {
              if (activeCategory) bridge.validateWorkspace(activeCategory).then(setValidation)
              handleRefresh()
            }}
          />
        )}

        {validation && !validation.valid && (
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

        {confirmAction && (
          <ConfirmModal
            title={confirmAction.title}
            message={confirmAction.message}
            onConfirm={confirmAction.onConfirm}
            onCancel={() => setConfirmAction(null)}
          />
        )}

        {state.hotkeys?.show_system_console && <LogConsole />}
      </div>
    </div>
  )
}
