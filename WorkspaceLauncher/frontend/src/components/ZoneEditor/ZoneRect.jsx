import { useRef, useState } from 'react';

export function ZoneRect({ zone, spacing, selected, onSelect, onSplit, onMouseMove, canvasW, canvasH, index, isDragging = false }) {
  const rectRef = useRef(null);
  const [isHovered, setIsHovered] = useState(false);

  const pixelW = Math.round(zone.w * canvasW);
  const pixelH = Math.round(zone.h * canvasH);

  const handleClick = (e) => {
    e.stopPropagation();
    if (e.shiftKey) {
      if (!rectRef.current) return;
      const rect = rectRef.current.getBoundingClientRect();
      const fx = (e.clientX - rect.left) / rect.width;
      const fy = (e.clientY - rect.top) / rect.height;
      onSplit(zone.id, fx, fy);
    } else {
      onSelect(e.ctrlKey || e.metaKey);
    }
  };

  const handleDoubleClick = (e) => {
    e.stopPropagation();
    if (!rectRef.current) return;
    const rect = rectRef.current.getBoundingClientRect();
    const fx = (e.clientX - rect.left) / rect.width;
    const fy = (e.clientY - rect.top) / rect.height;
    onSplit(zone.id, fx, fy);
  };

  const handleMouseMove = (e) => {
    if (isDragging) return;
    if (!rectRef.current) return;
    const rect = rectRef.current.getBoundingClientRect();
    const fx = (e.clientX - rect.left) / rect.width;
    const fy = (e.clientY - rect.top) / rect.height;
    onMouseMove(zone.id, fx, fy);
  };

  const transitionStyle = isDragging ? 'none' : 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)';

  const accentColor = 'var(--fz-accent, var(--accent, #00D2FF))';

  const dynamicBackground = (selected || isHovered)
    ? 'var(--fz-accent-dim, rgba(128, 128, 128, 0.15))'
    : 'var(--fz-accent-low, rgba(128, 128, 128, 0.05))';

  return (
    <div
      ref={rectRef}
      onMouseEnter={() => !isDragging && setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onMouseMove={handleMouseMove}
      style={{
        position: 'absolute',
        left: `${(zone.x * 100).toFixed(6)}%`,
        top: `${(zone.y * 100).toFixed(6)}%`,
        width: `${(zone.w * 100).toFixed(6)}%`,
        height: `${(zone.h * 100).toFixed(6)}%`,
        padding: spacing / 2,
        boxSizing: 'border-box',
        zIndex: selected ? 10 : 1,
        transition: transitionStyle,
        pointerEvents: isDragging ? 'none' : 'auto',
      }}
    >
      <div style={{
        width: '100%',
        height: '100%',
        borderRadius: 12,
        // Border: Fixed at 2px (No size jump, only color transition)
        border: (selected || isHovered)
          ? `2px solid ${accentColor}`
          : `2px solid var(--fz-accent-low, rgba(255, 255, 255, 0.15))`, // Faint white border if no theme
        background: dynamicBackground,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        transition: transitionStyle,
        boxShadow: (selected || isHovered) ? '0 0 25px var(--fz-accent-glow)' : 'none',
        backdropFilter: 'none',
      }}>
        {/* Number: No background, just color as requested */}
        <div style={{
          fontSize: 64,
          opacity: isDragging ? 0.3 : (selected || isHovered ? 1 : 0.8),
          fontWeight: 800,
          color: (selected || isHovered) ? accentColor : accentColor,
          letterSpacing: '-0.05em',
          lineHeight: 1,
          textShadow: '0 2px 15px rgba(0,0,0,0.8)',
          transition: transitionStyle,
          pointerEvents: 'none',
          background: 'transparent',
        }}>
          {index}
        </div>

        <div style={{
          fontSize: 14,
          opacity: isDragging ? 0.2 : (selected || isHovered ? 0.95 : 0.75),
          color: '#fff',
          fontWeight: 700,
          marginTop: 10,
          letterSpacing: '0.06em',
          textShadow: '0 2px 8px rgba(0,0,0,1)',
          transition: transitionStyle,
          pointerEvents: 'none',
          background: 'rgba(0,0,0,0.35)',
          padding: '2px 8px',
          borderRadius: 6,
        }}>
          {`${pixelW} × ${pixelH} px`}
        </div>
      </div>
    </div>
  );
}
