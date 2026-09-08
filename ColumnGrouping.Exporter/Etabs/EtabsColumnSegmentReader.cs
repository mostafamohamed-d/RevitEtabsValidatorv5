using System;
using System.Collections.Generic;
using System.Linq;
using ETABSv1;
using ColumnGrouping.Core.Models;

namespace ColumnGrouping.Exporter.Etabs
{
    public interface IColumnReinforcementRatioProvider
    {
        bool TryGetRatioDecimal(string etabsObjectName, string storyName, out double ratioDecimal, out string diagnostic);
    }

    public sealed class EtabsColumnSegmentReader
    {
        private readonly cSapModel _sap;
        private readonly IColumnReinforcementRatioProvider _ratioProvider;

        public EtabsColumnSegmentReader(cSapModel sap, IColumnReinforcementRatioProvider ratioProvider)
        {
            _sap = sap ?? throw new ArgumentNullException(nameof(sap));
            _ratioProvider = ratioProvider ?? throw new ArgumentNullException(nameof(ratioProvider));
        }

        public List<ColumnSegment> Read(out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            var segments = new List<ColumnSegment>();

            var frames = _sap.FrameObj;
            if (frames == null)
                throw new InvalidOperationException("ETABS FrameObj interface is not available.");

            int count = 0;
            string[] names = Array.Empty<string>();
            int rc = frames.GetNameList(ref count, ref names);
            if (rc != 0)
                throw new InvalidOperationException($"FrameObj.GetNameList failed. Return code={rc}.");

            foreach (var name in names ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("0", StringComparison.Ordinal))
                    continue;

                try
                {
                    var orientation = eFrameDesignOrientation.Null;
                    rc = frames.GetDesignOrientation(name, ref orientation);
                    if (rc != 0)
                    {
                        diagnostics.Add($"{name}: GetDesignOrientation failed, rc={rc}.");
                        continue;
                    }
                    if (orientation != eFrameDesignOrientation.Column)
                        continue;

                    string p1 = string.Empty, p2 = string.Empty;
                    rc = frames.GetPoints(name, ref p1, ref p2);
                    if (rc != 0)
                    {
                        diagnostics.Add($"{name}: GetPoints failed, rc={rc}.");
                        continue;
                    }

                    Point3 pStart = GetPoint(p1);
                    Point3 pEnd = GetPoint(p2);

                    string label = name;
                    string story = string.Empty;
                    string tmpLabel = string.Empty;
                    string tmpStory = string.Empty;
                    if (frames.GetLabelFromName(name, ref tmpLabel, ref tmpStory) == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(tmpLabel)) label = tmpLabel;
                        story = tmpStory ?? string.Empty;
                    }

                    string sectionName = string.Empty;
                    string autoSelect = string.Empty;
                    frames.GetSection(name, ref sectionName, ref autoSelect);

                    var (width, depth) = ReadRectangleSection(sectionName, diagnostics, name);

                    Point3 basePoint = pStart.Z <= pEnd.Z ? pStart : pEnd;
                    Point3 topPoint = pStart.Z <= pEnd.Z ? pEnd : pStart;

                    if (!_ratioProvider.TryGetRatioDecimal(name, story, out double ratio, out string ratioDiagnostic))
                    {
                        diagnostics.Add($"{name} / {story}: reinforcement ratio unavailable. {ratioDiagnostic}");
                        continue;
                    }

                    if (ratio < 0)
                    {
                        diagnostics.Add($"{name} / {story}: negative RFT ratio returned; segment skipped.");
                        continue;
                    }

                    segments.Add(new ColumnSegment
                    {
                        Id = name,
                        StoryName = story,
                        BaseElevationMm = basePoint.Z,
                        TopElevationMm = topPoint.Z,
                        BasePoint = new Point2D(basePoint.X, basePoint.Y),
                        TopPoint = new Point2D(topPoint.X, topPoint.Y),
                        WidthMm = width,
                        DepthMm = depth,
                        RftRatioDecimal = ratio,
                        SectionName = sectionName
                    });
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"{name}: extraction failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return segments
                .OrderBy(s => s.BaseElevationMm)
                .ThenBy(s => s.TopElevationMm)
                .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Point3 GetPoint(string pointName)
        {
            double x = 0, y = 0, z = 0;
            int rc = _sap.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z, "Global");
            if (rc != 0)
                throw new InvalidOperationException($"PointObj.GetCoordCartesian('{pointName}') failed. Return code={rc}.");
            return new Point3(x, y, z);
        }

        private (double Width, double Depth) ReadRectangleSection(string sectionName, List<string> diagnostics, string objectName)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new InvalidOperationException($"{objectName}: empty ETABS section name.");

            string fileName = string.Empty;
            string material = string.Empty;
            double t3 = 0, t2 = 0;
            int color = 0;
            string notes = string.Empty;
            string guid = string.Empty;

            int rc = _sap.PropFrame.GetRectangle(sectionName, ref fileName, ref material, ref t3, ref t2, ref color, ref notes, ref guid);
            if (rc != 0)
            {
                diagnostics.Add($"{objectName}: section '{sectionName}' is not a readable rectangular frame section, rc={rc}.");
                throw new InvalidOperationException($"PropFrame.GetRectangle('{sectionName}') failed. Return code={rc}.");
            }

            // Existing project convention: ETABS rectangle returns T2 and T3; preserve both dimensions.
            return (t2, t3);
        }

        private readonly struct Point3
        {
            public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }
    }

    /// <summary>
    /// Placeholder until the exact ETABS design-result ratio API is confirmed.
    /// This deliberately fails instead of guessing a method/field.
    /// </summary>
    public sealed class UnconfiguredReinforcementRatioProvider : IColumnReinforcementRatioProvider
    {
        public bool TryGetRatioDecimal(string etabsObjectName, string storyName, out double ratioDecimal, out string diagnostic)
        {
            ratioDecimal = 0;
            diagnostic = "ETABS column design reinforcement-ratio API field/method is not configured. Inspect the existing CTI/spColumn/spWall export implementation before enabling live extraction.";
            return false;
        }
    }
}
