import { AlertCircle } from 'lucide-react';

export function renderZones(info, activeIdx, onClick) {
  if (!info) return null;
  const type = info.type || "grid";

  if (type === "grid") {
    const rowsMap = info["cell-child-map"] || [[0]];
    const rowsPerc = info["rows-percentage"] || [10000];
    const colsPerc = info["columns-percentage"] || [10000];

    const zones = {};
    rowsMap.forEach((row, rIdx) => {
      row.forEach((zId, cIdx) => {
        if (!zones[zId]) zones[zId] = { minR: rIdx, maxR: rIdx, minC: cIdx, maxC: cIdx };
        else {
          zones[zId].minR = Math.min(zones[zId].minR, rIdx);
          zones[zId].maxR = Math.max(zones[zId].maxR, rIdx);
          zones[zId].minC = Math.min(zones[zId].minC, cIdx);
          zones[zId].maxC = Math.max(zones[zId].maxC, cIdx);
        }
      })
    });

    const gridStyle = {
      gridTemplateRows: rowsPerc.map(p => `${p}fr`).join(' '),
      gridTemplateColumns: colsPerc.map(p => `${p}fr`).join(' ')
    };

    return (
      <div style={{ ...gridStyle, width: '100%', height: '100%', display: 'grid', gap: info.spacing ? '4px' : '2px' }}>
        {Object.entries(zones).map(([zId, span]) => {
          const id = parseInt(zId);
          const isSelected = activeIdx === id;
          return (
            <button
              key={id}
              className={`fz-zone-btn ${isSelected ? 'selected' : ''}`}
              style={{
                gridRow: `${span.minR + 1} / ${span.maxR + 2}`,
                gridColumn: `${span.minC + 1} / ${span.maxC + 2}`,
                border: isSelected ? '2px solid var(--accent)' : '1px solid rgba(255,255,255,0.1)',
                background: isSelected ? 'var(--accent-low)' : 'rgba(255,255,255,0.05)',
                color: isSelected ? 'var(--accent)' : '#888',
                cursor: onClick ? 'pointer' : 'default',
                fontWeight: 'bold',
                transition: 'all 0.2s',
                borderRadius: '4px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: '0.9rem',
                padding: 0
              }}
              onClick={onClick ? () => onClick(id) : undefined}
            >
              {id + 1}
            </button>
          )
        })}
      </div>
    )
  }

  if (type === "canvas") {
    const zones = info.zones || [];
    return (
      <div style={{ position: 'relative', width: '100%', height: '100%' }}>
        {zones.map((z, idx) => {
          const isSelected = activeIdx === idx;
          const refW = info["ref-width"] || 10000;
          const refH = info["ref-height"] || 10000;
          const x = (z.X / refW) * 100;
          const y = (z.Y / refH) * 100;
          const w = (z.width / refW) * 100;
          const h = (z.height / refH) * 100;

          return (
            <button
              key={idx}
              className={`fz-zone-btn ${isSelected ? 'selected' : ''}`}
              style={{
                position: 'absolute',
                left: `${x}%`, top: `${y}%`, width: `${w}%`, height: `${h}%`,
                border: isSelected ? '2px solid var(--accent)' : '1px solid rgba(255,255,255,0.2)',
                background: isSelected ? 'var(--accent-low)' : 'rgba(255,255,255,0.1)',
                color: isSelected ? 'var(--accent)' : '#fff',
                opacity: 0.9,
                borderRadius: '4px',
                cursor: onClick ? 'pointer' : 'default',
                fontWeight: 'bold',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                padding: 0
              }}
              onClick={onClick ? () => onClick(idx) : undefined}
            >
              {idx + 1}
            </button>
          )
        })}
      </div>
    )
  }
  return null;
}
