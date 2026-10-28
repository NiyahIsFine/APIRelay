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
    private const string ManagedModelAliasPrefix = "apirelay-v1";
    private static readonly byte[] ProviderSecretEntropy = Encoding.UTF8.GetBytes("APIRelay.ProviderSecrets.v1");
    private void LoadProviderApiKeys()
    {
        providerApiKeys.Clear();
        try
        {
            if (!File.Exists(secretsPath))
            {
                return;
            }

            var secrets = JsonSerializer.Deserialize<List<ProtectedProviderSecret>>(File.ReadAllText(secretsPath)) ?? new();
            foreach (var secret in secrets)
            {
                var protectedBytes = Convert.FromBase64String(secret.ProtectedValue);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, ProviderSecretEntropy, DataProtectionScope.CurrentUser);
                var apiKey = Encoding.UTF8.GetString(plainBytes);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    providerApiKeys[secret.Protocol] = apiKey;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
        {
            AppendInternalException("Failed to load protected provider credentials.", ex);
            providerApiKeys.Clear();
        }
    }

    private void SaveProviderApiKey(ApiRouteKind protocol, string apiKey)
    {
        var updated = new Dictionary<ApiRouteKind, string>(providerApiKeys);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            updated.Remove(protocol);
        }
        else
        {
            updated[protocol] = apiKey.Trim();
        }
        SaveProviderApiKeys(updated);
    }

    private void SaveProviderApiKeys(IReadOnlyDictionary<ApiRouteKind, string> updated)
    {
        var secrets = updated
            .OrderBy(item => item.Key)
            .Select(item => new ProtectedProviderSecret
            {
                Protocol = item.Key,
                ProtectedValue = Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(item.Value),
                    ProviderSecretEntropy,
                    DataProtectionScope.CurrentUser))
            })
            .ToList();
        WriteAllTextAtomically(secretsPath, JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true }));
        providerApiKeys.Clear();
        foreach (var entry in updated) providerApiKeys[entry.Key] = entry.Value;
    }

    private static string CreateManagedModelAlias(ApiRouteKind protocol, string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model ID cannot be empty.", nameof(modelId));
        }

        var protocolToken = protocol switch
        {
            ApiRouteKind.Responses => "r",
            ApiRouteKind.ChatCompletions => "c",
            ApiRouteKind.AnthropicMessages => "a",
            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };
        var encodedModelId = Convert.ToBase64String(Encoding.UTF8.GetBytes(modelId))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{ManagedModelAliasPrefix}:{protocolToken}:{encodedModelId}";
    }

    private static bool TryParseManagedModelAlias(string alias, out ApiRouteKind protocol, out string modelId)
    {
        protocol = default;
        modelId = string.Empty;

        var parts = alias.Split(':');
        if (parts.Length != 3 || !parts[0].Equals(ManagedModelAliasPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        protocol = parts[1] switch
        {
            "r" => ApiRouteKind.Responses,
            "c" => ApiRouteKind.ChatCompletions,
            "a" => ApiRouteKind.AnthropicMessages,
            _ => default
        };
        if (parts[1] is not ("r" or "c" or "a") || parts[2].Length == 0)
        {
            return false;
        }

        try
        {
            var base64 = parts[2].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            modelId = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return modelId.Length > 0 && CreateManagedModelAlias(protocol, modelId).Equals(alias, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            modelId = string.Empty;
            return false;
        }
    }

    private async Task<ModelDiscoveryResult> DiscoverModelsAsync(ProviderEndpointConfig endpoint, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var modelListUrl = GetModelListUrl(endpoint);
            using var request = new HttpRequestMessage(HttpMethod.Get, modelListUrl);
            if (endpoint.ProviderType == ProviderType.Anthropic)
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", string.IsNullOrWhiteSpace(endpoint.AnthropicVersion) ? "2023-06-01" : endpoint.AnthropicVersion);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ModelDiscoveryResult(new(), $"HTTP {(int)response.StatusCode}");
            }

            var models = ParseModelListResponse(body)
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .GroupBy(model => model.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return models.Count == 0
                ? new ModelDiscoveryResult(models, "The provider returned no recognizable models.")
                : new ModelDiscoveryResult(models, string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return new ModelDiscoveryResult(new(), "Model list request failed.");
        }
    }

    private static string ReadProviderError(byte[] body)
    {
        if (body.Length == 0)
        {
            return "Empty response";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? "Unknown error";
                }

                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? "Unknown error";
                }
            }

            if (root.TryGetProperty("message", out var rootMessage))
            {
                return rootMessage.GetString() ?? "Unknown error";
            }
        }
        catch (JsonException)
        {
        }

        var text = Encoding.UTF8.GetString(body).Trim();
        return text.Length > 500 ? text[..500] : text;
    }

    internal sealed record ModelDiscoveryResult(List<ModelListItem> Models, string Error);

    private void RecoverManagedToolConfigurations()
    {
        RestoreManagedToolConfigurations();
    }

    private void ApplyEnabledManagedToolConfigurations(Uri localUri)
    {
        try
        {
            BuildManagedRuntimeRouteSnapshot();
            if (toolConfiguration.Claude.Enabled)
            {
                ApplyManagedConfiguration(ManagedToolKind.Claude, GetClaudeSettingsPath(), BuildClaudeSettings(localUri));
            }
            if (toolConfiguration.Codex.Enabled)
            {
                ApplyManagedConfiguration(ManagedToolKind.Codex, GetCodexSettingsPath(), BuildCodexSettings(localUri), BuildCodexCatalogContent(toolConfiguration.Codex));
            }
        }
        catch
        {
            RestoreManagedToolConfigurations();
            throw;
        }
    }

    private void BuildManagedRuntimeRouteSnapshot()
    {
        var routes = new Dictionary<string, ManagedRuntimeRoute>(StringComparer.Ordinal);
        var aliases = new List<string>();
        if (toolConfiguration.Claude.Enabled)
        {
            aliases.Add(toolConfiguration.Claude.HaikuModelAlias);
            aliases.Add(toolConfiguration.Claude.SonnetModelAlias);
            aliases.Add(toolConfiguration.Claude.OpusModelAlias);
        }
        if (toolConfiguration.Codex.Enabled)
        {
            aliases.AddRange(toolConfiguration.Codex.Candidates.Select(c => c.Alias));
        }

        foreach (var alias in aliases.Distinct(StringComparer.Ordinal))
        {
            if (!TryParseManagedModelAlias(alias, out var protocol, out var modelId))
            {
                throw new InvalidOperationException($"Managed model alias is invalid: {alias}");
            }

            var inClaude = toolConfiguration.Claude.Enabled && registeredModels.Any(model => model.Protocol == protocol && StringComparer.Ordinal.Equals(model.ModelId, modelId));
            var inCodex = toolConfiguration.Codex.Enabled && toolConfiguration.Codex.Candidates.Any(model => model.Protocol == protocol && StringComparer.Ordinal.Equals(model.ModelId, modelId));
            if (!inClaude && !inCodex)
                throw new InvalidOperationException($"Managed model is not configured: {GetRouteKindDisplayName(protocol)} / {modelId}");
            if (!providerConfigs.TryGetValue(protocol, out var endpoint) || string.IsNullOrWhiteSpace(endpoint.ProviderUrl))
            {
                throw new InvalidOperationException($"Provider URL is not configured for {GetRouteKindDisplayName(protocol)}.");
            }
            if (!providerApiKeys.TryGetValue(protocol, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException($"Provider API key is not stored for {GetRouteKindDisplayName(protocol)}.");
            }

            var providerUrl = ValidateProviderUrl(protocol, endpoint.ProviderUrl);
            routes[alias] = new ManagedRuntimeRoute(
                alias,
                protocol,
                modelId,
                new Uri(providerUrl),
                apiKey,
                endpoint.AnthropicVersion)
            {
                ClaudeAllowed = inClaude,
                CodexAllowed = inCodex,
                CodexEfforts = inCodex ? toolConfiguration.Codex.Candidates.First(c => c.Alias == alias).SupportedEfforts.ToArray() : null
            };
        }
        managedRuntimeRoutes = routes;
    }

    private bool TryResolveManagedRoute(byte[] requestBody, RelayRoute toolRoute, out RelayRoute resolvedRoute, out string error)
    {
        resolvedRoute = toolRoute;
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(requestBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("model", out var modelElement)
                || modelElement.ValueKind != JsonValueKind.String)
            {
                error = "Managed tool request must contain a model alias.";
                return false;
            }

            var alias = modelElement.GetString() ?? string.Empty;
            if (!TryParseManagedModelAlias(alias, out _, out _))
            {
                error = "Managed tool model alias is malformed.";
                return false;
            }
            if (!managedRuntimeRoutes.TryGetValue(alias, out var managedRoute))
            {
                error = "Managed tool model is not active or registered.";
                return false;
            }

            if (toolRoute.FromProtocol == ApiRouteKind.Responses ? !managedRoute.CodexAllowed : !managedRoute.ClaudeAllowed)
            {
                error = "This model is not enabled for the requesting tool.";
                return false;
            }

            resolvedRoute = new RelayRoute(managedRoute.Protocol, toolRoute.FromProtocol, true, managedRoute);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Managed tool request JSON is invalid: {ex.Message}";
            return false;
        }
    }

    private void ApplyManagedConfiguration(ManagedToolKind tool, string targetPath, byte[] managedContent, byte[]? catalogContent = null)
    {
        Directory.CreateDirectory(managedConfigDirectory);
        var markerPath = GetManagedMarkerPath(tool);
        ManagedConfigurationApplyState? state = null;
        if (File.Exists(markerPath))
        {
            state = JsonSerializer.Deserialize<ManagedConfigurationApplyState>(File.ReadAllText(markerPath));
            if (state == null || string.IsNullOrWhiteSpace(state.TargetPath) || string.IsNullOrWhiteSpace(state.BackupPath))
            {
                throw new InvalidOperationException("Managed configuration marker is invalid.");
            }

            if (state.RestoreCompleted)
            {
                File.Delete(markerPath);
                state = null;
            }
            else if (!File.Exists(state.BackupPath) && state.TargetOriginallyExisted)
            {
                throw new InvalidOperationException("Managed configuration backup is missing; refusing to replace it.");
            }
        }

        if (state == null)
        {
            var backupPath = Path.Combine(managedConfigDirectory, $"{tool.ToString().ToLowerInvariant()}.backup");
            var targetExisted = File.Exists(targetPath);
            if (targetExisted)
            {
                File.Copy(targetPath, backupPath, true);
            }
            else if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            state = new ManagedConfigurationApplyState
            {
                Tool = tool,
                TargetPath = targetPath,
                BackupPath = backupPath,
                TargetOriginallyExisted = targetExisted,
                AppliedAtUtc = DateTime.UtcNow
            };
        }

        if (catalogContent != null && state.CatalogPath == null)
        {
            state.CatalogPath = Path.Combine(Path.GetDirectoryName(targetPath)!, "apirelay-models.json");
            state.CatalogBackupPath = Path.Combine(managedConfigDirectory, "codex-catalog.backup");
            state.CatalogOriginallyExisted = File.Exists(state.CatalogPath);
            if (state.CatalogOriginallyExisted) File.Copy(state.CatalogPath, state.CatalogBackupPath, true);
        }
        WriteAllTextAtomically(markerPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (catalogContent != null) WriteAllBytesAtomically(state.CatalogPath!, catalogContent);
        WriteAllBytesAtomically(targetPath, managedContent);
    }

    private bool RestoreManagedToolConfigurations()
    {
        var restored = true;
        foreach (var tool in Enum.GetValues<ManagedToolKind>())
        {
            var markerPath = GetManagedMarkerPath(tool);
            if (File.Exists(markerPath))
            {
                restored &= RestoreManagedToolConfiguration(markerPath);
            }
        }
        return restored;
    }

    private bool RestoreManagedToolConfiguration(string markerPath)
    {
        try
        {
            var state = JsonSerializer.Deserialize<ManagedConfigurationApplyState>(File.ReadAllText(markerPath));
            if (state == null || string.IsNullOrWhiteSpace(state.TargetPath))
            {
                throw new InvalidOperationException("Managed configuration marker is invalid.");
            }

            if (!state.RestoreCompleted)
            {
                RestoreManagedConfigurationFiles(state);
                state.RestoreCompleted = true;
                WriteAllTextAtomically(markerPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (File.Exists(state.BackupPath))
            {
                File.Delete(state.BackupPath);
            }
            if (File.Exists(state.CatalogBackupPath)) File.Delete(state.CatalogBackupPath);
            File.Delete(markerPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            AppendInternalException("Failed to restore managed tool configuration.", ex);
            return false;
        }
    }

    internal static void RestoreManagedConfigurationFiles(ManagedConfigurationApplyState state)
    {
        if (state.CatalogOriginallyExisted && !File.Exists(state.CatalogBackupPath))
            throw new FileNotFoundException("Managed catalog backup is missing.");
        if (state.TargetOriginallyExisted)
        {
            if (!File.Exists(state.BackupPath))
            {
                throw new FileNotFoundException("Managed configuration backup is missing.", state.BackupPath);
            }
            WriteAllBytesAtomically(state.TargetPath, File.ReadAllBytes(state.BackupPath));
        }
        else if (File.Exists(state.TargetPath))
        {
            File.Delete(state.TargetPath);
        }
        if (state.CatalogPath != null)
        {
            if (state.CatalogOriginallyExisted)
            {
                if (!File.Exists(state.CatalogBackupPath)) throw new FileNotFoundException("Managed catalog backup is missing.");
                WriteAllBytesAtomically(state.CatalogPath, File.ReadAllBytes(state.CatalogBackupPath));
            }
            else if (File.Exists(state.CatalogPath)) File.Delete(state.CatalogPath);
        }
    }

    private byte[] BuildClaudeSettings(Uri localUri)
    {
        var path = GetClaudeSettingsPath();
        var existingContent = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        return BuildClaudeSettingsContent(existingContent, localUri, toolConfiguration.Claude);
    }

    private static byte[] BuildClaudeSettingsContent(string existingContent, Uri localUri, ClaudeToolSettings settings)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existingContent) ? new JsonObject() : JsonNode.Parse(existingContent) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Claude settings JSON is invalid: {ex.Message}", ex);
        }

        var env = root["env"] as JsonObject ?? new JsonObject();
        root["env"] = env;
        env["ANTHROPIC_BASE_URL"] = BuildLocalToolBaseUrl(localUri, "claude");
        env["ANTHROPIC_AUTH_TOKEN"] = "APIRELAY";
        SetClaudeModelEnvironment(env, "HAIKU", settings.HaikuModelAlias);
        SetClaudeModelEnvironment(env, "SONNET", settings.SonnetModelAlias);
        SetClaudeModelEnvironment(env, "OPUS", settings.OpusModelAlias);
        env["ENABLE_TOOL_SEARCH"] = settings.EnableToolSearch ? "true" : "false";
        env["CLAUDE_CODE_EFFORT_LEVEL"] = settings.UseMaximumEffort ? "max" : "high";
        return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private byte[] BuildCodexSettings(Uri localUri)
    {
        var path = GetCodexSettingsPath();
        var existingContent = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        return BuildCodexSettingsContent(existingContent, localUri, toolConfiguration.Codex);
    }

    internal static byte[] BuildCodexSettingsContent(string existingContent, Uri localUri, CodexToolSettings settings)
    {
        TomlTable root;
        if (!string.IsNullOrWhiteSpace(existingContent))
        {
            root = TomlSerializer.Deserialize<TomlTable>(existingContent) ?? new TomlTable();
        }
        else
        {
            root = new TomlTable();
        }

        root["model_provider"] = "apirelay";
        root["model"] = settings.ModelAlias;
        root["model_reasoning_effort"] = "high";
        SetOptionalTomlInteger(root, "model_context_window", settings.ModelContextWindow);
        SetOptionalTomlInteger(root, "model_auto_compact_token_limit", settings.ModelAutoCompactTokenLimit);
        root["model_catalog_json"] = GetCodexCatalogPath();
        var providers = root.TryGetValue("model_providers", out var providersValue) && providersValue is TomlTable existingProviders
            ? existingProviders
            : new TomlTable();
        root["model_providers"] = providers;
        var relayProvider = new TomlTable();
        providers["apirelay"] = relayProvider;
        relayProvider["name"] = "APIRelay";
        relayProvider["wire_api"] = "responses";
        relayProvider["requires_openai_auth"] = settings.PreserveAccountLogin;
        relayProvider["experimental_bearer_token"] = "APIRELAY";
        relayProvider["base_url"] = BuildLocalToolBaseUrl(localUri, "codex/v1");
        return Encoding.UTF8.GetBytes(TomlSerializer.Serialize(root));
    }

    private static void SetClaudeModelEnvironment(JsonObject env, string tier, string alias)
    {
        env[$"ANTHROPIC_DEFAULT_{tier}_MODEL"] = alias;
        env[$"ANTHROPIC_DEFAULT_{tier}_MODEL_NAME"] = TryParseManagedModelAlias(alias, out _, out var modelId) ? modelId : alias;
    }

    private static string BuildLocalToolBaseUrl(Uri localUri, string suffix)
    {
        return localUri.ToString().TrimEnd('/') + "/" + suffix.Trim('/');
    }

    private string GetManagedMarkerPath(ManagedToolKind tool) => Path.Combine(managedConfigDirectory, $"{tool.ToString().ToLowerInvariant()}.applied.json");
    private static string GetClaudeSettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    private static string GetCodexSettingsPath() => Path.Combine(GetCodexHome(), "config.toml");

    private static void WriteAllBytesAtomically(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("A storage directory is required.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, content);
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
}
