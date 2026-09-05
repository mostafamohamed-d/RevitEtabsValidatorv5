using ETABSv1;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.ETABS;

/// <summary>
/// Reads ETABS frame objects through the typed ETABSv1 OAPI.
/// ETABS geometry is read from the Global coordinate system in the current
/// present length units (the connection sets kN-mm-C before reading).
/// The project coordination contract is Revit Internal Origin ↔ ETABS Global.
/// No automatic XY translation is applied.
/// Frame objects whose ETABS object name starts with "0" are excluded because,
/// in this project, those objects are line-load/helper objects and must not
/// participate in Revit-to-ETABS structural-member validation.
/// </summary>
public sealed class EtabsModelReader
{
    private readonly cSapModel _sap;

    public EtabsModelReader(cSapModel sap)
    {
        _sap = sap ?? throw new ArgumentNullException(nameof(sap));
    }

    public int ExcludedZeroNameCount { get; private set; }

    public IReadOnlyDictionary<string, double> StoryElevationsMm { get; private set; }
        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public List<ColumnElement> ReadColumns() => ReadFrames<ColumnElement>(eFrameDesignOrientation.Column);

    public List<BeamElement> ReadBeams() => ReadFrames<BeamElement>(eFrameDesignOrientation.Beam);

    private List<T> ReadFrames<T>(eFrameDesignOrientation wantedOrientation)
        where T : ElementBase
    {
        var list = new List<T>();
        var frames = _sap.FrameObj;

        int count = 0;
        string[] names = Array.Empty<string>();
        int rc = frames.GetNameList(ref count, ref names);
        if (rc != 0 || names == null || names.Length == 0)
            return list;

        StoryElevationsMm = ReadStoryElevations();

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (name.StartsWith("0", StringComparison.Ordinal))
            {
                ExcludedZeroNameCount++;
                continue;
            }

            try
            {
                var orientation = eFrameDesignOrientation.Null;
                rc = frames.GetDesignOrientation(name, ref orientation);
                if (rc != 0 || orientation != wantedOrientation)
                    continue;

                string point1 = string.Empty;
                string point2 = string.Empty;
                rc = frames.GetPoints(name, ref point1, ref point2);
                if (rc != 0 || string.IsNullOrWhiteSpace(point1) || string.IsNullOrWhiteSpace(point2))
                    continue;

                var start = GetPoint(point1);
                var end = GetPoint(point2);

                string label = name;
                string story = string.Empty;
                try
                {
                    string tmpLabel = string.Empty;
                    string tmpStory = string.Empty;
                    if (frames.GetLabelFromName(name, ref tmpLabel, ref tmpStory) == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(tmpLabel))
                            label = tmpLabel;
                        story = tmpStory ?? string.Empty;
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(story))
                    story = ClosestStory((start.Z + end.Z) / 2.0, StoryElevationsMm);

                string sectionName = string.Empty;
                try
                {
                    string prop = string.Empty;
                    string autoSelect = string.Empty;
                    if (frames.GetSection(name, ref prop, ref autoSelect) == 0)
                        sectionName = prop ?? string.Empty;
                }
                catch { }

                var (width, depth) = ReadRectangleSection(sectionName);

                if (wantedOrientation == eFrameDesignOrientation.Column)
                {
                    double angle = 0.0;
                    try
                    {
                        bool advanced = false;
                        if (frames.GetLocalAxes(name, ref angle, ref advanced) != 0)
                            angle = 0.0;
                    }
                    catch { angle = 0.0; }

                    list.Add((T)(ElementBase)new ColumnElement
                    {
                        Id = name,
                        Name = label,
                        SectionName = sectionName,
                        LevelName = story,
                        Source = SourceApplication.Etabs,
                        CoordinateBasis = CoordinateReference.EtabsGlobal,
                        StartPoint = start,
                        EndPoint = end,
                        BaseElevation = Math.Min(start.Z, end.Z),
                        TopElevation = Math.Max(start.Z, end.Z),
                        Width = width,
                        Depth = depth,
                        Rotation = angle
                    });
                }
                else
                {
                    // Keep the existing project beam-geometry convention: the
                    // ETABS frame line is shifted vertically by half its depth
                    // so that the common beam midpoint plane corresponds to the
                    // Revit reference/elevation convention.
                    var topStart = new Point3D(start.X, start.Y, start.Z + depth / 2.0);
                    var topEnd = new Point3D(end.X, end.Y, end.Z + depth / 2.0);

                    list.Add((T)(ElementBase)new BeamElement
                    {
                        Id = name,
                        Name = label,
                        SectionName = sectionName,
                        LevelName = story,
                        Source = SourceApplication.Etabs,
                        CoordinateBasis = CoordinateReference.EtabsGlobal,
                        StartPoint = topStart,
                        EndPoint = topEnd,
                        Width = width,
                        Depth = depth
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ETABS frame {name} could not be read: {ex.Message}");
            }
        }

        return list;
    }

    private Point3D GetPoint(string pointName)
    {
        double x = 0.0, y = 0.0, z = 0.0;
        int rc = _sap.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z, "Global");
        if (rc != 0)
            throw new InvalidOperationException($"PointObj.GetCoordCartesian failed for '{pointName}' with return code {rc}.");
        return new Point3D(x, y, z);
    }

    private (double Width, double Depth) ReadRectangleSection(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return (0.0, 0.0);

        try
        {
            string fileName = string.Empty;
            string material = string.Empty;
            double t3 = 0.0;
            double t2 = 0.0;
            int color = 0;
            string notes = string.Empty;
            string guid = string.Empty;

            int rc = _sap.PropFrame.GetRectangle(
                sectionName, ref fileName, ref material, ref t3, ref t2,
                ref color, ref notes, ref guid);

            if (rc == 0)
                return (t2, t3);
        }
        catch { }

        return (0.0, 0.0);
    }

    private Dictionary<string, double> ReadStoryElevations()
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        try
        {
            int count = 0;
            string[] names = Array.Empty<string>();
            int rc = _sap.Story.GetNameList(ref count, ref names);
            if (rc != 0 || names == null)
                return result;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                double elevation = 0.0;
                if (_sap.Story.GetElevation(name, ref elevation) == 0)
                    result[name] = elevation;
            }
        }
        catch { }

        return result;
    }

    private static string ClosestStory(double z, IReadOnlyDictionary<string, double> stories)
    {
        if (stories.Count == 0)
            return string.Empty;

        string best = string.Empty;
        double bestDelta = double.MaxValue;
        foreach (var pair in stories)
        {
            double delta = Math.Abs(pair.Value - z);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = pair.Key;
            }
        }
        return best;
    }
}
