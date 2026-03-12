const PRESETS = [
  { key: '1col',       label: '1 columna' },
  { key: '2col',       label: '2 col' },
  { key: '3col',       label: '3 col' },
  { key: '2row',       label: '2 filas' },
  { key: 'main-right', label: 'Principal + der.' },
  { key: 'main-left',  label: 'Izq. + principal' },
];

export function ZoneToolbar({ selectedCount, onPreset, onMerge, onReset }) {
  const btnStyle = (color = '#3d4450') => ({
    padding: '4px 10px',
    borderRadius: 4,
    border: 'none',
    background: color,
    color: '#e8eaf0',
    fontSize: 12,
    cursor: 'pointer',
    lineHeight: 1.5,
  });

  return (
    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center', marginBottom: 8 }}>
      <span style={{ fontSize: 11, color: 'rgba(255,255,255,0.45)', marginRight: 4 }}>Presets:</span>
      {PRESETS.map(p => (
        <button key={p.key} style={btnStyle()} onClick={() => onPreset(p.key)}>{p.label}</button>
      ))}
      <div style={{ flex: 1 }} />
      {selectedCount >= 2 && (
        <button style={btnStyle('#4a3f6b')} onClick={onMerge}>Fusionar ({selectedCount})</button>
      )}
      <button style={btnStyle('#5c2d2d')} onClick={onReset}>Resetear</button>
      
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginLeft: 10, background: 'rgba(255,255,255,0.05)', padding: '4px 10px', borderRadius: 4 }}>
        <span style={{ fontSize: 11, color: 'rgba(255,255,255,0.45)' }}>Espaciado:</span>
        <input 
          type="range" min="0" max="40" value={spacing} 
          onChange={e => onSpacingChange(parseInt(e.target.value))}
          style={{ width: 60, cursor: 'pointer' }}
        />
        <span style={{ fontSize: 11, color: '#6db3f2', minWidth: 24 }}>{spacing}px</span>
      </div>

      <span style={{ fontSize: 11, color: 'rgba(255,255,255,0.3)' }}>
        Doble-clic para dividir · Shift+clic para multi-selección
      </span>
    </div>
  );
}
