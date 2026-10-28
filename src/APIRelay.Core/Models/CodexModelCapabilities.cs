using System.Text.Json;
using System.Text.Json.Nodes;
using static APIRelay.RelayEngine;

namespace APIRelay;

internal sealed class CodexModelCapabilities
{
    public List<string>? InputModalities { get; set; }
    public bool? SupportsExperimentalContext { get; set; }
    public bool? SupportsImageDetailOriginal { get; set; }
    public bool? SupportsParallelToolCalls { get; set; }
    public bool? SupportsReasoningEffortUpdates { get; set; }
    public bool? SupportsReasoningSummaries { get; set; }
    public bool? SupportsReasoningSummaryParameter { get; set; }
    public bool? SupportsSearchTool { get; set; }
    public bool? SupportVerbosity { get; set; }

    public static IReadOnlyList<string> SupportedInputModalities { get; } = ["text", "image", "audio"];

    public static IReadOnlyList<CodexCapabilityFlag> Flags { get; } =
    [
        new("supports_experimental_context", c => c.SupportsExperimentalContext, (c, v) => c.SupportsExperimentalContext = v),
        new("supports_image_detail_original", c => c.SupportsImageDetailOriginal, (c, v) => c.SupportsImageDetailOriginal = v),
        new("supports_parallel_tool_calls", c => c.SupportsParallelToolCalls, (c, v) => c.SupportsParallelToolCalls = v),
        new("supports_reasoning_effort_updates", c => c.SupportsReasoningEffortUpdates, (c, v) => c.SupportsReasoningEffortUpdates = v),
        new("supports_reasoning_summaries", c => c.SupportsReasoningSummaries, (c, v) => c.SupportsReasoningSummaries = v),
        new("supports_reasoning_summary_parameter", c => c.SupportsReasoningSummaryParameter, (c, v) => c.SupportsReasoningSummaryParameter = v),
        new("supports_search_tool", c => c.SupportsSearchTool, (c, v) => c.SupportsSearchTool = v),
        new("support_verbosity", c => c.SupportVerbosity, (c, v) => c.SupportVerbosity = v)
    ];

    public CodexModelCapabilities Clone()
    {
        var copy = new CodexModelCapabilities
        {
            InputModalities = InputModalities?.ToList()
        };
        foreach (var flag in Flags) flag.Write(copy, flag.Read(this));
        return copy;
    }

    public void Normalize()
    {
        InputModalities = NormalizeModalities(InputModalities);
    }

    internal static CodexModelCapabilities FromModelsDev(JsonElement model)
    {
        var capabilities = new CodexModelCapabilities();
        if (model.TryGetProperty("input_modalities", out var explicitModalities))
            capabilities.InputModalities = ReadModalities(explicitModalities);
        if (capabilities.InputModalities == null &&
            model.TryGetProperty("modalities", out var modalities) && modalities.ValueKind == JsonValueKind.Object &&
            modalities.TryGetProperty("input", out var inputs))
            capabilities.InputModalities = ReadModalities(inputs);

        return capabilities;
    }

    internal static CodexModelCapabilities FromOfficialModel(JsonElement model)
    {
        var capabilities = new CodexModelCapabilities();
        if (model.TryGetProperty("input_modalities", out var modalities))
            capabilities.InputModalities = ReadModalities(modalities);
        foreach (var flag in Flags) flag.Write(capabilities, ReadBoolean(model, flag.Key));
        if (!model.TryGetProperty("supports_parallel_tool_calls", out _))
            capabilities.SupportsParallelToolCalls = true;
        if (!model.TryGetProperty("supports_reasoning_summaries", out _))
            capabilities.SupportsReasoningSummaries = true;
        return capabilities;
    }

    internal static CodexModelCapabilities ResolveAutomatic(CodexCandidate candidate,
        CodexOfficialModelCatalog? officialCatalog = null)
    {
        var capabilities = (officialCatalog ?? CodexOfficialModelCatalog.Current).Match(candidate.ModelId) ?? new();
        capabilities.InputModalities ??= candidate.Capabilities?.InputModalities?.ToList();
        capabilities.Normalize();
        return capabilities;
    }

    internal static JsonObject BuildCatalogFields(CodexCandidate candidate,
        CodexOfficialModelCatalog? officialCatalog = null)
    {
        var capabilities = ResolveAutomatic(candidate, officialCatalog);
        var nativeResponses = candidate.Protocol == ApiRouteKind.Responses;
        var modalities = nativeResponses ? capabilities.InputModalities ?? ["text"] : ["text"];
        var acceptsImages = modalities.Contains("image", StringComparer.Ordinal);
        var fields = new JsonObject
        {
            ["input_modalities"] = new JsonArray(modalities.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
            ["web_search_tool_type"] = acceptsImages ? "text_and_image" : "text"
        };
        foreach (var flag in Flags)
        {
            var enabled = flag.Read(capabilities) == true &&
                (nativeResponses || flag.Key == "supports_parallel_tool_calls");
            if (flag.Key == "supports_image_detail_original") enabled &= acceptsImages;
            fields[flag.Key] = enabled;
        }
        fields["parallel_tool_calls"] = fields["supports_parallel_tool_calls"]!.GetValue<bool>();
        fields["default_reasoning_summary"] = fields["supports_reasoning_summary_parameter"]!.GetValue<bool>() ? "auto" : "none";
        fields["default_verbosity"] = null;
        return fields;
    }

    private static bool? ReadBoolean(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        } : null;

    private static List<string>? ReadModalities(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? NormalizeModalities(element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!))
        : null;

    private static List<string>? NormalizeModalities(IEnumerable<string>? values)
    {
        var modalities = values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().ToLowerInvariant())
            .Where(value => SupportedInputModalities.Contains(value)).Distinct(StringComparer.Ordinal).ToList();
        return modalities is { Count: > 0 } ? modalities : null;
    }

}

internal sealed record CodexCapabilityFlag(
    string Key,
    Func<CodexModelCapabilities, bool?> Read,
    Action<CodexModelCapabilities, bool?> Write);
