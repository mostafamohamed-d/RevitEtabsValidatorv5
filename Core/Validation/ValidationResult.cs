using System.Collections.Generic;

namespace RevitEtabsValidator.Core.Validation;

public sealed class ValidationResult
{
    public string? RevitElementId { get; set; }
    public string? EtabsElementId { get; set; }
    public string? RevitName { get; set; }
    public string? EtabsName { get; set; }

    public string ElementType { get; set; } = "";
    public string StoryOrLevel { get; set; } = "";
    public string Message { get; set; } = "";

    // Detailed coordination evidence used by the advanced UI.
    public string RevitLocation { get; set; } = "";
    public string EtabsLocation { get; set; } = "";
    public string RevitSection { get; set; } = "";
    public string EtabsSection { get; set; } = "";

    public ValidationStatus Status { get; set; }
    public Severity Severity { get; set; }
    public double Confidence { get; set; }
    public double PositionDeltaMm { get; set; }
    public double ElevationDeltaMm { get; set; }
    public double WidthDeltaMm { get; set; }
    public double DepthDeltaMm { get; set; }
    public double LengthDeltaMm { get; set; }
    public double RotationDeltaDeg { get; set; }
    public Dictionary<string, string> Differences { get; set; } = new();
}
