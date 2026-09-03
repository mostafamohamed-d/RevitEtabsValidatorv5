using System.Collections.Generic;
namespace RevitEtabsValidator.Core.Validation;
public sealed class ValidationResult
{
    // These four are genuinely one-sided for Missing* results (e.g. a
    // MissingInRevit result only ever sets the Etabs* fields) - nullable
    // matches how the rest of the codebase already treats them (MainWindow's
    // FindPoint and SelectInRevit_Click both null-check RevitElementId before
    // use), rather than silently allowing CS8618 to be ignored.
    public string? RevitElementId { get; set; }
    public string? EtabsElementId { get; set; }
    public string? RevitName { get; set; }
    public string? EtabsName { get; set; }

    // These are always populated by ModelComparer (via Base() or the
    // Missing*/AmbiguousMatch result builders), so a non-null default is
    // correct rather than making every consumer null-check them too.
    public string ElementType { get; set; } = "";
    public string StoryOrLevel { get; set; } = "";
    public string Message { get; set; } = "";

    public ValidationStatus Status { get; set; }
    public Severity Severity { get; set; }
    public double Confidence { get; set; }
    public double PositionDeltaMm { get; set; }
    public double ElevationDeltaMm { get; set; }
    public double WidthDeltaMm { get; set; }
    public double DepthDeltaMm { get; set; }
    public double LengthDeltaMm { get; set; }
    public double RotationDeltaDeg { get; set; }
    public Dictionary<string,string> Differences { get; set; } = new();
}
