using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace APIRelay;

internal sealed partial class RelayEngine : IAsyncDisposable
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    });

    private readonly string settingsPath;
    private readonly string modelCostsPath;
    private readonly string secretsPath;
    private readonly string logsDirectory;
    private readonly string recordsDirectory;
    private readonly string internalLogPath;
    private readonly string protocolLogPath;
    private readonly string managedConfigDirectory;
    private readonly object internalLogLock = new();
    private readonly object protocolLogLock = new();
    private readonly object recordsLock = new();
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<long, Task> requests = new();
    private long requestSequence;
    private readonly List<ModelCostConfig> modelCosts = [];
    private volatile List<RegisteredModelConfig> registeredModels = [];
    private readonly Dictionary<ApiRouteKind, ProviderEndpointConfig> providerConfigs = CreateDefaultProviderConfigs();
    private readonly Dictionary<ApiRouteKind, string> providerApiKeys = [];
    private volatile IReadOnlyDictionary<string, ManagedRuntimeRoute> managedRuntimeRoutes = new Dictionary<string, ManagedRuntimeRoute>(StringComparer.Ordinal);
    private ToolConfigurationSettings toolConfiguration = new();
    private AppLanguage currentLanguage = AppLanguage.Chinese;
    private volatile bool protocolTraceVisible;
    private bool relayShouldRun;
    private bool disposed;
    private CancellationTokenSource? listenerCancellation;
    private HttpListener? listener;
    private Task? listenerTask;
    private RelayConfig? activeConfig;
    private const string AnthropicMessagesRelayPath = "/anthropic/v1/messages";
    private const int DefaultAnthropicMaxTokens = 8192;
    private const long StreamingProgressLogIntervalMs = 5000;
    private const long StreamingProgressLogBytes = 256 * 1024;
    internal const int ProtocolLogFileCount = 50;
    internal const long ProtocolLogFileMaxBytes = 10L * 1024 * 1024;

    public event Action<string>? LogReceived;
    public event Action<RequestRecord>? RequestRecorded;
    public event Action? RunningChanged;

    public string LocalUrl { get; set; } = "http://127.0.0.1:18080/";
    public AppLanguage Language { get => currentLanguage; set => currentLanguage = value; }
    public string Theme { get; set; } = "Dark";
    public bool IsRunning => listener?.IsListening == true;
    public bool ResumeOnLaunch { get; set; } = true;
    public bool ShouldResume => ResumeOnLaunch && relayShouldRun;
    public bool TraceEnabled { get => protocolTraceVisible; set => protocolTraceVisible = value; }
    public bool BubbleVisible { get; set; } = true;
    public int? BubbleX { get; set; }
    public int? BubbleY { get; set; }
    public ApiRouteKind RouteServer { get; set; } = ApiRouteKind.ChatCompletions;
    public ApiRouteKind? RouteClient { get; set; }
    public string LogsDirectory => logsDirectory;
    public string RecordsDirectory => recordsDirectory;
    public string SettingsDirectory => Path.GetDirectoryName(settingsPath)!;
    public IReadOnlyList<ProviderEndpointConfig> Providers => providerConfigs.Values.OrderBy(p => p.RouteKind).Select(Clone).ToList();
    public IReadOnlyList<RegisteredModelConfig> RegisteredModels => registeredModels.Select(Clone).ToList();
    public IReadOnlyList<ModelCostConfig> ModelCosts
    {
        get
        {
            lock (modelCosts) return modelCosts.Select(CloneModelCost).ToList();
        }
    }
    public ToolConfigurationSettings ClientSettings
    {
        get
        {
            var copy = Clone(toolConfiguration);
            copy.Claude.Enabled = toolConfiguration.Claude.Enabled;
            copy.Codex.Enabled = toolConfiguration.Codex.Enabled;
            return copy;
        }
    }

    public RelayEngine(string? dataDirectory = null)
    {
        var local = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "APIRelay");
        var roaming = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "APIRelay");
        settingsPath = Path.Combine(roaming, "settings.json");
        modelCostsPath = Path.Combine(roaming, "model-costs.json");
        secretsPath = Path.Combine(local, "secrets.json");
        logsDirectory = Path.Combine(local, "Logs");
        recordsDirectory = Path.Combine(local, "Records");
        internalLogPath = Path.Combine(logsDirectory, "internal.txt");
        protocolLogPath = Path.Combine(logsDirectory, "protocol-trace.txt");
        managedConfigDirectory = Path.Combine(local, "ManagedConfig");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(recordsDirectory);
        EnsureInternalLogFile();
        if (!RestoreManagedToolConfigurations())
            throw new IOException("Some client configurations could not be restored. Close the affected client and check the backup files before continuing.");
        LoadProviderApiKeys();
        LoadSettings();
        AppendInternalLog("Application initialized.");
    }

    private void LoadSettings()
    {
        RelaySettings? saved = null;
        if (File.Exists(settingsPath))
        {
            saved = JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(settingsPath))
                ?? throw new InvalidDataException("The settings file is empty.");
            LocalUrl = string.IsNullOrWhiteSpace(saved.LocalUrl) ? LocalUrl : saved.LocalUrl;
            currentLanguage = Enum.TryParse<AppLanguage>(saved.Language, out var language) ? language : AppLanguage.Chinese;
            Theme = saved.Theme;
            relayShouldRun = saved.RelayShouldRun;
            ResumeOnLaunch = saved.ResumeOnLaunch;
            TraceEnabled = saved.ProtocolTraceVisible;
            BubbleVisible = saved.UsageBubbleVisible;
            BubbleX = saved.UsageBubbleLocationX;
            BubbleY = saved.UsageBubbleLocationY;
            RouteServer = saved.RouteHelperServerProtocol;
            RouteClient = saved.RouteHelperToolProtocol;
            if (saved.ProviderConfigs?.Count > 0)
            {
                foreach (var provider in saved.ProviderConfigs.Where(p => Enum.IsDefined(p.RouteKind)))
                    providerConfigs[provider.RouteKind] = provider;
            }
            else MigrateLegacyProviderSettings(saved);
            if (!string.IsNullOrWhiteSpace(saved.ApiKey) && providerApiKeys.Count == 0)
            {
                var protocol = saved.ProviderType == ProviderType.Anthropic.ToString() ? ApiRouteKind.AnthropicMessages : ApiRouteKind.ChatCompletions;
                SaveProviderApiKey(protocol, saved.ApiKey);
            }
            registeredModels.AddRange((saved.RegisteredModels ?? [])
                .Where(m => Enum.IsDefined(m.Protocol) && !string.IsNullOrWhiteSpace(m.ModelId))
                .DistinctBy(m => (m.Protocol, m.ModelId)));
            toolConfiguration = saved.ToolConfiguration ?? new();
            toolConfiguration.Claude ??= new();
            toolConfiguration.Codex ??= new();
            toolConfiguration.Codex.Normalize();
        }
        EnsureProviderConfigDefaults();
        LoadModelCosts(saved?.ModelCosts);
    }

    public bool SaveSettings()
    {
        var saved = new RelaySettings
        {
            LocalUrl = NormalizePrefix(LocalUrl),
            Language = currentLanguage.ToString(),
            Theme = Theme,
            RelayShouldRun = relayShouldRun,
            ResumeOnLaunch = ResumeOnLaunch,
            ProtocolTraceVisible = TraceEnabled,
            UsageBubbleVisible = BubbleVisible,
            UsageBubbleLocationX = BubbleX,
            UsageBubbleLocationY = BubbleY,
            RouteHelperServerProtocol = RouteServer,
            RouteHelperToolProtocol = RouteClient,
            ProviderConfigs = providerConfigs.Values.Select(Clone).ToList(),
            RegisteredModels = registeredModels.Select(Clone).ToList(),
            ToolConfiguration = toolConfiguration
        };
        WriteAllTextAtomically(settingsPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRunning) return;
            if (!Uri.TryCreate(NormalizePrefix(LocalUrl), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException(GetText(TextId.Txt57));
            activeConfig = new RelayConfig(uri);
            var cancellation = new CancellationTokenSource();
            var server = new HttpListener();
            server.Prefixes.Add(uri.ToString());
            listenerCancellation = cancellation;
            listener = server;
            try
            {
                server.Start();
                ApplyEnabledManagedToolConfigurations(uri);
                relayShouldRun = true;
                SaveSettings();
                listenerTask = Task.Run(() => ListenLoopAsync(server, cancellation.Token));
                AppendLog(GetText(TextId.Txt59, uri), true);
                RunningChanged?.Invoke();
            }
            catch
            {
                await StopCoreAsync(true).ConfigureAwait(false);
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync(bool clearRunIntent = true)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync(clearRunIntent).ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private async Task StopCoreAsync(bool clearRunIntent)
    {
        listenerCancellation?.Cancel();
        listener?.Close();
        if (listenerTask != null)
        {
            try { await listenerTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            { AppendInternalLog("Listener stopped."); }
        }
        await Task.WhenAll(requests.Values).ConfigureAwait(false);
        listenerTask = null;
        listener = null;
        activeConfig = null;
        listenerCancellation?.Dispose();
        listenerCancellation = null;
        var restored = RestoreManagedToolConfigurations();
        toolConfiguration.Claude.Enabled = false;
        toolConfiguration.Codex.Enabled = false;
        managedRuntimeRoutes = new Dictionary<string, ManagedRuntimeRoute>(StringComparer.Ordinal);
        if (clearRunIntent) relayShouldRun = false;
        SaveSettings();
        RunningChanged?.Invoke();
        if (!restored) throw new IOException("Client configuration recovery failed. Backups have been retained.");
    }

    public void SaveProviders(IEnumerable<ProviderEndpointConfig> providers, IReadOnlyDictionary<ApiRouteKind, string> keys)
    {
        if (IsRunning) throw new InvalidOperationException("Stop the relay before editing providers.");
        var configs = providers.Select(Clone).ToList();
        foreach (var config in configs)
        {
            if (!string.IsNullOrWhiteSpace(config.ProviderUrl))
                config.ProviderUrl = ValidateProviderUrl(config.RouteKind, config.ProviderUrl.Trim());
            if (config.ModelListUrlOverridden && (!Uri.TryCreate(config.ModelListUrl, UriKind.Absolute, out var list) ||
                list.Scheme is not ("http" or "https")))
                throw new ArgumentException("The model list address must be an HTTP or HTTPS URL.");
            if (!config.ModelListUrlOverridden) config.ModelListUrl = BuildModelListUrl(config.RouteKind, config.ProviderUrl);
        }
        SaveProviderApiKeys(keys);
        providerConfigs.Clear();
        foreach (var config in configs) providerConfigs[config.RouteKind] = config;
        EnsureProviderConfigDefaults();
        SaveSettings();
    }

    public string GetProviderKey(ApiRouteKind protocol) => providerApiKeys.GetValueOrDefault(protocol, "");

    public Task<ModelDiscoveryResult> DiscoverAsync(ApiRouteKind protocol, CancellationToken cancellationToken) =>
        DiscoverModelsAsync(Clone(providerConfigs[protocol]), GetProviderKey(protocol), cancellationToken);

    public async Task SaveRegisteredModelsAsync(IEnumerable<RegisteredModelConfig> models)
    {
        var updated = models.Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
            .DistinctBy(m => (m.Protocol, m.ModelId)).Select(Clone).ToList();
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (toolConfiguration.Claude.Enabled)
            {
                var aliases = new[] { toolConfiguration.Claude.HaikuModelAlias, toolConfiguration.Claude.SonnetModelAlias,
                    toolConfiguration.Claude.OpusModelAlias };
                if (aliases.Any(alias => !updated.Any(model => CreateManagedModelAlias(model.Protocol, model.ModelId) == alias)))
                    throw new InvalidOperationException(currentLanguage == AppLanguage.Chinese
                        ? "该模型正在被 Claude Code 使用，请先在客户端设置中切换模型，再移除。"
                        : "This model is in use by Claude Code. Select another model in client settings before removing it.");
            }
            var previous = registeredModels;
            var previousRoutes = managedRuntimeRoutes;
            try
            {
                registeredModels = updated;
                if (activeConfig != null) BuildManagedRuntimeRouteSnapshot();
                SaveSettings();
            }
            catch
            {
                registeredModels = previous;
                managedRuntimeRoutes = previousRoutes;
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }

    public async Task SaveClientSettingsAsync(ToolConfigurationSettings draft)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            draft.Codex.Normalize();
            var previous = toolConfiguration;
            if (!IsRunning && (draft.Claude.Enabled || draft.Codex.Enabled))
                throw new InvalidOperationException("Start the relay before enabling managed clients.");
            if (!RestoreManagedToolConfigurations()) throw new IOException("Cannot restore the previous client configuration.");
            toolConfiguration = Clone(draft);
            toolConfiguration.Claude.Enabled = draft.Claude.Enabled;
            toolConfiguration.Codex.Enabled = draft.Codex.Enabled;
            try
            {
                if (activeConfig != null) ApplyEnabledManagedToolConfigurations(activeConfig.LocalUri);
                SaveSettings();
            }
            catch
            {
                if (!RestoreManagedToolConfigurations())
                    throw new IOException("Client configuration recovery failed. Backups have been retained.");
                toolConfiguration = previous;
                if (activeConfig != null) ApplyEnabledManagedToolConfigurations(activeConfig.LocalUri);
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }

    public void SavePrices(IEnumerable<ModelCostConfig> prices)
    {
        var updated = prices.Where(p => !string.IsNullOrWhiteSpace(p.ModelName)).Select(CloneModelCost).ToList();
        if (updated.Any(p => p.InputCostPerMillion < 0 || p.OutputCostPerMillion < 0 || p.CacheHitCostPerMillion < 0 || p.CacheCreationCostPerMillion < 0))
            throw new ArgumentException("Prices cannot be negative.");
        updated = updated.DistinctBy(p => p.ModelName, StringComparer.OrdinalIgnoreCase).ToList();
        lock (modelCosts)
        {
            SaveModelCosts(updated);
            modelCosts.Clear();
            modelCosts.AddRange(updated);
        }
    }

    public IReadOnlyList<DateTime> GetRecordDates() => Directory.EnumerateFiles(recordsDirectory, "*.json")
        .Select(p => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var date) ? date : (DateTime?)null)
        .Where(d => d.HasValue).Select(d => d!.Value).Append(DateTime.Today).Distinct().OrderDescending().ToList();

    public IReadOnlyList<RequestRecord> GetRecords(DateTime? date) =>
        (date.HasValue ? LoadRecordsForDate(date.Value) : LoadAllRecords()).OrderByDescending(r => r.Timestamp).ToList();

    public void ClearRecords(DateTime? date)
    {
        lock (recordsLock)
        {
            if (date.HasValue) File.Delete(GetRecordPath(date.Value));
            else foreach (var file in Directory.EnumerateFiles(recordsDirectory, "*.json")) File.Delete(file);
        }
    }

    public decimal GetCost(RequestRecord record) => CalculateRecordCost(record);
    public string GetCostDescription(RequestRecord record) => BuildCostFormulaText(record);
    public (decimal Input, decimal Output, decimal CacheHit, decimal CacheCreation) GetUsageCosts(IEnumerable<RequestRecord> records) => CalculateUsageCosts(records);
    public static IReadOnlyList<ModelCostConfig> DefaultPrices() => CreateDefaultModelCosts();
    public static int GetInputTokens(RequestRecord record) => CalculateTotalInputTokens(record);
    public static string ModelAlias(ApiRouteKind protocol, string model) => CreateManagedModelAlias(protocol, model);
    public static string ProtocolName(ApiRouteKind protocol) => GetRouteKindDisplayName(protocol);
    public static string ModelListUrl(ApiRouteKind protocol, string url) => BuildModelListUrl(protocol, url);

    public string BuildRouteUrl()
    {
        if (!Uri.TryCreate(NormalizePrefix(LocalUrl), UriKind.Absolute, out var uri)) return "";
        var parts = new List<string> { uri.AbsolutePath.Trim('/'), GetRoutePathSegment(RouteServer) };
        if (RouteClient.HasValue && RouteClient != RouteServer) parts.Add(GetRoutePathSegment(RouteClient.Value));
        var route = string.Join('/', parts.Where(p => p.Length > 0));
        if (RouteClient == RouteServer) route += "/";
        return new UriBuilder(uri) { Path = route, Query = "", Fragment = "" }.Uri.ToString();
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private string GetText(TextId id, params object[] args) => AppTexts.GetText(currentLanguage, id, args);
    private void AppendLog(string message, bool includeTime) =>
        LogReceived?.Invoke(includeTime ? $"[{DateTime.Now:HH:mm:ss}] {message}" : message);
    private bool TryBeginInvoke(Action action)
    {
        if (disposed) return false;
        action();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync(false).ConfigureAwait(false);
        disposed = true;
        lifecycle.Dispose();
    }
}
