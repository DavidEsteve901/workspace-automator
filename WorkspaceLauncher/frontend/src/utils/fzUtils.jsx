import { useState } from 'react';
import { AlertCircle } from 'lucide-react';

export function renderZones(info, activeIdx, onClick, occupancy = {}) {
  try {
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
        <div style={{ ...gridStyle, width: '100%', height: '100%', display: 'grid', gap: info.spacing ? '3px' : '2px' }}>
          {Object.entries(zones).map(([zId, span]) => {
            const id = parseInt(zId);
            const isSelected = activeIdx === id;
            const rawOccupant = occupancy[id];
            const occupants = rawOccupant
              ? rawOccupant.split(',').map(s => s.trim()).filter(Boolean)
              : [];
            const hasOccupants = occupants.length > 0;

            return (
              <ZonePreviewCell
                key={id}
                zoneId={id}
                isSelected={isSelected}
                occupants={occupants}
                hasOccupants={hasOccupants}
                onClick={onClick}
                gridRow={`${span.minR + 1} / ${span.maxR + 2}`}
                gridColumn={`${span.minC + 1} / ${span.maxC + 2}`}
              />
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
            const refW = info["ref-width"] || info.refWidth || info.refwidth || 10000;
            const refH = info["ref-height"] || info.refHeight || info.refheight || 10000;
            const xVal = z.X !== undefined ? z.X : z.x || 0;
            const yVal = z.Y !== undefined ? z.Y : z.y || 0;
            const wVal = z.width || z.w || z.W || 10000;
            const hVal = z.height || z.h || z.H || 10000;

            const x = (xVal / refW) * 100;
            const y = (yVal / refH) * 100;
            const w = (wVal / refW) * 100;
            const h = (hVal / refH) * 100;
            const rawOccupant = occupancy[idx];
            const occupants = rawOccupant
              ? rawOccupant.split(',').map(s => s.trim()).filter(Boolean)
              : [];
            const hasOccupants = occupants.length > 0;

            return (
              <ZonePreviewCell
                key={idx}
                zoneId={idx}
                isSelected={isSelected}
                occupants={occupants}
                hasOccupants={hasOccupants}
                onClick={onClick}
                posStyle={{ position: 'absolute', left: `${x}%`, top: `${y}%`, width: `${w}%`, height: `${h}%` }}
              />
            )
          })}
        </div>
      )
    }
  } catch (e) {
    console.error("renderZones error:", e);
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%', fontSize: '0.7rem', color: '#ff4444' }}>
        ERR
      </div>
    );
  }
  return null;
}

// ── Color palette for occupant chips ─────────────────────────────────────────
const OCCUPANT_COLORS = ['var(--accent, #00D2FF)', '#00E676', '#FFEA00', '#FF6B9D', '#A78BFA', '#FF9D4D'];

// ── Premium Zone Preview Cell ─────────────────────────────────────────────────
function ZonePreviewCell({ zoneId, isSelected, occupants, hasOccupants, onClick, gridRow, gridColumn, posStyle }) {
  const [hovered, setHovered] = useState(false);

  const bgColor = isSelected
    ? 'linear-gradient(135deg, rgba(var(--accent-rgb, 0,210,255), 0.25) 0%, rgba(var(--accent-rgb, 0,210,255), 0.12) 100%)'
    : hovered
      ? 'linear-gradient(135deg, rgba(var(--accent-rgb, 0,210,255), 0.12) 0%, rgba(var(--accent-rgb, 0,210,255), 0.05) 100%)'
      : 'linear-gradient(135deg, rgba(255,255,255,0.05) 0%, rgba(255,255,255,0.02) 100%)';

  const borderColor = isSelected
    ? 'rgba(var(--accent-rgb, 0,210,255), 0.6)'
    : hovered
      ? 'rgba(var(--accent-rgb, 0,210,255), 0.35)'
      : 'rgba(255,255,255,0.10)';

  const containerStyles = {
    background: bgColor,
    border: `2px solid ${borderColor}`,
    borderRadius: 8,
    cursor: onClick ? 'pointer' : 'default',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 3,
    padding: '4px 3px',
    overflow: 'hidden',
    position: 'relative',
    transition: 'all 0.18s cubic-bezier(0.4,0,0.2,1)',
    boxShadow: isSelected
      ? '0 0 0 1px var(--border-accent), inset 0 1px 0 rgba(255,255,255,0.1), 0 4px 12px var(--accent-glow)'
      : hasOccupants && hovered
        ? '0 4px 14px rgba(0,0,0,0.4)'
        : 'none',
    transform: hovered && !isSelected ? 'scale(0.97)' : isSelected ? 'scale(0.96)' : 'scale(1)',
    ...(gridRow ? { gridRow, gridColumn } : posStyle),
  };

  return (
    <>
      <button
        style={containerStyles}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onClick={onClick ? () => onClick(zoneId) : undefined}
        title={hasOccupants ? `Ocupada por: ${occupants.join(', ')}` : `Zona ${zoneId + 1}`}
      >
        {/* Selection glow */}
        {isSelected && (
          <div style={{
            position: 'absolute',
            inset: 0,
            background: 'radial-gradient(ellipse at center, rgba(var(--accent-rgb, 0, 210, 255), 0.15) 0%, transparent 70%)',
            pointerEvents: 'none',
            borderRadius: 6,
          }} />
        )}

        {/* Zone number */}
        <span style={{
          fontSize: 11,
          fontWeight: 800,
          color: isSelected
            ? 'var(--accent)'
            : 'var(--text-muted)',
          lineHeight: 1,
          letterSpacing: '-0.02em',
          transition: 'color 0.15s ease',
          position: 'relative',
          zIndex: 1,
        }}>
          {zoneId + 1}
        </span>

        {/* Expanded occupant chips on hover/select */}
        {hasOccupants && (hovered || isSelected) && (
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 2,
            width: '100%',
            padding: '0 2px',
            animation: 'chipFadeIn 0.2s ease-out',
            position: 'relative',
            zIndex: 1,
          }}>
            {occupants.slice(0, 3).map((app, i) => (
              <div
                key={i}
                style={{
                  background: `${OCCUPANT_COLORS[i % OCCUPANT_COLORS.length]}1A`,
                  border: `1px solid ${OCCUPANT_COLORS[i % OCCUPANT_COLORS.length]}44`,
                  borderRadius: 4,
                  padding: '1px 6px',
                  fontSize: 8,
                  fontWeight: 700,
                  color: '#fff',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  textAlign: 'center',
                  animation: `chipSlide ${0.12 + i * 0.05}s ease-out`,
                  letterSpacing: '0.02em',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 4,
                  margin: '0 auto',
                  maxWidth: '92%'
                }}
              >
                <span style={{
                  width: 4,
                  height: 4,
                  borderRadius: '50%',
                  background: OCCUPANT_COLORS[i % OCCUPANT_COLORS.length],
                  flexShrink: 0,
                  boxShadow: `0 0 4px ${OCCUPANT_COLORS[i % OCCUPANT_COLORS.length]}`,
                }} />
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {app.length > 10 ? app.substring(0, 9) + '…' : app}
                </span>
              </div>
            ))}
            {occupants.length > 3 && (
              <div style={{
                fontSize: 8,
                color: 'rgba(255,255,255,0.4)',
                textAlign: 'center',
                fontWeight: 700,
              }}>
                +{occupants.length - 3}
              </div>
            )}
          </div>
        )}

        {/* Collapsed dot indicators */}
        {hasOccupants && !hovered && !isSelected && (
          <div style={{ display: 'flex', gap: 2, flexWrap: 'wrap', justifyContent: 'center', position: 'relative', zIndex: 1 }}>
            {occupants.slice(0, Math.min(4, occupants.length)).map((_, i) => (
              <div key={i} style={{
                width: 5,
                height: 5,
                borderRadius: '50%',
                background: OCCUPANT_COLORS[i % OCCUPANT_COLORS.length],
                opacity: 0.85,
                boxShadow: `0 0 5px ${OCCUPANT_COLORS[i % OCCUPANT_COLORS.length]}88`,
                animation: `dotPulse ${1.5 + i * 0.2}s infinite ease-in-out`,
                transition: 'transform 0.2s ease',
              }} />
            ))}
            {occupants.length > 4 && (
              <div style={{
                fontSize: 7,
                color: 'rgba(255,255,255,0.4)',
                fontWeight: 800,
                lineHeight: '5px',
              }}>+{occupants.length - 4}</div>
            )}
          </div>
        )}
      </button>

      <style>{`
        @keyframes chipFadeIn {
          from { opacity: 0; transform: translateY(3px); }
          to   { opacity: 1; transform: translateY(0); }
        }
        @keyframes chipSlide {
          from { opacity: 0; transform: translateX(-3px); }
          to   { opacity: 1; transform: translateX(0); }
        }
        @keyframes dotPulse {
          0% { transform: scale(1); opacity: 0.85; }
          50% { transform: scale(1.3); opacity: 1; box-shadow: 0 0 8px ${OCCUPANT_COLORS[0]}aa; }
          100% { transform: scale(1); opacity: 0.85; }
        }
      `}</style>
    </>
  );
}
