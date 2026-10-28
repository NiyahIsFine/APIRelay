using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using APIRelay.Desktop.Controls;
using APIRelay.Desktop.Services;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly RelayEngine engine;
    private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer refreshTimer;
    private readonly List<string> logLines = [];
    private bool recordsDirty = true;
    private bool refreshing;
    private int refreshVersion;
    private DateTime lastDay = DateTime.Today;
    private int selectedPage;
    private bool isBusy;
    private string notice = "";
    private bool isError;
    private string localUrl;
    private DateTime selectedDate = DateTime.Today;
    private bool allDates;
    private string recordSearch = "";
    private RecordRow? selectedRecord;
    private IReadOnlyList<RequestRecord> snapshot = [];

    public ShellViewModel(RelayEngine engine)
    {
        this.engine = engine;
        localUrl = engine.LocalUrl;
        TextCatalog.Current.Chinese = engine.Language == AppLanguage.Chinese;
        RecordsView = CollectionViewSource.GetDefaultView(Records);
        RecordsView.Filter = value => value is RecordRow row &&
            (string.IsNullOrWhiteSpace(RecordSearch) ||
             $"{row.Model} {row.Summary} {row.Path} {row.StatusCode}".Contains(RecordSearch, StringComparison.OrdinalIgnoreCase));
        StartCommand = Command(async () =>
        {
            engine.LocalUrl = LocalUrl;
            await engine.StartAsync();
            Notify(T["Running"]);
        }, () => !IsRunning);
        StopCommand = new AsyncCommand(async () => { await engine.StopAsync(); LoadClients(); Notify(T["Stopped"]); }, ShowError, () => IsRunning);
        SaveSettingsCommand = Command(() => { SavePreferences(); Notify(T["Saved"]); });
        RefreshRecordsCommand = Command(RefreshRecordsAsync);
        CopyRouteCommand = Command(() => { Clipboard.SetText(RoutePreview); Notify(T["Copied"]); }, () => RoutePreview.Length > 0);
        ViewRequestsCommand = new RelayCommand(() => SelectedPage = 1);
        ClearDateCommand = Command(async () =>
        {
            if (!DialogService.Confirm(T["ConfirmDelete"])) return;
            await Task.Run(() => engine.ClearRecords(SelectedDate));
            await RefreshRecordsAsync();
        });
        ClearAllCommand = Command(async () =>
        {
            if (!DialogService.Confirm(T["ConfirmDelete"])) return;
            await Task.Run(() => engine.ClearRecords(null));
            await RefreshRecordsAsync();
        });
        DismissNoticeCommand = new RelayCommand(() => Notice = "");
        OpenLogsCommand = Command(() => OpenDirectory(engine.LogsDirectory));
        OpenRecordsCommand = Command(() => OpenDirectory(engine.RecordsDirectory));
        OpenSettingsCommand = Command(() => OpenDirectory(engine.SettingsDirectory));
        InitializeProviderCommands();
        InitializeModelCommands();
        InitializeClientCommands();
        InitializePricingCommands();
        InitializeLogCommands();
        engine.LogReceived += OnLog;
        engine.RequestRecorded += OnRecord;
        engine.RunningChanged += OnRunningChanged;
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refreshTimer.Tick += OnRefreshTick;
        refreshTimer.Start();
    }

    private TextCatalog T => TextCatalog.Current;
    public int SelectedPage { get => selectedPage; set => Set(ref selectedPage, value); }
    public bool IsRunning => engine.IsRunning;
    public bool CanEdit => !IsRunning && !IsBusy;
    public bool CanEditModels => !IsBusy;
    public bool IsEditingLocked => !CanEdit;
    public bool IsBusy { get => isBusy; private set { Set(ref isBusy, value); Raise(nameof(CanEdit)); Raise(nameof(CanEditModels)); Raise(nameof(IsEditingLocked)); CommandManager.InvalidateRequerySuggested(); } }
    public string RunningText => T[IsRunning ? "Running" : "Stopped"];
    public string Notice { get => notice; private set { Set(ref notice, value); Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;
    public bool IsError { get => isError; private set => Set(ref isError, value); }
    public string LocalUrl
    {
        get => localUrl;
        set { if (Set(ref localUrl, value)) { if (!IsRunning) engine.LocalUrl = value; Raise(nameof(RoutePreview)); } }
    }
    public string RoutePreview => engine.BuildRouteUrl();
    public IReadOnlyList<ProtocolOption> Protocols { get; } = Enum.GetValues<ApiRouteKind>().Select(p => new ProtocolOption(p, ProtocolName(p))).ToArray();
    public IReadOnlyList<ProtocolOption> ClientProtocols => new[] { new ProtocolOption(null, T["Direct"]) }.Concat(Protocols).ToArray();
    public ApiRouteKind RouteServer
    {
        get => engine.RouteServer;
        set { engine.RouteServer = value; Raise(); Raise(nameof(RoutePreview)); }
    }
    public ApiRouteKind? RouteClient
    {
        get => engine.RouteClient;
        set { engine.RouteClient = value; Raise(); Raise(nameof(RoutePreview)); }
    }
    public DateTime SelectedDate
    {
        get => selectedDate;
        set { if (Set(ref selectedDate, value.Date)) QueueRefresh(); }
    }
    public bool AllDates { get => allDates; set { if (Set(ref allDates, value)) QueueRefresh(); } }
    private bool useCostChart;
    public bool UseCostChart { get => useCostChart; set => Set(ref useCostChart, value); }
    public string RecordSearch { get => recordSearch; set { Set(ref recordSearch, value); RecordsView.Refresh(); Raise(nameof(FilteredCount)); } }
    public RecordRow? SelectedRecord
    {
        get => selectedRecord;
        set { Set(ref selectedRecord, value); Raise(nameof(HasSelectedRecord)); }
    }
    public bool HasSelectedRecord => SelectedRecord != null;
    public ObservableCollection<DateTime> RecordDates { get; } = [];
    public ObservableCollection<RecordRow> Records { get; } = [];
    public ObservableCollection<RecordRow> RecentRecords { get; } = [];
    public ICollectionView RecordsView { get; }
    public int FilteredCount => RecordsView.Cast<object>().Count();
    public bool HasRecords => Records.Count > 0;
    public bool NoRecords => Records.Count == 0;
    public string RequestCount => snapshot.Count.ToString("N0");
    public string InputTotal => snapshot.Sum(r => (long)GetInputTokens(r)).ToString("N0");
    public string OutputTotal => snapshot.Sum(r => (long)r.CompletionTokens).ToString("N0");
    public string CacheReadTotal => snapshot.Sum(r => (long)r.CachedTokens).ToString("N0");
    public string CacheWriteTotal => snapshot.Sum(r => (long)r.CacheCreationTokens).ToString("N0");
    public string TokenTotal => snapshot.Sum(r => (long)r.TotalTokens).ToString("N0");
    public string CostTotal => Currency(snapshot.Sum(engine.GetCost));
    public string CacheRate
    {
        get
        {
            var input = snapshot.Sum(r => (long)GetInputTokens(r));
            return input == 0 ? "0.0%" : ((double)snapshot.Sum(r => (long)r.CachedTokens) / input).ToString("P1");
        }
    }
    public string InputCost => Currency(engine.GetUsageCosts(snapshot).Input);
    public string OutputCost => Currency(engine.GetUsageCosts(snapshot).Output);
    public string CacheReadCost => Currency(engine.GetUsageCosts(snapshot).CacheHit);
    public string CacheWriteCost => Currency(engine.GetUsageCosts(snapshot).CacheCreation);
    public string BubbleTokens { get; private set; } = "0";
    public string BubbleInput { get; private set; } = "0";
    public string BubbleOutput { get; private set; } = "0";
    public string BubbleCost { get; private set; } = "$0.0000";
    public string BubbleRate { get; private set; } = "0.0%";
    public string LastRequestCost { get; private set; } = "";
    public IReadOnlyList<ChartPoint> ChartPoints { get; private set; } = [];
    public string RuntimeLog => string.Join(Environment.NewLine, logLines);
    public string SettingsPath => engine.SettingsDirectory;
    public string RecordsPath => engine.RecordsDirectory;
    public string LogsPath => engine.LogsDirectory;
    public int ThemeIndex
    {
        get => engine.Theme == "Light" ? 1 : 0;
        set
        {
            if (value is not (0 or 1) || value == ThemeIndex) return;
            engine.Theme = value == 1 ? "Light" : "Dark";
            App.ApplyTheme(engine.Theme);
            Raise();
        }
    }
    public int LanguageIndex
    {
        get => engine.Language == AppLanguage.Chinese ? 0 : 1;
        set
        {
            if (value is not (0 or 1) || value == LanguageIndex) return;
            engine.Language = value == 0 ? AppLanguage.Chinese : AppLanguage.English;
            T.Chinese = value == 0;
            Raise();
            Raise(nameof(RunningText)); Raise(nameof(ClientProtocols));
            Raise(nameof(TraceRetentionDisplay));
            Raise(nameof(ClientStatus)); Raise(nameof(EffortOptions)); Raise(nameof(DefaultEffortOptions));
            foreach (var row in Records) row.RefreshCostDescription(engine);
            App.CurrentApp.RefreshTray();
        }
    }
    public bool ShowBubble
    {
        get => engine.BubbleVisible;
        set { engine.BubbleVisible = value; Raise(); App.CurrentApp.SetBubbleVisibility(value); }
    }
    public bool ResumeOnLaunch { get => engine.ResumeOnLaunch; set { engine.ResumeOnLaunch = value; Raise(); } }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshRecordsCommand { get; }
    public ICommand ClearDateCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand CopyRouteCommand { get; }
    public ICommand ViewRequestsCommand { get; }
    public ICommand DismissNoticeCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenRecordsCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    private AsyncCommand Command(Func<Task> action, Func<bool>? canExecute = null) => new(async () =>
    {
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }, ShowError, () => !IsBusy && (canExecute?.Invoke() ?? true));
    private AsyncCommand Command(Action action, Func<bool>? canExecute = null) => Command(() => { action(); return Task.CompletedTask; }, canExecute);
    public void Notify(string message) { IsError = false; Notice = message; }
    public void ShowError(Exception exception) { IsError = true; Notice = $"{T["Error"]} · {exception.Message}"; }
    private static string Currency(decimal value) => "$" + value.ToString("0.0000", CultureInfo.InvariantCulture);
    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    public void SavePreferences() { engine.LocalUrl = LocalUrl; engine.SaveSettings(); }
    private void QueueRefresh() { refreshVersion++; recordsDirty = true; }
    private async void OnRefreshTick(object? sender, EventArgs e)
    {
        if (lastDay != DateTime.Today)
        {
            var previousDay = lastDay;
            lastDay = DateTime.Today;
            if (SelectedDate == previousDay) SelectedDate = lastDay;
            QueueRefresh();
        }
        if (!recordsDirty || refreshing) return;
        try { await RefreshRecordsAsync(); }
        catch (Exception ex) { recordsDirty = false; ShowError(ex); }
    }
    public async Task RefreshRecordsAsync()
    {
        if (refreshing) { QueueRefresh(); return; }
        refreshing = true;
        recordsDirty = false;
        var version = refreshVersion;
        var date = AllDates ? (DateTime?)null : SelectedDate;
        try
        {
            var data = await Task.Run(() =>
            {
                var records = engine.GetRecords(date);
                var today = date == DateTime.Today || date == null
                    ? records.Where(r => r.Timestamp.Date == DateTime.Today).ToArray()
                    : engine.GetRecords(DateTime.Today);
                return (records, today, dates: engine.GetRecordDates());
            }, lifetime.Token);
            if (version != refreshVersion) { recordsDirty = true; return; }
            snapshot = data.records;
            var selectedTimestamp = SelectedRecord?.Timestamp;
            Records.Clear();
            foreach (var record in snapshot) Records.Add(new RecordRow(record, engine));
            RecentRecords.Clear();
            foreach (var record in Records.Take(6)) RecentRecords.Add(record);
            if (selectedTimestamp.HasValue) SelectedRecord = Records.FirstOrDefault(r => r.Timestamp == selectedTimestamp.Value);
            if (!RecordDates.SequenceEqual(data.dates))
            {
                RecordDates.Clear();
                foreach (var item in data.dates) RecordDates.Add(item);
                Raise(nameof(SelectedDate));
            }
            BubbleTokens = data.today.Sum(r => (long)r.TotalTokens).ToString("N0");
            BubbleInput = CompactTokens(data.today.Sum(r => (long)GetInputTokens(r)));
            BubbleOutput = CompactTokens(data.today.Sum(r => (long)r.CompletionTokens));
            BubbleCost = Currency(data.today.Sum(engine.GetCost));
            var totalInput = data.today.Sum(r => (long)GetInputTokens(r));
            BubbleRate = totalInput == 0 ? "0.0%" : ((double)data.today.Sum(r => (long)r.CachedTokens) / totalInput).ToString("P1");
            BuildChart();
            foreach (var property in new[] { nameof(InputTotal), nameof(OutputTotal), nameof(CacheReadTotal), nameof(CacheWriteTotal),
                nameof(TokenTotal), nameof(CostTotal), nameof(RequestCount), nameof(CacheRate), nameof(HasRecords), nameof(NoRecords),
                nameof(BubbleTokens), nameof(BubbleInput), nameof(BubbleOutput), nameof(BubbleCost), nameof(BubbleRate), nameof(FilteredCount),
                nameof(InputCost), nameof(OutputCost), nameof(CacheReadCost), nameof(CacheWriteCost) }) Raise(property);
        }
        finally { refreshing = false; }
    }
    private void BuildChart()
    {
        IEnumerable<(string Label, IEnumerable<RequestRecord> Records)> buckets;
        if (AllDates && snapshot.Count > 0)
            buckets = snapshot.GroupBy(r => r.Timestamp.Date).OrderBy(g => g.Key).Select(g => (g.Key.ToString("MM/dd"), g.AsEnumerable()));
        else buckets = Enumerable.Range(0, 24).Select(h => ($"{h:00}:00", snapshot.Where(r => r.Timestamp.Hour == h)));
        ChartPoints = buckets.Select(b => new ChartPoint(b.Label,
            b.Records.Sum(r => (long)GetInputTokens(r)), b.Records.Sum(r => (long)r.CompletionTokens),
            b.Records.Sum(r => (long)r.CachedTokens), b.Records.Sum(r => (long)r.CacheCreationTokens),
            b.Records.Sum(engine.GetCost))).ToArray();
        Raise(nameof(ChartPoints));
    }
    private static string CompactTokens(long value) => value >= 1_000_000 ? $"{value / 1_000_000d:0.##}M" : value >= 1000 ? $"{value / 1000d:0.#}K" : value.ToString("N0");
    private void OnLog(string line) => dispatcher.BeginInvoke(() =>
    {
        logLines.Add(line);
        if (logLines.Count > 1000) logLines.RemoveRange(0, logLines.Count - 1000);
        Raise(nameof(RuntimeLog));
    });
    private void OnRecord(RequestRecord record) => dispatcher.BeginInvoke(() =>
    {
        QueueRefresh();
        LastRequestCost = "+" + Currency(engine.GetCost(record));
        Raise(nameof(LastRequestCost));
        App.CurrentApp.ShowCostToast();
    });
    private void OnRunningChanged() => dispatcher.BeginInvoke(() =>
    {
        Raise(nameof(IsRunning)); Raise(nameof(CanEdit)); Raise(nameof(IsEditingLocked)); Raise(nameof(RunningText)); Raise(nameof(ClientStatus));
        CommandManager.InvalidateRequerySuggested();
        App.CurrentApp.RefreshTray();
    });
    public void Dispose()
    {
        refreshTimer.Stop();
        lifetime.Cancel();
        engine.LogReceived -= OnLog;
        engine.RequestRecorded -= OnRecord;
        engine.RunningChanged -= OnRunningChanged;
        lifetime.Dispose();
        metadataClient.Dispose();
    }
}

internal sealed record ProtocolOption(ApiRouteKind? Value, string Label);

internal sealed class RecordRow(RequestRecord record, RelayEngine engine) : ObservableObject
{
    public DateTime Timestamp => record.Timestamp;
    public string Model => record.Model;
    public string Path => record.Path;
    public string Summary => record.Summary;
    public int StatusCode => record.StatusCode;
    public int Input => GetInputTokens(record);
    public int Output => record.CompletionTokens;
    public int CacheRead => record.CachedTokens;
    public int CacheWrite => record.CacheCreationTokens;
    public int Total => record.TotalTokens;
    public long Duration => record.ElapsedMs;
    public long FirstByte => record.FirstResponseMs;
    public decimal Cost { get; } = engine.GetCost(record);
    public string CostDescription { get; private set; } = engine.GetCostDescription(record);
    public void RefreshCostDescription(RelayEngine service) { CostDescription = service.GetCostDescription(record); Raise(nameof(CostDescription)); }
}
