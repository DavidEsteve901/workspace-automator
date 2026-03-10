import { useState } from 'react'
import { FolderOpen, Plus, Trash2, Settings } from 'lucide-react'
import logo from '../../assets/logo.ico'
import './Sidebar.css'

export default function Sidebar({ categories, activeCategory, onSelect, onAddCategory, onDeleteCategory, onOpenConfig, configActive }) {
  const [adding, setAdding] = useState(false)
  const [newName, setNewName] = useState('')

  function handleAdd() {
    const name = newName.trim()
    if (name) {
      onAddCategory(name)
      setNewName('')
    }
    setAdding(false)
  }

  function handleKeyDown(e) {
    if (e.key === 'Enter') handleAdd()
    if (e.key === 'Escape') { setAdding(false); setNewName('') }
  }

  return (
    <aside className="sidebar">
      {/* Header */}
      <div className="sidebar-header">
        <div className="sidebar-logo-wrap">
          <img src={logo} className="sidebar-logo-img" alt="Logo" />
        </div>
        <span className="sidebar-title">Workspace</span>
      </div>

      {/* Category list */}
      <div className="sidebar-label">WORKSPACES</div>
      <nav className="sidebar-nav">
        {categories.map(cat => (
          <div
            key={cat}
            className={`sidebar-item ${cat === activeCategory ? 'active' : ''}`}
            onClick={() => onSelect(cat)}
          >
            <FolderOpen size={15} className="sidebar-item-icon" />
            <span className="sidebar-item-name" title={cat}>{cat}</span>
            <button
              className="sidebar-item-delete"
              title="Eliminar workspace"
              onClick={e => { e.stopPropagation(); onDeleteCategory(cat) }}
            >
              <Trash2 size={13} />
            </button>
          </div>
        ))}

        {adding ? (
          <div className="sidebar-add-input">
            <input
              autoFocus
              value={newName}
              onChange={e => setNewName(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Nombre del workspace"
              onBlur={handleAdd}
            />
          </div>
        ) : (
          <button className="sidebar-add-btn" onClick={() => setAdding(true)}>
            <Plus size={15} /> Nuevo workspace
          </button>
        )}
      </nav>

      {/* Footer */}
      <div className="sidebar-footer">
        <button
          className={`sidebar-config-btn ${configActive ? 'active' : ''}`}
          onClick={onOpenConfig}
        >
          <Settings size={15} /> Configuración
        </button>
      </div>
    </aside>
  )
}
