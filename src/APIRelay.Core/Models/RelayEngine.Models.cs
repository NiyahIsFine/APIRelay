using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APIRelay
{
    internal enum AppLanguage
    {
        English,
        Chinese
    }

    internal sealed partial class RelayEngine
    {
        private sealed record UsageInfo(int PromptTokens, int CompletionTokens, int CachedTokens, int CacheCreationTokens, int TotalTokens, string Model, bool CacheTokensSeparateFromInput)
        {
            public static UsageInfo Empty { get; } = new(0, 0, 0, 0, 0, string.Empty, false);
        }

        private sealed class StreamingProgressLogger
        {
            private readonly RelayEngine owner;
            private readonly string requestId;
            private readonly string mode;
            private readonly Stopwatch stopwatch;
            private long lastLogMs;
            private long lastLogProviderBytes;

            public StreamingProgressLogger(RelayEngine owner, string requestId, string mode, Stopwatch stopwatch)
            {
                this.owner = owner;
                this.requestId = requestId;
                this.mode = mode;
                this.stopwatch = stopwatch;
            }

            public void Report(long providerBytes, long clientBytes, bool force)
            {
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                var elapsedSinceLastLogMs = elapsedMs - lastLogMs;
                var bytesSinceLastLog = providerBytes - lastLogProviderBytes;
                if (!force && elapsedSinceLastLogMs < StreamingProgressLogIntervalMs && bytesSinceLastLog < StreamingProgressLogBytes)
                {
                    return;
                }

                lastLogMs = elapsedMs;
                lastLogProviderBytes = providerBytes;
                owner.AppendInternalLog($"Request {requestId} streaming progress. Mode={mode}; ProviderBytes={providerBytes}; ClientBytes={clientBytes}; SinceLastLogMs={elapsedSinceLastLogMs}; ElapsedMs={elapsedMs}");
            }
        }

        internal sealed class RequestRecord
        {
            public DateTime Timestamp { get; set; }
            public string Model { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public int StatusCode { get; set; }
            public int PromptTokens { get; set; }
            public int CompletionTokens { get; set; }
            public int CachedTokens { get; set; }
            public int CacheCreationTokens { get; set; }
            public bool CacheTokensSeparateFromInput { get; set; }
            public int TotalTokens { get; set; }
            public decimal? Cost { get; set; }
            public long ElapsedMs { get; set; }
            public long FirstResponseMs { get; set; }
        }

        private sealed record RelayConfig(Uri LocalUri);

        private sealed record RelayRoute(
            ApiRouteKind ToProtocol,
            ApiRouteKind FromProtocol,
            bool IsManagedTool = false,
            ManagedRuntimeRoute? ManagedRoute = null);

        private sealed record ClientResponseBody(byte[] Body, string? ContentType);

        private sealed record StreamingRelayResult(long FirstByteMs, UsageInfo Usage, long ProviderBytes, long ClientBytes);

        private sealed record ProtocolRequest(
            string Model,
            JsonElement Messages,
            JsonElement? System,
            JsonElement? Input,
            JsonElement? Tools,
            int MaxTokens,
            bool? Stream,
            JsonElement? Temperature,
            JsonElement? TopP);

        private sealed record ProtocolResponse(string Id, string Model, string Content, string FinishReason, UsageInfo Usage, string? Error = null);

        internal sealed record ModelListItem(string Id, string DisplayName, long? Created);

        private sealed class StreamingProtocolConversionState
        {
            private readonly Dictionary<int, ToolCallStreamState> toolCallsByAnthropicIndex = new();

            public StreamingProtocolConversionState(string model)
            {
                Model = model;
            }

            public string Id { get; set; } = string.Empty;
            public string Model { get; set; }
            public bool Created { get; set; }
            public bool OpenAiRoleSent { get; set; }
            public string FinishReason { get; set; } = "stop";
            public string? ErrorMessage { get; set; }
            public UsageInfo Usage { get; set; } = UsageInfo.Empty;
            public string ResponsesTextItemId { get; } = "msg_" + Guid.NewGuid().ToString("N");
            public StringBuilder ResponsesText { get; } = new();
            public bool ResponsesTextStarted { get; set; }
            public IEnumerable<ToolCallStreamState> ToolCalls => toolCallsByAnthropicIndex.Values.OrderBy(toolCall => toolCall.OpenAiIndex);

            public ToolCallStreamState GetOrCreateToolCall(int anthropicIndex, string id, string name)
            {
                if (toolCallsByAnthropicIndex.TryGetValue(anthropicIndex, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        existing.Id = id;
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        existing.Name = name;
                    }

                    return existing;
                }

                var toolCall = new ToolCallStreamState(toolCallsByAnthropicIndex.Count, id, name);
                toolCallsByAnthropicIndex[anthropicIndex] = toolCall;
                return toolCall;
            }

            public bool TryGetToolCall(int anthropicIndex, out ToolCallStreamState toolCall)
            {
                return toolCallsByAnthropicIndex.TryGetValue(anthropicIndex, out toolCall!);
            }
        }

        private sealed class ToolCallStreamState(int openAiIndex, string id, string name)
        {
            public int OpenAiIndex { get; } = openAiIndex;
            public string Id { get; set; } = id;
            public string Name { get; set; } = name;
            public bool HasArgumentsDelta { get; set; }
            public string ResponsesItemId { get; } = "fc_" + Guid.NewGuid().ToString("N");
            public StringBuilder Arguments { get; } = new();
        }

        private sealed class StreamingUsageAccumulator
        {
            private readonly Decoder decoder;
            private readonly char[] charBuffer = new char[81920];
            private readonly StringBuilder lineBuffer = new();
            private readonly StringBuilder eventData = new();

            public StreamingUsageAccumulator(Encoding encoding)
            {
                decoder = encoding.GetDecoder();
            }

            public UsageInfo Usage { get; private set; } = UsageInfo.Empty;

            public void AppendBytes(byte[] buffer, int length)
            {
                var completed = false;
                var bytesUsedTotal = 0;

                while (!completed && bytesUsedTotal < length)
                {
                    decoder.Convert(
                        buffer,
                        bytesUsedTotal,
                        length - bytesUsedTotal,
                        charBuffer,
                        0,
                        charBuffer.Length,
                        false,
                        out var bytesUsed,
                        out var charsUsed,
                        out completed);

                    bytesUsedTotal += bytesUsed;
                    AppendChars(charBuffer.AsSpan(0, charsUsed));
                }
            }

            public void AppendLine(string line)
            {
                ProcessLine(line.TrimEnd('\r'));
            }

            public void Complete()
            {
                if (lineBuffer.Length > 0)
                {
                    ProcessLine(lineBuffer.ToString().TrimEnd('\r'));
                    lineBuffer.Clear();
                }

                FlushEventData();
            }

            private void AppendChars(ReadOnlySpan<char> chars)
            {
                foreach (var character in chars)
                {
                    if (character == '\n')
                    {
                        ProcessLine(lineBuffer.ToString().TrimEnd('\r'));
                        lineBuffer.Clear();
                        continue;
                    }

                    lineBuffer.Append(character);
                }
            }

            private void ProcessLine(string line)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.Length == 0)
                {
                    FlushEventData();
                    return;
                }

                if (!trimmedLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                eventData.AppendLine(trimmedLine[5..].TrimStart());
            }

            private void FlushEventData()
            {
                if (eventData.Length == 0)
                {
                    return;
                }

                Usage = MergeUsage(Usage, ExtractUsageFromEventData(eventData.ToString()));
                eventData.Clear();
            }
        }

        private sealed class StreamingProtocolTraceAccumulator
        {
            private readonly Decoder decoder;
            private readonly Action<string> appendEvent;
            private readonly char[] charBuffer = new char[81920];
            private readonly StringBuilder lineBuffer = new();
            private readonly StringBuilder eventData = new();
            private readonly StringBuilder rawData = new();

            public StreamingProtocolTraceAccumulator(Encoding encoding, Action<string> appendEvent)
            {
                decoder = encoding.GetDecoder();
                this.appendEvent = appendEvent;
            }

            public void AppendBytes(byte[] buffer, int length)
            {
                var completed = false;
                var bytesUsedTotal = 0;

                while (!completed && bytesUsedTotal < length)
                {
                    decoder.Convert(
                        buffer,
                        bytesUsedTotal,
                        length - bytesUsedTotal,
                        charBuffer,
                        0,
                        charBuffer.Length,
                        false,
                        out var bytesUsed,
                        out var charsUsed,
                        out completed);

                    bytesUsedTotal += bytesUsed;
                    AppendChars(charBuffer.AsSpan(0, charsUsed));
                }
            }

            public void Complete()
            {
                if (lineBuffer.Length > 0)
                {
                    ProcessLine(lineBuffer.ToString().TrimEnd('\r'));
                    lineBuffer.Clear();
                }

                FlushEventData();
            }

            private void AppendChars(ReadOnlySpan<char> chars)
            {
                foreach (var character in chars)
                {
                    if (character == '\n')
                    {
                        ProcessLine(lineBuffer.ToString().TrimEnd('\r'));
                        lineBuffer.Clear();
                        continue;
                    }

                    lineBuffer.Append(character);
                }
            }

            private void ProcessLine(string line)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.Length == 0)
                {
                    FlushEventData();
                    return;
                }

                if (trimmedLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    eventData.AppendLine(trimmedLine[5..].TrimStart());
                    return;
                }

                rawData.AppendLine(line);
            }

            private void FlushEventData()
            {
                if (eventData.Length > 0)
                {
                    appendEvent(eventData.ToString());
                    eventData.Clear();
                }

                if (rawData.Length == 0)
                {
                    return;
                }

                appendEvent(rawData.ToString());
                rawData.Clear();
            }
        }

        internal enum ApiRouteKind
        {
            Responses,
            ChatCompletions,
            AnthropicMessages
        }

        internal enum ProviderType
        {
            OpenAICompatible,
            Anthropic
        }

        internal enum ManagedToolKind
        {
            Claude,
            Codex
        }

        private enum ProtocolTraceDirection
        {
            ClientToTool,
            ToolToServer,
            ServerToTool,
            ToolToClient
        }

        internal sealed class RelaySettings
        {
            public string LocalUrl { get; set; } = string.Empty;
            public string Language { get; set; } = AppLanguage.English.ToString();
            public string ProviderUrl { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string ProviderType { get; set; } = string.Empty;
            public string AnthropicVersion { get; set; } = "2023-06-01";
            public ApiRouteKind RouteHelperServerProtocol { get; set; } = ApiRouteKind.ChatCompletions;
            public ApiRouteKind? RouteHelperToolProtocol { get; set; }
            public bool AutoStartRelay { get; set; }
            public bool RelayShouldRun { get; set; }
            public bool ResumeOnLaunch { get; set; } = true;
            public bool ProtocolTraceVisible { get; set; }
            public bool UsageBubbleVisible { get; set; } = true;
            public string Theme { get; set; } = "Dark";
            public int? UsageBubbleLocationX { get; set; }
            public int? UsageBubbleLocationY { get; set; }
            public List<ProviderEndpointConfig> ProviderConfigs { get; set; } = new();
            public List<RegisteredModelConfig> RegisteredModels { get; set; } = new();
            public ToolConfigurationSettings ToolConfiguration { get; set; } = new();
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<ModelCostConfig>? ModelCosts { get; set; }
        }

        internal sealed class RegisteredModelConfig
        {
            public ApiRouteKind Protocol { get; set; }
            public string ModelId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public DateTime RegisteredAtUtc { get; set; }
        }

        private sealed class RegisteredModelIdentityComparer : IEqualityComparer<(ApiRouteKind Protocol, string ModelId)>
        {
            public bool Equals((ApiRouteKind Protocol, string ModelId) left, (ApiRouteKind Protocol, string ModelId) right)
            {
                return left.Protocol == right.Protocol && StringComparer.Ordinal.Equals(left.ModelId, right.ModelId);
            }

            public int GetHashCode((ApiRouteKind Protocol, string ModelId) value)
            {
                return HashCode.Combine(value.Protocol, StringComparer.Ordinal.GetHashCode(value.ModelId));
            }
        }

        internal sealed class ToolConfigurationSettings
        {
            public ClaudeToolSettings Claude { get; set; } = new();
            public CodexToolSettings Codex { get; set; } = new();
        }

        internal sealed class ClaudeToolSettings
        {
            public string HaikuModelAlias { get; set; } = string.Empty;
            public string SonnetModelAlias { get; set; } = string.Empty;
            public string OpusModelAlias { get; set; } = string.Empty;
            public bool EnableToolSearch { get; set; }
            public bool UseMaximumEffort { get; set; }
            [JsonIgnore]
            public bool Enabled { get; set; }
        }

        internal sealed class CodexToolSettings
        {
            public string ModelAlias { get; set; } = string.Empty;
            public string ReasoningEffort { get; set; } = string.Empty;
            public bool PreserveAccountLogin { get; set; } = true;
            public ApiRouteKind DiscoveryProtocol { get; set; } = ApiRouteKind.Responses;
            public List<CodexCandidate> Candidates { get; set; } = new();
            public long? ModelContextWindow { get; set; }
            public long? ModelAutoCompactTokenLimit { get; set; }
            [JsonIgnore]
            public bool Enabled { get; set; }

            public CodexToolSettings Clone() => new()
            {
                ModelAlias = ModelAlias,
                ReasoningEffort = ReasoningEffort,
                PreserveAccountLogin = PreserveAccountLogin,
                DiscoveryProtocol = DiscoveryProtocol,
                Candidates = Candidates.Select(c => c.Clone()).ToList(),
                ModelContextWindow = ModelContextWindow,
                ModelAutoCompactTokenLimit = ModelAutoCompactTokenLimit,
                Enabled = Enabled
            };

            public void Normalize()
            {
                Candidates = (Candidates ?? new()).Where(c => c != null && Enum.IsDefined(c.Protocol) && !string.IsNullOrWhiteSpace(c.ModelId))
                    .DistinctBy(c => (c.Protocol, c.ModelId)).ToList();
                foreach (var candidate in Candidates)
                {
                    candidate.Normalize();
                }
                var selected = Candidates.FirstOrDefault(c => c.Alias == ModelAlias);
                if (selected == null)
                {
                    selected = Candidates.FirstOrDefault();
                    ModelAlias = selected?.Alias ?? "";
                    if (selected == null) Enabled = false;
                }
                if (!ReasoningEfforts.TryParse(ReasoningEffort, out var effort) || selected?.SupportedEfforts.Contains(effort) != true)
                    ReasoningEffort = "";
                else ReasoningEffort = effort.ToWire();
                if (ModelContextWindow <= 0) ModelContextWindow = null;
                if (ModelAutoCompactTokenLimit <= 0) ModelAutoCompactTokenLimit = null;
            }
        }

        internal sealed class CodexCandidate
        {
            public ApiRouteKind Protocol { get; set; }
            public string ModelId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public long? ContextWindow { get; set; }
            public List<ReasoningEffort> SupportedEfforts { get; set; } = new();
            public ReasoningEffort? DefaultEffort { get; set; }
            public string MetadataSource { get; set; } = "";
            public string ModelDescription { get; set; } = "";
            public CodexModelCapabilities Capabilities { get; set; } = new();
            public bool ContextIsManual { get; set; }
            public bool EffortsAreManual { get; set; }
            [JsonIgnore] public string Alias => CreateManagedModelAlias(Protocol, ModelId);
            public CodexCandidate Clone() => new()
            {
                Protocol = Protocol,
                ModelId = ModelId,
                DisplayName = DisplayName,
                ContextWindow = ContextWindow,
                SupportedEfforts = SupportedEfforts.ToList(),
                DefaultEffort = DefaultEffort,
                MetadataSource = MetadataSource,
                ModelDescription = ModelDescription,
                Capabilities = (Capabilities ?? new()).Clone(),
                ContextIsManual = ContextIsManual,
                EffortsAreManual = EffortsAreManual
            };

            public ReasoningEffort? ResolveDefaultEffort()
            {
                if (DefaultEffort is { } selected && SupportedEfforts.Contains(selected) && Enum.IsDefined(selected)) return selected;
                if (SupportedEfforts.Contains(APIRelay.ReasoningEffort.High)) return APIRelay.ReasoningEffort.High;
                return SupportedEfforts.Where(Enum.IsDefined).Order().Select(e => (ReasoningEffort?)e).FirstOrDefault();
            }

            public void Normalize()
            {
                Capabilities ??= new();
                Capabilities.Normalize();
                SupportedEfforts = (SupportedEfforts ?? []).Where(Enum.IsDefined).Distinct().Order().ToList();
                DefaultEffort = ResolveDefaultEffort();
                if (ContextWindow <= 0) ContextWindow = null;
            }

            public void FillMissingMetadata(CodexModelMetadata metadata)
            {
                if (!ContextIsManual && ContextWindow == null && metadata.ContextWindow is > 0)
                {
                    ContextWindow = metadata.ContextWindow;
                }
                if (!EffortsAreManual && SupportedEfforts.Count == 0 && metadata.Efforts.Length > 0)
                {
                    SupportedEfforts = metadata.Efforts.ToList();
                }
                if (!string.IsNullOrWhiteSpace(metadata.Source))
                {
                    Capabilities = metadata.Capabilities.Clone();
                    ModelDescription = metadata.Description;
                    MetadataSource = metadata.Source;
                }
                Normalize();
            }
        }

        private sealed record ManagedRuntimeRoute(
            string Alias,
            ApiRouteKind Protocol,
            string ModelId,
            Uri ProviderUri,
            string ApiKey,
            string AnthropicVersion)
        {
            public IReadOnlyCollection<ReasoningEffort>? CodexEfforts { get; init; }
            public bool ClaudeAllowed { get; init; }
            public bool CodexAllowed { get; init; }
        }

        internal sealed class ManagedConfigurationApplyState
        {
            public ManagedToolKind Tool { get; set; }
            public string TargetPath { get; set; } = string.Empty;
            public string BackupPath { get; set; } = string.Empty;
            public bool TargetOriginallyExisted { get; set; }
            public bool RestoreCompleted { get; set; }
            public DateTime AppliedAtUtc { get; set; }
            public string? CatalogPath { get; set; }
            public string? CatalogBackupPath { get; set; }
            public bool CatalogOriginallyExisted { get; set; }
        }

        private sealed class ProtectedProviderSecret
        {
            public ApiRouteKind Protocol { get; set; }
            public string ProtectedValue { get; set; } = string.Empty;
        }

        internal sealed class ProviderEndpointConfig
        {
            public ApiRouteKind RouteKind { get; set; }
            public ProviderType ProviderType { get; set; }
            public string ProviderUrl { get; set; } = string.Empty;
            public string ModelListUrl { get; set; } = string.Empty;
            public bool ModelListUrlOverridden { get; set; }
            public string AnthropicVersion { get; set; } = "2023-06-01";
        }

        internal sealed class ModelCostConfig
        {
            public string ModelName { get; set; } = string.Empty;
            public decimal InputCostPerMillion { get; set; }
            public decimal OutputCostPerMillion { get; set; }
            public decimal CacheHitCostPerMillion { get; set; }
            public decimal CacheCreationCostPerMillion { get; set; }
            public bool Overwrite { get; set; }
            public bool ReferencePrice { get; set; }
        }
    }
}

