using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitEtabsValidator.Revit.UI;

public sealed class FloorScopeItem
{
    public string RevitLevel { get; init; } = "";
    public double RevitElevationMm { get; init; }
    public string EtabsStory { get; init; } = "";
    public double EtabsElevationMm { get; init; }
    public bool IsSelected { get; set; }

    public string Display => string.IsNullOrWhiteSpace(EtabsStory)
        ? $"{RevitLevel}   |   Revit EL {RevitElevationMm:F0} mm   |   ETABS: no mapped story"
        : $"{RevitLevel}   |   Revit EL {RevitElevationMm:F0} mm   |   ETABS {EtabsStory} (EL {EtabsElevationMm:F0} mm)";
}

public sealed class FloorSelectionWindow : Window
{
    private readonly List<FloorScopeItem> _items;
    private readonly StackPanel _rows = new();

    public IReadOnlyList<FloorScopeItem> SelectedItems => _items.Where(x => x.IsSelected).ToList();

    public FloorSelectionWindow(IEnumerable<FloorScopeItem> items)
    {
        _items = items.ToList();
        Title = "Validation Scope — Select Floors";
        Width = 720;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = Brushes.White;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Choose the floors to validate",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Only selected Revit levels and their mapped ETABS stories will enter the comparison. This can significantly reduce validation time on large models.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(header);

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.Content = _rows;
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        BuildRows();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(Button("Select All", 100, (_, _) => SetAll(true)));
        buttons.Children.Add(Button("Clear", 90, (_, _) => SetAll(false)));
        buttons.Children.Add(Button("Cancel", 90, (_, _) => { DialogResult = false; Close(); }));
        buttons.Children.Add(Button("Run Selected", 120, (_, _) =>
        {
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select at least one floor, or cancel to stop validation.", "Validation Scope", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        }));
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        foreach (var item in _items)
        {
            var check = new CheckBox
            {
                IsChecked = item.IsSelected,
                Content = item.Display,
                FontSize = 13,
                Margin = new Thickness(4, 7, 4, 7)
            };
            check.Checked += (_, _) => item.IsSelected = true;
            check.Unchecked += (_, _) => item.IsSelected = false;
            _rows.Children.Add(check);
        }
    }

    private void SetAll(bool value)
    {
        foreach (var item in _items) item.IsSelected = value;
        BuildRows();
    }

    private static Button Button(string text, double width, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Width = width, Height = 30, Margin = new Thickness(5, 0, 0, 0) };
        button.Click += click;
        return button;
    }
}
