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
            // Register the ETABS resolver first, then proactively load ETABSv1 from the
            // installed ETABS version. This prevents a stale ETABSv1.dll in another
            // add-in directory from winning the .NET 8 assembly bind.
            RevitEtabsValidator.ETABS.EtabsAssemblyResolver.Initialize();
#if ETABS22 || ETABS21
            try
            {
                RevitEtabsValidator.ETABS.EtabsAssemblyResolver.EnsureEtabsApiLoaded();
            }
            catch (Exception ex)
            {
                LogStartup("ETABS API preload warning: " + ex);
                // The validator can still start; the Connect button will report the detailed error.
            }
#endif

            try
            {
                application.CreateRibbonTab(RibbonTab);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
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
                    pb.ToolTip = "Compare Revit structural beams and columns against ETABS.";
                    pb.LongDescription =
                        "Compares structural members using the project's Revit Internal Origin ↔ ETABS Global coordinate contract.";
                }
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            LogStartup("RevitEtabsValidator startup failure: " + ex);
            return Result.Failed;
        }
    }

    private static void LogStartup(string text)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RevitEtabsValidator");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "startup-error.log"),
                DateTime.Now.ToString("O") + Environment.NewLine + text + Environment.NewLine);
        }
        catch
        {
        }
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
