import { Globe, Terminal, Code2, MonitorSmartphone, FileCode, Cog, ArrowUp, ArrowDown, Pencil, Trash2, GripVertical } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import './AppCard.css'

const TYPE_CONFIG = {
  url:        { icon: Globe,              label: 'WEB',      color: 'var(--cat-web)' },
  ide:        { icon: Code2,              label: 'IDE',      color: 'var(--cat-ide)' },
  vscode:     { icon: FileCode,           label: 'IDE',      color: 'var(--cat-ide)' },
  obsidian:   { icon: MonitorSmartphone,  label: 'Obsidian', color: 'var(--cat-obsidian)' },
  powershell: { icon: Terminal,           label: 'Terminal', color: 'var(--cat-terminal)' },
  exe:        { icon: Cog,                label: 'EXE',      color: 'var(--cat-exe)' },
}

function getDisplayName(item) {
  switch (item.type) {
    case 'exe': {
      const parts = item.path.replace(/\\/g, '/').split('/')
      return parts[parts.length - 1].replace(/\.exe$/i, '')
    }
    case 'url':
      try { return new URL(item.path).hostname } catch { return item.path }
    case 'ide':
      return `${item.ide_cmd}: ${item.path.split(/[/\\]/).pop()}`
    case 'vscode':
      return `VSCode: ${item.path.split(/[/\\]/).pop()}`
    case 'powershell':
      return `Terminal: ${item.path.split(/[/\\]/).pop()}`
    case 'obsidian':
      return `Obsidian: ${item.path.split(/[/\\]/).pop()}`
    default:
      return item.path
  }
}

function getSubtitle(item, t) {
  const parts = []
  if (item.desktop && item.desktop !== t('common.default')) {
    // Replace "Escritorio X" with "Desktop X" (or localized equivalent)
    parts.push(item.desktop.replace(/Escritorio\s+(\d+)/i, `${t('common.desktop')} $1`))
  }
  if (item.monitor && item.monitor !== t('common.default')) {
    parts.push(item.monitor)
  }
  if (item.fancyzone && item.fancyzone !== t('common.none')) {
    // Replace "... - Zona X" with "... - Zone X"
    parts.push(item.fancyzone.replace(/Zona\s+(\d+)/i, `${t('common.zone')} $1`))
  }
  return parts.join(' · ')
}

export default function AppCard({ item, index, total, onEdit, onDelete, onMoveUp, onMoveDown, dragging, onDragStart, onDragOver, onDragEnd }) {
  const { t } = useTranslation()
  const cfg = TYPE_CONFIG[item.type] || TYPE_CONFIG.exe
  const IconComponent = cfg.icon

  return (
    <div 
      className={`app-card ${dragging ? 'dragging' : ''} ${item.is_enabled === false ? 'disabled' : ''}`}
      draggable
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragEnd={onDragEnd}
    >
      <div className="app-card-grip">
        <GripVertical size={16} />
      </div>

      {/* Category-colored icon circle */}
      <div className="app-card-avatar" style={{ '--card-color': cfg.color }}>
        <IconComponent size={20} />
      </div>

      {/* Info */}
      <div className="app-card-info">
        <div className="app-card-name" title={item.path}>{getDisplayName(item)}</div>
        <div className="app-card-sub" title={item.path}>{getSubtitle(item, t)}</div>
        {item.delay && item.delay !== '0' && (
          <div className="app-card-delay">⏱ {item.delay}ms</div>
        )}
      </div>

      {/* Type badge */}
      {item.is_enabled !== false ? (
        <span className="app-type-badge" style={{ '--badge-color': cfg.color }}>{cfg.label}</span>
      ) : (
        <span className="app-type-badge" style={{ '--badge-color': '#9e9e9e' }}>{t('app_card.disabled')}</span>
      )}

      {/* Actions — visible only on hover */}
      <div className="app-card-actions">
        <button className="card-btn" title={t('app_card.move_up')} onClick={onMoveUp} disabled={!onMoveUp}>
          <ArrowUp size={14} />
        </button>
        <button className="card-btn" title={t('app_card.move_down')} onClick={onMoveDown} disabled={!onMoveDown}>
          <ArrowDown size={14} />
        </button>
        <button className="card-btn" title={t('app_card.edit')} onClick={onEdit}>
          <Pencil size={14} />
        </button>
        <button className="card-btn danger" title={t('app_card.delete')} onClick={onDelete}>
          <Trash2 size={14} />
        </button>
      </div>
    </div>
  )
}
