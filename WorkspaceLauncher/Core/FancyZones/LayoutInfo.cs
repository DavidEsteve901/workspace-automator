using System.Text.Json.Serialization;

namespace WorkspaceLauncher.Core.FancyZones;

public class LayoutInfo
{
    public string? Type { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public bool ShowSpacing { get; set; }
    public int Spacing { get; set; }
    public int[]? RowsPercentage { get; set; }
    public int[]? ColumnsPercentage { get; set; }
    public int[][]? CellChildMap { get; set; }
    public CanvasZoneInfo[]? CanvasZones { get; set; }
    public double ReferenceWidth { get; set; }
    public double ReferenceHeight { get; set; }
}

public class CanvasZoneInfo
{
    // FancyZones JSON uses uppercase "X"/"Y" but lowercase "width"/"height"
    [JsonPropertyName("X")]
    public int X { get; set; }

    [JsonPropertyName("Y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}


