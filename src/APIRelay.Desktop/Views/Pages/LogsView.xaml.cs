using System.Windows;
using System.Windows.Controls;

namespace APIRelay.Desktop.Views.Pages;

public partial class LogsView : UserControl
{
    private bool? stackedLayout;
    private bool bodyFocused;
    private GridLength listWidth = new(2, GridUnitType.Star);
    private GridLength bodyWidth = new(3, GridUnitType.Star);
    private GridLength listHeight = new(2, GridUnitType.Star);
    private GridLength bodyHeight = new(3, GridUnitType.Star);

    public LogsView() => InitializeComponent();

    private void RuntimeText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox text && !text.IsKeyboardFocusWithin) text.ScrollToEnd();
    }

    private void TraceBodyText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox text) return;
        text.ScrollToHome();
        text.ScrollToHorizontalOffset(0);
    }

    private void TraceWorkspace_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTraceLayout();
    private void FocusBodyToggle_Changed(object sender, RoutedEventArgs e) => UpdateTraceLayout();

    private void UpdateTraceLayout()
    {
        if (TraceWorkspace == null || TraceBodyPanel == null || FocusBodyToggle == null) return;
        var stacked = TraceWorkspace.ActualWidth < 760;
        var focused = FocusBodyToggle.IsChecked == true;
        if (stackedLayout == stacked && bodyFocused == focused) return;

        if (stackedLayout.HasValue && !bodyFocused)
        {
            if (stackedLayout.Value) { listHeight = TraceListRow.Height; bodyHeight = TraceBodyRow.Height; }
            else { listWidth = TraceListColumn.Width; bodyWidth = TraceBodyColumn.Width; }
        }
        stackedLayout = stacked;
        bodyFocused = focused;
        TraceListPanel.Visibility = TraceSplitter.Visibility = focused ? Visibility.Collapsed : Visibility.Visible;
        TraceListColumn.MinWidth = !stacked && !focused ? 280 : 0;
        TraceBodyColumn.MinWidth = !stacked && !focused ? 380 : 0;
        TraceListRow.MinHeight = stacked && !focused ? 100 : 0;
        TraceBodyRow.MinHeight = stacked && !focused ? 120 : 0;
        TraceListColumn.Width = stacked || focused ? new GridLength(1, GridUnitType.Star) : listWidth;
        TraceGapColumn.Width = new GridLength(stacked || focused ? 0 : 9);
        TraceBodyColumn.Width = stacked || focused ? new GridLength(0) : bodyWidth;
        TraceListRow.Height = stacked && !focused ? listHeight : new GridLength(1, GridUnitType.Star);
        TraceGapRow.Height = new GridLength(stacked && !focused ? 9 : 0);
        TraceBodyRow.Height = stacked && !focused ? bodyHeight : new GridLength(0);
        Grid.SetColumn(TraceBodyPanel, stacked || focused ? 0 : 2);
        Grid.SetRow(TraceBodyPanel, stacked && !focused ? 2 : 0);
        Grid.SetColumn(TraceSplitter, stacked ? 0 : 1);
        Grid.SetRow(TraceSplitter, stacked ? 1 : 0);
        TraceSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        TraceSplitter.Width = stacked ? double.NaN : 9;
        TraceSplitter.Height = stacked ? 9 : double.NaN;
    }
}
