import { useState } from 'react'
import { X, RefreshCcw, AlertCircle, Monitor, Layout, CheckCircle2, ChevronDown, ChevronRight, Share2 } from 'lucide-react'
import { bridge } from '../../api/bridge.js'
import { renderZones } from '../../utils/fzUtils.jsx'
import './SyncModal.css'

export default function SyncModal({ category, validation, onClose, onSynced, fzSyncEnabled }) {
  const [syncing, setSyncing] = useState(false)
  const [done, setDone] = useState(false)
  const [resolutions, setResolutions] = useState({})
  const [layoutResolutions, setLayoutResolutions] = useState({})
  const [expandedWarnings, setExpandedWarnings] = useState({})

  const handleMonitorSelect = (index, monitor) => {
    setResolutions(prev => ({ ...prev, [index]: monitor }))
  }

  const handleLayoutSelect = (warningIndex, layoutUuid) => {
    setLayoutResolutions(prev => ({ ...prev, [warningIndex]: layoutUuid }))
  }

  const toggleExpand = (index) => {
    setExpandedWarnings(prev => ({ ...prev, [index]: !prev[index] }))
  }

  const handleSyncAll = async () => {
    setSyncing(true)
    try {
      // 1. Solve Missing Layouts (importing definitions into PowerToys) - ONLY IF FZ SYNC IS ON
      if (fzSyncEnabled && validation.missingLayouts?.length > 0) {
        await bridge.syncWorkspaceLayouts(validation.missingLayouts)
      }

      // 2. Solve Mismatches (changing active layouts)
      const warningsList = validation?.warnings || [];
      for (let i = 0; i < warningsList.length; i++) {
        const w = warningsList[i];
        if (w.type === 'layout_mismatch') {
          const selectedLayoutUuid = layoutResolutions[i] || w.layoutUuid;
          
          if (fzSyncEnabled) {
              const layoutObj = (validation.availableLayouts || []).find(l => l.uuid === selectedLayoutUuid);
              await bridge.changeLayoutAssignment(
                w.monitorInstance, 
                w.monitorName, 
                w.monitorSerial,
                w.desktopId, 
                selectedLayoutUuid, 
                layoutObj?.type || w.layoutType || "custom"
              );
          } else {
              // CZE sync
              await bridge.czeSetLayoutByMonitor({
                  monitorInstance: w.monitorInstance,
                  desktopId: w.desktopId,
                  layoutId: selectedLayoutUuid
              });
          }
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
              <span>{fzSyncEnabled ? 'Los layouts han sido inyectados en PowerToys.' : 'La configuración del motor nativo ha sido actualizada.'}</span>
            </div>
          ) : (
            <>
              <div className="sync-intro">
                <AlertCircle size={20} color="#f59e0b" />
                <p>
                  Se han detectado inconsistencias entre la configuración guardada y el entorno actual 
                  ({fzSyncEnabled ? 'monitores o PowerToys' : 'monitores o layouts locales'}).
                </p>
              </div>

              {fzSyncEnabled && validation?.missingLayouts?.length > 0 && (
                <div className="sync-intro" style={{ background: 'rgba(16, 185, 129, 0.1)', borderColor: 'rgba(16, 185, 129, 0.3)', marginTop: '-8px' }}>
                  <Share2 size={20} color="#10b981" />
                  <p style={{ color: '#10b981' }}>
                    <strong>Aviso de Portabilidad:</strong> Este workspace contiene layouts diseñados en otro equipo. Aparecerán listados abajo como "Diseños a Importar" y se crearán automáticamente en tu FancyZones local al sincronizar.
                  </p>
                </div>
              )}

              <div className="sync-warnings-list">
                {fzSyncEnabled && warnings.filter(w => w.type === 'layout_missing').length > 0 && (
                  <div className="sync-warnings-group">
                    <div className="sync-warnings-group-title">
                      <Share2 size={16} /> Diseños a Importar (Portabilidad)
                    </div>
                    {warnings.map((w, i) => {
                      if (w.type !== 'layout_missing') return null;
                      return renderWarning(w, i);
                    })}
                  </div>
                )}

                <div className="sync-warnings-group">
                  <div className="sync-warnings-group-title">
                    <AlertCircle size={16} /> Conflictos de Entorno
                  </div>
                  {warnings.map((w, i) => {
                    if (fzSyncEnabled && w.type === 'layout_missing') return null;
                    try {
                        return renderWarning(w, i);
                    } catch (e) {
                        console.error("renderWarning error:", e, w);
                        return (
                            <div key={i} className="sync-warning-item error">
                                <AlertCircle size={16} color="var(--cat-error)" />
                                <span>Error al renderizar conflicto: {w.message}</span>
                            </div>
                        );
                    }
                  })}
                </div>
              </div>

              <div className="sync-footer-info">
                {fzSyncEnabled 
                  ? 'Al sincronizar, se intentarán crear los layouts faltantes en PowerToys utilizando las definiciones guardadas en el caché de la aplicación.'
                  : 'Al sincronizar, se ajustarán los layouts nativos asignados a cada monitor para que coincidan con los guardados en el workspace.'}
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

  function renderWarning(w, i) {
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
      solution = fzSyncEnabled 
        ? "Se creará e inyectará el Layout faltante en PowerToys."
        : "Se asignará el diseño nativo configurado.";
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

                        {/* Layout Details / Visualization */}
                        {(w.type === 'layout_mismatch' || w.type === 'layout_missing') && (
                          <>
                            <button 
                              className="sync-details-toggle"
                              onClick={() => toggleExpand(i)}
                            >
                              {expandedWarnings[i] ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                              {expandedWarnings[i] ? 'Ocultar detalles' : 'Ver detalles del diseño'}
                              {w.type === 'layout_missing' && (
                                <span className="portability-badge">
                                  <Share2 size={10} /> Portabilidad
                                </span>
                              )}
                            </button>

                            {expandedWarnings[i] && (
                              <div className="sync-layout-details">
                                <div className="sync-layout-preview-container">
                                  {w.type === 'layout_mismatch' && (
                                    <>
                                      <div className="sync-layout-preview-item">
                                        <div className="sync-layout-preview-label">Activo actualmente</div>
                                        <div className="sync-layout-preview-name">{w.activeLayout}</div>
                                        <div className="sync-layout-preview-box">
                                          {w.activeInfo && renderZones(w.activeInfo, -1)}
                                          {!w.activeInfo && <div className="fz-no-preview">Sin zonas</div>}
                                        </div>
                                      </div>
                                      <div className="sync-layout-arrow">
                                        <ChevronRight size={20} />
                                      </div>
                                    </>
                                  )}
                                  <div className="sync-layout-preview-item">
                                    <div className="sync-layout-preview-label">Requerido por Workspace</div>
                                    <div className="sync-layout-preview-name">{w.assignedLayout || w.layoutName}</div>
                                    <div className="sync-layout-preview-box">
                                      {(w.assignedInfo || w.info) && renderZones(w.assignedInfo || w.info, -1)}
                                      {!(w.assignedInfo || w.info) && <div className="fz-no-preview">Cargando...</div>}
                                    </div>
                                  </div>
                                </div>
                                
                                {w.type === 'layout_missing' && (
                                  <div className="sync-portability-info">
                                    Este diseño fue creado en otro equipo pero está guardado en el caché de la aplicación. 
                                    Al sincronizar, se creará automáticamente en este equipo.
                                  </div>
                                )}
                              </div>
                            )}
                          </>
                        )}
                      </div>
                    </div>
                  );
  }
}
