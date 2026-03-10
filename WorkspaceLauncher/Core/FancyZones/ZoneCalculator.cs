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
        int w       = workArea.Width;
        int h       = workArea.Height;

        // Compute row heights
        double totalRowPct = layout.RowsPercentage.Sum();
        var rowHeights = layout.RowsPercentage
            .Select(p => (int)Math.Round(p / totalRowPct * h))
            .ToArray();

        // Compute col widths
        double totalColPct = layout.ColumnsPercentage.Sum();
        var colWidths = layout.ColumnsPercentage
            .Select(p => (int)Math.Round(p / totalColPct * w))
            .ToArray();

        // Build zone rects by scanning cell-child-map
        var zoneRects = new Dictionary<int, RECT>();

        for (int r = 0; r < rows && r < layout.CellChildMap.Length; r++)
        {
            int rowOff = workArea.Top + rowHeights.Take(r).Sum() + (r > 0 ? spacing * r : 0);

            var rowCells = layout.CellChildMap[r];
            for (int c = 0; c < cols && c < rowCells.Length; c++)
            {
                int childId = rowCells[c];
                if (zoneRects.ContainsKey(childId)) continue; // Already covered (merged cell)

                int colOff = workArea.Left + colWidths.Take(c).Sum() + (c > 0 ? spacing * c : 0);

                // Find how many consecutive cells share this childId
                int spanCols = 1;
                for (int cc = c + 1; cc < cols && cc < rowCells.Length && rowCells[cc] == childId; cc++)
                    spanCols++;

                int spanRows = 1;
                for (int rr = r + 1; rr < rows && rr < layout.CellChildMap.Length && layout.CellChildMap[rr][c] == childId; rr++)
                    spanRows++;

                int zW = colWidths.Skip(c).Take(spanCols).Sum() + spacing * (spanCols - 1);
                int zH = rowHeights.Skip(r).Take(spanRows).Sum() + spacing * (spanRows - 1);

                zoneRects[childId] = new RECT
                {
                    Left   = colOff,
                    Top    = rowOff,
                    Right  = colOff + zW,
                    Bottom = rowOff + zH,
                };
            }
        }

        return zoneRects.TryGetValue(zoneIndex, out var rect) ? rect : null;
    }

    private static RECT? CalculateCanvasZone(LayoutInfo layout, int zoneIndex, RECT workArea)
    {
        if (layout.CanvasZones == null || zoneIndex >= layout.CanvasZones.Length)
            return null;

        var zone    = layout.CanvasZones[zoneIndex];
        double refW = layout.ReferenceWidth  > 0 ? layout.ReferenceWidth  : workArea.Width;
        double refH = layout.ReferenceHeight > 0 ? layout.ReferenceHeight : workArea.Height;

        double scaleX = workArea.Width  / refW;
        double scaleY = workArea.Height / refH;

        return new RECT
        {
            Left   = workArea.Left + (int)Math.Round(zone.X * scaleX),
            Top    = workArea.Top  + (int)Math.Round(zone.Y * scaleY),
            Right  = workArea.Left + (int)Math.Round((zone.X + zone.Width)  * scaleX),
            Bottom = workArea.Top  + (int)Math.Round((zone.Y + zone.Height) * scaleY),
        };
    }
}

/// <summary>Deserialized layout info used by ZoneCalculator.</summary>
public class LayoutInfo
{
    public string? Type    { get; set; }
    public int     Rows    { get; set; }
    public int     Columns { get; set; }

    public int[]?   RowsPercentage    { get; set; }
    public int[]?   ColumnsPercentage { get; set; }
    public int[][]? CellChildMap      { get; set; }

    public bool ShowSpacing { get; set; }
    public int  Spacing     { get; set; }

    // Canvas fields
    public CanvasZone[]? CanvasZones     { get; set; }
    public int           ReferenceWidth  { get; set; }
    public int           ReferenceHeight { get; set; }
}

public class CanvasZone
{
    public int X      { get; set; }
    public int Y      { get; set; }
    public int Width  { get; set; }
    public int Height { get; set; }
}
