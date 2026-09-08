using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ColumnGrouping.Core.Models;

namespace ColumnGrouping.Core.Engine
{
    public sealed class ColumnGroupingEngine
    {
        private readonly ColumnGroupingSettings _settings;
        private readonly ReinforcementCalculator _reinforcementCalculator;
        private readonly ColumnContinuityReconstructor _continuity;

        public ColumnGroupingEngine(ColumnGroupingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _reinforcementCalculator = new ReinforcementCalculator(_settings);
            _continuity = new ColumnContinuityReconstructor(_settings);
        }

        public GroupingReport Run(IReadOnlyCollection<ColumnSegment> segments)
        {
            var physical = _continuity.Reconstruct(segments);

            foreach (var column in physical)
            {
                foreach (var link in column.ContinuityLinks)
                {
                    if (link.Status == ContinuityStatus.ConnectedWithWarning)
                        column.ContinuityWarnings.Add(link.Message);
                }

                BuildLevelProfiles(column);
                column.Fingerprint = BuildFingerprint(column);
            }

            var groups = BuildGroups(physical);
            AssignGroupNames(groups);

            return new GroupingReport
            {
                CreatedUtc = DateTime.UtcNow,
                PhysicalColumns = { }
            }.WithPhysicalColumns(physical).WithGroups(groups);
        }

        private void BuildLevelProfiles(PhysicalColumn column)
        {
            var profiles = column.Segments
                .OrderBy(s => s.BaseElevationMm)
                .ThenBy(s => s.TopElevationMm)
                .ThenBy(s => s.StoryName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new ColumnLevelProfile
                {
                    LevelName = s.StoryName,
                    WidthMm = s.WidthMm,
                    DepthMm = s.DepthMm,
                    RftRatioDecimal = s.RftRatioDecimal,
                    Reinforcement = _reinforcementCalculator.Calculate(s.WidthMm, s.DepthMm, s.RftRatioDecimal)
                })
                .ToList();

            column.LevelProfiles.AddRange(profiles);
        }

        private string BuildFingerprint(PhysicalColumn column)
        {
            var sb = new StringBuilder();
            foreach (var p in column.LevelProfiles)
            {
                sb.Append(p.LevelName.Trim().ToUpperInvariant()).Append('|');
                sb.Append(NormalizeDimension(p.WidthMm)).Append('x').Append(NormalizeDimension(p.DepthMm)).Append('|');
                sb.Append(p.RftRatioDecimal.ToString("0.000000", CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }

        private List<ColumnGroup> BuildGroups(IReadOnlyList<PhysicalColumn> columns)
        {
            var groups = new List<ColumnGroup>();

            foreach (var column in columns
                         .OrderBy(c => LevelSetKey(c), StringComparer.Ordinal)
                         .ThenBy(c => SizeSortKey(c), StringComparer.Ordinal)
                         .ThenBy(c => c.PhysicalColumnId, StringComparer.Ordinal))
            {
                ColumnGroup? matched = null;

                foreach (var group in groups.Where(g => string.Equals(g.LevelSetKey, LevelSetKey(column), StringComparison.Ordinal)))
                {
                    if (ProfilesEquivalentToCanonical(column, group.Columns[0]))
                    {
                        matched = group;
                        break;
                    }
                }

                if (matched == null)
                {
                    matched = new ColumnGroup
                    {
                        LevelSetKey = LevelSetKey(column),
                        SizeSortKey = SizeSortKey(column)
                    };
                    groups.Add(matched);
                }

                matched.Columns.Add(column);
            }

            return groups
                .OrderBy(g => g.LevelSetKey, StringComparer.Ordinal)
                .ThenBy(g => g.SizeSortKey, StringComparer.Ordinal)
                .ToList();
        }

        private bool ProfilesEquivalentToCanonical(PhysicalColumn candidate, PhysicalColumn canonical)
        {
            if (candidate.LevelProfiles.Count != canonical.LevelProfiles.Count)
                return false;

            for (int i = 0; i < candidate.LevelProfiles.Count; i++)
            {
                var a = candidate.LevelProfiles[i];
                var b = canonical.LevelProfiles[i];

                if (!string.Equals(a.LevelName, b.LevelName, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (Math.Abs(a.WidthMm - b.WidthMm) > _settings.DimensionToleranceMm)
                    return false;

                if (Math.Abs(a.DepthMm - b.DepthMm) > _settings.DimensionToleranceMm)
                    return false;

                if (Math.Abs(a.RftRatioDecimal - b.RftRatioDecimal) > _settings.RftToleranceDecimal)
                    return false;
            }

            return true;
        }

        private static string LevelSetKey(PhysicalColumn column)
        {
            return string.Join("|", column.LevelProfiles
                .Select(p => p.LevelName.Trim().ToUpperInvariant()));
        }

        private static string SizeSortKey(PhysicalColumn column)
        {
            // Lexicographic profile sort: level order is already fixed, then width/depth at each level.
            return string.Join("|", column.LevelProfiles.Select(p =>
                NormalizeDimension(p.WidthMm) + "x" + NormalizeDimension(p.DepthMm)));
        }

        private static string NormalizeDimension(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void AssignGroupNames(List<ColumnGroup> groups)
        {
            int index = 1;
            foreach (var group in groups)
            {
                group.GroupName = "C" + index.ToString(CultureInfo.InvariantCulture);
                foreach (var column in group.Columns)
                    column.GroupName = group.GroupName;
                index++;
            }
        }
    }

    internal static class GroupingReportExtensions
    {
        public static GroupingReport WithPhysicalColumns(this GroupingReport report, IEnumerable<PhysicalColumn> columns)
        {
            report.PhysicalColumns.Clear();
            report.PhysicalColumns.AddRange(columns);
            return report;
        }

        public static GroupingReport WithGroups(this GroupingReport report, IEnumerable<ColumnGroup> groups)
        {
            report.Groups.Clear();
            report.Groups.AddRange(groups);
            return report;
        }
    }
}
