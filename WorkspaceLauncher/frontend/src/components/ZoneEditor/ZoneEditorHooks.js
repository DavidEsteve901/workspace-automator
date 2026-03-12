import { useState, useCallback, useMemo } from 'react';

const BASE = 10000;      // All percents sum to this
const MIN_P = 800;       // 8% minimum zone size in BASE units

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

/** [{x,y,w,h}] → Grid (best-effort reconstruction from rectangular zones). */
export function zonesToGrid(zones) {
  if (!zones || zones.length === 0) return initialGrid();
  if (zones.length === 1) return initialGrid();

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
  const { rows, cols, rowPercents, colPercents } = grid;
  const dividers = [];
  let cumX = 0;
  for (let c = 0; c < cols - 1; c++) {
    cumX += colPercents[c];
    dividers.push({ id: `col_${c}`, axis: 'v', index: c, position: cumX / BASE, overlapStart: 0, overlapEnd: 1 });
  }
  let cumY = 0;
  for (let r = 0; r < rows - 1; r++) {
    cumY += rowPercents[r];
    dividers.push({ id: `row_${r}`, axis: 'h', index: r, position: cumY / BASE, overlapStart: 0, overlapEnd: 1 });
  }
  return dividers;
}

// ── Grid operations ──────────────────────────────────────────────────────────

function splitGrid(grid, zoneId, clickFracX, clickFracY, forcedAxis = null) {
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
    const newMap = cellChildMap.map(row => {
      const nr = [...row];
      const cur = nr[splitC];
      nr.splice(splitC + 1, 0, cur === zoneId ? newId : cur);
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
    const newRow = cellChildMap[splitR].map(cur => cur === zoneId ? newId : cur);
    const newMap = [
      ...cellChildMap.slice(0, splitR + 1),
      newRow,
      ...cellChildMap.slice(splitR + 1),
    ];
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

function moveDividerInGrid(grid, divider, deltaFrac) {
  const delta = Math.round(deltaFrac * BASE);
  if (divider.axis === 'v') {
    const arr = [...grid.colPercents];
    const i = divider.index;
    const d = clamp(delta, -(arr[i] - MIN_P), arr[i + 1] - MIN_P);
    arr[i] += d; arr[i + 1] -= d;
    return { ...grid, colPercents: arr };
  } else {
    const arr = [...grid.rowPercents];
    const i = divider.index;
    const d = clamp(delta, -(arr[i] - MIN_P), arr[i + 1] - MIN_P);
    arr[i] += d; arr[i + 1] -= d;
    return { ...grid, rowPercents: arr };
  }
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
      const g = JSON.parse(gridStateJson);
      setGrid(g);
      if (newSpacing !== undefined) setSpacing(newSpacing);
      setSelectedIds(new Set());
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

  const moveDivider = useCallback((divider, deltaFrac) => {
    setGrid(prev => moveDividerInGrid(prev, divider, deltaFrac));
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
