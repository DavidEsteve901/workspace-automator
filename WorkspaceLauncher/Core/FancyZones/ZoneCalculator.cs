using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Calculates zone rectangles from FancyZones layout definitions.
/// Port of _calculate_zone_rect in Python.
/// </summary>
public static class ZoneCalculator
{
    /// <summary>
    /// Calculate the RECT for a specific zone index within a layout, given the monitor work area.
    /// </summary>
    public static RECT? CalculateZoneRect(LayoutInfo layout, int zoneIndex, RECT workArea)
    {
        return layout.Type?.ToLowerInvariant() switch
        {
            "grid"   => CalculateGridZone(layout, zoneIndex, workArea),
            "canvas" => CalculateCanvasZone(layout, zoneIndex, workArea),
            _        => null
        };
    }

    private static RECT? CalculateGridZone(LayoutInfo layout, int zoneIndex, RECT workArea)
    {
        if (layout.RowsPercentage == null || layout.ColumnsPercentage == null || layout.CellChildMap == null)
            return null;

        int rows    = layout.Rows;
        int cols    = layout.Columns;
        int spacing = layout.ShowSpacing ? layout.Spacing : 0;

        int totalW = workArea.Width;
        int totalH = workArea.Height;

        // Compute usable area (total minus all gaps between cells)
        int usableW = totalW - (cols - 1) * spacing;
        int usableH = totalH - (rows - 1) * spacing;

        double totalRowPct = layout.RowsPercentage.Sum();
        double totalColPct = layout.ColumnsPercentage.Sum();

        // Calculate absolute grid lines for cell boundaries
        // Each cell i occupies space between line i and line i+1
        int[] colLines = new int[cols + 1];
        int[] rowLines = new int[rows + 1];

        colLines[0] = 0;
        int currentW = 0;
        for (int i = 0; i < cols - 1; i++)
        {
            currentW += (int)Math.Round((double)layout.ColumnsPercentage[i] / totalColPct * usableW);
            colLines[i + 1] = currentW;
            currentW += spacing; // Jump over spacing for next cell start
        }
        colLines[cols] = totalW; // Last line is always the edge

        rowLines[0] = 0;
        int currentH = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            currentH += (int)Math.Round((double)layout.RowsPercentage[i] / totalRowPct * usableH);
            rowLines[i + 1] = currentH;
            currentH += spacing;
        }
        rowLines[rows] = totalH;

        // Find the bounding box of cells belonging to this zoneIndex
        int minRow = int.MaxValue, maxRow = int.MinValue;
        int minCol = int.MaxValue, maxCol = int.MinValue;
        bool found = false;

        for (int r = 0; r < rows && r < layout.CellChildMap.Length; r++)
        {
            var rowCells = layout.CellChildMap[r];
            for (int c = 0; c < cols && c < rowCells.Length; c++)
            {
                if (rowCells[c] == zoneIndex)
                {
                    minRow = Math.Min(minRow, r);
                    maxRow = Math.Max(maxRow, r);
                    minCol = Math.Min(minCol, c);
                    maxCol = Math.Max(maxCol, c);
                    found = true;
                }
            }
        }

        if (!found) return null;

        // Translate grid lines to absolute screen coordinates
        // Zone starts at the beginning of minCol and ends at the end of maxCol
        var result = new RECT
        {
            Left   = workArea.Left + colLines[minCol],
            Top    = workArea.Top  + rowLines[minRow],
            Right  = workArea.Left + (minCol == maxCol ? colLines[minCol] + (colLines[minCol + 1] - (minCol == cols - 1 ? 0 : spacing) - colLines[minCol]) : colLines[maxCol + 1] - (maxCol == cols - 1 ? 0 : spacing)),
            Bottom = workArea.Top  + (minRow == maxRow ? rowLines[minRow] + (rowLines[minRow + 1] - (minRow == rows - 1 ? 0 : spacing) - rowLines[minRow]) : rowLines[maxRow + 1] - (maxRow == rows - 1 ? 0 : spacing))
        };

        // Simplified right/bottom logic:
        // A zone spanning from col A to col B starts at line A and ends at line B+1,
        // BUT we must subtract the spacing that's baked into line i+1 if line i+1 is NOT the monitor edge.
        result.Right = workArea.Left + colLines[maxCol + 1];
        if (maxCol < cols - 1) result.Right -= spacing;

        result.Bottom = workArea.Top + rowLines[maxRow + 1];
        if (maxRow < rows - 1) result.Bottom -= spacing;

        Console.WriteLine($"[ZoneCalculator] Grid Zone {zoneIndex}: [{result.Left},{result.Top},{result.Right},{result.Bottom}] (Size: {result.Width}x{result.Height})");
        return result;
    }

    private static RECT? CalculateCanvasZone(LayoutInfo layout, int zoneIndex, RECT workArea)
    {
        if (layout.CanvasZones == null || zoneIndex >= layout.CanvasZones.Length)
            return null;

        var zone  = layout.CanvasZones[zoneIndex];
        double refW = layout.ReferenceWidth  > 0 ? layout.ReferenceWidth  : workArea.Width;
        double refH = layout.ReferenceHeight > 0 ? layout.ReferenceHeight : workArea.Height;

        double scaleX = workArea.Width  / refW;
        double scaleY = workArea.Height / refH;

        var result = new RECT
        {
            Left   = workArea.Left + (int)Math.Round(zone.X * scaleX),
            Top    = workArea.Top  + (int)Math.Round(zone.Y * scaleY),
            Right  = workArea.Left + (int)Math.Round((zone.X + zone.Width)  * scaleX),
            Bottom = workArea.Top  + (int)Math.Round((zone.Y + zone.Height) * scaleY),
        };
        Console.WriteLine($"[ZoneCalculator] Canvas Zone {zoneIndex}: [{result.Left},{result.Top},{result.Right},{result.Bottom}]");
        return result;
    }
}
