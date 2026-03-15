import { useState, useEffect, useRef } from 'react'
import { Terminal, Copy, Trash2, ChevronDown, ChevronUp } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { onEvent, offEvent } from '../../api/bridge.js'
import './LogConsole.css'

export default function LogConsole() {
  const { t } = useTranslation()
  const [logs, setLogs] = useState([])
  const [expanded, setExpanded] = useState(false)
  const bottomRef = useRef(null)

  useEffect(() => {
    const logHandler = (entry) => {
      setLogs(prev => [...prev, entry].slice(-500)) // Keep last 500
    }
    const errorHandler = (data) => {
      setLogs(prev => [...prev, {
        timestamp: new Date().toLocaleTimeString(),
        level: 'error',
        message: data.message || 'Error desconocido del sistema'
      }].slice(-500))
    }
    
    onEvent('system_log', logHandler)
    onEvent('error', errorHandler)
    return () => {
      offEvent('system_log', logHandler)
      offEvent('error', errorHandler)
    }
  }, [])

  useEffect(() => {
    if (expanded) {
      bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
    }
  }, [logs, expanded])

  function clearLogs() {
    setLogs([])
  }

  function copyToClipboard() {
    const text = logs.map(l => `[${l.timestamp}] [${l.level.toUpperCase()}] ${l.message}`).join('\n')
    navigator.clipboard.writeText(text)
  }

  return (
    <div className={`log-console ${expanded ? 'expanded' : ''}`}>
      <div className="log-header" onClick={() => setExpanded(!expanded)}>
        <div className="log-title">
          <Terminal size={14} />
          <span>{t('log_console.title')}</span>
          <span className="log-badge">{logs.length}</span>
        </div>
        <div className="log-actions">
          {expanded && (
            <>
              <button onClick={e => { e.stopPropagation(); copyToClipboard(); }} title={t('log_console.copy')}><Copy size={14} /></button>
              <button onClick={e => { e.stopPropagation(); clearLogs(); }} title={t('log_console.clear')}><Trash2 size={14} /></button>
            </>
          )}
          {expanded ? <ChevronDown size={16} /> : <ChevronUp size={16} />}
        </div>
      </div>

      {expanded && (
        <div className="log-body">
          {logs.length === 0 ? (
            <div className="log-empty">{t('log_console.empty')}</div>
          ) : (
            logs.map((l, i) => (
              <div key={i} className={`log-line ${l.level}`}>
                <span className="log-time">[{l.timestamp}]</span>
                <span className="log-msg">{l.message}</span>
              </div>
            ))
          )}
          <div ref={bottomRef} />
        </div>
      )}
    </div>
  )
}
