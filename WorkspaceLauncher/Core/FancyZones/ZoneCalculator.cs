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

        double totalRowPct = layout.RowsPercentage.Sum();
        double totalColPct = layout.ColumnsPercentage.Sum();
        if (totalColPct == 0) totalColPct = 10000;
        if (totalRowPct == 0) totalRowPct = 10000;

        // Build cumulative boundary arrays (exact Python port):
        //   col_bounds[0] = left_bound
        //   col_bounds[i] = left_bound + int((cumulative_pct / total_c) * width)
        // No spacing is involved here — spacing is applied as inset on all 4 sides at the end.
        int[] colBounds = new int[cols + 1];
        int[] rowBounds = new int[rows + 1];

        colBounds[0] = workArea.Left;
        double accumC = 0;
        for (int i = 0; i < cols; i++)
        {
            accumC += layout.ColumnsPercentage[i];
            colBounds[i + 1] = workArea.Left + (int)(accumC / totalColPct * totalW);
        }

        rowBounds[0] = workArea.Top;
        double accumR = 0;
        for (int i = 0; i < rows; i++)
        {
            accumR += layout.RowsPercentage[i];
            rowBounds[i + 1] = workArea.Top + (int)(accumR / totalRowPct * totalH);
        }

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

        // Apply spacing as inset on all 4 sides (exact Python port):
        //   z_l = col_bounds[min_c] + spacing
        //   z_r = col_bounds[max_c + 1] - spacing
        //   z_t = row_bounds[min_r] + spacing
        //   z_b = row_bounds[max_r + 1] - spacing
        int zLeft   = colBounds[minCol]     + spacing;
        int zRight  = colBounds[maxCol + 1] - spacing;
        int zTop    = rowBounds[minRow]     + spacing;
        int zBottom = rowBounds[maxRow + 1] - spacing;

        // Enforce minimum 50px size (same as Python's max(50, ...))
        if (zRight  - zLeft   < 50) zRight  = zLeft   + 50;
        if (zBottom - zTop    < 50) zBottom = zTop    + 50;

        var result = new RECT { Left = zLeft, Top = zTop, Right = zRight, Bottom = zBottom };
        Console.WriteLine($"[ZoneCalculator] Grid Zone {zoneIndex}: [{result.Left},{result.Top},{result.Right},{result.Bottom}] (Size: {result.Width}x{result.Height}) (Spacing: {spacing})");
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

        // PORT NOTE: Python calculates Width and Height independently and then
        // uses them in SetWindowPos. Calculating Right/Bottom directly in C#
        // and then deriving Width/Height can lead to 1px discrepancies due to truncation.
        int x = workArea.Left + (int)(zone.X * scaleX);
        int y = workArea.Top  + (int)(zone.Y * scaleY);
        int w = (int)(zone.Width  * scaleX);
        int h = (int)(zone.Height * scaleY);

        var result = new RECT
        {
            Left   = x,
            Top    = y,
            Right  = x + w,
            Bottom = y + h,
        };
        Console.WriteLine($"[ZoneCalculator] Canvas Zone {zoneIndex}: [{result.Left},{result.Top},{result.Right},{result.Bottom}] (Scaling: {scaleX:F2}x{scaleY:F2})");
        return result;
    }
}
