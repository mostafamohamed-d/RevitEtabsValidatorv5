using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitEtabsValidator.Revit.UI;

/// <summary>
/// Small compatibility fixes kept separate from MainWindow.xaml.cs so UI changes
/// can be evolved without repeatedly rewriting the large code-behind file.
/// </summary>
public partial class MainWindow
{
    // MainWindow.xaml.cs contains an old reverse-mapping name in BuildFloorMapping.
    // Keep it as a safe alias to the canonical reverse dictionary.
    private Dictionary<string, string> _revitToEtabsLevel => _etabsToRevitLevel;

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
