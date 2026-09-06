using Autodesk.Revit.UI;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.ETABS;
using RevitEtabsValidator.Revit.Commands;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfTextBox = System.Windows.Controls.TextBox;
using ValidationResult = RevitEtabsValidator.Core.Validation.ValidationResult;

namespace RevitEtabsValidator.Revit.UI;

public partial class MainWindow : Window
{
    private readonly UIApplication _uiapp;
    private readonly RevitRequestHandler _handler;
    private readonly ExternalEvent _event;
    private readonly EtabsConnection _etabs = new();
    private List<ColumnElement> _revitColumns = new(), _etabsColumns = new();
    private List<BeamElement> _revitBeams = new(), _etabsBeams = new();
    private ValidationReport _columnReport = new(), _beamReport = new();
    private List<ValidationResult> _all = new();
    private readonly ObservableCollection<ValidationResult> _visible = new();
    private ValidationResult? _selected;

    public MainWindow(UIApplication uiapp)
    {
        InitializeComponent();
        _uiapp = uiapp;
        _handler = new RevitRequestHandler { Window = this };
        _event = ExternalEvent.Create(_handler);
        ResultsGrid.ItemsSource = _visible;
    }

    // NOTE: The repository contains the full existing class. This focused replacement
    // block is intentionally limited to the floor-mapping method below.
    private void BuildFloorMapping()
    {
        _revitToEtabsStory.Clear();
        _etabsToRevitLevel.Clear();

        foreach (var level in _revitLevels)
        {
            if (_etabsStoryElevationsMm.Count == 0)
                continue;

            var nearest = _etabsStoryElevationsMm
                .OrderBy(x => Math.Abs(x.Value - level.ElevationMm))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(nearest.Key))
                continue;

            _revitToEtabsStory[level.Name] = nearest.Key;

            // Never index a dictionary with a key until the key has been confirmed.
            // When several Revit levels map to the same ETABS story, keep the Revit
            // level whose elevation is closest to the ETABS story elevation.
            if (!_etabsToRevitLevel.TryGetValue(nearest.Key, out var currentLevelName))
            {
                _etabsToRevitLevel[nearest.Key] = level.Name;
                continue;
            }

            if (!_revitLevels.Any(x => string.Equals(x.Name, currentLevelName, StringComparison.OrdinalIgnoreCase)))
            {
                _etabsToRevitLevel[nearest.Key] = level.Name;
                continue;
            }

            var currentLevel = _revitLevels.First(x =>
                string.Equals(x.Name, currentLevelName, StringComparison.OrdinalIgnoreCase));

            var currentDifference = Math.Abs(
                _etabsStoryElevationsMm[nearest.Key] - currentLevel.ElevationMm);
            var newDifference = Math.Abs(
                _etabsStoryElevationsMm[nearest.Key] - level.ElevationMm);

            if (newDifference < currentDifference)
                _etabsToRevitLevel[nearest.Key] = level.Name;
        }
    }
}
