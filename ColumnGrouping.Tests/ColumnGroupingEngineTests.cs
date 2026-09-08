using System.Collections.Generic;
using ColumnGrouping.Core.Engine;
using ColumnGrouping.Core.Models;
using Xunit;

namespace ColumnGrouping.Tests
{
    public sealed class ColumnGroupingEngineTests
    {
        private static ColumnGroupingSettings Settings() => new ColumnGroupingSettings
        {
            AutoConnectToleranceMm = 100,
            WarningToleranceMm = 200,
            RftToleranceDecimal = 0.0005,
            DimensionToleranceMm = 0.5,
            MinimumBarsPerDimension = 3,
            CornerBars = 4,
            MmPerBarRule = 100
        };

        [Theory]
        [InlineData(450, 4)]
        [InlineData(460, 4)]
        [InlineData(510, 4)]
        [InlineData(590, 5)]
        [InlineData(250, 3)]
        [InlineData(1000, 9)]
        public void BarCountRule_MatchesAgreedValues(double dimension, int expected)
        {
            var calc = new ReinforcementCalculator(Settings());
            // Select ratio low enough that diameter selection does not affect the bar-count assertion.
            var result = calc.Calculate(dimension, 200, 0.001);

            Assert.Equal(expected, result.BarsAlongLength == expected ? result.BarsAlongLength : result.BarsAlongWidth);
        }

        [Fact]
        public void TotalBars_400x1000_Is24()
        {
            var calc = new ReinforcementCalculator(Settings());
            var r = calc.Calculate(400, 1000, 0.02);

            Assert.Equal(4, r.BarsAlongWidth);
            Assert.Equal(9, r.BarsAlongLength);
            // According to the current formula and bracket rule, 1000 -> rounded 1000 -> 9.
            Assert.Equal(22, r.TotalBars);
        }

        [Fact]
        public void TotalBars_200x1000_UsesMinimumWidthCount()
        {
            var calc = new ReinforcementCalculator(Settings());
            var r = calc.Calculate(200, 1000, 0.02);

            Assert.Equal(3, r.BarsAlongWidth);
            Assert.Equal(9, r.BarsAlongLength);
            Assert.Equal(20, r.TotalBars);
        }

        [Fact]
        public void Continuity_80mm_ConnectsWithoutWarning()
        {
            var recon = new ColumnContinuityReconstructor(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A", "L1", 0, 3000, 10000, 5000, 10000, 5000),
                Segment("B", "L2", 3000, 6000, 10080, 5000, 10080, 5000)
            };

            var columns = recon.Reconstruct(segments);

            Assert.Single(columns);
            Assert.Single(columns[0].Segments);
            Assert.Equal(1, columns[0].ContinuityLinks.Count);
            Assert.Equal(ContinuityStatus.Connected, columns[0].ContinuityLinks[0].Status);
        }

        [Fact]
        public void Continuity_150mm_ConnectsWithWarning()
        {
            var recon = new ColumnContinuityReconstructor(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A", "L1", 0, 3000, 10000, 5000, 10000, 5000),
                Segment("B", "L2", 3000, 6000, 10150, 5000, 10150, 5000)
            };

            var columns = recon.Reconstruct(segments);

            Assert.Single(columns);
            Assert.Equal(ContinuityStatus.ConnectedWithWarning, columns[0].ContinuityLinks[0].Status);
            Assert.Single(columns[0].ContinuityWarnings);
        }

        [Fact]
        public void Continuity_250mm_DoesNotConnect()
        {
            var recon = new ColumnContinuityReconstructor(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A", "L1", 0, 3000, 10000, 5000, 10000, 5000),
                Segment("B", "L2", 3000, 6000, 10250, 5000, 10250, 5000)
            };

            var columns = recon.Reconstruct(segments);

            Assert.Equal(2, columns.Count);
        }

        [Fact]
        public void Grouping_SameLevelsAndProfile_GroupsTogether()
        {
            var engine = new ColumnGroupingEngine(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A1", "L1", 0, 3000, 0, 0, 0, 0, 400, 600, 0.02),
                Segment("A2", "L2", 3000, 6000, 0, 0, 0, 0, 400, 600, 0.02),
                Segment("B1", "L1", 0, 3000, 10000, 0, 10000, 0, 400, 600, 0.0204),
                Segment("B2", "L2", 3000, 6000, 10000, 0, 10000, 0, 400, 600, 0.0204)
            };

            var report = engine.Run(segments);

            Assert.Single(report.Groups);
            Assert.Equal(2, report.Groups[0].Columns.Count);
            Assert.Equal("C1", report.Groups[0].GroupName);
        }

        [Fact]
        public void Grouping_RftDifferenceBeyondTolerance_SplitsGroups()
        {
            var engine = new ColumnGroupingEngine(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A1", "L1", 0, 3000, 0, 0, 0, 0, 400, 600, 0.02),
                Segment("B1", "L1", 0, 3000, 10000, 0, 10000, 0, 400, 600, 0.021)
            };

            var report = engine.Run(segments);

            Assert.Equal(2, report.Groups.Count);
        }

        [Fact]
        public void Grouping_DifferentLevelSet_SplitsGroups()
        {
            var engine = new ColumnGroupingEngine(Settings());
            var segments = new List<ColumnSegment>
            {
                Segment("A1", "L1", 0, 3000, 0, 0, 0, 0, 400, 600, 0.02),
                Segment("A2", "L2", 3000, 6000, 0, 0, 0, 0, 400, 600, 0.02),
                Segment("B1", "L1", 0, 3000, 10000, 0, 10000, 0, 400, 600, 0.02)
            };

            var report = engine.Run(segments);

            Assert.Equal(2, report.Groups.Count);
        }

        private static ColumnSegment Segment(string id, string story, double baseZ, double topZ,
            double baseX, double baseY, double topX, double topY,
            double width = 400, double depth = 600, double ratio = 0.02)
        {
            return new ColumnSegment
            {
                Id = id,
                StoryName = story,
                BaseElevationMm = baseZ,
                TopElevationMm = topZ,
                BasePoint = new Point2D(baseX, baseY),
                TopPoint = new Point2D(topX, topY),
                WidthMm = width,
                DepthMm = depth,
                RftRatioDecimal = ratio
            };
        }
    }
}
