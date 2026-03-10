import { useState, useEffect, useRef } from 'react'
import { Terminal, Copy, Trash2, ChevronDown, ChevronUp } from 'lucide-react'
import { onEvent, offEvent } from '../../api/bridge.js'
import './LogConsole.css'

export default function LogConsole() {
  const [logs, setLogs] = useState([])
  const [expanded, setExpanded] = useState(false)
  const bottomRef = useRef(null)

  useEffect(() => {
    const handler = (entry) => {
      setLogs(prev => [...prev, entry].slice(-500)) // Keep last 500
    }
    onEvent('system_log', handler)
    return () => offEvent('system_log', handler)
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
          <span>Consola del Sistema</span>
          <span className="log-badge">{logs.length}</span>
        </div>
        <div className="log-actions">
          {expanded && (
            <>
              <button onClick={e => { e.stopPropagation(); copyToClipboard(); }} title="Copiar historial"><Copy size={14} /></button>
              <button onClick={e => { e.stopPropagation(); clearLogs(); }} title="Limpiar"><Trash2 size={14} /></button>
            </>
          )}
          {expanded ? <ChevronDown size={16} /> : <ChevronUp size={16} />}
        </div>
      </div>

      {expanded && (
        <div className="log-body">
          {logs.length === 0 ? (
            <div className="log-empty">No hay actividad registrada.</div>
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
