export function ZoneRect({ zone, spacing, selected, onSelect, onSplit, onMouseMove, canvasW, canvasH, index, style = {} }) {
  const pct = (v) => `${(v * 100).toFixed(3)}%`;

  const pixelW = Math.round(zone.w * canvasW);
  const pixelH = Math.round(zone.h * canvasH);

  const handleMouseMove = (e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const fracX = (e.clientX - rect.left) / rect.width;
    const fracY = (e.clientY - rect.top) / rect.height;
    if (onMouseMove) onMouseMove(zone.id, fracX, fracY);
  };

  const handleClick = (e) => {
    e.stopPropagation();
    if (e.shiftKey) {
      const rect = e.currentTarget.getBoundingClientRect();
      const fracX = (e.clientX - rect.left) / rect.width;
      const fracY = (e.clientY - rect.top) / rect.height;
      onSplit(zone.id, fracX, fracY);
    } else if (e.ctrlKey) {
      onSelect(zone.id, true);
    }
  };

  const handleDoubleClick = (e) => {
    e.stopPropagation();
    if (!e.shiftKey && !e.ctrlKey) {
      onSelect(zone.id, false);
    }
  };

  return (
    <div
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onMouseMove={handleMouseMove}
      style={{
        position: 'absolute',
        left: pct(zone.x),
        top: pct(zone.y),
        width: pct(zone.w),
        height: pct(zone.h),
        boxSizing: 'border-box',
        padding: spacing / 2,
        zIndex: selected ? 10 : 1,
        ...style,
      }}
    >
      <div style={{
        width: '100%',
        height: '100%',
        border: selected
          ? '3px solid var(--fz-accent)'
          : '2px dashed rgba(255,255,255,0.4)',
        background: selected
          ? 'rgba(0, 210, 255, 0.15)'
          : 'rgba(255,255,255,0.03)',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'white',
        fontWeight: 700,
        boxShadow: selected ? '0 0 0 1px var(--fz-accent), inset 0 0 40px rgba(0, 210, 255, 0.1)' : 'none',
        transition: 'all 0.12s ease-out',
        borderRadius: 4,
      }}>
        <div style={{ fontSize: 64, opacity: 0.85, letterSpacing: '-0.05em', lineHeight: 1 }}>{index}</div>
        <div style={{ fontSize: 12, opacity: 0.55, fontWeight: 500, marginTop: 6, letterSpacing: '0.03em' }}>
          {`${pixelW} × ${pixelH}`}
        </div>
      </div>
    </div>
  );
}
