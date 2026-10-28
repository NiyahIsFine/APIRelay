using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    private readonly HttpClient metadataClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private Dictionary<string, CodexModelMetadata> discoveredMetadata = [];
    private ApiRouteKind discoveryProtocol = ApiRouteKind.Responses;
    private ApiRouteKind discoveredProtocol = ApiRouteKind.Responses;
    private string discoveredProviderUrl = "";
    private string modelSearch = "";
    private string manualModelId = "";
    private string manualDisplayName = "";
    private CodexCandidate? selectedCandidate;
    private RegisteredModelConfig? selectedRegistration;
    private string candidateContext = "";
    private string candidateDisplayName = "";
    private string candidateDefault = "";
    private bool loadingCandidate;

    public ObservableCollection<DiscoveryRow> DiscoveredModels { get; } = [];
    public ObservableCollection<RegisteredModelConfig> Registrations { get; } = [];
    public ObservableCollection<CodexCandidate> Candidates { get; } = [];
    public ObservableCollection<EffortChoice> CandidateEfforts { get; } = [];
    public ICollectionView DiscoveredView { get; private set; } = null!;
    public ApiRouteKind DiscoveryProtocol
    {
        get => discoveryProtocol;
        set
        {
            if (!Set(ref discoveryProtocol, value)) return;
            DiscoveredModels.Clear();
            discoveredMetadata.Clear();
            Raise(nameof(DiscoveryCount));
        }
    }
    public string ModelSearch { get => modelSearch; set { Set(ref modelSearch, value); DiscoveredView.Refresh(); } }
    public int DiscoveryCount => DiscoveredModels.Count;
    public string ManualModelId { get => manualModelId; set => Set(ref manualModelId, value); }
    public string ManualDisplayName { get => manualDisplayName; set => Set(ref manualDisplayName, value); }
    public RegisteredModelConfig? SelectedRegistration { get => selectedRegistration; set => Set(ref selectedRegistration, value); }
    public CodexCandidate? SelectedCandidate
    {
        get => selectedCandidate;
        set { Set(ref selectedCandidate, value); LoadCandidateEditor(); Raise(nameof(HasCandidate)); }
    }
    public bool HasCandidate => SelectedCandidate != null;
    public string CandidateContext { get => candidateContext; set => Set(ref candidateContext, value); }
    public string CandidateDisplayName { get => candidateDisplayName; set => Set(ref candidateDisplayName, value); }
    public string CandidateDefault { get => candidateDefault; set => Set(ref candidateDefault, value); }
    public IReadOnlyList<Choice> DefaultEffortOptions => new[] { new Choice("", T["Auto"]) }.Concat(
        CandidateEfforts.Where(c => c.IsSelected).Select(c => new Choice(c.Value.ToWire(), c.Value.ToWire()))).ToArray();
    public ICommand DiscoverModelsCommand { get; private set; } = null!;
    public ICommand AddClaudeCommand { get; private set; } = null!;
    public ICommand AddCodexCommand { get; private set; } = null!;
    public ICommand ManualClaudeCommand { get; private set; } = null!;
    public ICommand ManualCodexCommand { get; private set; } = null!;
    public ICommand RemoveRegistrationCommand { get; private set; } = null!;
    public ICommand RemoveCandidateCommand { get; private set; } = null!;
    public ICommand SaveCandidateCommand { get; private set; } = null!;

    private void InitializeModelCommands()
    {
        foreach (var effort in Enum.GetValues<ReasoningEffort>())
        {
            var choice = new EffortChoice(effort);
            choice.PropertyChanged += (_, _) =>
            {
                if (loadingCandidate) return;
                var previous = CandidateDefault;
                Raise(nameof(DefaultEffortOptions));
                CandidateDefault = DefaultEffortOptions.Any(e => e.Value == previous) ? previous : "";
            };
            CandidateEfforts.Add(choice);
        }
        DiscoveredView = CollectionViewSource.GetDefaultView(DiscoveredModels);
        DiscoveredView.Filter = o => o is DiscoveryRow model && $"{model.Id} {model.Name}".Contains(ModelSearch, StringComparison.OrdinalIgnoreCase);
        foreach (var registered in engine.RegisteredModels) Registrations.Add(registered);
        foreach (var candidate in engine.ClientSettings.Codex.Candidates) Candidates.Add(candidate);
        DiscoverModelsCommand = Command(async () =>
        {
            var protocol = DiscoveryProtocol;
            var providerUrl = engine.Providers.First(p => p.RouteKind == protocol).ProviderUrl;
            Notify(T["Discovering"]);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var result = await engine.DiscoverAsync(protocol, timeout.Token);
            if (protocol != DiscoveryProtocol) return;
            if (result.Error.Length > 0) throw new InvalidOperationException($"{T["FailedDiscovery"]} {result.Error}");
            discoveredProtocol = protocol;
            discoveredProviderUrl = providerUrl;
            discoveredMetadata.Clear();
            DiscoveredModels.Clear();
            foreach (var model in result.Models) DiscoveredModels.Add(new DiscoveryRow(model.Id, model.DisplayName));
            Raise(nameof(DiscoveryCount));
            Notify($"{T["Updated"]} · {result.Models.Count} {T["Count"]}");
            try
            {
                var metadata = await ModelsDevMetadata.FetchAsync(metadataClient,
                    providerUrl, result.Models.Select(m => m.Id), timeout.Token);
                if (protocol == DiscoveryProtocol) discoveredMetadata = metadata;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { Notify(T.Chinese ? "模型已获取；参考元数据暂不可用，可手动填写。" : "Models loaded; reference metadata is unavailable. Manual editing is available."); }
        });
        AddClaudeCommand = Command(() => RegisterAsync(DiscoveredModels.Where(m => m.IsSelected), discoveredProtocol));
        AddCodexCommand = Command(() => AddCandidatesAsync(DiscoveredModels.Where(m => m.IsSelected), discoveredProtocol));
        ManualClaudeCommand = Command(async () =>
        {
            await RegisterAsync([ManualRow()], DiscoveryProtocol);
            ManualModelId = ""; ManualDisplayName = "";
        });
        ManualCodexCommand = Command(async () =>
        {
            await AddCandidatesAsync([ManualRow()], DiscoveryProtocol);
            ManualModelId = ""; ManualDisplayName = "";
        });
        RemoveRegistrationCommand = Command(async () =>
        {
            if (SelectedRegistration == null) return;
            var removed = SelectedRegistration;
            await engine.SaveRegisteredModelsAsync(Registrations.Where(model => model != removed));
            Registrations.Remove(removed);
            Raise(nameof(ClaudeModelOptions));
            Notify(T["Saved"]);
        }, () => SelectedRegistration != null);
        RemoveCandidateCommand = Command(async () =>
        {
            if (SelectedCandidate == null) return;
            await SaveCandidateListAsync(Candidates.Where(candidate => candidate != SelectedCandidate));
        }, () => SelectedCandidate != null);
        SaveCandidateCommand = Command(async () =>
        {
            if (SelectedCandidate == null) return;
            var context = ParsePositive(CandidateContext);
            var candidate = SelectedCandidate;
            candidate.DisplayName = CandidateDisplayName.Trim();
            candidate.ContextWindow = context;
            candidate.ContextIsManual = true;
            candidate.EffortsAreManual = true;
            candidate.SupportedEfforts = CandidateEfforts.Where(e => e.IsSelected).Select(e => e.Value).ToList();
            candidate.DefaultEffort = ReasoningEfforts.TryParse(CandidateDefault, out var effort) ? effort : null;
            candidate.Normalize();
            await SaveCandidateListAsync();
            SelectedCandidate = Candidates.FirstOrDefault(c => c.Alias == candidate.Alias);
        }, () => SelectedCandidate != null);
    }
    private DiscoveryRow ManualRow()
    {
        var id = ManualModelId.Trim();
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(T["ModelId"]);
        return new(id, string.IsNullOrWhiteSpace(ManualDisplayName) ? id : ManualDisplayName.Trim());
    }
    private async Task RegisterAsync(IEnumerable<DiscoveryRow> models, ApiRouteKind protocol)
    {
        var rows = models.ToArray();
        if (rows.Length == 0) throw new InvalidOperationException(T["SelectModel"]);
        var updated = Registrations.ToList();
        foreach (var row in rows)
            if (!updated.Any(m => m.Protocol == protocol && m.ModelId == row.Id))
                updated.Add(new() { Protocol = protocol, ModelId = row.Id, DisplayName = row.Name, RegisteredAtUtc = DateTime.UtcNow });
        await engine.SaveRegisteredModelsAsync(updated);
        foreach (var model in updated.Where(model => !Registrations.Contains(model))) Registrations.Add(model);
        Raise(nameof(ClaudeModelOptions));
        Notify(T["Saved"]);
    }
    private async Task AddCandidatesAsync(IEnumerable<DiscoveryRow> models, ApiRouteKind protocol)
    {
        var rows = models.ToArray();
        if (rows.Length == 0) throw new InvalidOperationException(T["SelectModel"]);
        var providerUrl = engine.Providers.First(p => p.RouteKind == protocol).ProviderUrl;
        var references = discoveredProtocol == protocol && StringComparer.Ordinal.Equals(discoveredProviderUrl, providerUrl)
            ? new Dictionary<string, CodexModelMetadata>(discoveredMetadata, StringComparer.Ordinal)
            : new Dictionary<string, CodexModelMetadata>(StringComparer.Ordinal);
        var missing = rows.Where(row => !references.ContainsKey(row.Id) &&
            !Candidates.Any(c => c.Protocol == protocol && c.ModelId == row.Id)).Select(row => row.Id).ToArray();
        var referenceUnavailable = false;
        if (missing.Length > 0)
        {
            try
            {
                var fetched = await ModelsDevMetadata.FetchAsync(metadataClient,
                    providerUrl, missing, lifetime.Token);
                foreach (var entry in fetched) references[entry.Key] = entry.Value;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                referenceUnavailable = true;
            }
        }
        foreach (var row in rows)
        {
            if (Candidates.Any(c => c.Protocol == protocol && c.ModelId == row.Id)) continue;
            var metadata = references.GetValueOrDefault(row.Id, CodexModelMetadata.Unknown);
            var candidate = new CodexCandidate
            {
                Protocol = protocol,
                ModelId = row.Id,
                DisplayName = row.Name
            };
            candidate.FillMissingMetadata(metadata);
            Candidates.Add(candidate);
        }
        await SaveCandidateListAsync();
        SelectedCandidate ??= Candidates.FirstOrDefault();
        if (referenceUnavailable) Notify(T["CapabilityReferenceUnavailable"]);
    }
    private async Task SaveCandidateListAsync(IEnumerable<CodexCandidate>? candidates = null)
    {
        var settings = engine.ClientSettings;
        settings.Codex.Candidates = (candidates ?? Candidates).Select(c => c.Clone()).ToList();
        await engine.SaveClientSettingsAsync(settings);
        var alias = SelectedCandidate?.Alias;
        Candidates.Clear();
        foreach (var candidate in engine.ClientSettings.Codex.Candidates) Candidates.Add(candidate);
        SelectedCandidate = Candidates.FirstOrDefault(c => c.Alias == alias) ?? Candidates.FirstOrDefault();
        LoadClients();
        Notify(T["Saved"]);
    }
    private void LoadCandidateEditor()
    {
        loadingCandidate = true;
        try
        {
            CandidateDisplayName = SelectedCandidate?.DisplayName ?? "";
            CandidateContext = SelectedCandidate?.ContextWindow?.ToString() ?? "";
            foreach (var choice in CandidateEfforts) choice.IsSelected = SelectedCandidate?.SupportedEfforts.Contains(choice.Value) ?? false;
            Raise(nameof(DefaultEffortOptions));
            CandidateDefault = SelectedCandidate?.DefaultEffort?.ToWire() ?? "";
        }
        finally { loadingCandidate = false; }
    }
    private long? ParsePositive(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TryOptionalPositive(value, out var number)) throw new ArgumentException(T["InvalidNumber"]);
        return number;
    }
}

internal sealed class DiscoveryRow(string id, string name) : ObservableObject
{
    private bool selected;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}
internal sealed class EffortChoice(ReasoningEffort effort) : ObservableObject
{
    private bool selected;
    public ReasoningEffort Value { get; } = effort;
    public string Label => Value.ToWire();
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}
internal sealed record Choice(string Value, string Label);
