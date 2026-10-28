using System.Windows;
using System.Windows.Media;
using APIRelay.Desktop.Services;
using APIRelay.Desktop.ViewModels;
using APIRelay.Desktop.Views;
using Wpf.Ui.Appearance;

namespace APIRelay.Desktop;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private RelayEngine? engine;
    private ShellViewModel? viewModel;
    private MainWindow? window;
    private UsageBubbleWindow? bubble;
    private TrayService? tray;
    private bool shuttingDown;
    private bool initialized;
    private bool ownsMutex;
    internal static App CurrentApp => (App)Current;
    internal bool IsShuttingDown => shuttingDown;
    internal bool HasTray => tray != null;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            instanceMutex = new Mutex(true, @"Global\APIRelay_Instance", out ownsMutex);
            if (!ownsMutex)
            {
                MessageBox.Show("APIRelay is already running.", "APIRelay", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            string? dataDirectory = null;
            var index = Array.IndexOf(e.Args, "--data-directory");
            if (index >= 0)
            {
                if (index + 1 >= e.Args.Length) throw new ArgumentException("--data-directory requires an absolute path.");
                dataDirectory = Path.GetFullPath(e.Args[index + 1]);
            }
            engine = new RelayEngine(dataDirectory);
            await Task.WhenAll(Task.Run(engine.Initialize), CodexOfficialModelCatalog.LoadInstalledAsync());
            initialized = true;
            ApplyTheme(engine.Theme);
            viewModel = new ShellViewModel(engine);
            window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
            try
            {
                tray = new TrayService(window, ShowWorkspace, () => viewModel.ShowBubble = !viewModel.ShowBubble,
                    () => _ = QuitAsync(), () => viewModel.ShowBubble);
            }
            catch (Exception ex) { viewModel.ShowError(ex); }
            SetBubbleVisibility(engine.BubbleVisible);
            RefreshTray();
            await viewModel.RefreshRecordsAsync();
            if (engine.ShouldResume && !e.Args.Contains("--no-auto-start"))
            {
                try { await engine.StartAsync(); }
                catch (Exception ex) { viewModel.ShowError(ex); }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "APIRelay", MessageBoxButton.OK, MessageBoxImage.Error);
            if (initialized) await QuitAsync(); else Shutdown(1);
        }
    }
    internal void ShowWorkspace()
    {
        if (window == null) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
    internal void RefreshTray() => tray?.Refresh(engine?.IsRunning == true);
    internal void SetBubbleVisibility(bool visible)
    {
        if (engine == null || viewModel == null || window == null) return;
        if (visible)
        {
            bubble ??= new UsageBubbleWindow(engine) { DataContext = viewModel };
            bubble.Show();
        }
        else bubble?.Hide();
        RefreshTray();
    }
    internal void ShowCostToast() => bubble?.ShowToast();
    internal async Task QuitAsync()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        try
        {
            bubble?.SaveLocation();
            if (initialized && engine != null)
            {
                viewModel?.SavePreferences();
                await engine.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            shuttingDown = false;
            ShowWorkspace();
            viewModel?.ShowError(ex);
            return;
        }
        viewModel?.Dispose();
        tray?.Dispose();
        bubble?.Close();
        window?.Close();
        Shutdown();
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        bubble?.SaveLocation();
        try { if (initialized && engine != null) Task.Run(async () => await engine.DisposeAsync()).GetAwaiter().GetResult(); }
        catch (Exception) { }
        shuttingDown = true;
        tray?.Dispose();
        base.OnSessionEnding(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
    internal static void ApplyTheme(string theme)
    {
        var light = theme == "Light";
        ApplicationThemeManager.Apply(light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        ApplicationAccentColorManager.Apply(Color.FromRgb(63, 174, 154), light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        var colors = light
            ? new[] { "#F3F6FA", "#FFFFFF", "#FFFFFF", "#F0F4F8", "#DCE4ED", "#1C2B3D", "#61748A", "#117A69", "#E1F3EE", "#98641D",
                "#EDF2F6", "#E5F2EE", "#D7EBE5", "#B2D4CA", "#438979" }
            : new[] { "#10151D", "#151B25", "#1B2330", "#202B39", "#2B3746", "#EDF3FA", "#9CAFC5", "#62D5BF", "#213D3E", "#F5BE76",
                "#263442", "#243C3D", "#2C4746", "#375A56", "#579C8F" };
        var keys = new[] { "CanvasBrush", "SidebarBrush", "CardBrush", "SubtleBrush", "LineBrush", "PrimaryTextBrush",
            "MutedTextBrush", "AccentBrush", "AccentSurfaceBrush", "WarningBrush", "SelectionHoverBrush", "SelectionSurfaceBrush",
            "SelectionHoverSurfaceBrush", "SelectionBorderBrush", "SelectionFocusBrush" };
        for (var i = 0; i < keys.Length; i++) Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
