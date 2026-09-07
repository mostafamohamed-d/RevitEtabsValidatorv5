using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private ValidationTolerance _lastTolerance = new();
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
    private readonly Dictionary<ValidationResult, List<Shape>> _planShapesByResult = new();
    private readonly List<Shape> _highlightedShapes = new();

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
                // RunValidation_Click set the toolbar busy before raising the Revit
                // read; ContinueValidation (which would normally clear it) never
                // runs on this failure path, so it must be cleared here instead or
                // the toolbar would stay disabled until the window is reopened.
                SetBusy(false, null);
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

    // ETABS is a separate out-of-process application (ETABS.exe), so every ETABS
    // COM call below is already an inter-process RPC regardless of which of our
    // threads makes it - the cost is COM marshaling overhead, not Revit/WPF API
    // affinity. At the reported model scale (16,652 beams) that is tens of
    // thousands of individual round trips, previously made synchronously on the
    // same thread hosting this window, which is why Revit could appear to hang
    // during Connect ETABS / Run Validation. Task.Run below moves only the pure
    // COM/data-fetching work off that thread; every line that touches a WPF
    // control still runs on the UI thread, either before the first `await` or
    // automatically after it (WPF's dispatcher-based SynchronizationContext
    // resumes `await` continuations on the original UI thread with no manual
    // Dispatcher call needed). ModelComparer and the rest of the UI-updating code
    // in RunComparisonForSelectedScope are deliberately left untouched - they are
    // pure, fast, already-Windows-agnostic C# (measured at ~0.4s for this exact
    // model scale), not COM, and not the bottleneck this addresses.
    private async void ConnectEtabs_Click(object s, RoutedEventArgs e)
    {
        SetBusy(true, "Connecting to ETABS...");
        try
        {
            // Enumerate every running ETABS process first so multiple open instances
            // can be offered as a choice instead of silently attaching to whichever
            // one plain GetActiveObject would have returned. Falls back to the
            // original single-instance ConnectRunning() path if enumeration finds
            // nothing (COM enumeration is best-effort - see ListRunningInstances).
            var running = await Task.Run(() => _etabs.ListRunningInstances());
            bool ok;

            if (running.Count > 1)
            {
                var picker = new EtabsInstancePickerWindow(running) { Owner = this };
                if (picker.ShowDialog() != true || picker.Selected == null)
                {
                    SetStatus("ETABS connection cancelled: choose one of the running instances to connect.");
                    return;
                }
                ok = await Task.Run(() => _etabs.ConnectTo(picker.Selected));
            }
            else if (running.Count == 1)
            {
                ok = await Task.Run(() => _etabs.ConnectTo(running[0]));
            }
            else
            {
                ok = await Task.Run(() => _etabs.ConnectRunning());
            }

            if (!ok && StartEtabs.IsChecked == true)
            {
                SetStatus("Starting ETABS...");
                ok = await Task.Run(() => _etabs.StartAndConnect());
            }

            if (!ok)
            {
                SetEtabsConnectionState(false, "ETABS: Not connected");
                SetStatus(_etabs.Message + " Enable 'Start ETABS if not running' when required.");
                return;
            }

            SetEtabsConnectionState(true, "ETABS: Connected");
            SetStatus(_etabs.Message);
            await ReadEtabsAsync();
        }
        catch (Exception ex)
        {
            SetEtabsConnectionState(false, "ETABS: Connection error");
            SetStatus("ETABS connection failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void SetEtabsConnectionState(bool connected, string label)
    {
        ConnectionStateText.Text = label;
        ConnectionDot.Fill = connected ? Brushes.SeaGreen : Brushes.IndianRed;
    }

    // Disables the toolbar (Read Revit / Connect ETABS / Run Validation / Scope /
    // exports) and shows a wait cursor for the duration of a background ETABS
    // operation, so a long read gives visible feedback instead of looking frozen,
    // and a second click can't start an overlapping operation on the same
    // EtabsConnection/SapModel while one is already in flight.
    private void SetBusy(bool busy, string? statusText)
    {
        ToolbarPanel.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        if (statusText != null)
            SetStatus(statusText);
    }

    private async Task ReadEtabsAsync()
    {
        try
        {
            var sapModel = _etabs.SapModel;
            if (sapModel == null)
            {
                SetEtabsConnectionState(false, "ETABS: Not connected");
                SetStatus("ETABS is not connected.");
                return;
            }

            SetStatus("Reading ETABS model (this can take a while for a large model)...");

            var (columns, beams, storyElevations, excludedCount, unitsOk) = await Task.Run(() =>
            {
                var unitsOkResult = _etabs.SetUnitsKnMmC();
                var reader = new EtabsModelReader(sapModel);
                var readColumns = reader.ReadColumns();
                var readBeams = reader.ReadBeams();
                return (readColumns, readBeams, reader.StoryElevationsMm, reader.ExcludedZeroNameCount, unitsOkResult);
            });

            if (!unitsOk)
                SetStatus("Warning: ETABS units were not confirmed as kN-mm-C.");

            _etabsStoryElevationsMm.Clear();
            foreach (var pair in storyElevations)
                _etabsStoryElevationsMm[pair.Key] = pair.Value;

            _etabsColumns = columns;
            _etabsBeams = beams;

            EtabsColCount.Text = _etabsColumns.Count.ToString();
            EtabsBeamCount.Text = _etabsBeams.Count.ToString();

            SetStatus($"ETABS read complete: {_etabsColumns.Count} columns, {_etabsBeams.Count} beams. Excluded zero-prefixed frames: {excludedCount}.");
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
        AngleToleranceDegrees = Read(AngleTol, 1),
        // Unlike the tolerances above, these are signed systematic corrections
        // (e.g. ETABS models the beam centerline at a different datum than Revit's
        // reference level), so they must accept negative values - ReadSigned, not Read.
        BeamZOffsetMm = ReadSigned(BeamZOffsetTol, 0),
        ColumnZOffsetMm = ReadSigned(ColumnZOffsetTol, 0)
    };

    private static double Read(WpfTextBox b, double d) =>
        double.TryParse(b.Text, out var v) && v >= 0 ? v : d;

    private static double ReadSigned(WpfTextBox b, double d) =>
        double.TryParse(b.Text, out var v) ? v : d;

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
        SetBusy(true, "Reading Revit model before validation...");
        Raise(RevitRequest.ReadModels);
    }

    private async void ContinueValidation()
    {
        try
        {
            if (_revitColumns.Count == 0 && _revitBeams.Count == 0)
            {
                SetStatus("Revit read completed but no beams or columns were found.");
                return;
            }

            await ReadEtabsAsync();
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
        finally
        {
            SetBusy(false, null);
        }
    }

    private void RunComparisonForSelectedScope()
    {
        try
        {
            var revitColumns = _revitColumns.Where(x => _selectedRevitLevels.Contains(x.LevelName)).ToList();
            var revitBeams = _revitBeams.Where(x => _selectedRevitLevels.Contains(x.LevelName)).ToList();

            // Filtering is skipped only when it would be a true no-op - every ETABS
            // story is already represented in the selection, so Where/Contains would
            // let everything through anyway (a performance shortcut, not a fallback).
            // Root cause of the bug this replaces: the previous condition also
            // required _selectedEtabsStories.Count > 0 to filter at all, so when NONE
            // of the selected Revit levels mapped to any ETABS story (a level ETABS
            // doesn't model), filtering was skipped entirely and etabsColumns/
            // etabsBeams silently fell back to the ENTIRE ETABS model instead of an
            // empty set - turning a single-floor validation into a full-model one and
            // flooding the results with thousands of unrelated MissingInRevit rows.
            // Filtering unconditionally on Count < Total fixes this: when
            // _selectedEtabsStories is empty, Where(x => _selectedEtabsStories.
            // Contains(...)) correctly yields nothing, exactly as an unmapped
            // selection should.
            var filterEtabs = _selectedEtabsStories.Count < _etabsStoryElevationsMm.Count;

            var etabsColumns = filterEtabs
                ? _etabsColumns.Where(x => _selectedEtabsStories.Contains(x.LevelName)).ToList()
                : _etabsColumns;
            var etabsBeams = filterEtabs
                ? _etabsBeams.Where(x => _selectedEtabsStories.Contains(x.LevelName)).ToList()
                : _etabsBeams;

            var t = Tol();
            _lastTolerance = t;
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

            if (_selectedEtabsStories.Count == 0 && _etabsStoryElevationsMm.Count > 0)
                SetStatus($"Validation complete: {_all.Count} comparison result(s), but none of the {_selectedRevitLevels.Count} selected floor(s) mapped to an ETABS story - every Revit element in scope will show as missing. Check the floor mapping or pick different floors.");
            else
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

            if (!_etabsToRevitLevel.TryGetValue(nearest.Key, out var currentLevelName))
            {
                _etabsToRevitLevel[nearest.Key] = level.Name;
                continue;
            }

            var currentLevel = _revitLevels.FirstOrDefault(x =>
                string.Equals(x.Name, currentLevelName, StringComparison.OrdinalIgnoreCase));

            var etabsElevation = _etabsStoryElevationsMm.TryGetValue(nearest.Key, out var z)
                ? z
                : nearest.Value;

            var currentDifference = currentLevel == default
                ? double.PositiveInfinity
                : Math.Abs(etabsElevation - currentLevel.ElevationMm);
            var newDifference = Math.Abs(etabsElevation - level.ElevationMm);

            if (newDifference < currentDifference)
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

    private void ClearFloorView_Click(object s, RoutedEventArgs e)
    {
        PlanFloorList.SelectedIndex = -1;
        PlanCanvas.Children.Clear();
        _planShapesByResult.Clear();
        _highlightedShapes.Clear();
        _planHasContent = false;
        PlanFloorHeaderText.Text = "—";
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
        _planShapesByResult.Clear();
        _highlightedShapes.Clear();
        _planHasContent = false;
        if (string.IsNullOrWhiteSpace(level))
        {
            PlanFloorHeaderText.Text = "—";
            return;
        }

        var mappedStory = _revitToEtabsStory.TryGetValue(level, out var story) ? story : "";
        var revB = _revitBeams.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList();
        var revC = _revitColumns.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList();
        var etaB = string.IsNullOrWhiteSpace(mappedStory)
            ? _etabsBeams.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList()
            : _etabsBeams.Where(x => string.Equals(x.LevelName, mappedStory, StringComparison.OrdinalIgnoreCase)).ToList();
        var etaC = string.IsNullOrWhiteSpace(mappedStory)
            ? _etabsColumns.Where(x => string.Equals(x.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList()
            : _etabsColumns.Where(x => string.Equals(x.LevelName, mappedStory, StringComparison.OrdinalIgnoreCase)).ToList();

        var storyLabel = string.IsNullOrWhiteSpace(mappedStory) ? "no mapped ETABS story" : $"ETABS \"{mappedStory}\"";
        PlanFloorHeaderText.Text = $"{level}  →  {storyLabel}   ·   Columns {revC.Count} Revit / {etaC.Count} ETABS   ·   Beams {revB.Count} Revit / {etaB.Count} ETABS";

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

        // _planHasContent must be set before the fit runs, not after: FitPlan_Click
        // uses it as its "is there anything to fit" guard. On the very first draw
        // after the window opens (or after a resize), PlanViewHost may not have been
        // laid out yet - ActualWidth/Height still read 0 - and FitPlan_Click below
        // detects that and retries itself once real layout is available, instead of
        // silently fitting against a placeholder-sized viewport and leaving the plan
        // permanently tiny/blank (the root cause of the plan appearing empty).
        _planHasContent = true;
        FitPlan_Click(null, null);
        HighlightSelectedOnPlan();
    }

    // One color per mismatch type (not just a single generic "problem" color) so a
    // glance at the plan tells you WHAT kind of coordination issue a member has,
    // matching the same legend shown in the plan view's bottom-left corner.
    private static Brush StatusBrush(ValidationStatus status) => status switch
    {
        ValidationStatus.PositionMismatch => Brushes.Gold,
        ValidationStatus.ElevationMismatch => Brushes.Crimson,
        ValidationStatus.SectionMismatch => Brushes.MediumPurple,
        ValidationStatus.RotationMismatch => Brushes.Teal,
        ValidationStatus.AmbiguousMatch => Brushes.DeepPink,
        ValidationStatus.MissingInRevit or ValidationStatus.MissingInEtabs => Brushes.Black,
        _ => Brushes.Crimson
    };

    private void AddBeamVisual(BeamElement beam, bool etabs, Point a, Point b)
    {
        var result = FindResultForPair(beam.Id, etabs);
        var problem = result != null && result.Status != ValidationStatus.Matched;

        // A translucent red halo drawn under the type-colored line makes every
        // mismatch - of any kind - unmistakably read as "red" at a glance. The
        // crisp line drawn on top still carries the specific mismatch-type color
        // (see StatusBrush/the legend), so a Position vs. Section vs. Elevation
        // problem is still distinguishable without a second click.
        if (problem)
        {
            var halo = new Line
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                Stroke = Brushes.Red,
                StrokeThickness = (etabs ? 2.0 : 3.0) + 6,
                Opacity = 0.30,
                IsHitTestVisible = false
            };
            PlanCanvas.Children.Add(halo);
        }

        var line = new Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = b.X,
            Y2 = b.Y,
            Stroke = problem ? StatusBrush(result!.Status) : (etabs ? Brushes.SlateGray : Brushes.SteelBlue),
            StrokeThickness = problem ? 3.5 : (etabs ? 2.0 : 3.0),
            Opacity = 0.9,
            Tag = result
        };
        if (etabs)
            line.StrokeDashArray = new DoubleCollection { 7, 5 };
        AttachVisual(line, beam.Name, etabs, beam.Id, result);
        PlanCanvas.Children.Add(line);
        RegisterPlanShape(result, line);
    }

    private void AddColumnVisual(ColumnElement column, bool etabs, Point p)
    {
        const double radius = 6;
        var result = FindResultForPair(column.Id, etabs);
        var problem = result != null && result.Status != ValidationStatus.Matched;

        if (problem)
        {
            const double haloRadius = radius + 5;
            var halo = new Ellipse
            {
                Width = haloRadius * 2,
                Height = haloRadius * 2,
                Fill = Brushes.Red,
                Opacity = 0.30,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(halo, p.X - haloRadius);
            Canvas.SetTop(halo, p.Y - haloRadius);
            PlanCanvas.Children.Add(halo);
        }

        var problemBrush = problem ? StatusBrush(result!.Status) : null;
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = problemBrush ?? (etabs ? Brushes.DarkOrange : Brushes.SteelBlue),
            Fill = etabs ? Brushes.Transparent : (problem ? Brushes.MistyRose : Brushes.LightSteelBlue),
            StrokeThickness = problem ? 3 : 2,
            Tag = result
        };
        Canvas.SetLeft(ellipse, p.X - radius);
        Canvas.SetTop(ellipse, p.Y - radius);
        AttachVisual(ellipse, column.Name, etabs, column.Id, result);
        PlanCanvas.Children.Add(ellipse);
        RegisterPlanShape(result, ellipse);
    }

    private void RegisterPlanShape(ValidationResult? result, Shape shape)
    {
        if (result == null)
            return;
        if (!_planShapesByResult.TryGetValue(result, out var list))
            _planShapesByResult[result] = list = new List<Shape>();
        list.Add(shape);
    }

    private void AttachVisual(FrameworkElement element, string name, bool etabs, string id, ValidationResult? result)
    {
        element.Cursor = Cursors.Hand;
        var statusNote = result != null && result.Status != ValidationStatus.Matched ? $"\n{result.Status}" : "";
        element.ToolTip = etabs ? $"ETABS: {name}\nID: {id}{statusNote}\nClick for coordination details" : $"Revit: {name}\nID: {id}{statusNote}\nClick for coordination details";
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
            HighlightSelectedOnPlan();
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
        HighlightSelectedOnPlan();
    }

    // Keeps the plan in sync with whichever member is "selected" - by a plan click
    // (PlanVisual_Click) or a results-grid row click (FloorResultsGrid_SelectionChanged)
    // both funnel through UpdateSelectedPanel, so either path highlights the same
    // Revit+ETABS pair of shapes on the plan with a glow, not just the side panel text.
    private void HighlightSelectedOnPlan()
    {
        foreach (var shape in _highlightedShapes)
            shape.Effect = null;
        _highlightedShapes.Clear();

        if (_selected == null || !_planShapesByResult.TryGetValue(_selected, out var shapes))
            return;

        var glow = new DropShadowEffect
        {
            Color = Colors.Gold,
            BlurRadius = 20,
            ShadowDepth = 0,
            Opacity = 1.0
        };
        foreach (var shape in shapes)
        {
            shape.Effect = glow;
            Panel.SetZIndex(shape, 100);
            _highlightedShapes.Add(shape);
        }
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

    // Instance method (not static) so it can report the tolerances/offsets that were
    // actually in effect for the run that produced this result (_lastTolerance),
    // which is what a user needs to answer "why does this show a mismatch".
    private string BuildReason(ValidationResult result, ElementBase? revit, ElementBase? etabs)
    {
        var t = _lastTolerance;
        if (result.Status == ValidationStatus.Matched)
            return "Matched: the plan geometry correspondence was established and all required validation checks are within the configured tolerances. Span-length difference is shown only as a diagnostic for analytical/physical end offsets.";
        if (result.Status == ValidationStatus.MissingInEtabs)
            return "Missing in ETABS: the Revit member did not find a valid ETABS counterpart through the plan-geometry identity gate.";
        if (result.Status == ValidationStatus.MissingInRevit)
            return "Missing in Revit: the ETABS member did not find a valid Revit counterpart through the plan-geometry identity gate.";
        if (result.Status == ValidationStatus.SectionMismatch)
            return $"Section mismatch (tolerance ±{t.DimensionToleranceMm:F0} mm). Revit = {FormatSection(revit, false)}; ETABS = {FormatSection(etabs, true)}.";
        if (result.Status == ValidationStatus.PositionMismatch)
            return $"Position mismatch: {result.PositionDeltaMm:F1} mm (tolerance ±{t.PositionToleranceMm:F0} mm). Revit location = {FormatLocation(revit)}; ETABS location = {FormatLocation(etabs)}.";
        if (result.Status == ValidationStatus.RotationMismatch)
            return $"Rotation mismatch: {result.RotationDeltaDeg:F1}° (tolerance ±{t.AngleToleranceDegrees:F1}°).";
        if (result.Status == ValidationStatus.ElevationMismatch)
        {
            var offset = result.ElementType == "Beam" ? t.BeamZOffsetMm : t.ColumnZOffsetMm;
            var offsetNote = offset == 0
                ? $"No {result.ElementType} Z-Offset correction is currently configured."
                : $"A {offset:+0.#;-0.#;0} mm {result.ElementType} Z-Offset correction is currently applied.";
            return $"Elevation mismatch: {result.ElevationDeltaMm:F1} mm (tolerance ±{t.ElevationToleranceMm:F0} mm). {offsetNote} If this same delta repeats across most/all members of this type, it is usually a systematic modeling-datum difference between Revit and ETABS rather than N separate errors - adjust the {result.ElementType} Z-Offset field in the tolerance bar to correct for it, then re-run. See the two model locations below for the actual Z values.";
        }
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
        if (!_planHasContent)
            return;

        // PlanViewHost may not have been arranged yet (e.g. the very first draw
        // right after RunValidation_Click, before WPF has run a layout pass over
        // this newly-populated panel) - ActualWidth/Height would read 0 here.
        // Fitting against that produces a near-zero scale that makes the whole
        // plan invisible, and nothing else would ever trigger a re-fit unless the
        // user happens to resize the window afterward. Retry once real layout is
        // available instead of silently committing to a bad fit.
        if (PlanViewHost.ActualWidth <= 1 || PlanViewHost.ActualHeight <= 1)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FitPlan_Click(null, null)));
            return;
        }

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
