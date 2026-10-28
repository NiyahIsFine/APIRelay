using System.ComponentModel;
using System.Windows;
using APIRelay.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace APIRelay.Desktop.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && App.CurrentApp.HasTray) Hide();
        };
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.CurrentApp.IsShuttingDown)
        {
            e.Cancel = true;
            if (App.CurrentApp.HasTray)
            {
                try { (DataContext as ShellViewModel)?.SavePreferences(); }
                catch (Exception ex) { (DataContext as ShellViewModel)?.ShowError(ex); return; }
                Hide();
            }
            else _ = App.CurrentApp.QuitAsync();
        }
        base.OnClosing(e);
    }
}
