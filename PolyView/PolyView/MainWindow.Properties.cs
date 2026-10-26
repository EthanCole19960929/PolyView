using System.Windows;
using System.Windows.Controls;

namespace PolyView;

/// <summary>Right column: outliner on top, Blender Properties editor below (replaces the outliner's old transform box).</summary>
public partial class MainWindow
{
    void InitProperties()
    {
        // Hide the outliner's legacy transform / info area; the Properties editor owns it now.
        if (Outliner.Content is Grid grid && grid.RowDefinitions.Count == 3)
        {
            grid.RowDefinitions[1].Height = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(0);
            grid.RowDefinitions[2].MinHeight = 0;
            foreach (UIElement child in grid.Children)
            {
                if (Grid.GetRow(child) > 0) child.Visibility = Visibility.Collapsed;
            }
        }

        Properties.Attach(Viewport, Animations);
    }
}
