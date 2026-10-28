using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn.Model;

namespace APIRelay;

internal sealed partial class RelayEngine
{
    private static string GetCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private static string GetCodexCatalogPath() => Path.Combine(GetCodexHome(), "apirelay-models.json");

    private static void SetOptionalTomlInteger(TomlTable table, string key, long? value)
    {
        if (value is > 0) table[key] = value.Value;
        else table.Remove(key);
    }

    internal static byte[] BuildCodexCatalogContent(CodexToolSettings settings,
        CodexOfficialModelCatalog? officialCatalog = null)
    {
        var models = new JsonArray();
        foreach (var candidate in settings.Candidates)
        {
            var levels = new JsonArray();
            foreach (var effort in candidate.SupportedEfforts)
                levels.Add(new JsonObject { ["effort"] = effort.ToWire(), ["description"] = effort.ToWire() });
            var model = new JsonObject
            {
                ["slug"] = candidate.Alias,
                ["display_name"] = $"{(string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.ModelId : candidate.DisplayName)} · {GetRouteKindDisplayName(candidate.Protocol)}",
                ["description"] = string.IsNullOrWhiteSpace(candidate.ModelDescription) ? candidate.ModelId : candidate.ModelDescription,
                ["default_reasoning_level"] = candidate.ResolveDefaultEffort()?.ToWire(),
                ["supported_reasoning_levels"] = levels,
                ["shell_type"] = "shell_command",
                ["visibility"] = "list",
                ["supported_in_api"] = true,
                ["priority"] = models.Count,
                ["base_instructions"] = "You are a coding assistant. Use the available tools to complete the user's request.",
                ["apply_patch_tool_type"] = "freeform",
                ["truncation_policy"] = new JsonObject { ["mode"] = "bytes", ["limit"] = 10000 },
                ["context_window"] = candidate.ContextWindow,
                ["max_context_window"] = candidate.ContextWindow,
                ["effective_context_window_percent"] = 100,
                ["experimental_supported_tools"] = new JsonArray()
            };
            foreach (var field in CodexModelCapabilities.BuildCatalogFields(candidate, officialCatalog))
                model[field.Key] = field.Value?.DeepClone();
            models.Add(model);
        }
        return Encoding.UTF8.GetBytes(new JsonObject { ["models"] = models }.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    internal static byte[] ApplyCodexReasoning(byte[] original, byte[] converted, ApiRouteKind target, IReadOnlyCollection<ReasoningEffort> supported)
    {
        var source = JsonNode.Parse(original) as JsonObject;
        var body = JsonNode.Parse(converted) as JsonObject;
        if (source == null || body == null) return converted;
        var raw = (source["reasoning"] as JsonObject)?["effort"];
        var requested = raw is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        var valid = ReasoningEfforts.TryParse(requested, out var effort) && supported.Contains(effort);
        body.Remove("reasoning_effort");
        if (body["reasoning"] is JsonObject reasoning)
        {
            reasoning.Remove("effort");
            if (reasoning.Count == 0) body.Remove("reasoning");
        }
        if (target == ApiRouteKind.Responses && valid)
        {
            var options = body["reasoning"] as JsonObject ?? new JsonObject();
            body["reasoning"] = options;
            options["effort"] = effort.ToWire();
        }
        else if (target == ApiRouteKind.ChatCompletions && valid) body["reasoning_effort"] = effort.ToWire();
        else if (target == ApiRouteKind.AnthropicMessages)
        {
            var output = body["output_config"] as JsonObject;
            output?.Remove("effort");
            if (valid)
            {
                output ??= new JsonObject();
                body["output_config"] = output;
                output["effort"] = effort.ToWire();
            }
        }
        return Encoding.UTF8.GetBytes(body.ToJsonString());
    }
}
