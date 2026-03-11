import { useState } from 'react'
import { X, RefreshCcw, AlertCircle, Monitor, Layout, CheckCircle2 } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import './SyncModal.css'

export default function SyncModal({ category, validation, onClose, onSynced }) {
  const [syncing, setSyncing] = useState(false)
  const [done, setDone] = useState(false)
  const [resolutions, setResolutions] = useState({})
  const [layoutResolutions, setLayoutResolutions] = useState({})

  const handleMonitorSelect = (index, monitor) => {
    setResolutions(prev => ({ ...prev, [index]: monitor }))
  }

  const handleLayoutSelect = (warningIndex, layoutUuid) => {
    setLayoutResolutions(prev => ({ ...prev, [warningIndex]: layoutUuid }))
  }

  const handleSyncAll = async () => {
    setSyncing(true)
    try {
      // 1. Solve Missing Layouts (importing definitions into PowerToys)
      if (validation.missingLayouts?.length > 0) {
        await bridge.syncWorkspaceLayouts(validation.missingLayouts)
      }

      // 2. Solve Mismatches (changing active layouts in PowerToys)
      const warningsList = validation?.warnings || [];
      for (let i = 0; i < warningsList.length; i++) {
        const w = warningsList[i];
        if (w.type === 'layout_mismatch') {
          const selectedLayoutUuid = layoutResolutions[i] || w.layoutUuid;
          const layoutObj = (validation.availableLayouts || []).find(l => l.uuid === selectedLayoutUuid);
          
          // bridge.changeLayoutAssignment(monitorInstance, monitorName, desktopId, layoutUuid, layoutType)
          await bridge.changeLayoutAssignment(
            w.monitorInstance, 
            w.monitorName, 
            w.desktopId, 
            selectedLayoutUuid, 
            layoutObj?.type || w.layoutType || "custom"
          );
        }
      }

      // 3. Solve Monitor resolutions
      const monitorMissing = warningsList.filter(w => w.type === 'monitor_missing');
      if (monitorMissing.length > 0) {
        const payloadRes = {};
        for (const mw of monitorMissing) {
          payloadRes[mw.itemIndex] = resolutions[mw.itemIndex] || mw.proposedMonitor;
        }
        await bridge.resolveMonitorConflicts(category, payloadRes);
      }

      // Re-validate or just signal parent to refresh
      setDone(true)
      setTimeout(() => {
        onSynced()
        onClose()
      }, 1500)
    } catch (err) {
      console.error("Sync error:", err)
      setSyncing(false)
    }
  }

  const warnings = validation?.warnings || []

  return (
    <div className="dialog-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="dialog sync-modal">
        <div className="dialog-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--accent)' }}>
            <RefreshCcw size={20} className={syncing && !done ? 'fz-spin' : ''} />
            <h2>Sincronizar Workspace: {category}</h2>
          </div>
          <button className="dialog-close" onClick={onClose}><X size={20} /></button>
        </div>

        <div className="dialog-body">
          {done ? (
            <div className="sync-done">
              <CheckCircle2 size={48} color="var(--cat-url)" />
              <p>Workspace sincronizado correctamente</p>
              <span>Los layouts han sido inyectados en PowerToys.</span>
            </div>
          ) : (
            <>
              <div className="sync-intro">
                <AlertCircle size={20} color="#f59e0b" />
                <p>Se han detectado inconsistencias entre la configuración guardada y el entorno actual (monitores o PowerToys).</p>
              </div>

              <div className="sync-warnings-list">
                {warnings.map((w, i) => {
                  let solution = "";
                  const isMonitor = w.type.includes('monitor');
                  
                  if (w.type === 'monitor_missing') {
                    solution = "Se adaptará la posición al monitor seleccionado.";
                  } else if (w.type === 'layout_mismatch') {
                    const selectedUuid = layoutResolutions[i] || w.layoutUuid;
                    const layoutObj = (validation.availableLayouts || []).find(l => l.uuid === selectedUuid);
                    const name = layoutObj ? layoutObj.name : "Ninguno / Libre";
                    solution = `Cambiar layout activo en monitor a '${name}'.`;
                  } else if (w.type === 'desktop_missing') {
                    solution = "Se crearán automáticamente al levantar el workspace.";
                  } else {
                    solution = "Se creará e inyectará el Layout faltante en PowerToys.";
                  }
                  
                  return (
                    <div key={i} className="sync-warning-item">
                      <div className="sync-warning-icon">
                        {isMonitor ? <Monitor size={16} /> : <Layout size={16} />}
                      </div>
                      <div className="sync-warning-content">
                        <div className="sync-conflict">
                          <span className="sync-label">Conflicto:</span>
                          <span className="sync-warning-msg">{w.message}</span>
                        </div>
                        <div className="sync-solution">
                          <span className="sync-label">Solución:</span>
                          <span>{solution}</span>
                          {w.type === 'monitor_missing' && (
                            <div style={{ marginTop: '6px' }}>
                              <select 
                                value={resolutions[w.itemIndex] || w.proposedMonitor}
                                onChange={(e) => handleMonitorSelect(w.itemIndex, e.target.value)}
                                className="sync-monitor-select"
                              >
                                {validation.activeMonitors?.map(mon => (
                                  <option key={mon} value={mon}>{mon}</option>
                                ))}
                              </select>
                            </div>
                          )}

                          {w.type === 'layout_mismatch' && (
                            <div style={{ marginTop: '6px' }}>
                              <select 
                                value={layoutResolutions[i] || w.layoutUuid}
                                onChange={(e) => handleLayoutSelect(i, e.target.value)}
                                className="sync-monitor-select"
                              >
                                {(validation.availableLayouts || []).map(layout => (
                                  <option key={layout.uuid} value={layout.uuid}>
                                    {layout.name}
                                  </option>
                                ))}
                                <option value="">Ninguno / Libre</option>
                              </select>
                            </div>
                          )}
                        </div>
                        {w.itemPath && (
                          <div className="sync-warning-app">App: {w.itemPath.split('\\').pop() || 'Sistema'}</div>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>

              <div className="sync-footer-info">
                Al sincronizar, se intentarán crear los layouts faltantes en PowerToys utilizando las definiciones guardadas en el caché de la aplicación.
              </div>
            </>
          )}
        </div>

        {!done && (
          <div className="dialog-footer">
            <button className="btn-secondary" onClick={onClose} disabled={syncing}>
              Cancelar
            </button>
            <button 
              className="btn-launch" 
              onClick={handleSyncAll}
              disabled={syncing || warnings.length === 0}
            >
              {syncing ? 'Sincronizando...' : 'Sincronizar y Adaptar'}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
