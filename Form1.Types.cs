using System.Diagnostics;
using System.Drawing.Drawing2D;
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

    public partial class Form1
    {
        private sealed record UsageInfo(int PromptTokens, int CompletionTokens, int CachedTokens, int CacheCreationTokens, int TotalTokens, string Model, bool CacheTokensSeparateFromInput)
        {
            public static UsageInfo Empty { get; } = new(0, 0, 0, 0, 0, string.Empty, false);
        }

        private sealed class StreamingProgressLogger
        {
            private readonly Form1 owner;
            private readonly string requestId;
            private readonly string mode;
            private readonly Stopwatch stopwatch;
            private long lastLogMs;
            private long lastLogProviderBytes;

            public StreamingProgressLogger(Form1 owner, string requestId, string mode, Stopwatch stopwatch)
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

        private sealed class RequestRecord
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
            public long ElapsedMs { get; set; }
            public long FirstResponseMs { get; set; }
        }

        private sealed class DailyChartBucket
        {
            public long InputTokens { get; set; }
            public long OutputTokens { get; set; }
            public long CachedTokens { get; set; }
            public long CacheCreationTokens { get; set; }
            public decimal Cost { get; set; }
        }

        private sealed class BufferedChartPanel : Panel
        {
            public BufferedChartPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }
        }

        private sealed record RelayConfig(Uri LocalUri);

        private sealed record RelayRoute(ApiRouteKind ToProtocol, ApiRouteKind FromProtocol);

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

        private sealed record ProtocolResponse(string Id, string Model, string Content, string FinishReason, UsageInfo Usage);

    private sealed record ModelListItem(string Id, string DisplayName, long? Created);

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

        private sealed record RouteProtocolOption(string DisplayName, ApiRouteKind? RouteKind)
        {
            public override string ToString()
            {
                return DisplayName;
            }
        }

        private enum ApiRouteKind
        {
            Responses,
            ChatCompletions,
            AnthropicMessages
        }

        private enum ProviderType
        {
            OpenAICompatible,
            Anthropic
        }

        private sealed class RelaySettings
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
            public int? UsageBubbleLocationX { get; set; }
            public int? UsageBubbleLocationY { get; set; }
            public List<ProviderEndpointConfig> ProviderConfigs { get; set; } = new();
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<ModelCostConfig>? ModelCosts { get; set; }
        }

        private sealed class ProviderEndpointConfig
        {
            public ApiRouteKind RouteKind { get; set; }
            public ProviderType ProviderType { get; set; }
            public string ProviderUrl { get; set; } = string.Empty;
            public string ModelListUrl { get; set; } = string.Empty;
            public bool ModelListUrlOverridden { get; set; }
            public string AnthropicVersion { get; set; } = "2023-06-01";
        }

        private sealed class ModelCostConfig
        {
            public string ModelName { get; set; } = string.Empty;
            public decimal InputCostPerMillion { get; set; }
            public decimal OutputCostPerMillion { get; set; }
            public decimal CacheHitCostPerMillion { get; set; }
            public decimal CacheCreationCostPerMillion { get; set; }
        }
    }
}

