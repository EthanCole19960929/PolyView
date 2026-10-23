using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// Blender-style outliner drag feedback: while dragging objects or collections over a collection
/// (or a row inside it), that collection row is highlighted and a label next to the cursor says
/// what releasing will do ("move" / Ctrl "link") plus the dragged name or "N objects".
/// Invalid targets show no highlight/label and the not-allowed cursor (Effects = None).
/// </summary>
public partial class OutlinerPanel
{
    Popup? _dragTip;
    TextBlock? _dragTipAction;
    TextBlock? _dragTipName;
    RowHighlightAdorner? _dragAdorner;
    bool _dragFeedbackHooked;

    void EnsureDragFeedbackHooks()
    {
        if (_dragFeedbackHooked)
        {
            return;
        }

        _dragFeedbackHooked = true;
        // handledEventsToo: the existing DragOver/Drop handlers mark the event handled.
        ItemList.AddHandler(DragOverEvent, new DragEventHandler(Feedback_DragOver), true);
        ItemList.AddHandler(DragLeaveEvent, new DragEventHandler(Feedback_DragLeave), true);
        ItemList.AddHandler(DropEvent, new DragEventHandler((_, _) => ClearDragFeedback()), true);
    }

    void Feedback_DragOver(object sender, DragEventArgs e)
    {
        var target = DropTarget(e, out var dragged);
        if (target == null || dragged == null)
        {
            ClearDragFeedback();
            return;
        }

        // Highlight the collection row that will receive the drop.
        OutlinerItem? row = RowAt(e.OriginalSource as DependencyObject);
        while (row != null && row.Collection != target)
        {
            row = row.Parent;
        }

        SetRowHighlight(row == null ? null : ItemList.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem);

        bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        string action;
        string name;
        if (dragged.Collection != null)
        {
            action = UiText.T("\u79fb\u52a8\u5230\u96c6\u5408\u5185", "Move into collection");
            name = dragged.Name;
        }
        else
        {
            action = ctrl
                ? UiText.T("\u5173\u8054\u5230\u96c6\u5408\u5185", "Link into collection")
                : UiText.T("\u79fb\u52a8\u5230\u96c6\u5408\u5185\uff08Ctrl \u4e3a\u5173\u8054\uff0cShift \u4e3a\u7236\u7ea7\uff09",
                    "Move inside collection (Ctrl to link, Shift to parent)");
            int n = ItemList.SelectedItems.Contains(dragged) ? SelectedObjectIds().Count : 1;
            name = n > 1 ? UiText.T($"{n} \u4e2a\u7269\u4f53", $"{n} objects") : dragged.Name;
        }

        ShowDragTip(action, name, e.GetPosition(ItemList));
    }

    void Feedback_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when crossing child elements; only clear when the cursor really left the list.
        var p = e.GetPosition(ItemList);
        if (p.X < 0 || p.Y < 0 || p.X >= ItemList.ActualWidth || p.Y >= ItemList.ActualHeight)
        {
            ClearDragFeedback();
        }
    }

    void ShowDragTip(string action, string name, Point at)
    {
        if (_dragTip == null)
        {
            _dragTipAction = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), FontSize = 12 };
            _dragTipName = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(_dragTipAction);
            stack.Children.Add(_dragTipName);
            _dragTip = new Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Placement = PlacementMode.Relative,
                PlacementTarget = ItemList,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x18, 0x18, 0x18)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 3, 6, 4),
                    IsHitTestVisible = false,
                    Child = stack,
                },
            };
        }

        _dragTipAction!.Text = action;
        _dragTipName!.Text = name;
        _dragTip.HorizontalOffset = at.X + 16;
        _dragTip.VerticalOffset = at.Y + 14;
        _dragTip.IsOpen = true;
    }

    void SetRowHighlight(ListBoxItem? container)
    {
        if (_dragAdorner?.AdornedElement == container)
        {
            return;
        }

        if (_dragAdorner != null)
        {
            AdornerLayer.GetAdornerLayer(_dragAdorner.AdornedElement)?.Remove(_dragAdorner);
            _dragAdorner = null;
        }

        if (container != null && AdornerLayer.GetAdornerLayer(container) is { } layer)
        {
            _dragAdorner = new RowHighlightAdorner(container);
            layer.Add(_dragAdorner);
        }
    }

    void ClearDragFeedback()
    {
        SetRowHighlight(null);
        if (_dragTip != null)
        {
            _dragTip.IsOpen = false;
        }
    }

    sealed class RowHighlightAdorner : Adorner
    {
        static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x47, 0x72, 0xB3)));
        static readonly Pen Edge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x8A, 0xD6)), 1));

        public RowHighlightAdorner(UIElement row) : base(row) => IsHitTestVisible = false;

        static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var s = AdornedElement.RenderSize;
            dc.DrawRoundedRectangle(Fill, Edge, new Rect(0.5, 0.5, Math.Max(0, s.Width - 1), Math.Max(0, s.Height - 1)), 3, 3);
        }
    }
}
