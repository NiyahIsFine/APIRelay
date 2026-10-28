using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    private string haiku = "", sonnet = "", opus = "", codexModel = "", reasoning = "", contextLimit = "", compactLimit = "";
    private bool claudeEnabled, codexEnabled, toolSearch, maximumEffort, preserveLogin = true;
    private ClientDraft? savedClientDraft;
    private bool loadingClients;
    private sealed record ClientDraft(
        string Haiku, string Sonnet, string Opus, string CodexModel, string Reasoning,
        bool ClaudeEnabled, bool CodexEnabled, bool ToolSearch, bool MaximumEffort, bool PreserveLogin,
        string ContextLimit, string CompactLimit);
    private ClientDraft CaptureClientDraft() => new(
        Haiku ?? "", Sonnet ?? "", Opus ?? "", CodexModel ?? "", Reasoning ?? "",
        ClaudeEnabled, CodexEnabled, ToolSearch, MaximumEffort, PreserveLogin,
        ContextLimit ?? "", CompactLimit ?? "");
    public bool HasClientChanges => savedClientDraft != null && CaptureClientDraft() != savedClientDraft;
    private bool SetClientValue<TValue>(ref TValue field, TValue value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        if (!loadingClients) RefreshClientChanges();
        return true;
    }
    private void RefreshClientChanges()
    {
        Raise(nameof(HasClientChanges));
        CommandManager.InvalidateRequerySuggested();
    }
    public IReadOnlyList<Choice> ClaudeModelOptions => new[] { new Choice("", T["SelectModel"]) }.Concat(
        Registrations.OrderBy(m => m.Protocol).ThenBy(m => m.ModelId).Select(m =>
            new Choice(ModelAlias(m.Protocol, m.ModelId), $"{m.ModelId} · {ProtocolName(m.Protocol)}"))).ToArray();
    public IReadOnlyList<Choice> CodexModelOptions => Candidates.Select(c => new Choice(c.Alias, $"{c.ModelId} · {ProtocolName(c.Protocol)}")).ToArray();
    public IReadOnlyList<Choice> EffortOptions => new[] { new Choice("", T["Auto"]) }.Concat(
        (Candidates.FirstOrDefault(c => c.Alias == CodexModel)?.SupportedEfforts ?? [])
        .Select(e => new Choice(e.ToWire(), e.ToWire()))).ToArray();
    public string Haiku { get => haiku; set => SetClientValue(ref haiku, value); }
    public string Sonnet { get => sonnet; set => SetClientValue(ref sonnet, value); }
    public string Opus { get => opus; set => SetClientValue(ref opus, value); }
    public string CodexModel
    {
        get => codexModel;
        set
        {
            if (!SetClientValue(ref codexModel, value)) return;
            var previous = Reasoning;
            Raise(nameof(EffortOptions));
            Reasoning = EffortOptions.Any(e => e.Value == previous) ? previous : "";
        }
    }
    public string Reasoning { get => reasoning; set => SetClientValue(ref reasoning, value); }
    public bool ClaudeEnabled { get => claudeEnabled; set => SetClientValue(ref claudeEnabled, value); }
    public bool CodexEnabled { get => codexEnabled; set => SetClientValue(ref codexEnabled, value); }
    public bool ToolSearch { get => toolSearch; set => SetClientValue(ref toolSearch, value); }
    public bool MaximumEffort { get => maximumEffort; set => SetClientValue(ref maximumEffort, value); }
    public bool PreserveLogin { get => preserveLogin; set => SetClientValue(ref preserveLogin, value); }
    public string ContextLimit { get => contextLimit; set => SetClientValue(ref contextLimit, value); }
    public string CompactLimit { get => compactLimit; set => SetClientValue(ref compactLimit, value); }
    public string ClientStatus
    {
        get
        {
            var current = engine.ClientSettings;
            return $"Claude · {T[current.Claude.Enabled ? "Managed" : "NotManaged"]}   /   Codex · {T[current.Codex.Enabled ? "Managed" : "NotManaged"]}";
        }
    }
    public ICommand ApplyClientsCommand { get; private set; } = null!;
    public ICommand ReloadClientsCommand { get; private set; } = null!;
    private void InitializeClientCommands()
    {
        LoadClients();
        ApplyClientsCommand = Command(async () =>
        {
            var draft = engine.ClientSettings;
            draft.Claude.HaikuModelAlias = Haiku ?? "";
            draft.Claude.SonnetModelAlias = Sonnet ?? "";
            draft.Claude.OpusModelAlias = Opus ?? "";
            draft.Claude.EnableToolSearch = ToolSearch;
            draft.Claude.UseMaximumEffort = MaximumEffort;
            draft.Claude.Enabled = ClaudeEnabled;
            draft.Codex.ModelAlias = CodexModel ?? "";
            draft.Codex.ReasoningEffort = Reasoning ?? "";
            draft.Codex.PreserveAccountLogin = PreserveLogin;
            draft.Codex.ModelContextWindow = ParsePositive(ContextLimit);
            draft.Codex.ModelAutoCompactTokenLimit = ParsePositive(CompactLimit);
            if (draft.Codex.ModelAutoCompactTokenLimit.HasValue && draft.Codex.ModelContextWindow.HasValue &&
                draft.Codex.ModelAutoCompactTokenLimit > draft.Codex.ModelContextWindow)
                throw new ArgumentException(T.Chinese ? "自动压缩阈值不能大于上下文窗口。" : "Auto-compact threshold cannot exceed the context window.");
            draft.Codex.Enabled = CodexEnabled;
            await engine.SaveClientSettingsAsync(draft);
            LoadClients();
            Notify(T["Saved"]);
        }, () => HasClientChanges);
        ReloadClientsCommand = Command(LoadClients, () => HasClientChanges);
    }
    private void LoadClients()
    {
        var settings = engine.ClientSettings;
        loadingClients = true;
        try
        {
            Haiku = settings.Claude.HaikuModelAlias;
            Sonnet = settings.Claude.SonnetModelAlias;
            Opus = settings.Claude.OpusModelAlias;
            ToolSearch = settings.Claude.EnableToolSearch;
            MaximumEffort = settings.Claude.UseMaximumEffort;
            ClaudeEnabled = settings.Claude.Enabled;
            Raise(nameof(ClaudeModelOptions)); Raise(nameof(CodexModelOptions));
            CodexModel = settings.Codex.ModelAlias;
            Raise(nameof(EffortOptions));
            Reasoning = settings.Codex.ReasoningEffort;
            PreserveLogin = settings.Codex.PreserveAccountLogin;
            ContextLimit = settings.Codex.ModelContextWindow?.ToString() ?? "";
            CompactLimit = settings.Codex.ModelAutoCompactTokenLimit?.ToString() ?? "";
            CodexEnabled = settings.Codex.Enabled;
            savedClientDraft = CaptureClientDraft();
        }
        finally
        {
            loadingClients = false;
            RefreshClientChanges();
        }
        Raise(nameof(ClientStatus));
    }
}
