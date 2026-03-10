import { X, AlertTriangle } from 'lucide-react'
import './ConfirmModal.css'

export default function ConfirmModal({ title, message, onConfirm, onCancel, confirmText = "Eliminar", cancelText = "Cancelar", isDanger = true }) {
  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onCancel()}>
      <div className="dialog confirm-modal">
        <div className="dialog-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: isDanger ? 'var(--danger)' : 'var(--accent)' }}>
            <AlertTriangle size={20} />
            <h2>{title}</h2>
          </div>
          <button className="dialog-close" onClick={onCancel}><X size={20} /></button>
        </div>

        <div className="dialog-body">
          <p className="confirm-message">{message}</p>
        </div>

        <div className="dialog-footer">
          <button className="btn-secondary" onClick={onCancel}>
            {cancelText}
          </button>
          <button 
            className={`btn-launch ${isDanger ? 'btn-danger' : ''}`} 
            onClick={onConfirm}
            style={{ minWidth: '120px' }}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  )
}
