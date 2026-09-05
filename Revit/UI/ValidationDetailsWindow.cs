using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitEtabsValidator.Revit.UI;

public sealed class ValidationDetailsWindow : Window
{
    public ValidationDetailsWindow(string title, string details)
    {
        Title = title;
        Width = 620;
        Height = 560;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(heading);

        var text = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(10),
            BorderBrush = Brushes.LightGray
        };
        Grid.SetRow(text, 1);
        root.Children.Add(text);

        var close = new Button { Content = "Close", Width = 90, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        Content = root;
    }
}
