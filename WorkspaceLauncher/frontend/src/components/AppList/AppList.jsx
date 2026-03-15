import { useState } from 'react'
import { Plus, Play, Inbox, RotateCcw, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import AppCard from './AppCard.jsx'
import './AppList.css'

export default function AppList({ category, items, onAddItem, onEditItem, onDeleteItem, onMoveItem, onLaunch, onRestore, onClean, launchStatus }) {
  const { t } = useTranslation()
  const [draggedIdx, setDraggedIdx] = useState(null)
  const launching = launchStatus && launchStatus.progress < 100

  // HTML5 Drag and Drop logic
  function handleDragStart(e, idx) {
    setDraggedIdx(idx)
    e.dataTransfer.effectAllowed = 'move'
    // Optional: make it look better while dragging
    setTimeout(() => {
      e.target.parentElement.classList.add('is-dragging-child')
    }, 0)
  }

  function handleDragOver(e, idx) {
    e.preventDefault()
    if (draggedIdx === null || draggedIdx === idx) return
    onMoveItem(draggedIdx, idx)
    setDraggedIdx(idx)
  }

  function handleDragEnd(e) {
    setDraggedIdx(null)
    e.target.parentElement.classList.remove('is-dragging-child')
  }

  return (
    <div className="applist">
      {/* Header */}
      <div className="applist-header">
        <div className="applist-title">
          <span className="applist-category">{category || t('app_list.no_category')}</span>
          <span className="applist-count">{items.length} {t('app_list.apps')}</span>
        </div>

        <div className="applist-actions">
          <button className="btn-secondary" onClick={onAddItem}>
            <Plus size={14} /> {t('app_list.add_app')}
          </button>
          <button
            className="btn-secondary"
            onClick={onRestore}
            disabled={launching || items.length === 0}
            title={t('app_list.restore_desc')}
          >
            <RotateCcw size={14} /> {t('app_list.restore')}
          </button>
          <button
            className="btn-secondary btn-danger"
            onClick={onClean}
            disabled={launching || items.length === 0}
            title={t('app_list.clean_desc')}
          >
            <Trash2 size={14} /> {t('app_list.clean')}
          </button>
          <button
            className="btn-launch"
            onClick={onLaunch}
            disabled={launching || items.length === 0}
          >
            {launching ? (
              <><span className="btn-launch-spinner" /> {t('app_list.launching')}</>
            ) : (
              <><Play size={14} /> {t('app_list.launch')}</>
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
            <div>{t('app_list.empty')}</div>
            <button className="btn-secondary" onClick={onAddItem} style={{ marginTop: 12 }}>
              <Plus size={14} /> {t('app_list.add_first')}
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
              dragging={draggedIdx === idx}
              onDragStart={(e) => handleDragStart(e, idx)}
              onDragOver={(e) => handleDragOver(e, idx)}
              onDragEnd={handleDragEnd}
            />
          ))
        )}
      </div>
    </div>
  )
}
