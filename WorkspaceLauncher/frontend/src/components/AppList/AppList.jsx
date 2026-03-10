import { Plus, Play, Inbox } from 'lucide-react'
import AppCard from './AppCard.jsx'
import './AppList.css'

export default function AppList({ category, items, onAddItem, onEditItem, onDeleteItem, onMoveItem, onLaunch, launchStatus }) {
  const launching = launchStatus && launchStatus.progress < 100

  return (
    <div className="applist">
      {/* Header */}
      <div className="applist-header">
        <div className="applist-title">
          <span className="applist-category">{category || 'Sin categoría'}</span>
          <span className="applist-count">{items.length} apps</span>
        </div>

        <div className="applist-actions">
          <button className="btn-secondary" onClick={onAddItem}>
            <Plus size={14} /> Añadir app
          </button>
          <button
            className="btn-launch"
            onClick={onLaunch}
            disabled={launching || items.length === 0}
          >
            {launching ? (
              <><span className="btn-launch-spinner" /> Lanzando...</>
            ) : (
              <><Play size={14} /> Lanzar workspace</>
            )}
          </button>
        </div>
      </div>

      {/* Progress bar */}
      {launchStatus && (
        <div className="launch-progress">
          <div className="launch-progress-bar" style={{ width: `${launchStatus.progress}%` }} />
          <span className="launch-progress-msg">{launchStatus.message}</span>
        </div>
      )}

      {/* Item list */}
      <div className="applist-items">
        {items.length === 0 ? (
          <div className="applist-empty">
            <Inbox size={48} strokeWidth={1} className="applist-empty-icon" />
            <div>Este workspace está vacío</div>
            <button className="btn-secondary" onClick={onAddItem} style={{ marginTop: 12 }}>
              <Plus size={14} /> Añadir primera app
            </button>
          </div>
        ) : (
          items.map((item, idx) => (
            <AppCard
              key={idx}
              item={item}
              index={idx}
              total={items.length}
              onEdit={() => onEditItem(idx)}
              onDelete={() => onDeleteItem(idx)}
              onMoveUp={idx > 0 ? () => onMoveItem(idx, idx - 1) : null}
              onMoveDown={idx < items.length - 1 ? () => onMoveItem(idx, idx + 1) : null}
            />
          ))
        )}
      </div>
    </div>
  )
}
