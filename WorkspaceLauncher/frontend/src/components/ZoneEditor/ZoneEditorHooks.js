import { useState, useCallback } from 'react';

const MIN_SIZE = 0.05;

function clamp(v, min = 0, max = 1) { return Math.max(min, Math.min(max, v)); }

/**
 * Core zone editing state machine.
 * zones: [{ id, x, y, w, h }]  (all values 0.0–1.0)
 */
export function useZoneEditor(initialZones = []) {
  const [zones, setZones] = useState(initialZones);
  const [selectedIds, setSelectedIds] = useState(new Set());

  const selectZone = useCallback((id, multi = false) => {
    setSelectedIds(prev => {
      if (multi) {
        const next = new Set(prev);
        next.has(id) ? next.delete(id) : next.add(id);
        return next;
      }
      return prev.has(id) && prev.size === 1 ? new Set() : new Set([id]);
    });
  }, []);

  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  /**
   * Split a zone at the click position.
   * clickFracX/Y are fractions WITHIN the zone (0–1).
   * Axis: if click is in left/right third → vertical split; else horizontal.
   */
  const splitZone = useCallback((zoneId, clickFracX, clickFracY) => {
    setZones(prev => {
      const zone = prev.find(z => z.id === zoneId);
      if (!zone) return prev;

      const isVertical = clickFracX < 0.35 || clickFracX > 0.65;
      const splitFrac = isVertical ? clickFracX : clickFracY;
      const clampedFrac = clamp(splitFrac, MIN_SIZE, 1 - MIN_SIZE);

      let a, b;
      if (isVertical) {
        a = { ...zone, w: zone.w * clampedFrac };
        b = { ...zone, id: Date.now(), x: zone.x + zone.w * clampedFrac, w: zone.w * (1 - clampedFrac) };
      } else {
        a = { ...zone, h: zone.h * clampedFrac };
        b = { ...zone, id: Date.now(), y: zone.y + zone.h * clampedFrac, h: zone.h * (1 - clampedFrac) };
      }
      return [...prev.filter(z => z.id !== zoneId), a, b];
    });
  }, []);

  /**
   * Merge all selected zones into their bounding box.
   */
  const mergeSelected = useCallback(() => {
    setZones(prev => {
      const sel = prev.filter(z => selectedIds.has(z.id));
      if (sel.length < 2) return prev;
      const x1 = Math.min(...sel.map(z => z.x));
      const y1 = Math.min(...sel.map(z => z.y));
      const x2 = Math.max(...sel.map(z => z.x + z.w));
      const y2 = Math.max(...sel.map(z => z.y + z.h));
      const merged = { id: Date.now(), x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
      return [...prev.filter(z => !selectedIds.has(z.id)), merged];
    });
    setSelectedIds(new Set());
  }, [selectedIds]);

  /**
   * Move a shared divider between adjacent zones by deltaFrac.
   * divider: { axis: 'v'|'h', position: number, zonesA: [id], zonesB: [id] }
   */
  const moveDivider = useCallback((divider, deltaFrac) => {
    setZones(prev => {
      return prev.map(z => {
        if (divider.zonesA.includes(z.id)) {
          if (divider.axis === 'v') {
            const newW = clamp(z.w + deltaFrac, MIN_SIZE, z.w + z.x + deltaFrac - z.x);
            return { ...z, w: newW };
          } else {
            const newH = clamp(z.h + deltaFrac, MIN_SIZE);
            return { ...z, h: newH };
          }
        }
        if (divider.zonesB.includes(z.id)) {
          if (divider.axis === 'v') {
            const newX = clamp(z.x + deltaFrac, 0, 1 - MIN_SIZE);
            const newW = clamp(z.w - deltaFrac, MIN_SIZE);
            return { ...z, x: newX, w: newW };
          } else {
            const newY = clamp(z.y + deltaFrac, 0, 1 - MIN_SIZE);
            const newH = clamp(z.h - deltaFrac, MIN_SIZE);
            return { ...z, y: newY, h: newH };
          }
        }
        return z;
      });
    });
  }, []);

  /**
   * Apply a layout preset.
   * presets: '1col', '2col', '3col', '2row', 'main-right', 'main-left'
   */
  const applyPreset = useCallback((preset) => {
    const presets = {
      '1col':       [{ id: 1, x: 0,    y: 0, w: 1,    h: 1 }],
      '2col':       [{ id: 1, x: 0,    y: 0, w: 0.5,  h: 1 }, { id: 2, x: 0.5, y: 0, w: 0.5, h: 1 }],
      '3col':       [{ id: 1, x: 0,    y: 0, w: 0.333, h: 1 }, { id: 2, x: 0.333, y: 0, w: 0.334, h: 1 }, { id: 3, x: 0.667, y: 0, w: 0.333, h: 1 }],
      '2row':       [{ id: 1, x: 0,    y: 0, w: 1, h: 0.5 }, { id: 2, x: 0, y: 0.5, w: 1, h: 0.5 }],
      'main-right': [{ id: 1, x: 0,    y: 0, w: 0.67, h: 1 }, { id: 2, x: 0.67, y: 0, w: 0.33, h: 1 }],
      'main-left':  [{ id: 1, x: 0,    y: 0, w: 0.33, h: 1 }, { id: 2, x: 0.33, y: 0, w: 0.67, h: 1 }],
    };
    if (presets[preset]) {
      setZones(presets[preset]);
      setSelectedIds(new Set());
    }
  }, []);

  const resetToFull = useCallback(() => {
    setZones([{ id: 1, x: 0, y: 0, w: 1, h: 1 }]);
    setSelectedIds(new Set());
  }, []);

  return { zones, setZones, selectedIds, selectZone, clearSelection, splitZone, mergeSelected, moveDivider, applyPreset, resetToFull };
}

/**
 * Compute the shared dividers between adjacent zones.
 * Returns an array of divider objects for ZoneDivider to render.
 */
export function computeDividers(zones) {
  const dividers = [];
  const TOLERANCE = 0.008;

  for (let i = 0; i < zones.length; i++) {
    for (let j = i + 1; j < zones.length; j++) {
      const a = zones[i], b = zones[j];

      // Vertical shared edge: right of A ≈ left of B
      if (Math.abs((a.x + a.w) - b.x) < TOLERANCE) {
        const overlapTop    = Math.max(a.y, b.y);
        const overlapBottom = Math.min(a.y + a.h, b.y + b.h);
        if (overlapBottom - overlapTop > TOLERANCE) {
          dividers.push({
            id: `v_${a.id}_${b.id}`,
            axis: 'v',
            position: a.x + a.w,
            overlapStart: overlapTop,
            overlapEnd: overlapBottom,
            zonesA: [a.id],
            zonesB: [b.id],
          });
        }
      }

      // Horizontal shared edge: bottom of A ≈ top of B
      if (Math.abs((a.y + a.h) - b.y) < TOLERANCE) {
        const overlapLeft  = Math.max(a.x, b.x);
        const overlapRight = Math.min(a.x + a.w, b.x + b.w);
        if (overlapRight - overlapLeft > TOLERANCE) {
          dividers.push({
            id: `h_${a.id}_${b.id}`,
            axis: 'h',
            position: a.y + a.h,
            overlapStart: overlapLeft,
            overlapEnd: overlapRight,
            zonesA: [a.id],
            zonesB: [b.id],
          });
        }
      }
    }
  }
  return dividers;
}
