export function ZoneRect({ zone, selected, onSelect, onSplit, style = {} }) {
  const pct = (v) => `${(v * 100).toFixed(3)}%`;

  const handleClick = (e) => {
    e.stopPropagation();
    if (e.shiftKey) {
      onSelect(zone.id, true);
    } else {
      onSelect(zone.id, false);
    }
  };

  const handleDblClick = (e) => {
    e.stopPropagation();
    // Get click position relative to this zone
    const rect = e.currentTarget.getBoundingClientRect();
    const fracX = (e.clientX - rect.left) / rect.width;
    const fracY = (e.clientY - rect.top) / rect.height;
    onSplit(zone.id, fracX, fracY);
  };

  return (
    <div
      onClick={handleClick}
      onDoubleClick={handleDblClick}
      title="Click to select · Shift+click multi-select · Double-click to split"
      style={{
        position: 'absolute',
        left: pct(zone.x),
        top: pct(zone.y),
        width: pct(zone.w),
        height: pct(zone.h),
        boxSizing: 'border-box',
        border: selected ? '2px solid #6db3f2' : '1.5px solid rgba(255,255,255,0.18)',
        background: selected ? 'rgba(109,179,242,0.18)' : 'rgba(255,255,255,0.04)',
        cursor: 'pointer',
        transition: 'background 0.12s, border-color 0.12s',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: selected ? '#6db3f2' : 'rgba(255,255,255,0.45)',
        fontSize: 11,
        userSelect: 'none',
        ...style,
      }}
    >
      {`${(zone.w * 100).toFixed(0)}×${(zone.h * 100).toFixed(0)}%`}
    </div>
  );
}
