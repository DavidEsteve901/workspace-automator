import { useRef } from 'react';

const HANDLE_SIZE = 8; // px clickable hit area

export function ZoneDivider({ divider, canvasW, canvasH, onMove }) {
  const dragStart = useRef(null);

  const isV = divider.axis === 'v';

  const handleMouseDown = (e) => {
    e.preventDefault();
    dragStart.current = { x: e.clientX, y: e.clientY };

    const onMove_ = (me) => {
      const dx = (me.clientX - dragStart.current.x) / canvasW;
      const dy = (me.clientY - dragStart.current.y) / canvasH;
      dragStart.current = { x: me.clientX, y: me.clientY };
      onMove(divider, isV ? dx : dy);
    };
    const onUp = () => {
      window.removeEventListener('mousemove', onMove_);
      window.removeEventListener('mouseup', onUp);
    };
    window.addEventListener('mousemove', onMove_);
    window.addEventListener('mouseup', onUp);
  };

  const style = isV
    ? {
        position: 'absolute',
        left: `calc(${(divider.position * 100).toFixed(3)}% - ${HANDLE_SIZE / 2}px)`,
        top: `${(divider.overlapStart * 100).toFixed(3)}%`,
        width: HANDLE_SIZE,
        height: `${((divider.overlapEnd - divider.overlapStart) * 100).toFixed(3)}%`,
        cursor: 'col-resize',
      }
    : {
        position: 'absolute',
        top: `calc(${(divider.position * 100).toFixed(3)}% - ${HANDLE_SIZE / 2}px)`,
        left: `${(divider.overlapStart * 100).toFixed(3)}%`,
        width: `${((divider.overlapEnd - divider.overlapStart) * 100).toFixed(3)}%`,
        height: HANDLE_SIZE,
        cursor: 'row-resize',
      };

  return (
    <div
      onMouseDown={handleMouseDown}
      style={{
        ...style,
        zIndex: 10,
        background: 'rgba(109,179,242,0.25)',
      }}
      title="Drag to resize"
    />
  );
}
