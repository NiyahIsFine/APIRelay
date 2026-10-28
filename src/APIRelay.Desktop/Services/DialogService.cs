using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace APIRelay.Desktop.Services;

internal static class DialogService
{
    public static bool Confirm(string message)
    {
        var dialog = new FluentWindow
        {
            Title = TextCatalog.Current["Confirm"],
            Width = 470,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("CardBrush"),
            Foreground = (Brush)Application.Current.FindResource("PrimaryTextBrush")
        };
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = TextCatalog.Current["Confirm"],
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Wpf.Ui.Controls.Button { Content = TextCatalog.Current["Cancel"], IsCancel = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
        var confirm = new Wpf.Ui.Controls.Button { Content = TextCatalog.Current["Confirm"], Appearance = ControlAppearance.Primary, MinWidth = 110 };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
