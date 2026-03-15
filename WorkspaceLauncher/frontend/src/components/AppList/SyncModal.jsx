import { useState } from 'react'
import { X, RefreshCcw, AlertCircle, Monitor, Layout, CheckCircle2, ChevronDown, ChevronRight, Share2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { bridge } from '../../api/bridge.js'
import { renderZones } from '../../utils/fzUtils.jsx'
import PremiumSelect from '../PremiumSelect.jsx'
import './SyncModal.css'

export default function SyncModal({ category, validation, onClose, onSynced, fzSyncEnabled }) {
  const { t } = useTranslation()
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
            <h2>{t('modals.sync_title', { category })}</h2>
          </div>
          <button className="dialog-close" onClick={onClose}><X size={20} /></button>
        </div>

        <div className="dialog-body">
          {done ? (
            <div className="sync-done">
              <CheckCircle2 size={48} color="var(--cat-url)" />
              <p>{t('modals.sync_done_msg')}</p>
              <span>{fzSyncEnabled ? t('modals.sync_fz_success') : t('modals.sync_cze_success')}</span>
            </div>
          ) : (
            <>
              <div className="sync-intro">
                <AlertCircle size={20} color="#f59e0b" />
                <p>
                  {t('modals.sync_conflict_intro')}
                  ({fzSyncEnabled ? t('modals.sync_intro_fz') : t('modals.sync_intro_cze')}).
                </p>
              </div>

              {fzSyncEnabled && validation?.missingLayouts?.length > 0 && (
                <div className="sync-intro" style={{ background: 'var(--success-dim)', borderColor: 'var(--success)', marginTop: '-8px' }}>
                  <Share2 size={20} color="var(--success)" />
                  <p style={{ color: 'var(--success-text)' }}>
                    <strong>{t('modals.sync_portability_title')}:</strong> {t('modals.sync_portability_msg')}
                  </p>
                </div>
              )}

              <div className="sync-warnings-list">
                {fzSyncEnabled && warnings.filter(w => w.type === 'layout_missing').length > 0 && (
                  <div className="sync-warnings-group">
                    <div className="sync-warnings-group-title">
                      <Share2 size={16} /> {t('modals.layouts_to_import')}
                    </div>
                    {warnings.map((w, i) => {
                      if (w.type !== 'layout_missing') return null;
                      return renderWarning(w, i);
                    })}
                  </div>
                )}

                <div className="sync-warnings-group">
                  <div className="sync-warnings-group-title">
                    <AlertCircle size={16} /> {t('modals.env_conflicts')}
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
                  ? t('modals.sync_fz_info')
                  : t('modals.sync_cze_info')}
              </div>
            </>
          )}
        </div>

        {!done && (
          <div className="dialog-footer">
            <button className="btn-secondary" onClick={onClose} disabled={syncing}>
              {t('common.cancel')}
            </button>
            <button 
              className="btn-launch" 
              onClick={handleSyncAll}
              disabled={syncing || warnings.length === 0}
            >
              {syncing ? t('common.syncing') : t('common.sync_and_adapt')}
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
      solution = t('modals.solution_monitor');
    } else if (w.type === 'layout_mismatch') {
      const selectedUuid = layoutResolutions[i] || w.layoutUuid;
      const layoutObj = (validation.availableLayouts || []).find(l => l.uuid === selectedUuid);
      const name = layoutObj ? layoutObj.name : (t('common.none') + " / " + t('common.free'));
      solution = t('modals.solution_layout', { name });
    } else if (w.type === 'desktop_missing') {
      solution = t('modals.solution_desktop');
    } else {
      solution = fzSyncEnabled 
        ? t('modals.solution_fz_create')
        : t('modals.solution_cze_assign');
    }
    
    return (
      <div key={i} className="sync-warning-item">
                      <div className="sync-warning-icon">
                        {isMonitor ? <Monitor size={16} /> : <Layout size={16} />}
                      </div>
                      <div className="sync-warning-content">
                        <div className="sync-conflict">
                          <span className="sync-label">{t('common.conflict')}:</span>
                          <span className="sync-warning-msg">{w.message}</span>
                        </div>
                        <div className="sync-solution">
                          <span className="sync-label">{t('common.solution')}:</span>
                          <span>{solution}</span>
                          {w.type === 'monitor_missing' && (
                            <div style={{ marginTop: '6px' }}>
                              <PremiumSelect 
                                value={resolutions[w.itemIndex] || w.proposedMonitor}
                                options={validation.activeMonitors?.map(mon => ({ value: mon, label: mon }))}
                                onChange={(val) => handleMonitorSelect(w.itemIndex, val)}
                              />
                            </div>
                          )}

                          {w.type === 'layout_mismatch' && (
                            <div style={{ marginTop: '6px' }}>
                              <PremiumSelect 
                                value={layoutResolutions[i] || w.layoutUuid}
                                options={[
                                  ...(validation.availableLayouts || []).map(layout => ({ value: layout.uuid, label: layout.name })),
                                  { value: '', label: 'Ninguno / Libre' }
                                ]}
                                onChange={(val) => handleLayoutSelect(i, val)}
                              />
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
                              {expandedWarnings[i] ? t('common.hide_details') : t('common.view_details')}
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
                                        <div className="sync-layout-preview-label">{t('modals.active_now')}</div>
                                        <div className="sync-layout-preview-name">{w.activeLayout}</div>
                                        <div className="sync-layout-preview-box">
                                          {w.activeInfo && renderZones(w.activeInfo, -1)}
                                          {!w.activeInfo && <div className="fz-no-preview">{t('common.no_zones')}</div>}
                                        </div>
                                      </div>
                                      <div className="sync-layout-arrow">
                                        <ChevronRight size={20} />
                                      </div>
                                    </>
                                  )}
                                  <div className="sync-layout-preview-item">
                                    <div className="sync-layout-preview-label">{t('modals.required_by_ws')}</div>
                                    <div className="sync-layout-preview-name">{w.assignedLayout || w.layoutName}</div>
                                    <div className="sync-layout-preview-box">
                                      {(w.assignedInfo || w.info) && renderZones(w.assignedInfo || w.info, -1)}
                                      {!(w.assignedInfo || w.info) && <div className="fz-no-preview">Cargando...</div>}
                                    </div>
                                  </div>
                                </div>
                                
                                {w.type === 'layout_missing' && (
                                  <div className="sync-portability-info">
                                    {t('modals.sync_portability_details')}
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
