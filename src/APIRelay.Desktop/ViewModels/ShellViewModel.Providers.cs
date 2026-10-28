using System.Collections.ObjectModel;
using System.Windows.Input;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    public ObservableCollection<ProviderDraft> ProviderDrafts { get; } = [];
    private ProviderDraft? selectedProvider;
    public ProviderDraft? SelectedProvider { get => selectedProvider; set => Set(ref selectedProvider, value); }
    public ICommand SaveProvidersCommand { get; private set; } = null!;
    public ICommand ReloadProvidersCommand { get; private set; } = null!;

    private void InitializeProviderCommands()
    {
        ReloadProviders();
        SaveProvidersCommand = Command(() =>
        {
            engine.SaveProviders(ProviderDrafts.Select(p => p.ToConfig()), ProviderDrafts.ToDictionary(p => p.Protocol, p => p.ApiKey));
            ReloadProviders();
            Notify(T["Saved"]);
        }, () => !IsRunning);
        ReloadProvidersCommand = Command(ReloadProviders, () => !IsRunning);
    }
    private void ReloadProviders()
    {
        var previous = SelectedProvider?.Protocol;
        ProviderDrafts.Clear();
        foreach (var provider in engine.Providers)
            ProviderDrafts.Add(new ProviderDraft(provider, engine.GetProviderKey(provider.RouteKind)));
        SelectedProvider = ProviderDrafts.FirstOrDefault(p => p.Protocol == previous) ?? ProviderDrafts.First();
    }
}

internal sealed class ProviderDraft : ObservableObject
{
    private string providerUrl;
    private string modelListUrl;
    private bool overrideList;
    private string apiKey;
    private string version;
    public ProviderDraft(ProviderEndpointConfig config, string key)
    {
        Protocol = config.RouteKind;
        providerUrl = config.ProviderUrl;
        modelListUrl = config.ModelListUrl;
        overrideList = config.ModelListUrlOverridden;
        apiKey = key;
        version = config.AnthropicVersion;
    }
    public ApiRouteKind Protocol { get; }
    public string Name => ProtocolName(Protocol);
    public string ProviderUrl
    {
        get => providerUrl;
        set { Set(ref providerUrl, value); if (!OverrideList) Raise(nameof(ModelListUrl)); }
    }
    public bool IsMessages => Protocol == ApiRouteKind.AnthropicMessages;
    public string ApiKey { get => apiKey; set => Set(ref apiKey, value); }
    public string ModelListUrl
    {
        get
        {
            if (OverrideList) return modelListUrl;
            try { return RelayEngine.ModelListUrl(Protocol, ProviderUrl); } catch (UriFormatException) { return ""; }
        }
        set => Set(ref modelListUrl, value);
    }
    public bool OverrideList
    {
        get => overrideList;
        set { if (value && !overrideList) modelListUrl = ModelListUrl; Set(ref overrideList, value); Raise(nameof(ModelListUrl)); }
    }
    public string Version { get => version; set => Set(ref version, value); }
    public ProviderEndpointConfig ToConfig() => new()
    {
        RouteKind = Protocol,
        ProviderType = IsMessages ? ProviderType.Anthropic : ProviderType.OpenAICompatible,
        ProviderUrl = ProviderUrl,
        ModelListUrl = ModelListUrl,
        ModelListUrlOverridden = OverrideList,
        AnthropicVersion = Version
    };
}
