using System;
using System.Collections.Generic;

namespace ColumnGrouping.Core.Models
{
    public sealed class ColumnGroupingSettings
    {
        public double AutoConnectToleranceMm { get; set; } = 100.0;
        public double WarningToleranceMm { get; set; } = 200.0;
        public double ElevationToleranceMm { get; set; } = 1.0;
        public double DimensionToleranceMm { get; set; } = 0.5;
        public double RftToleranceDecimal { get; set; } = 0.0005; // +/- 0.05%
        public int MinimumBarsPerDimension { get; set; } = 3;
        public int CornerBars { get; set; } = 4;
        public double MmPerBarRule { get; set; } = 100.0;
        public double ExactHalfRoundingRuleMm { get; set; } = 50.0;
        public double[] AllowedBarDiametersMm { get; set; } = { 12, 16, 20, 32 };
        public int[] AllowedBarAreasMm2 { get; set; } = { 113, 201, 314, 804 };

        public void Validate()
        {
            if (AutoConnectToleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(AutoConnectToleranceMm));
            if (WarningToleranceMm < AutoConnectToleranceMm) throw new ArgumentException("Warning tolerance must be >= auto-connect tolerance.");
            if (ElevationToleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(ElevationToleranceMm));
            if (DimensionToleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(DimensionToleranceMm));
            if (RftToleranceDecimal < 0) throw new ArgumentOutOfRangeException(nameof(RftToleranceDecimal));
            if (MinimumBarsPerDimension < 1) throw new ArgumentOutOfRangeException(nameof(MinimumBarsPerDimension));
            if (MmPerBarRule <= 0) throw new ArgumentOutOfRangeException(nameof(MmPerBarRule));
            if (AllowedBarDiametersMm == null || AllowedBarAreasMm2 == null || AllowedBarDiametersMm.Length != AllowedBarAreasMm2.Length || AllowedBarDiametersMm.Length == 0)
                throw new ArgumentException("Allowed bar diameters and areas must be non-empty and have equal lengths.");
        }
    }

    public readonly struct Point2D
    {
        public Point2D(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
    }

    public sealed class ColumnSegment
    {
        public string Id { get; set; } = "";
        public string StoryName { get; set; } = "";
        public double BaseElevationMm { get; set; }
        public double TopElevationMm { get; set; }
        public Point2D BasePoint { get; set; }
        public Point2D TopPoint { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double RftRatioDecimal { get; set; }
        public string? SectionName { get; set; }
    }

    public enum ContinuityStatus
    {
        Connected,
        ConnectedWithWarning,
        NotConnected
    }

    public sealed class ContinuityLink
    {
        public string LowerSegmentId { get; set; } = "";
        public string UpperSegmentId { get; set; } = "";
        public double ShiftMm { get; set; }
        public ContinuityStatus Status { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class ReinforcementResult
    {
        public double GrossAreaMm2 { get; set; }
        public double RequiredSteelAreaMm2 { get; set; }
        public int BarsAlongLength { get; set; }
        public int BarsAlongWidth { get; set; }
        public int TotalBars { get; set; }
        public double RequiredAreaPerBarMm2 { get; set; }
        public double SelectedBarDiameterMm { get; set; }
        public double SelectedBarAreaMm2 { get; set; }
        public double ProvidedSteelAreaMm2 { get; set; }
        public int RoundedLengthMm { get; set; }
        public int RoundedWidthMm { get; set; }
    }

    public sealed class ColumnLevelProfile
    {
        public string LevelName { get; set; } = "";
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double RftRatioDecimal { get; set; }
        public ReinforcementResult Reinforcement { get; set; } = new ReinforcementResult();
    }

    public sealed class PhysicalColumn
    {
        public string PhysicalColumnId { get; set; } = "";
        public List<ColumnSegment> Segments { get; } = new List<ColumnSegment>();
        public List<ContinuityLink> ContinuityLinks { get; } = new List<ContinuityLink>();
        public List<string> ContinuityWarnings { get; } = new List<string>();
        public List<ColumnLevelProfile> LevelProfiles { get; } = new List<ColumnLevelProfile>();
        public string GroupName { get; set; } = "";
        public string Fingerprint { get; set; } = "";
    }

    public sealed class ColumnGroup
    {
        public string GroupName { get; set; } = "";
        public string LevelSetKey { get; set; } = "";
        public string SizeSortKey { get; set; } = "";
        public List<PhysicalColumn> Columns { get; } = new List<PhysicalColumn>();
    }

    public sealed class GroupingReport
    {
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<PhysicalColumn> PhysicalColumns { get; } = new List<PhysicalColumn>();
        public List<ColumnGroup> Groups { get; } = new List<ColumnGroup>();
    }
}
