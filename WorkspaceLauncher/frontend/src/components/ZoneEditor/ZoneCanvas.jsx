import { useRef } from 'react';
import { ZoneRect } from './ZoneRect';
import { ZoneDivider } from './ZoneDivider';
import { computeDividers } from './ZoneEditorHooks';

export function ZoneCanvas({ zones, selectedIds, onSelectZone, onSplitZone, onMoveDivider, onClearSelection }) {
  const containerRef = useRef(null);

  const dividers = computeDividers(zones);

  const getCanvasDims = () => {
    if (!containerRef.current) return { w: 1, h: 1 };
    return { w: containerRef.current.offsetWidth, h: containerRef.current.offsetHeight };
  };

  return (
    <div
      ref={containerRef}
      onClick={(e) => {
        if (e.target === containerRef.current) onClearSelection();
      }}
      style={{
        position: 'relative',
        width: '100%',
        aspectRatio: '16/9',
        background: '#1a1d23',
        borderRadius: 6,
        overflow: 'hidden',
        border: '1.5px solid rgba(255,255,255,0.1)',
        cursor: 'default',
      }}
    >
      {zones.map(zone => (
        <ZoneRect
          key={zone.id}
          zone={zone}
          selected={selectedIds.has(zone.id)}
          onSelect={onSelectZone}
          onSplit={onSplitZone}
        />
      ))}
      {dividers.map(div => (
        <ZoneDivider
          key={div.id}
          divider={div}
          canvasW={getCanvasDims().w}
          canvasH={getCanvasDims().h}
          onMove={onMoveDivider}
        />
      ))}
      {zones.length === 0 && (
        <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'rgba(255,255,255,0.3)', fontSize: 13 }}>
          Añade zonas usando los presets o haz clic en "Dividir"
        </div>
      )}
    </div>
  );
}
