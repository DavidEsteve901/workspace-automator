import { Globe, Terminal, Code2, MonitorSmartphone, FileCode, Cog, ArrowUp, ArrowDown, Pencil, Trash2 } from 'lucide-react'
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

function getSubtitle(item) {
  const parts = []
  if (item.desktop && item.desktop !== 'Por defecto') parts.push(item.desktop)
  if (item.monitor && item.monitor !== 'Por defecto') parts.push(item.monitor)
  if (item.fancyzone && item.fancyzone !== 'Ninguna') parts.push(item.fancyzone)
  return parts.join(' · ')
}

export default function AppCard({ item, index, total, onEdit, onDelete, onMoveUp, onMoveDown }) {
  const cfg = TYPE_CONFIG[item.type] || TYPE_CONFIG.exe
  const IconComponent = cfg.icon

  return (
    <div className="app-card">
      {/* Category-colored icon circle */}
      <div className="app-card-avatar" style={{ '--card-color': cfg.color }}>
        <IconComponent size={20} />
      </div>

      {/* Info */}
      <div className="app-card-info">
        <div className="app-card-name" title={item.path}>{getDisplayName(item)}</div>
        <div className="app-card-sub" title={item.path}>{getSubtitle(item)}</div>
        {item.delay && item.delay !== '0' && (
          <div className="app-card-delay">⏱ {item.delay}ms</div>
        )}
      </div>

      {/* Type badge */}
      <span className="app-type-badge" style={{ '--badge-color': cfg.color }}>{cfg.label}</span>

      {/* Actions — visible only on hover */}
      <div className="app-card-actions">
        <button className="card-btn" title="Subir" onClick={onMoveUp} disabled={!onMoveUp}>
          <ArrowUp size={14} />
        </button>
        <button className="card-btn" title="Bajar" onClick={onMoveDown} disabled={!onMoveDown}>
          <ArrowDown size={14} />
        </button>
        <button className="card-btn" title="Editar" onClick={onEdit}>
          <Pencil size={14} />
        </button>
        <button className="card-btn danger" title="Eliminar" onClick={onDelete}>
          <Trash2 size={14} />
        </button>
      </div>
    </div>
  )
}
