import { useState, useCallback, useMemo } from 'react';

const BASE = 10000;      // All percents sum to this
const MIN_P = 100;       // 1% minimum zone size in BASE units (MinZoneSize 100)

function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

function nextId(cellChildMap) {
  const flat = cellChildMap.flat();
  return flat.length === 0 ? 0 : Math.max(...flat) + 1;
}

// ── Grid ↔ Zones conversion ──────────────────────────────────────────────────

export function initialGrid() {
  return { rows: 1, cols: 1, rowPercents: [BASE], colPercents: [BASE], cellChildMap: [[0]] };
}

/** Grid → [{id,x,y,w,h}] — reading-order, deduplicated. */
export function gridToZones(grid) {
  if (!grid || !grid.rows || !grid.cols || !grid.rowPercents || !grid.colPercents || !grid.cellChildMap) return [];
  const { rows, cols, rowPercents, colPercents, cellChildMap } = grid;
  const seen = new Set();
  const zones = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const zi = cellChildMap[r][c];
      if (seen.has(zi)) continue;
      seen.add(zi);
      let minR = rows, maxR = -1, minC = cols, maxC = -1;
      for (let rr = 0; rr < rows; rr++)
        for (let cc = 0; cc < cols; cc++)
          if (cellChildMap[rr][cc] === zi) {
            if (rr < minR) minR = rr; if (rr > maxR) maxR = rr;
            if (cc < minC) minC = cc; if (cc > maxC) maxC = cc;
          }
      const x = colPercents.slice(0, minC).reduce((a, b) => a + b, 0) / BASE;
      const y = rowPercents.slice(0, minR).reduce((a, b) => a + b, 0) / BASE;
      const w = colPercents.slice(minC, maxC + 1).reduce((a, b) => a + b, 0) / BASE;
      const h = rowPercents.slice(minR, maxR + 1).reduce((a, b) => a + b, 0) / BASE;
      zones.push({ id: zi, x, y, w, h });
    }
  }
  return zones;
}

/** [{x,y,w,h}] → Grid (best-effort reconstruction from rectangular zones).
 *
 * Single-zone layouts that don't cover the full area (e.g. a focus zone at 10%/10%/80%/80%)
 * are reconstructed as a multi-cell grid with surrounding padding cells, preserving the
 * zone's actual bounds.  Full-screen single zones collapse to the standard 1×1 initialGrid.
 */
export function zonesToGrid(zones) {
  if (!zones || zones.length === 0) return initialGrid();
  // NOTE: removed early `if (zones.length === 1) return initialGrid()` — that lost the
  // zone's actual position for partial-coverage single zones (e.g. CZE focus layouts).
  // The algorithm below handles single zones correctly: a full-screen zone produces a
  // 1×1 grid (identical to initialGrid) while a partial zone produces a multi-cell grid.

  // Collect unique column and row boundaries (in BASE units)
  const xSet = new Set([0, BASE]);
  const ySet = new Set([0, BASE]);
  zones.forEach(z => {
    xSet.add(Math.round(z.x * BASE));
    xSet.add(Math.round((z.x + z.w) * BASE));
    ySet.add(Math.round(z.y * BASE));
    ySet.add(Math.round((z.y + z.h) * BASE));
  });

  const sortedX = [...xSet].sort((a, b) => a - b);
  const sortedY = [...ySet].sort((a, b) => a - b);
  const cols = sortedX.length - 1;
  const rows = sortedY.length - 1;

  const colPercents = sortedX.slice(1).map((v, i) => v - sortedX[i]);
  const rowPercents = sortedY.slice(1).map((v, i) => v - sortedY[i]);

  // Fix rounding drift
  const dc = BASE - colPercents.reduce((a, b) => a + b, 0);
  const dr = BASE - rowPercents.reduce((a, b) => a + b, 0);
  colPercents[cols - 1] += dc;
  rowPercents[rows - 1] += dr;

  // Build cellChildMap
  const cellChildMap = Array.from({ length: rows }, () => Array(cols).fill(-1));
  zones.forEach((zone, zi) => {
    const zx1 = Math.round(zone.x * BASE);
    const zy1 = Math.round(zone.y * BASE);
    const zx2 = Math.round((zone.x + zone.w) * BASE);
    const zy2 = Math.round((zone.y + zone.h) * BASE);
    for (let r = 0; r < rows; r++) {
      const ry1 = sortedY[r], ry2 = sortedY[r + 1];
      if (ry1 < zy2 - 50 && ry2 > zy1 + 50)
        for (let c = 0; c < cols; c++) {
          const cx1 = sortedX[c], cx2 = sortedX[c + 1];
          if (cx1 < zx2 - 50 && cx2 > zx1 + 50) cellChildMap[r][c] = zi;
        }
    }
  });

  // Fill unmapped cells
  let nid = zones.length;
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++)
      if (cellChildMap[r][c] === -1) cellChildMap[r][c] = nid++;

  return { rows, cols, rowPercents, colPercents, cellChildMap };
}

/** Compute dividers (vertical & horizontal) for the grid. */
export function computeGridDividers(grid) {
  if (!grid || !grid.rows || !grid.cols || !grid.rowPercents || !grid.colPercents) return [];
  const { rows, cols, rowPercents, colPercents, cellChildMap } = grid;
  const dividers = [];

  // Vertical dividers
  let cumX = 0;
  for (let c = 0; c < cols - 1; c++) {
    cumX += colPercents[c];
    const posX = cumX / BASE;
    
    // Fallback if cellChildMap is missing (e.g. old layouts)
    if (!cellChildMap) {
      dividers.push({ id: `col_${c}`, axis: 'v', index: c, position: posX, overlapStart: 0, overlapEnd: 1 });
      continue;
    }

    // Find segments where left cell != right cell
    let segments = [];
    let currentSegment = null;
    let cumY = 0;
    
    for (let r = 0; r < rows; r++) {
      const h = rowPercents[r];
      const isDivider = cellChildMap[r][c] !== cellChildMap[r][c + 1];
      
      if (isDivider) {
        if (!currentSegment) {
          currentSegment = { start: cumY / BASE };
        }
      } else {
        if (currentSegment) {
          currentSegment.end = cumY / BASE;
          segments.push(currentSegment);
          currentSegment = null;
        }
      }
      cumY += h;
    }
    if (currentSegment) {
      currentSegment.end = cumY / BASE;
      segments.push(currentSegment);
    }
    
    segments.forEach((seg, sIdx) => {
      dividers.push({
        id: `col_${c}_seg_${sIdx}`,
        axis: 'v', index: c, position: posX, 
        overlapStart: seg.start, overlapEnd: seg.end 
      });
    });
  }

  // Horizontal dividers
  let cumY = 0;
  for (let r = 0; r < rows - 1; r++) {
    cumY += rowPercents[r];
    const posY = cumY / BASE;
    
    if (!cellChildMap) {
      dividers.push({ id: `row_${r}`, axis: 'h', index: r, position: posY, overlapStart: 0, overlapEnd: 1 });
      continue;
    }

    let segments = [];
    let currentSegment = null;
    let cumX = 0;
    
    for (let c = 0; c < cols; c++) {
      const w = colPercents[c];
      const isDivider = cellChildMap[r][c] !== cellChildMap[r + 1][c];
      
      if (isDivider) {
        if (!currentSegment) {
          currentSegment = { start: cumX / BASE };
        }
      } else {
        if (currentSegment) {
          currentSegment.end = cumX / BASE;
          segments.push(currentSegment);
          currentSegment = null;
        }
      }
      cumX += w;
    }
    if (currentSegment) {
      currentSegment.end = cumX / BASE;
      segments.push(currentSegment);
    }
    
    segments.forEach((seg, sIdx) => {
      dividers.push({
        id: `row_${r}_seg_${sIdx}`,
        axis: 'h', index: r, position: posY, 
        overlapStart: seg.start, overlapEnd: seg.end 
      });
    });
  }
  return dividers;
}

// ── Grid operations ──────────────────────────────────────────────────────────

function splitGrid(grid, zoneId, clickFracX, clickFracY, forcedAxis = null) {
  if (!grid || !grid.cellChildMap) return grid;
  const { rows, cols, rowPercents, colPercents, cellChildMap } = grid;

  // Find zone bounding box
  let minR = rows, maxR = -1, minC = cols, maxC = -1;
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++)
      if (cellChildMap[r][c] === zoneId) {
        if (r < minR) minR = r; if (r > maxR) maxR = r;
        if (c < minC) minC = c; if (c > maxC) maxC = c;
      }
  if (maxR < 0) return grid; // zone not found

  const isVertical = forcedAxis ? forcedAxis === 'v' : (clickFracX < 0.35 || clickFracX > 0.65);
  const newId = nextId(cellChildMap);

  if (isVertical) {
    // Find which column within zone's span to split
    const zoneCols = colPercents.slice(minC, maxC + 1);
    const totalW = zoneCols.reduce((a, b) => a + b, 0);
    let target = clickFracX * totalW, cum = 0, splitC = minC;
    for (let i = 0; i < zoneCols.length; i++) {
      if (cum + zoneCols[i] > target) { splitC = minC + i; break; }
      cum += zoneCols[i];
      splitC = minC + i;
    }
    const orig = colPercents[splitC];
    const targetInCell = target - cum;
    const h1 = clamp(Math.round(targetInCell), MIN_P, orig - MIN_P);
    const h2 = orig - h1;

    const newColPercents = [...colPercents];
    newColPercents.splice(splitC, 1, h1, h2);
    
    // Create new map: update IDs to separate the zones
    const newMap = cellChildMap.map((row, r) => {
      const nr = [...row];
      const curId = nr[splitC];
      
      // We only split if this row belongs to the targeted zone
      // AND we only change the ID on the "right" side of the split
      const shouldSplitThisRow = (r >= minR && r <= maxR && curId === zoneId);
      
      // Even if we don't split the ID in this row, we MUST insert the new column
      // to keep the grid consistent. If it's not the zone we are splitting,
      // the new cell just inherits the current ID.
      nr.splice(splitC + 1, 0, shouldSplitThisRow ? newId : curId);
      
      // CRITICAL: If this zone spans multiple columns to the right, we must 
      // also update those columns to the newId to keep the new zone contiguous 
      // and separate from the left one.
      if (shouldSplitThisRow) {
        for (let c = splitC + 2; c <= maxC + 1; c++) {
          if (nr[c] === zoneId) nr[c] = newId;
        }
      }
      
      return nr;
    });
    return { ...grid, cols: cols + 1, colPercents: newColPercents, cellChildMap: newMap };
  } else {
    // Horizontal split
    const zoneRows = rowPercents.slice(minR, maxR + 1);
    const totalH = zoneRows.reduce((a, b) => a + b, 0);
    let target = clickFracY * totalH, cum = 0, splitR = minR;
    for (let i = 0; i < zoneRows.length; i++) {
      if (cum + zoneRows[i] > target) { splitR = minR + i; break; }
      cum += zoneRows[i];
      splitR = minR + i;
    }
    const orig = rowPercents[splitR];
    const targetInCell = target - cum;
    const h1 = clamp(Math.round(targetInCell), MIN_P, orig - MIN_P);
    const h2 = orig - h1;

    const newRowPercents = [...rowPercents];
    newRowPercents.splice(splitR, 1, h1, h2);
    
    // Create the new row by splitting the IDs of the original row
    const newRow = cellChildMap[splitR].map((curId, c) => {
      // Only change ID if we are within the zone's horizontal span and it's the target zone
      return (c >= minC && c <= maxC && curId === zoneId) ? newId : curId;
    });
    
    // Update the mapping
    const newMap = cellChildMap.map((row, r) => {
      // If we are below the split point and it's the targeted zone, update the ID
      if (r > splitR && r <= maxR) {
        return row.map((curId, c) => 
          (c >= minC && c <= maxC && curId === zoneId) ? newId : curId
        );
      }
      return [...row];
    });
    
    // Insert the new row
    newMap.splice(splitR + 1, 0, newRow);
    
    return { ...grid, rows: rows + 1, rowPercents: newRowPercents, cellChildMap: newMap };
  }
}

function mergeGrid(grid, zoneIds) {
  if (zoneIds.size < 2) return grid;
  const { rows, cols, cellChildMap } = grid;
  const ids = [...zoneIds];
  const targetId = Math.min(...ids);
  let minR = rows, maxR = -1, minC = cols, maxC = -1;
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++)
      if (ids.includes(cellChildMap[r][c])) {
        if (r < minR) minR = r; if (r > maxR) maxR = r;
        if (c < minC) minC = c; if (c > maxC) maxC = c;
      }
  const newMap = cellChildMap.map((row, r) =>
    row.map((cell, c) =>
      r >= minR && r <= maxR && c >= minC && c <= maxC ? targetId : cell
    )
  );
  return { ...grid, cellChildMap: newMap };
}

function moveDividerInGrid(grid, divider, targetPos) {
  const isV = divider.axis === 'v';
  const arr = [...(isV ? grid.colPercents : grid.rowPercents)];
  const i = divider.index;

  // Calculate absolute boundaries (PrefixSum)
  // prevSplitterPos is the absolute position of the divider to the left/top of the current one.
  const prevSplitterPos = arr.slice(0, i).reduce((sum, p) => sum + p, 0);
  
  // nextSplitterPos is the absolute position of the divider to the right/bottom of the current one.
  // If there are no more splitters, the boundary is 10,000.
  const nextSplitterPos = (i + 2 < arr.length) 
    ? arr.slice(0, i + 2).reduce((sum, p) => sum + p, 0)
    : BASE;

  const targetPos10k = Math.round(targetPos * BASE);
  
  // Strict Clamp: [PrevPos + MIN_P, NextPos - MIN_P]
  const clampedTarget = clamp(targetPos10k, prevSplitterPos + MIN_P, nextSplitterPos - MIN_P);
  
  // Recalculate relative percentages for the two affected zones
  const newLeftP = clampedTarget - prevSplitterPos;
  const newRightP = nextSplitterPos - clampedTarget;

  // No change? Return original grid
  if (arr[i] === newLeftP && arr[i+1] === newRightP) return grid;

  arr[i] = newLeftP;
  arr[i+1] = newRightP;

  return isV 
    ? { ...grid, colPercents: arr }
    : { ...grid, rowPercents: arr };
}

function removeDividerInGrid(grid, divider) {
  const { rows, cols, rowPercents, colPercents, cellChildMap } = grid;
  const i = divider.index;

  if (divider.axis === 'v') {
    if (cols <= 1) return grid;
    const newColPercents = [...colPercents];
    const p1 = newColPercents.splice(i, 1)[0];
    newColPercents[i] += p1; // Merge with subsequent

    const newMap = cellChildMap.map(row => {
      const nr = [...row];
      const leftId = nr[i];
      const rightId = nr[i + 1];
      // Merge IDs: replace all instances of rightId with leftId to "auto-adjust" the zones
      const mergedId = leftId;
      const updatedRow = nr.map(id => id === rightId ? mergedId : id);
      updatedRow.splice(i + 1, 1);
      return updatedRow;
    });

    return { ...grid, cols: cols - 1, colPercents: newColPercents, cellChildMap: newMap };
  } else {
    if (rows <= 1) return grid;
    const newRowPercents = [...rowPercents];
    const p1 = newRowPercents.splice(i, 1)[0];
    newRowPercents[i] += p1;

    // Merge IDs in the rows being joined
    const topRow = cellChildMap[i];
    const botRow = cellChildMap[i + 1];
    const idMap = new Map();
    for(let c=0; c<cols; c++) idMap.set(botRow[c], topRow[c]);

    const newMap = cellChildMap.map(row => row.map(id => idMap.has(id) ? idMap.get(id) : id));
    newMap.splice(i + 1, 1);

    return { ...grid, rows: rows - 1, rowPercents: newRowPercents, cellChildMap: newMap };
  }
}

// ── Presets ──────────────────────────────────────────────────────────────────

const GRID_PRESETS = {
  '1col':       { rows:1, cols:1, rowPercents:[BASE],         colPercents:[BASE],            cellChildMap:[[0]] },
  '2col':       { rows:1, cols:2, rowPercents:[BASE],         colPercents:[5000,5000],        cellChildMap:[[0,1]] },
  '3col':       { rows:1, cols:3, rowPercents:[BASE],         colPercents:[3334,3333,3333],   cellChildMap:[[0,1,2]] },
  '2row':       { rows:2, cols:1, rowPercents:[5000,5000],    colPercents:[BASE],             cellChildMap:[[0],[1]] },
  '2x2':        { rows:2, cols:2, rowPercents:[5000,5000],    colPercents:[5000,5000],        cellChildMap:[[0,1],[2,3]] },
  'main-right': { rows:1, cols:2, rowPercents:[BASE],         colPercents:[6700,3300],        cellChildMap:[[0,1]] },
  'main-left':  { rows:1, cols:2, rowPercents:[BASE],         colPercents:[3300,6700],        cellChildMap:[[0,1]] },
  '3col-mid':   { rows:1, cols:3, rowPercents:[BASE],         colPercents:[2500,5000,2500],   cellChildMap:[[0,1,2]] },
};

// ── Hook ─────────────────────────────────────────────────────────────────────

export function useZoneEditor(initialZones = []) {
  const [grid, setGrid] = useState(() => zonesToGrid(initialZones));
  const [spacing, setSpacing] = useState(0);
  const [selectedIds, setSelectedIds] = useState(new Set());

  const zones = useMemo(() => gridToZones(grid), [grid]);

  const setGridFromZones = useCallback((zones, newSpacing) => {
    setGrid(zonesToGrid(zones));
    if (newSpacing !== undefined) setSpacing(newSpacing);
    setSelectedIds(new Set());
  }, []);

  const setGridFromGridState = useCallback((gridStateJson, newSpacing) => {
    try {
      if (!gridStateJson) return;
      const g = JSON.parse(gridStateJson);
      if (g && Array.isArray(g.rowPercents) && Array.isArray(g.colPercents) && Array.isArray(g.cellChildMap)) {
        setGrid(g);
        if (newSpacing !== undefined) setSpacing(newSpacing);
        setSelectedIds(new Set());
      }
    } catch {
      // ignore parse errors
    }
  }, []);

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

  const splitZone = useCallback((zoneId, clickFracX, clickFracY, axis = null) => {
    setGrid(prev => splitGrid(prev, zoneId, clickFracX, clickFracY, axis));
    setSelectedIds(new Set());
  }, []);

  const mergeSelected = useCallback(() => {
    setGrid(prev => mergeGrid(prev, selectedIds));
    setSelectedIds(new Set());
  }, [selectedIds]);

  const moveDivider = useCallback((divider, targetPos) => {
    setGrid(prev => moveDividerInGrid(prev, divider, targetPos));
  }, []);

  const removeDivider = useCallback((divider) => {
    setGrid(prev => removeDividerInGrid(prev, divider));
  }, []);

  const applyPreset = useCallback((key) => {
    const preset = GRID_PRESETS[key];
    if (preset) { setGrid({ ...preset }); setSelectedIds(new Set()); }
  }, []);

  const resetToFull = useCallback(() => {
    setGrid(initialGrid());
    setSelectedIds(new Set());
  }, []);

  return {
    grid, setGrid,
    zones,
    spacing, setSpacing,
    selectedIds, selectZone, clearSelection,
    splitZone, mergeSelected, moveDivider, removeDivider,
    applyPreset, resetToFull,
    setGridFromZones, setGridFromGridState,
  };
}
