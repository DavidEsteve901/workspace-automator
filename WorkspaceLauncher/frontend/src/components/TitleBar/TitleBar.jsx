import { Minus, Square, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { bridge } from '../../api/bridge.js'
import logo from '../../assets/logo.ico'
import './TitleBar.css'

export default function TitleBar({ title }) {
  const { t } = useTranslation()
  const displayTitle = title || t('title_bar.title') || 'Workspace Launcher'
  const handleMouseDown = (e) => {
    // We can drag from the titlebar-drag container
    if (e.target.closest('.titlebar-drag')) {
      bridge.startDrag()
    }
  }

  return (
    <div className="titlebar" onMouseDown={handleMouseDown}>
      {/* Drag region — covers most of the bar */}
      <div className="titlebar-drag">
        <img src={logo} className="titlebar-logo-img" alt="" />
        <span className="titlebar-title">{displayTitle}</span>
      </div>

      {/* Window controls */}
      <div className="titlebar-controls">
        <button className="wc-btn minimize" title={t('title_bar.minimize')} onClick={() => bridge.minimize()}>
          <Minus size={14} />
        </button>
        <button className="wc-btn maximize" title={t('title_bar.maximize')} onClick={() => bridge.maximize()}>
          <Square size={12} />
        </button>
        <button className="wc-btn close" title={t('title_bar.close')} onClick={() => bridge.close()}>
          <X size={14} />
        </button>
      </div>
    </div>
  )
}
