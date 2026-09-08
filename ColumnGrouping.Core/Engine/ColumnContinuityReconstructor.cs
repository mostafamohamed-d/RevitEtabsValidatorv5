using System;
using System.Collections.Generic;
using System.Linq;
using ColumnGrouping.Core.Models;

namespace ColumnGrouping.Core.Engine
{
    public sealed class ColumnContinuityReconstructor
    {
        private readonly ColumnGroupingSettings _settings;

        public ColumnContinuityReconstructor(ColumnGroupingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
        }

        public IReadOnlyList<PhysicalColumn> Reconstruct(IReadOnlyCollection<ColumnSegment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));

            var byBase = segments
                .Where(IsValidSegment)
                .OrderBy(s => s.BaseElevationMm)
                .ThenBy(s => s.TopElevationMm)
                .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<PhysicalColumn>();
            int id = 1;

            foreach (var start in byBase)
            {
                if (used.Contains(start.Id)) continue;

                var physical = new PhysicalColumn { PhysicalColumnId = $"PC{id++:000}" };
                var current = start;
                physical.Segments.Add(current);
                used.Add(current.Id);

                while (true)
                {
                    var candidate = FindNextCandidate(current, byBase, used);
                    if (candidate == null) break;

                    var link = Evaluate(current, candidate);
                    physical.ContinuityLinks.Add(link);

                    if (link.Status == ContinuityStatus.NotConnected)
                        break;

                    physical.Segments.Add(candidate);
                    used.Add(candidate.Id);
                    current = candidate;
                }

                result.Add(physical);
            }

            return result;
        }

        private ColumnSegment? FindNextCandidate(ColumnSegment current, List<ColumnSegment> all, HashSet<string> used)
        {
            return all
                .Where(s => !used.Contains(s.Id))
                .Where(s => Math.Abs(s.BaseElevationMm - current.TopElevationMm) <= _settings.ElevationToleranceMm)
                .OrderBy(s => PlanDistance(current.TopPoint, s.BasePoint))
                .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private ContinuityLink Evaluate(ColumnSegment lower, ColumnSegment upper)
        {
            double shift = PlanDistance(lower.TopPoint, upper.BasePoint);
            ContinuityStatus status;
            string message;

            if (shift <= _settings.AutoConnectToleranceMm)
            {
                status = ContinuityStatus.Connected;
                message = $"Connected automatically. Horizontal shift = {shift:F1} mm.";
            }
            else if (shift <= _settings.WarningToleranceMm)
            {
                status = ContinuityStatus.ConnectedWithWarning;
                message = $"Connected with warning. Horizontal shift = {shift:F1} mm; engineer review recommended.";
            }
            else
            {
                status = ContinuityStatus.NotConnected;
                message = $"Not connected. Horizontal shift = {shift:F1} mm exceeds { _settings.WarningToleranceMm:F1} mm.";
            }

            return new ContinuityLink
            {
                LowerSegmentId = lower.Id,
                UpperSegmentId = upper.Id,
                ShiftMm = shift,
                Status = status,
                Message = message
            };
        }

        private static double PlanDistance(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsValidSegment(ColumnSegment segment)
        {
            return segment != null
                   && !string.IsNullOrWhiteSpace(segment.Id)
                   && segment.TopElevationMm >= segment.BaseElevationMm;
        }
    }
}
