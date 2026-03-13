import { useRef, useState, useEffect, useCallback } from 'react';
import { ZoneRect } from './ZoneRect';
import { ZoneDivider } from './ZoneDivider';
import { computeGridDividers } from './ZoneEditorHooks';

export function ZoneCanvas({ grid, zones, spacing, selectedIds, onSelectZone, onSplitZone, onMoveDivider, onRemoveDivider, onClearSelection, onCommit }) {
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
          setHoveredDiv(null);
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
        />
      ))}

      {/* 3) Fixed Hover Reference Line: SOLID Accent Color */}
      {!isDragging && !hoveredDiv && preview && previewZone && (
        <div style={{
          position: 'absolute',
          left: `${((previewZone.x + (splitMode === 'v' ? preview.x * previewZone.w : 0)) * 100).toFixed(6)}%`,
          top: `${((previewZone.y + (splitMode === 'h' ? preview.y * previewZone.h : 0)) * 100).toFixed(6)}%`,
          width: splitMode === 'v' ? 3 : `${(previewZone.w * 100).toFixed(6)}%`,
          height: splitMode === 'h' ? 3 : `${(previewZone.h * 100).toFixed(6)}%`,
          background: 'var(--fz-accent, var(--accent, #fff))', // White fallback instead of blue
          opacity: 1,
          pointerEvents: 'none',
          zIndex: 50,
          boxShadow: '0 0 15px var(--fz-accent-glow), 0 0 5px rgba(0,0,0,0.4)',
          borderRadius: 2
        }} />
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
    </div>
  );
}
