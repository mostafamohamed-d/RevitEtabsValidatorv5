using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Lightweight XY grid used to reduce all-to-all Revit/ETABS candidate searches.
/// The grid is only a broad-phase accelerator; all final identity gates remain in
/// ModelComparer and therefore retain exact tolerances and geometry checks.
/// </summary>
internal sealed class SpatialGridIndex<T> where T : ElementBase
{
    private readonly double _cellSize;
    private readonly Dictionary<GridKey, List<T>> _cells = new();

    public SpatialGridIndex(double cellSizeMm)
    {
        _cellSize = Math.Max(1.0, Math.Abs(cellSizeMm));
    }

    public void Add(T item)
    {
        var minX = Math.Min(item.StartPoint.X, item.EndPoint.X);
        var maxX = Math.Max(item.StartPoint.X, item.EndPoint.X);
        var minY = Math.Min(item.StartPoint.Y, item.EndPoint.Y);
        var maxY = Math.Max(item.StartPoint.Y, item.EndPoint.Y);

        var x0 = Cell(minX);
        var x1 = Cell(maxX);
        var y0 = Cell(minY);
        var y1 = Cell(maxY);

        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            var key = new GridKey(x, y);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<T>();
                _cells[key] = list;
            }
            list.Add(item);
        }
    }

    public IEnumerable<T> Query(double minX, double minY, double maxX, double maxY)
    {
        var x0 = Cell(Math.Min(minX, maxX));
        var x1 = Cell(Math.Max(minX, maxX));
        var y0 = Cell(Math.Min(minY, maxY));
        var y1 = Cell(Math.Max(minY, maxY));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            if (!_cells.TryGetValue(new GridKey(x, y), out var list))
                continue;

            foreach (var item in list)
            {
                if (seen.Add(item.Id))
                    yield return item;
            }
        }
    }

    private long Cell(double value) => (long)Math.Floor(value / _cellSize);

    private readonly record struct GridKey(long X, long Y);
}
