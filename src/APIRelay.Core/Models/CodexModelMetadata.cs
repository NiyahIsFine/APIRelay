using System.Text.Json;
using System.Text.Json.Serialization;

namespace APIRelay;

[JsonConverter(typeof(ReasoningEffortConverter))]
internal enum ReasoningEffort { None, Minimal, Low, Medium, High, XHigh, Max, Ultra }

internal sealed class ReasoningEffortConverter : JsonConverter<ReasoningEffort>
{
    public override ReasoningEffort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ReasoningEfforts.TryParse(reader.GetString(), out var effort)) return effort;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined((ReasoningEffort)number)) return (ReasoningEffort)number;
        throw new JsonException("Unrecognized reasoning effort.");
    }
    public override void Write(Utf8JsonWriter writer, ReasoningEffort value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToWire());
}

internal static class ReasoningEfforts
{
    public static string ToWire(this ReasoningEffort value) => value.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out ReasoningEffort effort)
    {
        effort = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, true, out effort) && Enum.IsDefined(effort)
            && effort.ToWire().Equals(value, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record CodexModelMetadata(long? ContextWindow, ReasoningEffort[] Efforts, string Source, string ReasoningType)
{
    public static CodexModelMetadata Unknown { get; } = new(null, [], "", "");
    public string Description { get; init; } = "";
    public CodexModelCapabilities Capabilities { get; init; } = new();
}

internal static class ModelsDevMetadata
{
    private static readonly HashSet<string> OriginalPublishers = new(StringComparer.Ordinal)
    {
        "openai", "anthropic", "google", "deepseek", "xai", "mistral", "cohere",
        "moonshotai", "zhipuai", "alibaba", "minimax", "xiaomi", "stepfun", "meta",
        "perplexity", "ai21", "inception", "upstage"
    };

    public static async Task<Dictionary<string, CodexModelMetadata>> FetchAsync(
        HttpClient client, string providerUrl, IEnumerable<string> modelIds, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ModelsDevPricing.ApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return modelIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id,
            id => Match(document.RootElement, providerUrl, id), StringComparer.Ordinal);
    }

    internal static CodexModelMetadata Match(JsonElement root, string providerUrl, string modelId)
    {
        if (root.ValueKind != JsonValueKind.Object) return CodexModelMetadata.Unknown;
        var providers = root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object).ToList();
        if (Uri.TryCreate(providerUrl, UriKind.Absolute, out var endpoint))
        {
            var direct = providers.Where(p => MatchesEndpoint(p.Value, endpoint))
                .Select(p => ReadModel(p, modelId)).Where(m => m != null).ToList();
            if (direct.Count == 1) return direct[0]!;
            if (direct.Count > 1) return CodexModelMetadata.Unknown;
        }

        var slash = modelId.IndexOf('/');
        if (slash > 0 && OriginalPublishers.Contains(modelId[..slash]))
        {
            var publisher = providers.FirstOrDefault(p => p.Name == modelId[..slash]);
            if (publisher.Value.ValueKind == JsonValueKind.Object)
                return ReadModel(publisher, modelId[(slash + 1)..]) ?? CodexModelMetadata.Unknown;
        }
        var original = providers.Where(p => OriginalPublishers.Contains(p.Name))
            .Select(p => ReadModel(p, modelId)).Where(m => m != null).ToList();
        return original.Count == 1 ? original[0]! : CodexModelMetadata.Unknown;
    }

    private static bool MatchesEndpoint(JsonElement provider, Uri endpoint)
    {
        if (!provider.TryGetProperty("api", out var api) || api.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(api.GetString(), UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == endpoint.Port
            && (endpoint.AbsolutePath + "/").StartsWith(uri.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    private static CodexModelMetadata? ReadModel(JsonProperty provider, string id)
    {
        if (!provider.Value.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object
            || !models.TryGetProperty(id, out var model) || model.ValueKind != JsonValueKind.Object) return null;
        long? context = null;
        if (model.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Object
            && limit.TryGetProperty("context", out var window) && window.ValueKind == JsonValueKind.Number
            && window.TryGetInt64(out var count) && count > 0) context = count;
        var efforts = new List<ReasoningEffort>();
        var kinds = new List<string>();
        if (model.TryGetProperty("reasoning_options", out var options))
        {
            if (options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray()) ReadReasoningOption(option, efforts, kinds);
            else ReadReasoningOption(options, efforts, kinds);
        }
        return new(context, efforts.Distinct().Order().ToArray(), $"models.dev · {provider.Name}/{id}", string.Join(",", kinds.Distinct()))
        {
            Description = model.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                ? description.GetString() ?? "" : "",
            Capabilities = CodexModelCapabilities.FromModelsDev(model)
        };
    }

    private static void ReadReasoningOption(JsonElement option, List<ReasoningEffort> efforts, List<string> kinds)
    {
        if (option.ValueKind != JsonValueKind.Object || !option.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String) return;
        var kind = type.GetString() ?? "";
        kinds.Add(kind);
        if (kind != "effort" || !option.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return;
        foreach (var value in values.EnumerateArray())
            if (value.ValueKind == JsonValueKind.String && ReasoningEfforts.TryParse(value.GetString(), out var effort))
                efforts.Add(effort);
    }
}

internal sealed class CodexDiscoverySession : IDisposable
{
    private CancellationTokenSource? request;
    private readonly Dictionary<int, string> keys = new();
    private int? protocol;
    private bool disposed;
    public int Revision { get; private set; }
    public string? Key => protocol is int selected ? keys.GetValueOrDefault(selected) : null;

    public void SelectProtocol(int value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (protocol == value) return;
        CancelRequest();
        protocol = value;
    }

    public void SetKey(string value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (protocol is not int selected) throw new InvalidOperationException("Select a protocol before entering a key.");
        keys[selected] = value;
    }
    public void RejectKey()
    {
        if (protocol is int selected) keys.Remove(selected);
    }

    public (int Revision, CancellationToken Token) BeginRequest()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelRequest();
        request = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        return (Revision, request.Token);
    }

    public bool IsCurrent(int revision) => !disposed && revision == Revision;
    private void CancelRequest()
    {
        Revision++;
        request?.Cancel();
        request?.Dispose();
        request = null;
    }
    public void Dispose() { CancelRequest(); keys.Clear(); disposed = true; }
}
