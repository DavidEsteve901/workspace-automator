import { useRef, useState, useEffect, useCallback } from 'react';
import { ZoneRect } from './ZoneRect';
import { ZoneDivider } from './ZoneDivider';
import { computeGridDividers } from './ZoneEditorHooks';

export function ZoneCanvas({ grid, zones, spacing, selectedIds, onSelectZone, onSplitZone, onMoveDivider, onRemoveDivider, onClearSelection, onCommit, occupancyMap = {} }) {
  const containerRef = useRef(null);
  const [splitMode, setSplitMode] = useState('v');
  const [preview, setPreview] = useState(null);
  const [hoveredDiv, setHoveredDiv] = useState(null);
  const [isDragging, setIsDragging] = useState(false);
  const activeHoverId = useRef(null);

  const dividers = grid ? computeGridDividers(grid) : [];

  const commitTimer = useRef(null);
  const safeCommit = useCallback(() => {
    if (commitTimer.current) clearTimeout(commitTimer.current);
    commitTimer.current = setTimeout(() => {
      onCommit?.();
      commitTimer.current = null;
    }, 50);
  }, [onCommit]);

  const handleHover = useCallback((div) => {
    activeHoverId.current = div?.id || null;
    setHoveredDiv(div);
  }, []);

  useEffect(() => {
    const handleKeys = (e) => {
      if (e.key === 'Tab') {
        e.preventDefault();
        setSplitMode(prev => prev === 'v' ? 'h' : 'v');
      } else if (e.key === 'Delete' || e.key === 'Backspace') {
        if (hoveredDiv) {
          onRemoveDivider(hoveredDiv);
          handleHover(null);
        }
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [hoveredDiv, onRemoveDivider]);

  const getCanvasDims = () => {
    if (!containerRef.current) return { w: 1, h: 1 };
    return { w: containerRef.current.offsetWidth, h: containerRef.current.offsetHeight };
  };

  const handleZoneMouseMove = useCallback((zoneId, x, y) => {
    if (isDragging || activeHoverId.current) {
      if (preview) setPreview(null);
      return;
    }
    setPreview({ zoneId, x, y });
  }, [isDragging, preview]);

  const handleCanvasMouseLeave = () => {
    setPreview(null);
  };

  const handleZoneSplit = useCallback((zoneId, x, y) => {
    onSplitZone(zoneId, x, y, splitMode);
  }, [splitMode, onSplitZone]);

  const previewZone = preview ? zones.find(z => z.id === preview.zoneId) : null;

  // Build per-zone occupancy arrays
  const getZoneApps = (zoneIndex) => {
    const val = occupancyMap[zoneIndex];
    if (!val) return [];
    return val.split(',').map(s => s.trim()).filter(Boolean);
  };

  return (
    <div
      ref={containerRef}
      onMouseLeave={handleCanvasMouseLeave}
      onClick={(e) => {
        if (e.target === containerRef.current) onClearSelection();
      }}
      style={{
        position: 'absolute',
        inset: 0,
        background: 'transparent',
        cursor: 'default',
      }}
    >
      {(zones || []).map((zone, i) => (
        <ZoneRect
          key={zone.id}
          zone={zone}
          spacing={spacing}
          selected={selectedIds.has(zone.id)}
          onSelect={onSelectZone}
          onSplit={handleZoneSplit}
          onMouseMove={handleZoneMouseMove}
          canvasW={getCanvasDims().w}
          canvasH={getCanvasDims().h}
          index={i + 1}
          isDragging={isDragging}
          occupancyApps={getZoneApps(i)}
        />
      ))}

      {/* Split preview line — premium accent with glow */}
      {!isDragging && !hoveredDiv && preview && previewZone && (
        <div style={{
          position: 'absolute',
          left: splitMode === 'v'
            ? `calc(${(previewZone.x + preview.x * previewZone.w) * 100}% - 2px)`
            : `${previewZone.x * 100}%`,
          top: splitMode === 'h'
            ? `calc(${(previewZone.y + preview.y * previewZone.h) * 100}% - 2px)`
            : `${previewZone.y * 100}%`,
          width: splitMode === 'v' ? 4 : `${previewZone.w * 100}%`,
          height: splitMode === 'h' ? 4 : `${previewZone.h * 100}%`,
          background: 'linear-gradient(90deg, transparent, var(--accent, #00D2FF), transparent)',
          pointerEvents: 'none',
          zIndex: 9999,
          boxShadow: '0 0 12px rgba(var(--accent-rgb, 0, 210, 255), 0.6), 0 0 4px rgba(0,0,0,0.8)',
          borderRadius: 3,
          animation: 'splitLineAppear 0.15s ease-out',
        }} />
      )}

      {/* Split mode indicator */}
      {!isDragging && (
        <div style={{
          position: 'absolute',
          bottom: 12,
          left: '50%',
          transform: 'translateX(-50%)',
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          background: 'rgba(0,0,0,0.6)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255,255,255,0.08)',
          borderRadius: 99,
          padding: '5px 14px',
          pointerEvents: 'none',
          zIndex: 100,
          opacity: 0.65,
        }}>
          <div style={{
            width: 18,
            height: 14,
            borderRadius: 3,
            border: '1.5px solid rgba(255,255,255,0.3)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            position: 'relative',
            overflow: 'hidden',
          }}>
            <div style={{
              position: 'absolute',
              ...(splitMode === 'v'
                ? { left: '50%', top: 0, bottom: 0, width: 1.5, background: 'var(--accent, #00D2FF)' }
                : { top: '50%', left: 0, right: 0, height: 1.5, background: 'var(--accent, #00D2FF)' }),
            }} />
          </div>
          <span style={{ fontSize: 10, fontWeight: 700, color: 'rgba(255,255,255,0.5)', letterSpacing: '0.04em' }}>
            {splitMode === 'v' ? 'VERTICAL' : 'HORIZONTAL'} · Tab para cambiar
          </span>
        </div>
      )}

      {dividers.map(div => (
        <ZoneDivider
          key={div.id}
          divider={div}
          canvasW={getCanvasDims().w}
          canvasH={getCanvasDims().h}
          onMove={onMoveDivider}
          onCommit={safeCommit}
          isHovered={hoveredDiv?.id === div.id}
          onHover={handleHover}
          onDraggingChange={setIsDragging}
        />
      ))}

      <style>{`
        @keyframes splitLineAppear {
          from { opacity: 0; transform: scaleX(0.5); }
          to   { opacity: 1; transform: scaleX(1); }
        }
      `}</style>
    </div>
  );
}
