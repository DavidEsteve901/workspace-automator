import { useState } from 'react'
import { FolderOpen, Plus, Trash2, Settings, ChevronUp, ChevronDown, GripVertical, Check } from 'lucide-react'
import logo from '../../assets/logo.ico'
import './Sidebar.css'

export default function Sidebar({ 
  categories, 
  activeCategory, 
  onSelect, 
  onAddCategory, 
  onDeleteCategory, 
  onMoveCategory, 
  onRenameCategory, 
  onOpenConfig, 
  configActive,
  disabled
}) {
  const [adding, setAdding] = useState(false)
  const [newName, setNewName] = useState('')
  const [editing, setEditing] = useState(null) // cat name
  const [editValue, setEditValue] = useState('')
  const [draggedIdx, setDraggedIdx] = useState(null)

  function handleAdd() {
    if (disabled) return
    const name = newName.trim()
    if (name) {
      onAddCategory(name)
      setNewName('')
    }
    setAdding(false)
  }

  function handleStartEdit(cat) {
    if (disabled) return
    setEditing(cat)
    setEditValue(cat)
  }

  function handleConfirmRename() {
    if (disabled) return
    const val = editValue.trim()
    if (val && val !== editing) {
      onRenameCategory(editing, val)
    }
    setEditing(null)
  }

  function handleEditKeyDown(e) {
    if (e.key === 'Enter') handleConfirmRename()
    if (e.key === 'Escape') setEditing(null)
  }

  function handleKeyDown(e) {
    if (e.key === 'Enter') handleAdd()
    if (e.key === 'Escape') { setAdding(false); setNewName('') }
  }

  function handleMove(idx, direction) {
    if (disabled) return
    onMoveCategory(idx, idx + direction)
  }

  // HTML5 Drag and Drop
  function onDragStart(e, index) {
    if (disabled) {
      e.preventDefault()
      return
    }
    setDraggedIdx(index)
    e.dataTransfer.effectAllowed = 'move'
  }

  function onDragOver(e, index) {
    e.preventDefault()
    if (disabled || draggedIdx === null || draggedIdx === index) return
    onMoveCategory(draggedIdx, index)
    setDraggedIdx(index)
  }

  function onDragEnd() {
    setDraggedIdx(null)
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
      <div 
        className={`sidebar-list-wrap ${disabled ? 'sidebar-locked' : ''}`}
        onClick={disabled ? onOpenConfig : undefined}
      >
        <div className="sidebar-label">WORKSPACES</div>
        <nav className="sidebar-nav">
        {categories.map((cat, idx) => (
          <div
            key={cat}
            className={`sidebar-item ${cat === activeCategory ? 'active' : ''} ${draggedIdx === idx ? 'dragging' : ''} ${editing === cat ? 'editing' : ''}`}
            onClick={() => onSelect(cat)}
            onDoubleClick={() => handleStartEdit(cat)}
            draggable={!disabled && !editing}
            onDragStart={(e) => onDragStart(e, idx)}
            onDragOver={(e) => onDragOver(e, idx)}
            onDragEnd={onDragEnd}
          >
            {editing === cat ? (
              <div className="sidebar-edit-row" onClick={e => e.stopPropagation()}>
                <input
                  autoFocus
                  value={editValue}
                  onChange={e => setEditValue(e.target.value)}
                  onKeyDown={handleEditKeyDown}
                  onBlur={handleConfirmRename}
                  className="sidebar-edit-input"
                />
                <button className="sidebar-edit-confirm" onClick={handleConfirmRename}>
                  <Check size={14} />
                </button>
              </div>
            ) : (
              <>
                <GripVertical size={14} className="sidebar-item-grip" />
                <FolderOpen size={15} className="sidebar-item-icon" />
                <span className="sidebar-item-name" title={cat}>{cat}</span>
                
                <div className="sidebar-item-actions">
                  <button 
                    className="sidebar-item-move" 
                    onClick={(e) => { e.stopPropagation(); handleMove(idx, -1) }}
                    disabled={idx === 0}
                    title="Subir"
                  >
                    <ChevronUp size={12} />
                  </button>
                  <button 
                    className="sidebar-item-move" 
                    onClick={(e) => { e.stopPropagation(); handleMove(idx, 1) }}
                    disabled={idx === categories.length - 1}
                    title="Bajar"
                  >
                    <ChevronDown size={12} />
                  </button>
                  <button
                    className="sidebar-item-delete"
                    title="Eliminar workspace"
                    onClick={e => { e.stopPropagation(); onDeleteCategory(cat) }}
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </>
            )}
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
      </div>

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
