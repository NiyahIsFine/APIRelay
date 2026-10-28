using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    private string? selectedTraceFile;
    private ProtocolTraceEntry? selectedTrace;
    private string traceBody = "";
    private string traceSearch = "";
    private string traceDirection = "";
    private bool prettyTrace = true;
    private int traceRevision;
    private int bodyRevision;
    private bool isTraceLoading;
    private int visibleTraceCount;
    private Task traceLoadTask = Task.CompletedTask;
    public ObservableCollection<string> TraceFiles { get; } = [];
    public ObservableCollection<ProtocolTraceEntry> TraceEntries { get; private set; } = [];
    public ICollectionView TraceView { get; private set; } = null!;
    public IReadOnlyList<string> TraceDirections { get; } = ["", "client->tool", "tool->server", "server->tool", "tool->client"];
    public string? SelectedTraceFile
    {
        get => selectedTraceFile;
        set { if (Set(ref selectedTraceFile, value)) traceLoadTask = LoadTraceFileAsync(); }
    }
    public ProtocolTraceEntry? SelectedTrace
    {
        get => selectedTrace;
        set
        {
            if (!Set(ref selectedTrace, value)) return;
            Raise(nameof(TraceSelectionDisplay));
            _ = ReadTraceBodyAsync();
        }
    }
    public string TraceBody
    {
        get => traceBody;
        private set { if (Set(ref traceBody, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public bool PrettyTrace { get => prettyTrace; set { if (Set(ref prettyTrace, value)) _ = ReadTraceBodyAsync(); } }
    public string TraceSearch { get => traceSearch; set { if (Set(ref traceSearch, value)) ApplyTraceFilter(); } }
    public string TraceDirection { get => traceDirection; set { if (Set(ref traceDirection, value)) ApplyTraceFilter(); } }
    public bool IsTraceLoading { get => isTraceLoading; private set => Set(ref isTraceLoading, value); }
    public bool HasTraceEntries => visibleTraceCount > 0;
    public string TraceCountDisplay => $"{visibleTraceCount:N0} / {TraceEntries.Count:N0}";
    public string TraceRetentionDisplay => string.Format(T["TraceRetention"], ProtocolLogFileMaxBytes / (1024 * 1024), ProtocolLogFileCount);
    public string TraceSelectionDisplay => SelectedTrace is { } entry
        ? $"{entry.RequestId:x8} · {entry.Timestamp:HH:mm:ss.fff} · {entry.DirectionDisplay} · {entry.ProtocolDisplay} · {entry.BodyLength:N0} B"
        : "";
    public bool TraceEnabled
    {
        get => engine.TraceEnabled;
        set { engine.TraceEnabled = value; Raise(); try { engine.SaveSettings(); } catch (Exception ex) { ShowError(ex); } }
    }
    public ICommand RefreshTraceCommand { get; private set; } = null!;
    public ICommand CopyTraceCommand { get; private set; } = null!;
    public ICommand ClearLogCommand { get; private set; } = null!;
    private void InitializeLogCommands()
    {
        TraceView = CreateTraceView();
        RefreshTraceCommand = Command(RefreshTraceAsync);
        CopyTraceCommand = Command(() =>
        {
            if (TraceBody.Length == 0) return;
            System.Windows.Clipboard.SetText(TraceBody);
            Notify(T["Copied"]);
        }, () => TraceBody.Length > 0);
        ClearLogCommand = new RelayCommand(() => { logLines.Clear(); Raise(nameof(RuntimeLog)); });
        ReloadTraceFiles();
    }

    private ICollectionView CreateTraceView()
    {
        var view = CollectionViewSource.GetDefaultView(TraceEntries);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProtocolTraceEntry.RequestId)));
            view.Filter = value => value is ProtocolTraceEntry entry &&
                (string.IsNullOrWhiteSpace(TraceDirection) || entry.DirectionDisplay == TraceDirection) &&
                $"{entry.RequestId:x8} {entry.ProtocolDisplay} {entry.DirectionDisplay}".Contains(TraceSearch.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        return view;
    }

    internal async Task RefreshTraceAsync()
    {
        var previous = SelectedTraceFile;
        ReloadTraceFiles();
        if (previous == SelectedTraceFile) traceLoadTask = LoadTraceFileAsync();
        await traceLoadTask;
    }

    private void ReloadTraceFiles()
    {
        var previous = SelectedTraceFile;
        var files = ProtocolTraceCatalog.EnumerateLogFiles(engine.LogsDirectory, ProtocolLogFileCount)
            .Select(Path.GetFileName).OfType<string>().ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            var existing = TraceFiles.IndexOf(files[index]);
            if (existing < 0) TraceFiles.Insert(index, files[index]);
            else if (existing != index) TraceFiles.Move(existing, index);
        }
        while (TraceFiles.Count > files.Length) TraceFiles.RemoveAt(TraceFiles.Count - 1);
        SelectedTraceFile = files.Contains(previous) ? previous : files.FirstOrDefault();
    }

    private async Task LoadTraceFileAsync()
    {
        var revision = ++traceRevision;
        var file = SelectedTraceFile;
        var previous = SelectedTrace;
        SelectedTrace = null;
        TraceEntries.Clear();
        UpdateTraceSummary();
        IsTraceLoading = file != null;
        if (file == null) return;
        try
        {
            var path = Path.Combine(engine.LogsDirectory, file);
            var entries = await Task.Run(() => ProtocolTraceParser.IndexFile(path), lifetime.Token);
            if (revision != traceRevision || lifetime.IsCancellationRequested) return;
            TraceEntries = new ObservableCollection<ProtocolTraceEntry>(entries.Reverse());
            TraceView = CreateTraceView();
            Raise(nameof(TraceEntries));
            Raise(nameof(TraceView));
            UpdateTraceSummary();
            SelectedTrace = previous is { } selected && TraceView.Contains(selected)
                ? selected
                : TraceView.Cast<ProtocolTraceEntry>().Select(entry => (ProtocolTraceEntry?)entry).FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (revision == traceRevision) ShowError(ex); }
        finally { if (revision == traceRevision) IsTraceLoading = false; }
    }

    private void ApplyTraceFilter()
    {
        TraceView.Refresh();
        UpdateTraceSummary();
        if (SelectedTrace is not { } selected || !TraceView.Contains(selected))
            SelectedTrace = TraceView.Cast<ProtocolTraceEntry>().Select(entry => (ProtocolTraceEntry?)entry).FirstOrDefault();
    }

    private void UpdateTraceSummary()
    {
        visibleTraceCount = TraceView.Cast<ProtocolTraceEntry>().Count();
        Raise(nameof(HasTraceEntries));
        Raise(nameof(TraceCountDisplay));
    }

    private async Task ReadTraceBodyAsync()
    {
        var revision = ++bodyRevision;
        TraceBody = "";
        try
        {
            if (SelectedTrace is not { } entry || SelectedTraceFile == null) { TraceBody = ""; return; }
            var path = Path.Combine(engine.LogsDirectory, SelectedTraceFile);
            var pretty = PrettyTrace;
            var body = await Task.Run(() =>
            {
                var raw = ProtocolTraceParser.ReadBody(path, entry);
                return pretty ? ProtocolTraceParser.FormatBody(raw) : raw;
            }, lifetime.Token);
            if (revision == bodyRevision) TraceBody = body;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (revision == bodyRevision) ShowError(ex); }
    }
}
