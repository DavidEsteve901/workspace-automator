import { useState, useEffect, useCallback } from 'react'
import { bridge, onEvent, offEvent } from './api/bridge.js'
import Sidebar from './components/Sidebar/Sidebar.jsx'
import AppList from './components/AppList/AppList.jsx'
import ItemDialog from './components/AppList/ItemDialog.jsx'
import ConfigPanel from './components/ConfigPanel/ConfigPanel.jsx'
import TitleBar from './components/TitleBar/TitleBar.jsx'
import LogConsole from './components/LogConsole/LogConsole.jsx'
import './App.css'

export default function App() {
  const [state, setState]               = useState(null)
  const [activeCategory, setActiveCategory] = useState(null)
  const [view, setView]                 = useState('main') // 'main' | 'config'
  const [itemDialog, setItemDialog]     = useState(null)   // { category, index, item } | null
  const [launchStatus, setLaunchStatus] = useState(null)   // { message, progress } | null

  // ── Bootstrap ────────────────────────────────────────────────────────
  useEffect(() => {
    const handler = (data) => {
      setState(data)
      if (!activeCategory) setActiveCategory(data.lastCategory || Object.keys(data.categories)[0])
    }
    onEvent('state_update', handler)
    bridge.getState()
    return () => offEvent('state_update', handler)
  }, [])

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
    bridge.deleteItem(category, index)
    setState(prev => {
      const apps = { ...prev.categories }
      apps[category] = apps[category].filter((_, i) => i !== index)
      return { ...prev, categories: apps }
    })
  }, [])

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
      categories: { ...prev.categories, [name]: [] }
    }))
    setActiveCategory(name)
  }, [])

  const handleDeleteCategory = useCallback((name) => {
    bridge.deleteCategory(name)
    setState(prev => {
      const cats = { ...prev.categories }
      delete cats[name]
      return { ...prev, categories: cats }
    })
    setActiveCategory(Object.keys(state?.categories || {}).find(c => c !== name) || null)
  }, [state])

  const handleSaveConfig = useCallback((config) => {
    bridge.saveConfig(config)
    if (config.fzCustomPath !== undefined) {
      bridge.saveFzPath(config.fzCustomPath)
    }
    setState(prev => ({ ...prev, ...config }))
  }, [])

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
      <div className="app-body">
      <Sidebar
        categories={Object.keys(state.categories)}
        activeCategory={activeCategory}
        onSelect={handleCategorySelect}
        onAddCategory={handleAddCategory}
        onDeleteCategory={handleDeleteCategory}
        onOpenConfig={() => setView(v => v === 'config' ? 'main' : 'config')}
        configActive={view === 'config'}
      />

      <div className="main-content">
        {view === 'config' ? (
          <ConfigPanel
            hotkeys={state.hotkeys}
            pipWatcher={state.pipWatcher}
            fzCustomPath={state.fzCustomPath}
            fzDetectedPath={state.fzDetectedPath}
            onSave={handleSaveConfig}
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
      
      <LogConsole />
    </div>
    </div>
  )
}
