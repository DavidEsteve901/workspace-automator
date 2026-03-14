import { useRef } from 'react';

const HANDLE_SIZE = 16;
const MIN_ZONE_FRAC = 1 / 100;

export function ZoneDivider({ divider, canvasW, canvasH, onMove, onCommit, isHovered, onHover, onDraggingChange }) {
  const dragging = useRef(false);
  const isV = divider.axis === 'v';

  const handleMouseDown = (e) => {
    e.preventDefault();
    e.stopPropagation();
    dragging.current = true;
    onDraggingChange?.(true);

    const canvasRect = e.currentTarget.parentElement.getBoundingClientRect();

    const onMouseMove = (me) => {
      // Direct absolute coordinate mapping for 100% precision
      const posPx = isV ? (me.clientX - canvasRect.left) : (me.clientY - canvasRect.top);
      const canvasSize = isV ? canvasRect.width : canvasRect.height;
      const targetPos = posPx / canvasSize;
      
      onMove(divider, targetPos);
    };

    const onMouseUp = () => {
      dragging.current = false;
      onDraggingChange?.(false);
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      onCommit?.();
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  const handleMouseEnter = () => onHover(divider);
  const handleMouseLeave = () => onHover(null); 

  const hitStyle = isV
    ? {
      position: 'absolute',
      left: `calc(${(divider.position * 100).toFixed(4)}% - ${HANDLE_SIZE / 2}px)`,
      top: `${(divider.overlapStart * 100).toFixed(4)}%`,
      width: HANDLE_SIZE,
      height: `${((divider.overlapEnd - divider.overlapStart) * 100).toFixed(4)}%`,
      cursor: 'col-resize',
      zIndex: 30,
    }
    : {
      position: 'absolute',
      top: `calc(${(divider.position * 100).toFixed(4)}% - ${HANDLE_SIZE / 2}px)`,
      left: `${(divider.overlapStart * 100).toFixed(4)}%`,
      width: `${((divider.overlapEnd - divider.overlapStart) * 100).toFixed(4)}%`,
      height: HANDLE_SIZE,
      cursor: 'row-resize',
      zIndex: 30,
    };

  const isHighlighted = isHovered || dragging.current;

  return (
    <div
      onMouseDown={handleMouseDown}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      style={{ ...hitStyle, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
    >
      {/* 1) Divider Line - ALWAYS SOLID Accent Color */}
      <div style={{
        position: 'absolute',
        background: 'var(--fz-accent, #FFEA00)', // Yellow fallback
        width: isV ? 2.5 : '100%',
        height: isV ? '100%' : 2.5,
        opacity: isHighlighted ? 1 : 0.7,
        boxShadow: isHighlighted ? '0 0 12px var(--fz-accent-glow)' : 'none',
        pointerEvents: 'none',
        transition: 'all 0.15s ease',
      }} />

      {/* 1) Divider Button - ALWAYS SOLID Accent Color */}
      <div style={{
        width: 26,
        height: 26,
        borderRadius: '50%',
        background: 'var(--fz-accent, #FFEA00)', // Solid color as requested
        border: '2px solid white',
        boxShadow: '0 4px 15px rgba(0,0,0,0.6)',
        zIndex: 31,
        cursor: isV ? 'col-resize' : 'row-resize',
        flexShrink: 0,
        transition: 'all 0.15s cubic-bezier(0.34, 1.56, 0.64, 1)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        transform: isHighlighted ? 'scale(1.25)' : 'scale(1)',
      }}>
        <svg width="12" height="12" viewBox="0 0 14 14" fill="black">
          {isV ? (
            <>
              <polygon points="1,7 4,4 4,10" />
              <polygon points="13,7 10,4 10,10" />
              <rect x="6" y="3" width="2" height="8" rx="1" />
            </>
          ) : (
            <>
              <polygon points="7,1 4,4 10,4" />
              <polygon points="7,13 4,10 10,10" />
              <rect x="3" y="6" width="8" height="2" rx="1" />
            </>
          )}
        </svg>
      </div>
    </div>
  );
}
