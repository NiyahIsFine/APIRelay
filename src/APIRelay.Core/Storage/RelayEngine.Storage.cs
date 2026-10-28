using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace APIRelay;

internal sealed partial class RelayEngine
{
    private void LoadModelCosts(List<ModelCostConfig>? legacyModelCosts)
    {
        try
        {
            var persistedCosts = new List<ModelCostConfig>();
            if (File.Exists(modelCostsPath))
            {
                persistedCosts.AddRange(JsonSerializer.Deserialize<List<ModelCostConfig>>(File.ReadAllText(modelCostsPath)) ?? new List<ModelCostConfig>());
            }
            else if (legacyModelCosts?.Count > 0)
            {
                persistedCosts.AddRange(legacyModelCosts);
                AppendInternalLog("Migrating model costs from settings file to separate model-costs file.");
            }

            modelCosts.Clear();
            modelCosts.AddRange(MergeModelCosts(persistedCosts, CreateDefaultModelCosts()));
            SaveModelCosts();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppendInternalException("Failed to load model costs.", ex);
            AppendLog(GetText(TextId.Txt92, ex.Message), true);
            throw;
        }
    }

    private static List<ModelCostConfig> MergeModelCosts(
        IEnumerable<ModelCostConfig> persistedCosts,
        IEnumerable<ModelCostConfig> defaultCosts)
    {
        var persistedByName = persistedCosts
            .Where(cost => !string.IsNullOrWhiteSpace(cost.ModelName))
            .GroupBy(cost => cost.ModelName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.LastOrDefault(cost => cost.Overwrite) ?? group.Last(),
                StringComparer.OrdinalIgnoreCase);
        var defaultByName = defaultCosts
            .Where(cost => !string.IsNullOrWhiteSpace(cost.ModelName))
            .ToDictionary(cost => cost.ModelName, StringComparer.OrdinalIgnoreCase);
        var mergedCosts = new List<ModelCostConfig>();

        foreach (var defaultCost in defaultByName.Values)
        {
            if (persistedByName.TryGetValue(defaultCost.ModelName, out var persistedCost) && (persistedCost.Overwrite || persistedCost.ReferencePrice))
            {
                mergedCosts.Add(CloneModelCost(persistedCost));
            }
            else
            {
                var currentDefault = CloneModelCost(defaultCost);
                currentDefault.Overwrite = false;
                mergedCosts.Add(currentDefault);
            }
        }

        foreach (var persistedCost in persistedByName.Values)
        {
            if (!defaultByName.ContainsKey(persistedCost.ModelName))
            {
                mergedCosts.Add(CloneModelCost(persistedCost));
            }
        }

        return mergedCosts;
    }

    private static ModelCostConfig CloneModelCost(ModelCostConfig cost)
    {
        return new ModelCostConfig
        {
            ModelName = cost.ModelName.Trim(),
            InputCostPerMillion = cost.InputCostPerMillion,
            OutputCostPerMillion = cost.OutputCostPerMillion,
            CacheHitCostPerMillion = cost.CacheHitCostPerMillion,
            CacheCreationCostPerMillion = cost.CacheCreationCostPerMillion,
            ReferencePrice = cost.ReferencePrice,
            Overwrite = cost.Overwrite
        };
    }

    private void SaveModelCosts(IEnumerable<ModelCostConfig>? prices = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modelCostsPath)!);
            var costs = (prices ?? ModelCosts)
                .Where(cost => !string.IsNullOrWhiteSpace(cost.ModelName))
                .Select(cost => new ModelCostConfig
                {
                    ModelName = cost.ModelName.Trim(),
                    InputCostPerMillion = cost.InputCostPerMillion,
                    OutputCostPerMillion = cost.OutputCostPerMillion,
                    CacheHitCostPerMillion = cost.CacheHitCostPerMillion,
                    CacheCreationCostPerMillion = cost.CacheCreationCostPerMillion,
                    ReferencePrice = cost.ReferencePrice,
                    Overwrite = cost.Overwrite
                })
                .ToList();

            WriteAllTextAtomically(modelCostsPath, JsonSerializer.Serialize(costs, new JsonSerializerOptions { WriteIndented = true }));
            AppendInternalLog($"Model costs saved. ModelCosts={costs.Count}");
        }
        catch (Exception ex)
        {
            AppendInternalException("Failed to save model costs.", ex);
            AppendLog(GetText(TextId.Txt93, ex.Message), true);
            throw;
        }
    }

    private void AppendInternalLog(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (internalLogLock)
            {
                Directory.CreateDirectory(logsDirectory);
                File.AppendAllText(internalLogPath, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }
    }

    private void AppendInternalException(string message, Exception exception)
    {
        AppendInternalLog($"{message} Exception={exception.GetType().Name}; Message={exception.Message}; StackTrace={exception.StackTrace}");
    }

    private void EnsureInternalLogFile()
    {
        lock (internalLogLock)
        {
            Directory.CreateDirectory(logsDirectory);
            if (!File.Exists(internalLogPath))
            {
                File.WriteAllText(internalLogPath, string.Empty, Encoding.UTF8);
            }
        }
    }

    private static void WriteAllTextAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("A storage directory is required.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizePrefix(string value)
    {
        var normalized = value.Trim();
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private void MigrateLegacyProviderSettings(RelaySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProviderUrl))
        {
            return;
        }

        var providerType = string.Equals(settings.ProviderType, ProviderType.Anthropic.ToString(), StringComparison.OrdinalIgnoreCase)
            ? ProviderType.Anthropic
            : ProviderType.OpenAICompatible;
        var routeKind = providerType == ProviderType.Anthropic ? ApiRouteKind.AnthropicMessages : ApiRouteKind.ChatCompletions;

        providerConfigs[routeKind] = new ProviderEndpointConfig
        {
            RouteKind = routeKind,
            ProviderType = providerType,
            ProviderUrl = settings.ProviderUrl,
            ModelListUrl = BuildModelListUrl(routeKind, settings.ProviderUrl),
            AnthropicVersion = string.IsNullOrWhiteSpace(settings.AnthropicVersion) ? "2023-06-01" : settings.AnthropicVersion
        };

        if (providerType == ProviderType.OpenAICompatible)
        {
            providerConfigs[ApiRouteKind.Responses] = new ProviderEndpointConfig
            {
                RouteKind = ApiRouteKind.Responses,
                ProviderType = ProviderType.OpenAICompatible,
                ProviderUrl = settings.ProviderUrl,
                ModelListUrl = BuildModelListUrl(ApiRouteKind.Responses, settings.ProviderUrl),
                AnthropicVersion = "2023-06-01"
            };
        }
    }

    private void EnsureProviderConfigDefaults()
    {
        foreach (var config in CreateDefaultProviderConfigs())
        {
            if (!providerConfigs.ContainsKey(config.Key))
            {
                providerConfigs[config.Key] = config.Value;
            }
        }
    }

    private static Dictionary<ApiRouteKind, ProviderEndpointConfig> CreateDefaultProviderConfigs()
    {
        return new Dictionary<ApiRouteKind, ProviderEndpointConfig>
        {
            [ApiRouteKind.Responses] = new()
            {
                RouteKind = ApiRouteKind.Responses,
                ProviderType = ProviderType.OpenAICompatible,
                AnthropicVersion = "2023-06-01"
            },
            [ApiRouteKind.ChatCompletions] = new()
            {
                RouteKind = ApiRouteKind.ChatCompletions,
                ProviderType = ProviderType.OpenAICompatible,
                AnthropicVersion = "2023-06-01"
            },
            [ApiRouteKind.AnthropicMessages] = new()
            {
                RouteKind = ApiRouteKind.AnthropicMessages,
                ProviderType = ProviderType.Anthropic,
                AnthropicVersion = "2023-06-01"
            }
        };
    }

    private static string GetRouteKindDisplayName(ApiRouteKind routeKind)
    {
        return routeKind switch
        {
            ApiRouteKind.Responses => "Responses",
            ApiRouteKind.ChatCompletions => "Chat Completions",
            ApiRouteKind.AnthropicMessages => "Anthropic Messages",
            _ => routeKind.ToString()
        };
    }

    private static string GetRoutePathSegment(ApiRouteKind routeKind)
    {
        return routeKind switch
        {
            ApiRouteKind.Responses => "responses",
            ApiRouteKind.ChatCompletions => "compatible",
            ApiRouteKind.AnthropicMessages => "anthropic",
            _ => "compatible"
        };
    }

    private static string BuildModelListUrl(ApiRouteKind routeKind, string providerUrl)
    {
        var trimmedProviderUrl = providerUrl.Trim();
        if (!Uri.TryCreate(trimmedProviderUrl, UriKind.Absolute, out var providerUri)
            || (providerUri.Scheme != Uri.UriSchemeHttp && providerUri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var path = NormalizePathSlashes(providerUri.AbsolutePath).TrimEnd('/');
        path = RemoveRelayFromProtocolSegment(routeKind, path);
        var suffix = routeKind switch
        {
            ApiRouteKind.Responses => "/responses",
            ApiRouteKind.ChatCompletions => "/chat/completions",
            ApiRouteKind.AnthropicMessages => "/messages",
            _ => string.Empty
        };

        var basePath = path;
        if (!string.IsNullOrEmpty(suffix) && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            basePath = path[..^suffix.Length].TrimEnd('/');
        }

        var builder = new UriBuilder(providerUri)
        {
            Path = (string.IsNullOrEmpty(basePath) ? string.Empty : basePath) + "/models",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }

    private static string RemoveRelayFromProtocolSegment(ApiRouteKind routeKind, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !TryParseApiRouteKind(segments[0], out var toProtocol) || toProtocol != routeKind)
        {
            return path;
        }

        if (!TryParseApiRouteKind(segments[1], out _))
        {
            return path;
        }

        var keptSegments = new List<string>(segments.Length - 1) { segments[0] };
        keptSegments.AddRange(segments.Skip(2));
        return "/" + string.Join('/', keptSegments);
    }
}
