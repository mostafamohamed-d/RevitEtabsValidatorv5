using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitEtabsValidator.Revit.UI;

/// <summary>
/// Small UI compatibility fixes kept separate from MainWindow.xaml.cs.
/// </summary>
public partial class MainWindow
{
    // The floor-mapping dictionaries are defined in MainWindow.xaml.cs.
    // Do not redeclare them here; this file only contains UI compatibility behavior.

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(HideUnusedAllFloorButton));
    }

    private static void HideUnusedAllFloorButton(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        foreach (var button in FindVisualChildren<Button>(window))
        {
            if (button.Content is string text &&
                string.Equals(text.Trim(), "All", StringComparison.OrdinalIgnoreCase))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
