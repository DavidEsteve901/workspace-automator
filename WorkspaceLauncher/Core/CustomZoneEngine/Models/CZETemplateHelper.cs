using System;
using System.Linq;
using System.Collections.Generic;
using WorkspaceLauncher.Core.CustomZoneEngine.Models;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Models;

public static class CZETemplateHelper
{
    public static CZELayout? GetVirtualTemplate(string layoutId)
    {
        var id = (layoutId ?? "").ToLowerInvariant();
        var layout = new CZELayout { Id = id, Name = id };

        switch (id)
        {
            case "foco":
                layout.Name = "Foco";
                layout.Zones = new List<CZEZone>
                {
                    new CZEZone { Id = 1, X = 1000, Y = 1000, W = 8000, H = 8000 }
                };
                break;
            case "columnas":
                layout.Name = "Columnas";
                layout.Zones = new List<CZEZone>
                {
                    new CZEZone { Id = 1, X = 0, Y = 0, W = 3333, H = 10000 },
                    new CZEZone { Id = 2, X = 3333, Y = 0, W = 3334, H = 10000 },
                    new CZEZone { Id = 3, X = 6667, Y = 0, W = 3333, H = 10000 }
                };
                break;
            case "filas":
                layout.Name = "Filas";
                layout.Zones = new List<CZEZone>
                {
                    new CZEZone { Id = 1, X = 0, Y = 0, W = 10000, H = 3333 },
                    new CZEZone { Id = 2, X = 0, Y = 3333, W = 10000, H = 3334 },
                    new CZEZone { Id = 3, X = 0, Y = 6667, W = 10000, H = 3333 }
                };
                break;
            case "cuadricula":
                layout.Name = "Cuadrícula";
                layout.Zones = new List<CZEZone>
                {
                    new CZEZone { Id = 1, X = 0, Y = 0, W = 5000, H = 5000 },
                    new CZEZone { Id = 2, X = 5000, Y = 0, W = 5000, H = 5000 },
                    new CZEZone { Id = 3, X = 0, Y = 5000, W = 5000, H = 5000 },
                    new CZEZone { Id = 4, X = 5000, Y = 5000, W = 5000, H = 5000 }
                };
                break;
            default:
                return null;
        }

        return layout;
    }

    /// <summary>
    /// Converts a CZELayout (Engine Model) to a CzeLayoutEntry (Config Model)
    /// </summary>
    public static CzeLayoutEntry ToConfigEntry(CZELayout layout)
    {
        return new CzeLayoutEntry
        {
            Id = layout.Id,
            Name = layout.Name,
            Spacing = layout.Spacing,
            Zones = layout.Zones.Select(z => new CzeZoneEntry
            {
                Id = z.Id,
                X = z.X,
                Y = z.Y,
                W = z.W,
                H = z.H
            }).ToList(),
            RefWidth = 10000,
            RefHeight = 10000
        };
    }
}
