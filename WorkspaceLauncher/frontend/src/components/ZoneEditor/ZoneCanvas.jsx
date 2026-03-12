import { useRef, useState, useEffect, useCallback } from 'react';
import { ZoneRect } from './ZoneRect';
import { ZoneDivider } from './ZoneDivider';
import { computeGridDividers } from './ZoneEditorHooks';

export function ZoneCanvas({ grid, zones, spacing, selectedIds, onSelectZone, onSplitZone, onMoveDivider, onRemoveDivider, onClearSelection }) {
  const containerRef = useRef(null);
  const [splitMode, setSplitMode] = useState('v'); // 'v' or 'h'
  const [preview, setPreview] = useState(null); // { zoneId, fracX, fracY }
  const [hoveredDiv, setHoveredDiv] = useState(null);

  const dividers = grid ? computeGridDividers(grid) : [];

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
    setPreview({ zoneId, x, y });
  }, []);

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
        />
      ))}

      {/* Zone Highlight */}
      {previewZone && (
        <div style={{
          position: 'absolute',
          left: `${(previewZone.x * 100).toFixed(3)}%`,
          top: `${(previewZone.y * 100).toFixed(3)}%`,
          width: `${(previewZone.w * 100).toFixed(3)}%`,
          height: `${(previewZone.h * 100).toFixed(3)}%`,
          background: 'rgba(255, 59, 48, 0.08)',
          pointerEvents: 'none',
          zIndex: 5,
        }} />
      )}

      {/* Preview Line */}
      {preview && previewZone && (
        <div style={{
            position: 'absolute',
            left: `${((previewZone.x + (splitMode === 'v' ? preview.x * previewZone.w : 0)) * 100).toFixed(3)}%`,
            top: `${((previewZone.y + (splitMode === 'h' ? preview.y * previewZone.h : 0)) * 100).toFixed(3)}%`,
            width: splitMode === 'v' ? 2 : `${(previewZone.w * 100).toFixed(3)}%`,
            height: splitMode === 'h' ? 2 : `${(previewZone.h * 100).toFixed(3)}%`,
            background: 'var(--fz-red)',
            opacity: 0.8,
            pointerEvents: 'none',
            zIndex: 30,
            boxShadow: '0 0 10px var(--accent-glow)'
        }} />
      )}

      {dividers.map(div => (
        <ZoneDivider
          key={div.id}
          divider={div}
          canvasW={getCanvasDims().w}
          canvasH={getCanvasDims().h}
          onMove={onMoveDivider}
          isHovered={hoveredDiv?.id === div.id}
          onHover={setHoveredDiv}
        />
      ))}
      {(!zones || zones.length === 0) && (
        <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'rgba(255,255,255,0.3)', fontSize: 13, pointerEvents: 'none' }}>
          Pulsa Tab para cambiar entre vertical/horizontal. Shift + Clic para dividir.
        </div>
      )}
    </div>
  );
}
