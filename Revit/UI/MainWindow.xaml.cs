using Autodesk.Revit.UI;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.ETABS;
using RevitEtabsValidator.Revit.Commands;
using RevitEtabsValidator.Revit.Services;
using System.Collections.ObjectModel;
using System.IO;
using IOPath = System.IO.Path;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private List<ColumnElement> _revitColumns = new();
    private List<ColumnElement> _etabsColumns = new();
    private List<BeamElement> _revitBeams = new();
    private List<BeamElement> _etabsBeams = new();

    private ValidationReport _columnReport = new();
    private ValidationReport _beamReport = new();
    private List<ValidationResult> _all = new();
    private readonly ObservableCollection<ValidationResult> _floorVisible = new();
    private ValidationResult? _selected;
    private bool _validationPending;

    private List<(string Name, double ElevationMm)> _revitLevels = new();
    private readonly Dictionary<string, double> _etabsStoryElevationsMm = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _revitToEtabsStory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _etabsToRevitLevel = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _selectedRevitLevels = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _selectedEtabsStories = new(StringComparer.OrdinalIgnoreCase);

    private readonly TransformGroup _planTransform = new();
    private readonly ScaleTransform _planScale = new(1, 1);
    private readonly TranslateTransform _planTranslate = new(0, 0);
    private double _fitScale = 1;
    private double _zoom = 1;
    private Point _pan;
    private Point _panStart;
    private Point _translationStart;
    private bool _isPanning;
    private bool _planHasContent;
    private bool _ignorePlanResize;

    public MainWindow(UIApplication uiapp)
    {
        InitializeComponent();
        _uiapp = uiapp;
        _handler = new RevitRequestHandler { Window = this };
        _event = ExternalEvent.Create(_handler);
        FloorResultsGrid.ItemsSource = _floorVisible;

        _planTransform.Children.Add(_planScale);
        _planTransform.Children.Add(_planTranslate);
        PlanCanvas.RenderTransform = _planTransform;
        UpdateSelectedPanel();
    }

    public void SetRevitElements(List<ColumnElement> c, List<BeamElement> b)
    {
        _revitColumns = c ?? new List<ColumnElement>();
        _revitBeams = b ?? new List<BeamElement>();
        RevitColCount.Text = _revitColumns.Count.ToString();
        RevitBeamCount.Text = _revitBeams.Count.ToString();
    }

    public void SetStatus(string s)
    {
        if (Dispatcher.CheckAccess())
            StatusText.Text = s;
        else
            Dispatcher.BeginInvoke(new Action(() => StatusText.Text = s));
    }

    public void OnRevitReadCompleted()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_validationPending)
            {
                _validationPending = false;
                ContinueValidation();
            }
            else
            {
                PopulateRevitLevels();
            }
        }));
    }

    public void OnRevitReadFailed(Exception ex)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_validationPending)
            {
                _validationPending = false;
                SetStatus("Validation stopped because Revit could not be read: " + ex.Message);
            }
            else
            {
                SetStatus("Revit read failed: " + ex.Message);
            }
        }));
    }

    private void Raise(RevitRequest r, string id = "")
    {
        _handler.Request = r;
        _handler.IdToSelect = id;
        _event.Raise();
    }

    private void ReadRevit_Click(object s, RoutedEventArgs e)
    {
        _validationPending = false;
        SetStatus("Reading Revit structural framing and columns...");
        Raise(RevitRequest.ReadModels);
    }

    private void ConnectEtabs_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var ok = _etabs.ConnectRunning();
            if (!ok && StartEtabs.IsChecked == true)
                ok = _etabs.StartAndConnect();

            if (!ok)
            {
                ConnectionStateText.Text = "ETABS: Not connected";
                SetStatus(_etabs.Message + " Enable 'Start ETABS if not running' when required.");
                return;
            }

            ConnectionStateText.Text = "ETABS: Connected";
            SetStatus(_etabs.Message);
            ReadEtabs();
        }
        catch (Exception ex)
        {
            ConnectionStateText.Text = "ETABS: Connection error";
            SetStatus("ETABS connection failed: " + ex.Message);
        }
    }

    private void ReadEtabs()
    {
        try
        {
            var sapModel = _etabs.SapModel;
            if (sapModel == null)
            {
                ConnectionStateText.Text = "ETABS: Not connected";
                SetStatus("ETABS is not connected.");
                return;
            }

            if (!_etabs.SetUnitsKnMmC())
                SetStatus("Warning: ETABS units were not confirmed as kN-mm-C.");

            var reader = new EtabsModelReader(sapModel);
            var columns = reader.ReadColumns();
            var beams = reader.ReadBeams();

            _etabsStoryElevationsMm.Clear();
            foreach (var pair in reader.StoryElevationsMm)
                _etabsStoryElevationsMm[pair.Key] = pair.Value;

            _etabsColumns = columns;
            _etabsBeams = beams;

            EtabsColCount.Text = _etabsColumns.Count.ToString();
            EtabsBeamCount.Text = _etabsBeams.Count.ToString();

            SetStatus($"ETABS read complete: {_etabsColumns.Count} columns, {_etabsBeams.Count} beams. Excluded zero-prefixed frames: {reader.ExcludedZeroNameCount}.");
        }
        catch (Exception ex)
        {
            SetStatus("ETABS read failed: " + ex.Message);
        }
    }

    private ValidationTolerance Tol() => new()
    {
        PositionToleranceMm = Read(PositionTol, 25),
        ElevationToleranceMm = Read(ElevationTol, 25),
        DimensionToleranceMm = Read(SectionTol, 5),
        LengthToleranceMm = Read(LengthTol, 25),
        AngleToleranceDegrees = Read(AngleTol, 1)
    };

    private static double Read(WpfTextBox b, double d) =>
        double.TryParse(b.Text, out var v) && v >= 0 ? v : d;

    private void RunValidation_Click(object s, RoutedEventArgs e)
    {
        if (!_etabs.IsConnected)
        {
            SetStatus("Connect ETABS first.");
            return;
        }

        _validationPending = true;
        _all.Clear();
        _floorVisible.Clear();
        UpdateSummary();
        SetStatus("Reading Revit model before validation...");
        Raise(RevitRequest.ReadModels);
    }

    private void ContinueValidation()
    {
        try
        {
            if (_revitColumns.Count == 0 && _revitBeams.Count == 0)
            {
                SetStatus("Revit read completed but no beams or columns were found.");
                return;
            }

            ReadEtabs();
            if (!_etabs.IsConnected)
            {
                SetStatus("ETABS connection was lost before validation.");
                return;
            }

            PopulateRevitLevels();
            if (!ShowFloorSelection())
            {
                SetStatus("Validation cancelled by user.");
                return;
            }

            RunComparisonForSelectedScope();
        }
        catch (Exception ex)
        {
            SetStatus("Validation failed: " + ex);
        }
    }

    private void RunComparisonForSelectedScope()
    {
        try
        {
            var revitColumns = _revitColumns.Where(x => _selectedRevitLevels.Contains(x.LevelName)).ToList();
            var revitBeams = _revitBeams.Where(x => _selectedRevitLevels.Contains(x.LevelName)).ToList();

            var filterEtabs = _selectedEtabsStories.Count > 0 &&
                              _selectedEtabsStories.Count < _etabsStoryElevationsMm.Count;

            var etabsColumns = filterEtabs
                ? _etabsColumns.Where(x => _selectedEtabsStories.Contains(x.LevelName)).ToList()
                : _etabsColumns;
            var etabsBeams = filterEtabs
                ? _etabsBeams.Where(x => _selectedEtabsStories.Contains(x.LevelName)).ToList()
                : _etabsBeams;

            var t = Tol();
            var cmp = new ModelComparer();
            _columnReport = cmp.CompareColumns(revitColumns, etabsColumns, t);
            _beamReport = cmp.CompareBeams(revitBeams, etabsBeams, t);

            _all = _columnReport.Results
                .Concat(_beamReport.Results)
                .OrderBy(x => x.StoryOrLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ElementType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RevitName ?? x.EtabsName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            NormalizeEtabsOnlyResultLevels();
            UpdateSummary();
            PopulatePlanFloors();
            if (PlanFloorList.Items.Count > 0)
                PlanFloorList.SelectedIndex = 0;
            UpdateFloorResults();
            SetStatus($"Validation complete: {_all.Count} comparison results across {_selectedRevitLevels.Count} selected floor(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Validation failed: " + ex);
        }
    }

    private void UpdateSummary()
    {
        MatchedCount.Text = _all.Count(x => x.Status == ValidationStatus.Matched).ToString();
        WarningCount.Text = _all.Count(x => x.Severity == Severity.Warning).ToString();
        ErrorCount.Text = _all.Count(x => x.Severity == Severity.Error || x.Severity == Severity.Critical).ToString();
        MissingCount.Text = _all.Count(x => x.Status == ValidationStatus.MissingInRevit || x.Status == ValidationStatus.MissingInEtabs).ToString();
    }

    private void PopulateRevitLevels()
    {
        try
        {
            var doc = _uiapp.ActiveUIDocument?.Document;
            _revitLevels = doc == null
                ? new List<(string Name, double ElevationMm)>()
                : RevitLevelService.GetAll(doc).ToList();
        }
        catch
        {
            _revitLevels = new List<(string Name, double ElevationMm)>();
        }

        if (_revitLevels.Count == 0)
        {
            _revitLevels = _revitColumns.Concat<ElementBase>(_revitBeams)
                .GroupBy(x => x.LevelName, StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => (g.Key, g.Average(x => x.CenterPoint.Z)))
                .OrderBy(x => x.Item2)
                .ToList();
        }

        BuildFloorMapping();
    }

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
            if (!_etabsToRevitLevel.ContainsKey(nearest.Key) ||
                Math.Abs(_etabsStoryElevationsMm[nearest.Key] - level.ElevationMm) <
                Math.Abs(_etabsStoryElevationsMm[_revitToEtabsLevel[nearest.Key]] - level.ElevationMm))
                _etabsToRevitLevel[nearest.Key] = level.Name;
        }
    }

    private bool ShowFloorSelection()
    {
        if (_revitLevels.Count == 0)
        {
            SetStatus("No Revit levels were available for floor selection.");
            return false;
        }

        var structuralLevelNames = _revitColumns.Concat<ElementBase>(_revitBeams)
            .Select(x => x.LevelName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = _revitLevels
            .Where(x => structuralLevelNames.Count == 0 || structuralLevelNames.Contains(x.Name))
            .Select(x => new FloorScopeItem
            {
                RevitLevel = x.Name,
                RevitElevationMm = x.ElevationMm,
                EtabsStory = _revitToEtabsStory.TryGetValue(x.Name, out var story) ? story : "",
                EtabsElevationMm = _revitToEtabsStory.TryGetValue(x.Name, out var story2) && _etabsStoryElevationsMm.TryGetValue(story2, out var el) ? el : 0,
                IsSelected = true
            })
            .ToList();

        var dialog = new FloorSelectionWindow(items) { Owner = this };
        if (dialog.ShowDialog() != true)
            return false;

        ApplyFloorScope(dialog.SelectedItems);
        return true;
    }

    private void ApplyFloorScope(IReadOnlyList<FloorScopeItem> items)
    {
        _selectedRevitLevels = items.Select(x => x.RevitLevel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedEtabsStories = items
            .Select(x => x.EtabsStory)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ScopeText.Text = _selectedRevitLevels.Count == _revitLevels.Count
            ? "Scope: all floors"
            : $"Scope: {_selectedRevitLevels.Count} floor(s)";

        PopulatePlanFloors();
    }

    private void SelectFloors_Click(object s, RoutedEventArgs e)
    {
        if (_revitLevels.Count == 0)
            PopulateRevitLevels();

        if (_revitLevels.Count == 0)
        {
            MessageBox.Show(this, "Read Revit first so the available levels can be loaded.", "Validation Scope", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ShowFloorSelection() && _all.Count > 0)
            RunComparisonForSelectedScope();
    }

    private void AllFloors_Click(object s, RoutedEventArgs e)
    {
        if (_revitLevels.Count == 0)
            PopulateRevitLevels();

        ApplyFloorScope(_revitLevels.Select(x => new FloorScopeItem
        {
            RevitLevel = x.Name,
            RevitElevationMm = x.ElevationMm,
            EtabsStory = _revitToEtabsStory.TryGetValue(x.Name, out var story) ? story : "",
            EtabsElevationMm = _revitToEtabsStory.TryGetValue(x.Name, out var st) && _etabsStoryElevationsMm.TryGetValue(st, out var el) ? el : 0,
            IsSelected = true
        }).ToList());
        if (_all.Count > 0)
            RunComparisonForSelectedScope();
    }

    private void ClearFloorView_Click(object s, RoutedEventArgs e)
    {
        PlanFloorList.SelectedIndex = -1;
        PlanCanvas.Children.Clear();
        _planHasContent = false;
        SetStatus("Floor view cleared.");
    }

    private void PopulatePlanFloors()
    {
        var floors = _revitLevels
            .Select(x => x.Name)
            .Where(x => _selectedRevitLevels.Count == 0 || _selectedRevitLevels.Contains(x))
            .ToList();

        if (floors.Count == 0)
            floors = _all.Select(x => x.StoryOrLevel).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        PlanFloorList.ItemsSource = floors;
        if (floors.Count > 0 && PlanFloorList.SelectedIndex < 0)
            PlanFloorList.SelectedIndex = 0;
    }

    private void PlanFloorList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;
        var floor = PlanFloorList.SelectedItem?.ToString() ?? "";
        UpdateFloorResults();
        DrawPlan(floor);
    }

    private void UpdateFloorResults()
    {
        _floorVisible.Clear();
        var floor = PlanFloorList.SelectedItem?.ToString() ?? "";
        foreach (var result in _all.Where(x => string.IsNullOrWhiteSpace(floor) || string.Equals(x.StoryOrLevel, floor, StringComparison.OrdinalIgnoreCase)))
            _floorVisible.Add(result);

        FloorResultsSummary.Text = $"{_floorVisible.Count} result(s)";
    }

    private void FloorResultsGrid_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        _selected = FloorResultsGrid.SelectedItem as ValidationResult;
        UpdateSelectedPanel();
    }

    private ValidationResult? FindResultForElement(string id)
        => _all.FirstOrDefault(x => string.Equals(x.RevitElementId, id, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(x.EtabsElementId, id, StringComparison.OrdinalIgnoreCase));

    private ValidationResult? FindResultForPair(string id, bool etabs)
        => _all.FirstOrDefault(x => etabs
            ? string.Equals(x.EtabsElementId, id, StringComparison.OrdinalIgnoreCase)
            : string.Equals(x.RevitElementId, id, StringComparison.OrdinalIgnoreCase));

    private void DrawPlan(string level)
    {
        PlanCanvas.Children.Clear();
        _planHasContent = false;
        if (string.IsNullOrWhiteSpace(level))
            return;

        var mappedStory = _revitToEtabsStory.TryGetValue(level, out var story) ? story : "";
        var revB = _revitBeams.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList();
        var revC = _revitColumns.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList();
        var etaB = string.IsNullOrWhiteSpace(mappedStory)
            ? _etabsBeams.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList()
            : _etabsBeams.Where(x => string.Equals(x.LevelName, mappedStory, StringComparison.OrdinalIgnoreCase)).ToList();
        var etaC = string.IsNullOrWhiteSpace(mappedStory)
            ? _etabsColumns.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList()
            : _etabsColumns.Where(x => string.Equals(x.LevelName, mappedStory, StringComparison.OrdinalIgnoreCase)).ToList();

        var points = new List<Point3D>();
        points.AddRange(revB.SelectMany(x => new[] { x.StartPoint, x.EndPoint }));
        points.AddRange(etaB.SelectMany(x => new[] { x.StartPoint, x.EndPoint }));
        points.AddRange(revC.Select(x => x.CenterPoint));
        points.AddRange(etaC.Select(x => x.CenterPoint));

        if (points.Count == 0)
            return;

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var worldW = Math.Max(1, maxX - minX);
        var worldH = Math.Max(1, maxY - minY);
        PlanCanvas.Width = worldW + 40;
        PlanCanvas.Height = worldH + 40;

        Point Map(Point3D p) => new(p.X - minX + 20, maxY - p.Y + 20);

        foreach (var b in revB)
            AddBeamVisual(b, false, Map(b.StartPoint), Map(b.EndPoint));
        foreach (var b in etaB)
            AddBeamVisual(b, true, Map(b.StartPoint), Map(b.EndPoint));
        foreach (var c in revC)
            AddColumnVisual(c, false, Map(c.CenterPoint));
        foreach (var c in etaC)
            AddColumnVisual(c, true, Map(c.CenterPoint));

        FitPlan_Click(null, null);
        _planHasContent = true;
    }

    private void AddBeamVisual(BeamElement beam, bool etabs, Point a, Point b)
    {
        var line = new Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = b.X,
            Y2 = b.Y,
            Stroke = etabs ? Brushes.SlateGray : Brushes.SteelBlue,
            StrokeThickness = etabs ? 2.0 : 3.0,
            Opacity = 0.9,
            Tag = FindResultForPair(beam.Id, etabs)
        };
        if (etabs)
            line.StrokeDashArray = new DoubleCollection { 7, 5 };
        AttachVisual(line, beam.Name, etabs, beam.Id);
        PlanCanvas.Children.Add(line);
    }

    private void AddColumnVisual(ColumnElement column, bool etabs, Point p)
    {
        const double radius = 6;
        var result = FindResultForPair(column.Id, etabs);
        var problem = result != null && result.Status != ValidationStatus.Matched;
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = problem ? Brushes.Red : (etabs ? Brushes.DarkOrange : Brushes.SteelBlue),
            Fill = etabs ? Brushes.Transparent : (problem ? Brushes.MistyRose : Brushes.LightSteelBlue),
            StrokeThickness = 2,
            Tag = result
        };
        Canvas.SetLeft(ellipse, p.X - radius);
        Canvas.SetTop(ellipse, p.Y - radius);
        AttachVisual(ellipse, column.Name, etabs, column.Id);
        PlanCanvas.Children.Add(ellipse);
    }

    private void AttachVisual(FrameworkElement element, string name, bool etabs, string id)
    {
        element.Cursor = Cursors.Hand;
        element.ToolTip = etabs ? $"ETABS: {name}\nID: {id}\nClick for coordination details" : $"Revit: {name}\nID: {id}\nClick for coordination details";
        element.MouseLeftButtonDown += PlanVisual_Click;
    }

    private void PlanVisual_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ValidationResult result)
        {
            _selected = result;
            UpdateSelectedPanel();
            OpenDetails(result);
            e.Handled = true;
        }
    }

    private void UpdateSelectedPanel()
    {
        if (_selected == null)
        {
            SelectedTypeText.Text = "—";
            SelectedStatusText.Text = "No member selected";
            SelectedRevitText.Text = "Revit: —";
            SelectedEtabsText.Text = "ETABS: —";
            SelectedSectionText.Text = "Revit: —\nETABS: —";
            SelectedLocationText.Text = "Revit: —\nETABS: —";
            SelectedDeltaText.Text = "—";
            SelectedReasonText.Text = "Select a beam or column in the plan.";
            return;
        }

        SelectedTypeText.Text = _selected.ElementType;
        SelectedStatusText.Text = $"{_selected.Status} · {_selected.Severity} · Confidence {_selected.Confidence:F0}%";
        SelectedRevitText.Text = $"Revit: {_selected.RevitName ?? "—"} [{_selected.RevitElementId ?? "—"}]";
        SelectedEtabsText.Text = $"ETABS: {_selected.EtabsName ?? "—"} [{_selected.EtabsElementId ?? "—"}]";

        var revit = GetRevitElement(_selected);
        var etabs = GetEtabsElement(_selected);
        SelectedSectionText.Text = $"Revit: {FormatSection(revit, false)}\nETABS: {FormatSection(etabs, true)}";
        SelectedLocationText.Text = $"Revit: {FormatLocation(revit)}\nETABS: {FormatLocation(etabs)}";
        SelectedDeltaText.Text = $"ΔPos   {_selected.PositionDeltaMm:F1} mm\nΔElev  {_selected.ElevationDeltaMm:F1} mm\nΔW     {_selected.WidthDeltaMm:F1} mm\nΔD     {_selected.DepthDeltaMm:F1} mm\nΔL     {_selected.LengthDeltaMm:F1} mm\nΔRot   {_selected.RotationDeltaDeg:F1}°";
        SelectedReasonText.Text = BuildReason(_selected, revit, etabs);
    }

    private ElementBase? GetRevitElement(ValidationResult r)
        => r.RevitElementId == null ? null :
           (ElementBase?)_revitColumns.FirstOrDefault(x => x.Id == r.RevitElementId) ??
           _revitBeams.FirstOrDefault(x => x.Id == r.RevitElementId);

    private ElementBase? GetEtabsElement(ValidationResult r)
        => r.EtabsElementId == null ? null :
           (ElementBase?)_etabsColumns.FirstOrDefault(x => x.Id == r.EtabsElementId) ??
           _etabsBeams.FirstOrDefault(x => x.Id == r.EtabsElementId);

    private static string FormatSection(ElementBase? element, bool etabs)
    {
        if (element == null)
            return "—";
        if (element is ColumnElement)
        {
            if (etabs)
                return $"{element.SectionName} | Width {element.Width:F0} × Depth {element.Depth:F0} mm";
            return $"{element.SectionName} | b {element.Width:F0} × h {element.Depth:F0} mm";
        }
        if (element is BeamElement)
            return $"{element.SectionName} | {element.Width:F0} × {element.Depth:F0} mm";
        return element.SectionName;
    }

    private static string FormatLocation(ElementBase? element)
    {
        if (element == null)
            return "—";
        var c = element.CenterPoint;
        if (element is ColumnElement)
            return $"Mid: X {c.X:F1}, Y {c.Y:F1}, Z {c.Z:F1} mm";
        return $"Mid: X {c.X:F1}, Y {c.Y:F1}, Z {c.Z:F1} mm\nA:   X {element.StartPoint.X:F1}, Y {element.StartPoint.Y:F1}, Z {element.StartPoint.Z:F1} mm\nB:   X {element.EndPoint.X:F1}, Y {element.EndPoint.Y:F1}, Z {element.EndPoint.Z:F1} mm";
    }

    private static string BuildReason(ValidationResult result, ElementBase? revit, ElementBase? etabs)
    {
        if (result.Status == ValidationStatus.Matched)
            return "Matched: the plan geometry correspondence was established and all required validation checks are within the configured tolerances. Span-length difference is shown only as a diagnostic for analytical/physical end offsets.";
        if (result.Status == ValidationStatus.MissingInEtabs)
            return "Missing in ETABS: the Revit member did not find a valid ETABS counterpart through the plan-geometry identity gate.";
        if (result.Status == ValidationStatus.MissingInRevit)
            return "Missing in Revit: the ETABS member did not find a valid Revit counterpart through the plan-geometry identity gate.";
        if (result.Status == ValidationStatus.SectionMismatch)
            return $"Section mismatch. Revit = {FormatSection(revit, false)}; ETABS = {FormatSection(etabs, true)}.";
        if (result.Status == ValidationStatus.PositionMismatch)
            return $"Position mismatch. Revit location = {FormatLocation(revit)}; ETABS location = {FormatLocation(etabs)}.";
        if (result.Status == ValidationStatus.ElevationMismatch)
            return $"Elevation mismatch. The compared elevation difference is {result.ElevationDeltaMm:F1} mm. See the two model locations below for the actual Z values.";
        return result.Message;
    }

    private void OpenDetails_Click(object s, RoutedEventArgs e)
    {
        if (_selected != null)
            OpenDetails(_selected);
    }

    private void OpenDetails(ValidationResult result)
    {
        var revit = GetRevitElement(result);
        var etabs = GetEtabsElement(result);
        var body = new StringBuilder();
        body.AppendLine($"STATUS: {result.Status}    SEVERITY: {result.Severity}    CONFIDENCE: {result.Confidence:F0}%");
        body.AppendLine($"TYPE: {result.ElementType}    LEVEL: {result.StoryOrLevel}");
        body.AppendLine();
        body.AppendLine($"REVIT: {result.RevitName ?? "—"}    ID: {result.RevitElementId ?? "—"}");
        body.AppendLine(FormatSection(revit, false));
        body.AppendLine(FormatLocation(revit));
        body.AppendLine();
        body.AppendLine($"ETABS: {result.EtabsName ?? "—"}    ID: {result.EtabsElementId ?? "—"}");
        body.AppendLine(FormatSection(etabs, true));
        body.AppendLine(FormatLocation(etabs));
        body.AppendLine();
        body.AppendLine("DIFFERENCES");
        body.AppendLine($"Position   : {result.PositionDeltaMm:F1} mm");
        body.AppendLine($"Elevation  : {result.ElevationDeltaMm:F1} mm");
        body.AppendLine($"Width      : {result.WidthDeltaMm:F1} mm");
        body.AppendLine($"Depth      : {result.DepthDeltaMm:F1} mm");
        body.AppendLine($"Length     : {result.LengthDeltaMm:F1} mm");
        body.AppendLine($"Rotation   : {result.RotationDeltaDeg:F1} deg");
        body.AppendLine();
        body.AppendLine("WHY / STATUS");
        body.AppendLine(BuildReason(result, revit, etabs));
        body.AppendLine();
        body.AppendLine("RAW MESSAGE");
        body.AppendLine(result.Message);

        var dialog = new ValidationDetailsWindow($"{result.ElementType} — {result.Status}", body.ToString()) { Owner = this };
        dialog.ShowDialog();
    }

    private void SelectInRevit_Click(object s, RoutedEventArgs e)
    {
        if (_selected?.RevitElementId != null)
            Raise(RevitRequest.SelectRevitElement, _selected.RevitElementId);
    }

    private void FitPlan_Click(object? s, RoutedEventArgs? e)
    {
        if (!_planHasContent && (PlanCanvas.Width <= 0 || PlanCanvas.Height <= 0))
            return;

        var viewW = Math.Max(100, PlanViewHost.ActualWidth - 30);
        var viewH = Math.Max(100, PlanViewHost.ActualHeight - 30);
        var cw = Math.Max(1, PlanCanvas.Width);
        var ch = Math.Max(1, PlanCanvas.Height);
        _fitScale = Math.Min(viewW / cw, viewH / ch) * 0.94;
        _zoom = 1;
        _pan = new Point((viewW - cw * _fitScale) / 2.0 + 15, (viewH - ch * _fitScale) / 2.0 + 15);
        ApplyPlanTransform();
        ZoomText.Text = "100%";
    }

    private void ZoomIn_Click(object s, RoutedEventArgs e) => ZoomAt(1.25, new Point(PlanViewHost.ActualWidth / 2, PlanViewHost.ActualHeight / 2));

    private void ZoomOut_Click(object s, RoutedEventArgs e) => ZoomAt(0.8, new Point(PlanViewHost.ActualWidth / 2, PlanViewHost.ActualHeight / 2));

    private void PlanCanvas_MouseWheel(object s, MouseWheelEventArgs e)
    {
        var point = e.GetPosition(PlanViewHost);
        ZoomAt(e.Delta > 0 ? 1.15 : 0.87, point);
        e.Handled = true;
    }

    private void ZoomAt(double factor, Point viewportPoint)
    {
        if (!_planHasContent && PlanCanvas.Width <= 0)
            return;

        var oldScale = Math.Max(1e-9, _fitScale * _zoom);
        var newZoom = Math.Max(0.15, Math.Min(12.0, _zoom * factor));
        var newScale = Math.Max(1e-9, _fitScale * newZoom);
        var worldX = (viewportPoint.X - _pan.X) / oldScale;
        var worldY = (viewportPoint.Y - _pan.Y) / oldScale;
        _zoom = newZoom;
        _pan = new Point(viewportPoint.X - worldX * newScale, viewportPoint.Y - worldY * newScale);
        ApplyPlanTransform();
        ZoomText.Text = $"{(_zoom * 100):F0}%";
    }

    private void ApplyPlanTransform()
    {
        _planScale.ScaleX = Math.Max(0.0001, _fitScale * _zoom);
        _planScale.ScaleY = Math.Max(0.0001, _fitScale * _zoom);
        _planTranslate.X = _pan.X;
        _planTranslate.Y = _pan.Y;
    }

    private void PlanCanvas_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(PlanViewHost);
            _translationStart = _pan;
            PlanViewHost.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PlanCanvas_MouseMove(object s, MouseEventArgs e)
    {
        if (!_isPanning)
            return;
        var current = e.GetPosition(PlanViewHost);
        var dx = current.X - _panStart.X;
        var dy = current.Y - _panStart.Y;
        _pan = new Point(_translationStart.X + dx, _translationStart.Y + dy);
        ApplyPlanTransform();
    }

    private void PlanCanvas_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (_isPanning && e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = false;
            PlanViewHost.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void PlanCanvas_SizeChanged(object s, SizeChangedEventArgs e)
    {
        if (_ignorePlanResize || !_planHasContent)
            return;
        _ignorePlanResize = true;
        try { Dispatcher.BeginInvoke(new Action(() => FitPlan_Click(null, null))); }
        finally { _ignorePlanResize = false; }
    }

    private void NormalizeEtabsOnlyResultLevels()
    {
        foreach (var result in _all.Where(x => string.IsNullOrWhiteSpace(x.RevitElementId) && !string.IsNullOrWhiteSpace(x.EtabsElementId)).ToList())
        {
            if (_etabsToRevitLevel.TryGetValue(result.StoryOrLevel, out var revitLevel))
                result.StoryOrLevel = revitLevel;
        }
    }

    private void ExportCsv_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var path = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RevitEtabsValidation.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Type,Level,Revit,RevitId,ETABS,ETABSId,Status,Severity,PositionMm,ElevationMm,WidthMm,DepthMm,LengthMm,RotationDeg,Confidence,Message");
            foreach (var r in _all)
            {
                static string Q(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
                sb.AppendLine(string.Join(",", Q(r.ElementType), Q(r.StoryOrLevel), Q(r.RevitName), Q(r.RevitElementId), Q(r.EtabsName), Q(r.EtabsElementId), Q(r.Status.ToString()), Q(r.Severity.ToString()), r.PositionDeltaMm.ToString("F1"), r.ElevationDeltaMm.ToString("F1"), r.WidthDeltaMm.ToString("F1"), r.DepthDeltaMm.ToString("F1"), r.LengthDeltaMm.ToString("F1"), r.RotationDeltaDeg.ToString("F1"), r.Confidence.ToString("F1"), Q(r.Message)));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            SetStatus("CSV exported: " + path);
        }
        catch (Exception ex)
        {
            SetStatus("CSV export failed: " + ex.Message);
        }
    }

    private void ExportJson_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var path = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RevitEtabsValidation.json");
            File.WriteAllText(path, JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus("JSON exported: " + path);
        }
        catch (Exception ex)
        {
            SetStatus("JSON export failed: " + ex.Message);
        }
    }

    private void Rerun_Click(object s, RoutedEventArgs e) => RunValidation_Click(s, e);
}
