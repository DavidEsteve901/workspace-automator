import { useState, useEffect } from 'react'
import { X, Trash2, CheckSquare, Square, AlertTriangle, Monitor } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { bridge } from '../../api/bridge.js'
import './CleanModal.css'

export default function CleanModal({ category, onClose }) {
  const { t } = useTranslation()
  const [windows, setWindows] = useState([])
  const [selectedHwnds, setSelectedHwnds] = useState(new Set())
  const [loading, setLoading] = useState(true)
  const [closing, setClosing] = useState(false)

  useEffect(() => {
    async function load() {
      setLoading(true)
      try {
        const data = await bridge.getWindowsToClean(category)
        setWindows(data || [])
        // Select all by default
        setSelectedHwnds(new Set((data || []).map(w => w.hwnd)))
      } catch (err) {
        console.error("Error loading windows to clean:", err)
      }
      setLoading(false)
    }
    load()
  }, [category])

  const toggleSelect = (hwnd) => {
    const next = new Set(selectedHwnds)
    if (next.has(hwnd)) next.delete(hwnd)
    else next.add(hwnd)
    setSelectedHwnds(next)
  }

  const toggleAll = () => {
    if (selectedHwnds.size === windows.length) {
      setSelectedHwnds(new Set())
    } else {
      setSelectedHwnds(new Set(windows.map(w => w.hwnd)))
    }
  }

  const handleConfirm = async () => {
    if (selectedHwnds.size === 0) return
    setClosing(true)
    try {
      await bridge.closeWindows(Array.from(selectedHwnds))
      onClose()
    } catch (err) {
      console.error("Error closing windows:", err)
      setClosing(false)
    }
  }

  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="dialog clean-modal">
        <div className="dialog-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--cat-exe)' }}>
            <Trash2 size={20} />
            <h2>{t('clean_modal.title', { category })}</h2>
          </div>
          <button className="dialog-close" onClick={onClose}><X size={20} /></button>
        </div>

        <div className="dialog-body">
          <div className="clean-warning">
            <AlertTriangle size={18} />
            <p>{t('clean_modal.intro')}</p>
          </div>

          {loading ? (
            <div className="clean-loading">
              <div className="spinner-small" />
              <span>{t('clean_modal.searching')}</span>
            </div>
          ) : windows.length === 0 ? (
            <div className="clean-empty">
              <Monitor size={32} opacity={0.3} />
              <p>{t('clean_modal.empty')}</p>
            </div>
          ) : (
            <div className="clean-list-container">
              <div className="clean-list-header" onClick={toggleAll}>
                {selectedHwnds.size === windows.length ? <CheckSquare size={16} color="var(--accent)" /> : <Square size={16} />}
                <span>{t('clean_modal.select_all', { count: windows.length })}</span>
              </div>
              <div className="clean-list">
                {windows.map(win => (
                  <div 
                    key={win.hwnd} 
                    className={`clean-item ${selectedHwnds.has(win.hwnd) ? 'selected' : ''}`}
                    onClick={() => toggleSelect(win.hwnd)}
                  >
                    <div className="clean-item-check">
                      {selectedHwnds.has(win.hwnd) ? <CheckSquare size={16} color="var(--accent)" /> : <Square size={16} />}
                    </div>
                    <div className="clean-item-info">
                      <div className="clean-item-title">{win.title || t('clean_modal.no_title')}</div>
                      <div className="clean-item-sub">
                        <span className="clean-process">{win.processName}.exe</span>
                        <span className="clean-sep">·</span>
                        <span className="clean-app-ref">{t('clean_modal.reference', { name: win.appName })}</span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="dialog-footer">
          <button className="btn-secondary" onClick={onClose} style={{ border: 'none', background: 'transparent' }}>
            {t('common.cancel')}
          </button>
          <button 
            className="btn-launch btn-danger-action" 
            onClick={handleConfirm}
            disabled={selectedHwnds.size === 0 || closing}
          >
            {closing ? t('clean_modal.closing') : t('clean_modal.close_selected', { count: selectedHwnds.size })}
          </button>
        </div>
      </div>
    </div>
  )
}
