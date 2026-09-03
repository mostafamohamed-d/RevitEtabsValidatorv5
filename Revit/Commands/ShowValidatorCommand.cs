using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitEtabsValidator.Revit.UI;
namespace RevitEtabsValidator.Revit.Commands;
[Transaction(TransactionMode.Manual)]
public sealed class ShowValidatorCommand:IExternalCommand
{
    public Result Execute(ExternalCommandData commandData,ref string message,ElementSet elements)
    { AppState.Open(commandData.Application); return Result.Succeeded; }
}
