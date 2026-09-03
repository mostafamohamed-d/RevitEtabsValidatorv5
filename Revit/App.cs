using Autodesk.Revit.UI;
using System.Reflection;
using System.IO;

namespace RevitEtabsValidator;

public sealed class App : IExternalApplication
{
    private const string RibbonTab = "Structural QA";
    private const string RibbonPanel = "Model Coordination";
    private const string CommandName = "RevitEtabsValidator";
    private const string CommandType = "RevitEtabsValidator.Revit.Commands.ShowValidatorCommand";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            try
            {
                application.CreateRibbonTab(RibbonTab);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists. Continue and reuse it.
            }

            var panel = application.GetRibbonPanels(RibbonTab)
                .FirstOrDefault(p => string.Equals(p.Name, RibbonPanel, StringComparison.OrdinalIgnoreCase));

            if (panel == null)
                panel = application.CreateRibbonPanel(RibbonTab, RibbonPanel);

            var alreadyThere = panel.GetItems()
                .OfType<PushButton>()
                .Any(x => string.Equals(x.Name, CommandName, StringComparison.OrdinalIgnoreCase));

            if (!alreadyThere)
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                var button = new PushButtonData(
                    CommandName,
                    "Revit ↔ ETABS\nValidator",
                    asm,
                    CommandType);

                if (panel.AddItem(button) is PushButton pb)
                {
                    pb.ToolTip = "Compare Revit 2025 structural beams and columns against ETABS.";
                    pb.LongDescription =
                        "Reads Revit structural framing/columns and compares them with ETABS frame objects by level, " +
                        "position, elevation, section dimensions, length and rotation using configurable tolerances.";
                }
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RevitEtabsValidator");
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, "startup-error.log"),
                    DateTime.Now.ToString("O") + Environment.NewLine + ex);
            }
            catch
            {
                // Do not allow logging failure to mask the original startup failure.
            }

            // Prevent an unhandled startup exception from making the add-in unload without diagnostics.
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
