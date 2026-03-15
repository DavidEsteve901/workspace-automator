import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

export function ZoneRect({ zone, spacing, selected, onSelect, onSplit, onMouseMove, canvasW, canvasH, index, isDragging = false, occupancyApps = [] }) {
  const { t } = useTranslation();
  const rectRef = useRef(null);
  const [isHovered, setIsHovered] = useState(false);
  const [showOccupancy, setShowOccupancy] = useState(false);

  const pixelW = Math.round(zone.w * canvasW);
  const pixelH = Math.round(zone.h * canvasH);

  const hasApps = occupancyApps.length > 0;

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

  const transitionStyle = isDragging ? 'none' : 'all 0.22s cubic-bezier(0.4, 0, 0.2, 1)';
  const accentColor = 'var(--accent, #00D2FF)';

  // Dynamic background based on state
  let dynamicBackground;
  if (selected) {
    dynamicBackground = 'linear-gradient(135deg, rgba(var(--accent-rgb, 0, 210, 255), 0.18) 0%, rgba(var(--accent-rgb, 0, 210, 255), 0.08) 100%)';
  } else if (isHovered) {
    dynamicBackground = 'linear-gradient(135deg, rgba(var(--accent-rgb, 0, 210, 255), 0.10) 0%, rgba(var(--accent-rgb, 0, 210, 255), 0.04) 100%)';
  } else if (hasApps) {
    dynamicBackground = 'linear-gradient(135deg, rgba(0,230,118,0.07) 0%, rgba(0,230,118,0.02) 100%)';
  } else {
    dynamicBackground = 'linear-gradient(135deg, rgba(255,255,255,0.04) 0%, rgba(255,255,255,0.01) 100%)';
  }

  const borderColor = selected
    ? accentColor
    : isHovered
      ? 'var(--fz-accent-dim)'
      : hasApps
        ? 'var(--fz-accent-low)'
        : 'var(--fz-border)';

  const boxShadow = selected
    ? '0 0 0 1px var(--fz-accent-dim), 0 0 30px var(--fz-accent-low), inset 0 1px 0 rgba(255,255,255,0.08)'
    : isHovered
      ? '0 0 20px rgba(var(--accent-rgb), 0.08)'
      : hasApps
        ? '0 0 0 1px var(--fz-accent-low)'
        : 'none';

  return (
    <div
      ref={rectRef}
      onMouseEnter={() => { if (!isDragging) { setIsHovered(true); if (hasApps) setShowOccupancy(true); } }}
      onMouseLeave={() => { setIsHovered(false); setShowOccupancy(false); }}
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
        zIndex: selected ? 10 : isHovered ? 5 : 1,
        transition: transitionStyle,
        pointerEvents: isDragging ? 'none' : 'auto',
      }}
    >
      <div style={{
        width: '100%',
        height: '100%',
        borderRadius: 14,
        border: `2px solid ${borderColor}`,
        background: dynamicBackground,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        transition: transitionStyle,
        boxShadow,
        backdropFilter: (selected || isHovered) ? 'blur(6px)' : 'none',
        position: 'relative',
        overflow: 'hidden',
      }}>

        {/* Shimmer effect on hover */}
        {isHovered && !isDragging && (
          <div style={{
            position: 'absolute',
            top: 0,
            left: '-60%',
            width: '40%',
            height: '100%',
            background: 'linear-gradient(90deg, transparent, var(--fz-bg-alt), transparent)',
            animation: 'zoneShimmer 0.7s ease-out forwards',
            pointerEvents: 'none',
          }} />
        )}

        {/* Selection indicator ring */}
        {selected && (
          <div style={{
            position: 'absolute',
            inset: 0,
            borderRadius: 12,
            background: 'radial-gradient(ellipse at center, var(--fz-accent-low) 0%, transparent 70%)',
            pointerEvents: 'none',
          }} />
        )}

        {/* Zone number */}
        <div style={{
          fontSize: isDragging ? 48 : Math.min(64, Math.max(28, (zone.w * canvasW) / 2.5)),
          opacity: isDragging ? 0.3 : (selected ? 1 : isHovered ? 0.9 : 0.65),
          fontWeight: 800,
          color: selected
            ? accentColor
            : isHovered
              ? accentColor
              : hasApps
                ? 'var(--fz-accent)'
                : 'var(--fz-text-muted)',
          letterSpacing: '-0.06em',
          lineHeight: 1,
          textShadow: (selected || isHovered) ? `0 0 30px var(--fz-accent-dim)` : 'none',
          transition: transitionStyle,
          pointerEvents: 'none',
          background: 'transparent',
          zIndex: 2,
        }}>
          {index}
        </div>

        {/* Pixel dimensions */}
        {!isDragging && (
          <div style={{
            fontSize: 11,
            opacity: (selected || isHovered) ? 0.85 : 0.45,
            color: 'var(--fz-text)',
            fontWeight: 700,
            marginTop: 8,
            letterSpacing: '0.04em',
            transition: transitionStyle,
            pointerEvents: 'none',
            background: 'var(--fz-bg-alt)',
            padding: '3px 8px',
            borderRadius: 6,
            backdropFilter: 'blur(4px)',
            border: '1px solid var(--fz-border)',
            zIndex: 2,
          }}>
            {pixelW} × {pixelH}
          </div>
        )}

        {/* Occupancy badge — always visible when apps present */}
        {hasApps && (
          <div style={{
            position: 'absolute',
            top: 8,
            right: 8,
            zIndex: 20,
          }}>
            <OccupancyBadge apps={occupancyApps} isExpanded={showOccupancy} />
          </div>
        )}
      </div>

      <style>{`
        @keyframes zoneShimmer {
          0%   { transform: translateX(0); opacity: 0; }
          20%  { opacity: 1; }
          100% { transform: translateX(400%); opacity: 0; }
        }
        @keyframes occupancyPop {
          0%   { transform: scale(0.7) translateY(4px); opacity: 0; }
          70%  { transform: scale(1.05) translateY(-1px); }
          100% { transform: scale(1) translateY(0); opacity: 1; }
        }
        @keyframes stackFloat {
          0%, 100% { transform: translateY(0); }
          50%       { transform: translateY(-2px); }
        }
      `}</style>
    </div>
  );
}

// ── Occupancy Badge: glassmorphism stacked app chips ─────────────────────────
function OccupancyBadge({ apps, isExpanded }) {
  const maxStack = 3;
  const shown = apps.slice(0, maxStack);
  const extra = apps.length - maxStack;
  const colors = ['#00D2FF', '#00E676', '#FFEA00', '#FF6B9D', '#A78BFA'];

  if (!isExpanded) {
    // Collapsed: simple stacked dots
    return (
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'flex-end',
        gap: 3,
        animation: 'occupancyPop 0.28s cubic-bezier(0.34,1.56,0.64,1)',
      }}>
        <div style={{
          display: 'flex',
          gap: 3,
          alignItems: 'center',
          background: 'rgba(0,0,0,0.55)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(0,230,118,0.3)',
          borderRadius: 99,
          padding: '3px 7px',
          boxShadow: '0 4px 12px rgba(0,0,0,0.5)',
        }}>
          <div style={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: '#00E676',
            boxShadow: '0 0 6px #00E676',
            animation: 'stackFloat 2s ease-in-out infinite',
          }} />
          <span style={{
            fontSize: 10,
            fontWeight: 800,
            color: '#00E676',
            letterSpacing: '0.03em',
            lineHeight: 1,
          }}>
            {apps.length}
          </span>
        </div>
      </div>
    );
  }

  // Expanded: glassmorphism chip stack
  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      gap: 3,
      animation: 'occupancyPop 0.3s cubic-bezier(0.34,1.56,0.64,1)',
    }}>
      {shown.map((app, i) => (
        <div
          key={i}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 5,
            background: 'rgba(0,0,0,0.7)',
            backdropFilter: 'blur(14px)',
            WebkitBackdropFilter: 'blur(14px)',
            border: `1px solid ${colors[i % colors.length]}44`,
            borderRadius: 8,
            padding: '3px 8px',
            boxShadow: `0 4px 14px rgba(0,0,0,0.6), inset 0 1px 0 rgba(255,255,255,0.05)`,
            animation: `occupancyPop ${0.2 + i * 0.06}s cubic-bezier(0.34,1.56,0.64,1)`,
            maxWidth: 110,
            minWidth: 60,
          }}
        >
          <div style={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: colors[i % colors.length],
            boxShadow: `0 0 6px ${colors[i % colors.length]}`,
            flexShrink: 0,
          }} />
          <span style={{
            fontSize: 9,
            fontWeight: 700,
            color: '#fff',
            whiteSpace: 'nowrap',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            letterSpacing: '0.02em',
          }}>
            {app}
          </span>
        </div>
      ))}
      {extra > 0 && (
        <div style={{
          background: 'rgba(255,255,255,0.08)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: 8,
          padding: '3px 8px',
          fontSize: 9,
          fontWeight: 700,
          color: 'rgba(255,255,255,0.55)',
          textAlign: 'center',
          animation: `occupancyPop ${0.2 + maxStack * 0.06}s cubic-bezier(0.34,1.56,0.64,1)`,
        }}>
          {t('zone_editor.more_apps', { count: extra })}
        </div>
      )}
    </div>
  );
}
