using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// Blender-style collections in the outliner: Scene Collection root with nested collections,
/// new / rename / delete, M "Move to Collection", drag and drop of objects and collections.
/// </summary>
public partial class OutlinerPanel
{
    SceneCollection? _active;
    OutlinerItem? _dragItem;
    Point _dragStart;

    /// <summary>The "Scene Collection" root.</summary>
    public SceneCollection Root { get; private set; } = CreateDefaultRoot();

    /// <summary>Raised when the collection tree or membership changes (for the dirty flag).</summary>
    public event EventHandler? CollectionsChanged;

    static SceneCollection CreateDefaultRoot()
    {
        var root = new SceneCollection { Name = "Scene Collection" };
        root.AddChild(new SceneCollection { Name = "Collection" });
        return root;
    }

    void OnCollectionsChanged() => CollectionsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Collection new objects go to: the one last clicked or holding the selection (Blender's active collection).</summary>
    public SceneCollection ActiveCollection
    {
        get
        {
            if (_active != null && (_active == Root || Root.IsAncestorOf(_active)))
            {
                return _active;
            }

            return Root.Children.Count > 0 ? Root.Children[0] : Root;
        }
    }

    /// <summary>Drops deleted objects from collections and files new ones into the active collection.</summary>
    void SyncMembership()
    {
        if (_viewport == null)
        {
            return;
        }

        var ids = _viewport.ObjectIds.ToHashSet();
        var seen = new HashSet<int>();
        foreach (var c in Root.SelfAndDescendants())
        {
            var inThis = new HashSet<int>();
            c.Objects.RemoveAll(id => !ids.Contains(id) || !inThis.Add(id));
            seen.UnionWith(c.Objects);
        }

        var target = ActiveCollection;
        foreach (int id in _viewport.ObjectIds)
        {
            if (!seen.Contains(id))
            {
                target.Objects.Add(id);
            }
        }
    }

    public SceneCollection? FindCollection(string nameOrId)
    {
        if (int.TryParse(nameOrId, out int id) && Root.Find(id) is { } byId)
        {
            return byId;
        }

        return nameOrId.Equals("Scene Collection", StringComparison.OrdinalIgnoreCase) ? Root : Root.FindByName(nameOrId);
    }

    // ---------------- operations (UI and MCP) ----------------

    public SceneCollection NewCollection(SceneCollection? parent = null, IEnumerable<int>? objects = null, string? name = null)
    {
        parent ??= Root;
        var c = new SceneCollection { Name = Root.UniqueName(string.IsNullOrWhiteSpace(name) ? "Collection" : name.Trim()) };
        parent.AddChild(c);
        foreach (int id in objects ?? [])
        {
            c.MoveObjectHere(Root, id);
        }

        parent.Expanded = true;
        _active = c;
        Rebuild();
        OnCollectionsChanged();
        return c;
    }

    public void MoveObjects(IEnumerable<int> ids, SceneCollection target)
    {
        foreach (int id in ids)
        {
            target.MoveObjectHere(Root, id);
        }

        target.Expanded = true;
        Rebuild();
        OnCollectionsChanged();
    }

    /// <summary>Blender Shift+M / Ctrl-drag: also put the objects in target (they stay in their other collections).</summary>
    public void LinkObjects(IEnumerable<int> ids, SceneCollection target)
    {
        foreach (int id in ids)
        {
            if (!target.Objects.Contains(id))
            {
                target.Objects.Add(id);
            }
        }

        target.Expanded = true;
        Rebuild();
        OnCollectionsChanged();
    }

    static (int, int) RowKey(OutlinerItem r) => (r.ObjectId, r.NodeIndex);

    /// <summary>Selected objects (outliner multi-selection, plus the viewport's active object).</summary>
    public IReadOnlyList<int> SelectedObjectIds()
    {
        var ids = ItemList.SelectedItems.OfType<OutlinerItem>()
            .Where(r => r.Collection == null && r.ObjectId >= 0).Select(r => r.ObjectId).Distinct().ToList();
        if (_viewport is { SelectedId: >= 0 } v && !ids.Contains(v.SelectedId))
        {
            if (ids.Count > 0 && ItemList.SelectedItems.Count > 0)
            {
                ids.Add(v.SelectedId);
            }
            else
            {
                ids = [v.SelectedId];
            }
        }

        return ids;
    }

    public bool MoveCollection(SceneCollection c, SceneCollection target)
    {
        if (c.IsRoot || c == target || c.IsAncestorOf(target) || c.Parent == target)
        {
            return false;
        }

        target.AddChild(c);
        target.Expanded = true;
        Rebuild();
        OnCollectionsChanged();
        return true;
    }

    public bool RenameCollection(SceneCollection c, string name)
    {
        name = name.Trim();
        if (c.IsRoot || name.Length == 0 || name == c.Name)
        {
            return false;
        }

        c.Name = Root.FindByName(name) is { } other && other != c ? Root.UniqueName(name) : name;
        Rebuild();
        OnCollectionsChanged();
        return true;
    }

    public bool DeleteCollection(SceneCollection c)
    {
        if (c.IsRoot)
        {
            return false;
        }

        c.DeleteKeepChildren();
        Rebuild();
        OnCollectionsChanged();
        return true;
    }

    public void SetCollectionFlags(SceneCollection c, bool? visible, bool? included)
    {
        if (c.IsRoot)
        {
            return;
        }

        c.Visible = visible ?? c.Visible;
        c.Included = included ?? c.Included;
        Rebuild();
        OnCollectionsChanged();
    }

    /// <summary>For get_scene: nested collection tree with object ids.</summary>
    public object DescribeCollections() => Describe(Root);

    static object Describe(SceneCollection c) => new
    {
        id = c.Id,
        name = c.Name,
        visible = c.Visible,
        included = c.Included,
        objects = c.Objects.ToArray(),
        children = c.Children.Select(Describe).ToArray(),
    };

    bool ConfirmDeleteCollection(SceneCollection c) =>
        PvMessageDialog.Show(Window.GetWindow(this),
            UiText.T($"删除集合「{c.Name}」？\n其中的物体和子集合会移到上一级，不会被删除。",
                $"Delete collection \"{c.Name}\"?\nIts objects and child collections move to the parent; nothing is deleted."),
            "PolyView", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // ---------------- UI wiring ----------------

    void InitCollectionsUi()
    {
        NewCollectionButton.ToolTip = UiText.T("新建集合", "New Collection");
        NewCollectionButton.Click += (_, _) => NewCollection(SelectedCollectionOrOwner());
        ItemList.AllowDrop = true;
        ItemList.PreviewMouseLeftButtonDown += ItemList_PreviewMouseLeftButtonDown;
        ItemList.PreviewMouseMove += ItemList_PreviewMouseMove;
        ItemList.PreviewMouseLeftButtonUp += ItemList_PreviewMouseLeftButtonUp;
        ItemList.DragOver += ItemList_DragOver;
        ItemList.Drop += ItemList_Drop;
        ItemList.PreviewMouseRightButtonUp += ItemList_RightClick;
        ItemList.MouseDoubleClick += ItemList_MouseDoubleClick;
        ItemList.PreviewKeyDown += ItemList_PreviewKeyDown;
    }

    SceneCollection SelectedCollectionOrOwner()
    {
        var item = ItemList.SelectedItem as OutlinerItem;
        if (item?.Collection is { } c)
        {
            return c;
        }

        if (item != null && item.ObjectId >= 0 && Root.FindOwner(item.ObjectId) is { } owner)
        {
            return owner;
        }

        return Root;
    }

    static OutlinerItem? RowAt(DependencyObject? source)
    {
        for (var d = source; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem { DataContext: OutlinerItem item })
            {
                return item;
            }
        }

        return null;
    }

    static bool InsideButton(DependencyObject? source)
    {
        for (var d = source; d != null && d is not ListBoxItem; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    void ItemList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        _dragItem = InsideButton(src) ? null : RowAt(src);
        if (_dragItem != null && (_dragItem.Kind == OutlinerKind.Scene || _dragItem.IsDataRow || _dragItem.NodeIndex >= 0))
        {
            _dragItem = null;
        }

        _dragStart = e.GetPosition(ItemList);
        // Pressing on an already selected row of a multi-selection keeps the selection so all of it can be dragged.
        _pendingSingle = null;
        if (_dragItem != null && Keyboard.Modifiers == ModifierKeys.None && ItemList.SelectedItems.Count > 1 && ItemList.SelectedItems.Contains(_dragItem))
        {
            _pendingSingle = _dragItem;
            e.Handled = true;
        }
    }

    OutlinerItem? _pendingSingle;

    void ItemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSingle != null)
        {
            var row = _pendingSingle;
            _pendingSingle = null;
            _dragItem = null;
            ItemList.SelectedItems.Clear();
            ItemList.SelectedItem = row;
        }
    }

    void ItemList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var d = e.GetPosition(ItemList) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragItem;
        _dragItem = null;
        _pendingSingle = null;
        EnsureDragFeedbackHooks();
        DragDrop.DoDragDrop(ItemList, new DataObject(typeof(OutlinerItem), item), DragDropEffects.Move | DragDropEffects.Link);
        ClearDragFeedback();
    }

    SceneCollection? DropTarget(DragEventArgs e, out OutlinerItem? dragged)
    {
        dragged = e.Data.GetData(typeof(OutlinerItem)) as OutlinerItem;
        var row = RowAt(e.OriginalSource as DependencyObject);
        if (dragged == null || row == null)
        {
            return null;
        }

        SceneCollection? target = null;
        for (var r = row; r != null && target == null; r = r.Parent)
        {
            target = r.Collection;
        }

        if (target == null)
        {
            return null;
        }

        if (dragged.Collection is { } c && (c == target || c.IsAncestorOf(target)))
        {
            return null;
        }

        return target;
    }

    void ItemList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropTarget(e, out var dragged) == null ? DragDropEffects.None
            : dragged?.Collection == null && (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Link : DragDropEffects.Move;
        e.Handled = true;
    }

    void ItemList_Drop(object sender, DragEventArgs e)
    {
        var target = DropTarget(e, out var dragged);
        e.Handled = true;
        if (target == null || dragged == null)
        {
            return;
        }

        if (dragged.Collection is { } c)
        {
            MoveCollection(c, target);
        }
        else if (dragged.ObjectId >= 0)
        {
            // Dragging one row of a multi-selection drags every selected object; Ctrl links instead of moving.
            var ids = ItemList.SelectedItems.Contains(dragged) ? SelectedObjectIds() : [dragged.ObjectId];
            if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            {
                LinkObjects(ids, target);
            }
            else
            {
                MoveObjects(ids, target);
            }
        }
    }

    void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (!InsideButton(src) && RowAt(src) is { } row)
        {
            BeginRename(row);
            e.Handled = true;
        }
    }

    void BeginRename(OutlinerItem row)
    {
        bool can = row.Collection is { IsRoot: false } || (row.ObjectId >= 0 && row.NodeIndex == -1);
        if (can)
        {
            row.IsEditing = true;
        }
    }

    void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
        {
            box.Text = (box.DataContext as OutlinerItem)?.Name ?? "";
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: OutlinerItem row } box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitRename(row, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsEditing = false;
            e.Handled = true;
        }
    }

    void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: OutlinerItem { IsEditing: true } row } box)
        {
            CommitRename(row, box.Text);
        }
    }

    void CommitRename(OutlinerItem row, string text)
    {
        if (!row.IsEditing)
        {
            return;
        }

        row.IsEditing = false;
        text = text.Trim();
        if (text.Length == 0 || text == row.Name)
        {
            return;
        }

        if (row.Collection is { } c)
        {
            RenameCollection(c, text);
        }
        else if (row.ObjectId >= 0)
        {
            _viewport?.RenameObject(row.ObjectId, text);
        }
    }

    void ItemList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        var row = ItemList.SelectedItem as OutlinerItem;
        switch (e.Key)
        {
            case Key.F2 when row != null:
                BeginRename(row);
                e.Handled = true;
                break;
            case Key.M when SelectedObjectIds().Count > 0:
                ShowMoveToCollectionMenu(SelectedObjectIds(), ItemList, Keyboard.Modifiers == ModifierKeys.Shift);
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.None:
                NewCollection(SelectedCollectionOrOwner());
                e.Handled = true;
                break;
            case Key.Delete or Key.X when row?.Collection is { IsRoot: false } c:
                if (ConfirmDeleteCollection(c))
                {
                    DeleteCollection(c);
                }

                e.Handled = true;
                break;
        }
    }

    ContextMenu NewMenu(UIElement target)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.MousePoint,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        };
        if (Window.GetWindow(this) is { } window)
        {
            menu.Resources.MergedDictionaries.Add(window.Resources);
        }

        return menu;
    }

    static MenuItem Item(string header, Action action, string? gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        mi.Click += (_, _) => action();
        return mi;
    }

    void ItemList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var row = RowAt(e.OriginalSource as DependencyObject);
        if (row != null && !ItemList.SelectedItems.Contains(row))
        {
            ItemList.SelectedItem = row;
        }

        var menu = NewMenu(ItemList);
        var parent = SelectedCollectionOrOwner();
        menu.Items.Add(Item(UiText.T("新建集合", "New Collection"), () => NewCollection(row == null ? Root : parent), "C"));
        if (row?.Collection is { IsRoot: false } c)
        {
            menu.Items.Add(Item(UiText.T("重命名", "Rename"), () => BeginRename(row), "F2"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(UiText.T("删除集合", "Delete Collection"), () =>
            {
                if (ConfirmDeleteCollection(c))
                {
                    DeleteCollection(c);
                }
            }, "X"));
        }
        else if (row is { ObjectId: >= 0 })
        {
            var ids = SelectedObjectIds();
            menu.Items.Add(Item(UiText.T("重命名", "Rename"), () => BeginRename(row), "F2"));
            var move = new MenuItem { Header = UiText.T("移动到集合", "Move to Collection"), InputGestureText = "M" };
            PopulateMoveMenu(move.Items, ids, false);
            menu.Items.Add(move);
            var link = new MenuItem { Header = UiText.T("链接到集合", "Link to Collection"), InputGestureText = "Shift M" };
            PopulateMoveMenu(link.Items, ids, true);
            menu.Items.Add(link);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Blender M menu: every collection (indented), then "+ New Collection" which moves the objects into it.</summary>
    public void ShowMoveToCollectionMenu(IReadOnlyList<int> ids, UIElement placement, bool link = false)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var menu = NewMenu(placement);
        menu.Items.Add(new MenuItem { Header = link ? UiText.T("链接到集合", "Link to Collection") : UiText.T("移动到集合", "Move to Collection"), IsEnabled = false, Tag = "header" });
        PopulateMoveMenu(menu.Items, ids, link);
        menu.IsOpen = true;
    }

    void PopulateMoveMenu(ItemCollection items, IReadOnlyList<int> ids, bool link)
    {
        void Walk(SceneCollection c, int depth)
        {
            var target = c;
            var name = c.IsRoot ? UiText.T("场景集合", "Scene Collection") : c.Name;
            items.Add(Item(new string(' ', depth * 4) + name, () =>
            {
                if (link)
                {
                    LinkObjects(ids, target);
                }
                else
                {
                    MoveObjects(ids, target);
                }
            }));
            foreach (var child in c.Children)
            {
                Walk(child, depth + 1);
            }
        }

        Walk(Root, 0);
        items.Add(new Separator());
        items.Add(Item("+ " + UiText.T("新建集合", "New Collection"), () =>
        {
            var c = link ? NewCollection(Root) : NewCollection(Root, ids);
            if (link)
            {
                LinkObjects(ids, c);
            }

            var row = AllItems().FirstOrDefault(i => i.Collection == c);
            if (row != null)
            {
                ItemList.SelectedItem = row;
                ItemList.ScrollIntoView(row);
                BeginRename(row);
            }
        }));
    }
}
