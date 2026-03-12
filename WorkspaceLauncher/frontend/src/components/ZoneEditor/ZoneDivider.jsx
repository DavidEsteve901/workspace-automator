import { useRef } from 'react';

const HANDLE_SIZE = 10;

export function ZoneDivider({ divider, canvasW, canvasH, onMove, isHovered, onHover }) {
  const dragStart = useRef(null);
  const isV = divider.axis === 'v';

  const handleMouseDown = (e) => {
    e.preventDefault();
    e.stopPropagation();
    dragStart.current = { x: e.clientX, y: e.clientY };

    const onMouseMove = (me) => {
      const dx = (me.clientX - dragStart.current.x) / canvasW;
      const dy = (me.clientY - dragStart.current.y) / canvasH;
      dragStart.current = { x: me.clientX, y: me.clientY };
      onMove(divider, isV ? dx : dy);
    };
    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
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
        zIndex: 20,
      }
    : {
        position: 'absolute',
        top: `calc(${(divider.position * 100).toFixed(4)}% - ${HANDLE_SIZE / 2}px)`,
        left: `${(divider.overlapStart * 100).toFixed(4)}%`,
        width: `${((divider.overlapEnd - divider.overlapStart) * 100).toFixed(4)}%`,
        height: HANDLE_SIZE,
        cursor: 'row-resize',
        zIndex: 20,
      };

  return (
    <div 
      onMouseDown={handleMouseDown} 
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      style={{ ...hitStyle, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
    >
      {/* Visible line */}
      <div style={{
        position: 'absolute',
        background: isHovered ? '#fff' : 'hsl(3,100%,57%)',
        width: isV ? 2 : '100%',
        height: isV ? '100%' : 2,
        opacity: isHovered ? 1 : 0.7,
        boxShadow: isHovered ? '0 0 10px #fff' : '0 0 6px hsla(3,100%,57%,0.5)',
        pointerEvents: 'none',
        transition: 'all 0.2s',
      }} />
      {/* Drag handle circle */}
      <div style={{
        width: isHovered ? 34 : 28,
        height: isHovered ? 34 : 28,
        borderRadius: '50%',
        background: isHovered ? '#fff' : 'hsl(3,100%,57%)',
        boxShadow: isHovered 
          ? '0 0 20px #fff, 0 4px 15px rgba(0,0,0,0.6)' 
          : '0 0 0 3px hsla(3,100%,57%,0.25), 0 4px 12px rgba(0,0,0,0.5)',
        zIndex: 21,
        cursor: isV ? 'col-resize' : 'row-resize',
        flexShrink: 0,
        animation: isHovered ? 'divPulseFast 0.8s ease-in-out infinite' : 'divPulse 2s ease-in-out infinite',
        transition: 'all 0.15s cubic-bezier(0.34, 1.56, 0.64, 1)',
      }} />
      <style>{`
        @keyframes divPulse {
          0%, 100% { box-shadow: 0 0 0 3px hsla(3,100%,57%,0.25), 0 4px 12px rgba(0,0,0,0.5); }
          50%       { box-shadow: 0 0 0 6px hsla(3,100%,57%,0.15), 0 4px 12px rgba(0,0,0,0.5); }
        }
        @keyframes divPulseFast {
          0%, 100% { transform: scale(1); box-shadow: 0 0 20px #fff, 0 4px 15px rgba(0,0,0,0.6); }
          50%       { transform: scale(1.1); box-shadow: 0 0 30px #fff, 0 4px 20px rgba(0,0,0,0.7); }
        }
      `}</style>
    </div>
  );
}
