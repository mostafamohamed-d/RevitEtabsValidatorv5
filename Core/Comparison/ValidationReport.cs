using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.Core.Models;
namespace RevitEtabsValidator.Core.Comparison;
public sealed class ValidationReport
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ValidationResult> Results { get; set; } = new();
    public int Total => Results.Count;
    public int Matched => Results.Count(x=>x.Status==ValidationStatus.Matched);
    public int Errors => Results.Count(x=>x.Severity==Severity.Error || x.Severity==Severity.Critical);
    public int Warnings => Results.Count(x=>x.Severity==Severity.Warning);
    public int MissingRevit => Results.Count(x=>x.Status==ValidationStatus.MissingInRevit);
    public int MissingEtabs => Results.Count(x=>x.Status==ValidationStatus.MissingInEtabs);
}
