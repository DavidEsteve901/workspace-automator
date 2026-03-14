const PRESETS = [
  { key: '1col',       label: '1 col' },
  { key: '2col',       label: '2 col' },
  { key: '3col',       label: '3 col' },
  { key: '2row',       label: '2 filas' },
  { key: 'main-right', label: 'Prin+Dch' },
  { key: 'main-left',  label: 'Izq+Prin' },
];

export function ZoneToolbar({ selectedCount, spacing, onSpacingChange, onPreset, onMerge, onReset }) {
  return (
    <div style={{
      display: 'flex',
      gap: 6,
      flexWrap: 'wrap',
      alignItems: 'center',
      marginBottom: 8,
      padding: '6px 10px',
      background: 'rgba(0,0,0,0.4)',
      backdropFilter: 'blur(12px)',
      borderRadius: 10,
      border: '1px solid rgba(255,255,255,0.06)',
    }}>
      <span style={{
        fontSize: 9,
        fontWeight: 800,
        color: 'rgba(255,255,255,0.3)',
        textTransform: 'uppercase',
        letterSpacing: '0.1em',
        marginRight: 2,
        flexShrink: 0,
      }}>Presets</span>

      {PRESETS.map(p => (
        <button
          key={p.key}
          onClick={() => onPreset(p.key)}
          style={{
            padding: '4px 10px',
            borderRadius: 7,
            border: '1px solid rgba(255,255,255,0.08)',
            background: 'rgba(255,255,255,0.04)',
            color: 'rgba(255,255,255,0.6)',
            fontSize: 11,
            fontWeight: 600,
            cursor: 'pointer',
            transition: 'all 0.16s cubic-bezier(0.4,0,0.2,1)',
            letterSpacing: '0.02em',
          }}
          onMouseEnter={e => {
            e.currentTarget.style.background = 'rgba(var(--accent-rgb, 0, 210, 255), 0.12)';
            e.currentTarget.style.borderColor = 'rgba(var(--accent-rgb, 0, 210, 255), 0.3)';
            e.currentTarget.style.color = 'var(--accent, #00D2FF)';
            e.currentTarget.style.transform = 'translateY(-1px)';
          }}
          onMouseLeave={e => {
            e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)';
            e.currentTarget.style.color = 'rgba(255,255,255,0.6)';
            e.currentTarget.style.transform = 'translateY(0)';
          }}
        >
          {p.label}
        </button>
      ))}

      <div style={{ flex: 1 }} />

      {selectedCount >= 2 && (
        <button
          onClick={onMerge}
          style={{
            padding: '4px 12px',
            borderRadius: 7,
            border: '1px solid rgba(167,139,250,0.3)',
            background: 'rgba(167,139,250,0.1)',
            color: '#A78BFA',
            fontSize: 11,
            fontWeight: 700,
            cursor: 'pointer',
            transition: 'all 0.16s ease',
            letterSpacing: '0.02em',
          }}
          onMouseEnter={e => { e.currentTarget.style.background = 'rgba(167,139,250,0.2)'; e.currentTarget.style.transform = 'translateY(-1px)'; }}
          onMouseLeave={e => { e.currentTarget.style.background = 'rgba(167,139,250,0.1)'; e.currentTarget.style.transform = 'translateY(0)'; }}
        >
          Fusionar ({selectedCount})
        </button>
      )}

      <button
        onClick={onReset}
        style={{
          padding: '4px 12px',
          borderRadius: 7,
          border: '1px solid rgba(255,255,255,0.07)',
          background: 'rgba(255,255,255,0.03)',
          color: 'rgba(255,255,255,0.45)',
          fontSize: 11,
          fontWeight: 600,
          cursor: 'pointer',
          transition: 'all 0.16s ease',
        }}
        onMouseEnter={e => { e.currentTarget.style.background = 'rgba(255,59,48,0.1)'; e.currentTarget.style.borderColor = 'rgba(255,59,48,0.25)'; e.currentTarget.style.color = '#ff3b30'; }}
        onMouseLeave={e => { e.currentTarget.style.background = 'rgba(255,255,255,0.03)'; e.currentTarget.style.borderColor = 'rgba(255,255,255,0.07)'; e.currentTarget.style.color = 'rgba(255,255,255,0.45)'; }}
      >
        Resetear
      </button>

      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        marginLeft: 6,
        background: 'rgba(255,255,255,0.04)',
        padding: '4px 12px',
        borderRadius: 8,
        border: '1px solid rgba(255,255,255,0.06)',
      }}>
        <span style={{ fontSize: 10, color: 'rgba(255,255,255,0.35)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em' }}>Gap</span>
        <input
          type="range" min="0" max="40" value={spacing}
          onChange={e => onSpacingChange(parseInt(e.target.value))}
          style={{ width: 56, cursor: 'pointer', accentColor: 'var(--accent, #00D2FF)' }}
        />
        <span style={{ fontSize: 11, color: 'var(--accent, #00D2FF)', minWidth: 28, fontWeight: 700, fontFamily: 'monospace' }}>{spacing}px</span>
      </div>

      <span style={{ fontSize: 10, color: 'rgba(255,255,255,0.2)', marginLeft: 2, letterSpacing: '0.01em' }}>
        2×Clic dividir · Shift+drag multi
      </span>
    </div>
  );
}
