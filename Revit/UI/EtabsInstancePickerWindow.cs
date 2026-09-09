using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitEtabsValidator.ETABS;

namespace RevitEtabsValidator.Revit.UI;

// Shown when Connect ETABS finds more than one running ETABS instance, so the user
// picks which one to attach to instead of the tool silently grabbing whichever one
// GetActiveObject would have returned.
public sealed class EtabsInstancePickerWindow : Window
{
    private readonly ListBox _list = new();
    private readonly IReadOnlyList<EtabsRunningInstance> _instances;

    public EtabsRunningInstance? Selected { get; private set; }

    public EtabsInstancePickerWindow(IReadOnlyList<EtabsRunningInstance> instances)
    {
        _instances = instances;

        Title = "Select ETABS Instance";
        Width = 440;
        Height = 380;
        MinWidth = 380;
        MinHeight = 300;
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
            Text = $"{instances.Count} ETABS instances are currently running",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Choose which one this session should connect to.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(header);

        _list.ItemsSource = instances.Select(x => x.DisplayName).ToList();
        _list.SelectedIndex = 0;
        _list.FontSize = 13;
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(Button("Cancel", 90, (_, _) => { DialogResult = false; Close(); }));
        buttons.Children.Add(Button("Connect", 100, (_, _) =>
        {
            if (_list.SelectedIndex < 0)
            {
                MessageBox.Show(this, "Select an ETABS instance, or cancel.", "Select ETABS Instance", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Selected = _instances[_list.SelectedIndex];
            DialogResult = true;
            Close();
        }));
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private static Button Button(string text, double width, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Width = width, Height = 30, Margin = new Thickness(5, 0, 0, 0) };
        button.Click += click;
        return button;
    }
}
