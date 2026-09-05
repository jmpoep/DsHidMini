using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nefarius.DsHidMini.ControlApp.Helpers;

public interface IListReorderable
{
    bool CanReorder { get; }
}

public sealed class ListViewReorderRequest
{
    public ListViewReorderRequest(object item, int newIndex)
    {
        Item = item;
        NewIndex = newIndex;
    }

    public object Item { get; }

    public int NewIndex { get; }
}

/// <summary>
///     Drag-and-drop reorder for a <see cref="ListBox" />. Index 0 is treated as pinned and is not a valid drop target.
/// </summary>
public static class ListViewReorderBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ListViewReorderBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ReorderCommandProperty = DependencyProperty.RegisterAttached(
        "ReorderCommand",
        typeof(ICommand),
        typeof(ListViewReorderBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty DragStateProperty = DependencyProperty.RegisterAttached(
        "DragState",
        typeof(DragState),
        typeof(ListViewReorderBehavior),
        new PropertyMetadata(null));

    private const string DragFormat = "ControlApp.ListViewReorder";

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static ICommand? GetReorderCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(ReorderCommandProperty);

    public static void SetReorderCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(ReorderCommandProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        Detach(listBox);
        if (e.NewValue is true)
        {
            Attach(listBox);
        }
    }

    private static void Attach(ListBox listBox)
    {
        listBox.AllowDrop = true;
        DragState state = new();
        listBox.SetValue(DragStateProperty, state);

        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove += OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        listBox.DragOver += OnDragOver;
        listBox.Drop += OnDrop;
    }

    private static void Detach(ListBox listBox)
    {
        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove -= OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        listBox.DragOver -= OnDragOver;
        listBox.Drop -= OnDrop;
        listBox.ClearValue(DragStateProperty);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || GetDragState(listBox) is not DragState state)
        {
            return;
        }

        object? item = GetItemFromSource(listBox, e.OriginalSource as DependencyObject);
        if (item is null || !CanReorder(item))
        {
            state.Reset();
            return;
        }

        state.DragItem = item;
        state.StartPoint = e.GetPosition(listBox);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox
            || e.LeftButton != MouseButtonState.Pressed
            || GetDragState(listBox) is not { DragItem: { } item } state)
        {
            return;
        }

        Point current = e.GetPosition(listBox);
        Vector delta = current - state.StartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        state.Reset();
        DataObject data = new(DragFormat, item);
        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            GetDragState(listBox)?.Reset();
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || e.Data.GetData(DragFormat) is not { } item || !CanReorder(item))
        {
            return;
        }

        int? newIndex = GetTargetIndex(listBox, item, e.GetPosition(listBox));
        if (newIndex is null)
        {
            return;
        }

        ICommand? command = GetReorderCommand(listBox);
        ListViewReorderRequest request = new(item, newIndex.Value);
        if (command?.CanExecute(request) == true)
        {
            command.Execute(request);
        }

        e.Handled = true;
    }

    private static int? GetTargetIndex(ListBox listBox, object draggedItem, Point position)
    {
        int sourceIndex = listBox.Items.IndexOf(draggedItem);
        if (sourceIndex < 0)
        {
            return null;
        }

        int insertIndex = Math.Max(1, GetInsertionIndex(listBox, position));
        int newIndex = insertIndex > sourceIndex ? insertIndex - 1 : insertIndex;
        if (newIndex < 1)
        {
            newIndex = 1;
        }

        if (newIndex >= listBox.Items.Count)
        {
            newIndex = listBox.Items.Count - 1;
        }

        return newIndex == sourceIndex ? null : newIndex;
    }

    private static int GetInsertionIndex(ListBox listBox, Point position)
    {
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            Point topLeft = container.TransformToAncestor(listBox).Transform(new Point(0, 0));
            if (position.Y < topLeft.Y + container.ActualHeight / 2)
            {
                return i;
            }
        }

        return listBox.Items.Count;
    }

    private static object? GetItemFromSource(ListBox listBox, DependencyObject? origin)
    {
        if (origin is null)
        {
            return null;
        }

        DependencyObject? container = ItemsControl.ContainerFromElement(listBox, origin);
        if (container is not null)
        {
            return listBox.ItemContainerGenerator.ItemFromContainer(container);
        }

        while (origin is not null)
        {
            if (origin is ListBoxItem listBoxItem)
            {
                return listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }

    private static bool CanReorder(object item) =>
        item is IListReorderable reorderable && reorderable.CanReorder;

    private static DragState? GetDragState(ListBox listBox) =>
        listBox.GetValue(DragStateProperty) as DragState;

    private sealed class DragState
    {
        public object? DragItem { get; set; }

        public Point StartPoint { get; set; }

        public void Reset()
        {
            DragItem = null;
            StartPoint = default;
        }
    }
}
