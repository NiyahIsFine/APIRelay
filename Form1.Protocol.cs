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
    public partial class Form1
    {
        private static string ConvertStreamingEvent(string eventData, RelayRoute relayRoute, StreamingProtocolConversionState state)
        {
            var json = eventData.Trim();
            if (json.Length == 0 || json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return ConvertStreamingEvent(document.RootElement, relayRoute, state);
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ConvertStreamingEvent(JsonElement root, RelayRoute relayRoute, StreamingProtocolConversionState state)
        {
            return relayRoute.ToProtocol switch
            {
                ApiRouteKind.AnthropicMessages => ConvertAnthropicStreamingEvent(root, relayRoute.FromProtocol, state),
                ApiRouteKind.Responses => ConvertResponsesStreamingEvent(root, relayRoute.FromProtocol, state),
                _ => ConvertOpenAiStreamingEvent(root, relayRoute.FromProtocol, state)
            };
        }

        private static string ConvertAnthropicStreamingEvent(JsonElement root, ApiRouteKind targetProtocol, StreamingProtocolConversionState state)
        {
            var eventType = ReadString(root, "type");
            if (eventType == "message_start" && root.TryGetProperty("message", out var messageElement))
            {
                state.Id = ReadString(messageElement, "id");
                state.Model = ReadString(messageElement, "model");
                state.Usage = TryReadUsage(messageElement, out var startUsage) ? MergeUsage(state.Usage, startUsage) : state.Usage;
                return BuildStreamingCreatedEvent(targetProtocol, state);
            }

            if (eventType == "content_block_start" && root.TryGetProperty("content_block", out var contentBlockElement))
            {
                if (ReadString(contentBlockElement, "type").Equals("tool_use", StringComparison.OrdinalIgnoreCase))
                {
                    var toolCall = state.GetOrCreateToolCall(ReadInt(root, "index"), ReadString(contentBlockElement, "id"), ReadString(contentBlockElement, "name"));
                    state.FinishReason = "tool_calls";
                    return BuildStreamingToolCallStartEvent(targetProtocol, state, toolCall);
                }

                return string.Empty;
            }

            if (eventType == "content_block_delta" && root.TryGetProperty("delta", out var deltaElement))
            {
                if (ReadString(deltaElement, "type").Equals("input_json_delta", StringComparison.OrdinalIgnoreCase))
                {
                    var toolCall = state.GetOrCreateToolCall(ReadInt(root, "index"), string.Empty, string.Empty);
                    state.FinishReason = "tool_calls";
                    var argumentsDelta = ReadString(deltaElement, "partial_json");
                    if (!string.IsNullOrEmpty(argumentsDelta))
                    {
                        toolCall.HasArgumentsDelta = true;
                    }

                    return BuildStreamingToolCallArgumentsDeltaEvent(targetProtocol, state, toolCall, argumentsDelta);
                }

                return BuildStreamingTextDeltaEvent(targetProtocol, state, ReadString(deltaElement, "text"));
            }

            if (eventType == "content_block_stop")
            {
                if (state.TryGetToolCall(ReadInt(root, "index"), out var toolCall) && !toolCall.HasArgumentsDelta)
                {
                    toolCall.HasArgumentsDelta = true;
                    return BuildStreamingToolCallArgumentsDeltaEvent(targetProtocol, state, toolCall, "{}");
                }

                return string.Empty;
            }

            if (eventType == "message_delta")
            {
                if (root.TryGetProperty("delta", out var messageDeltaElement))
                {
                    var finishReason = ConvertAnthropicStopReasonToOpenAi(ReadString(messageDeltaElement, "stop_reason"));
                    if (!string.IsNullOrWhiteSpace(finishReason))
                    {
                        state.FinishReason = finishReason;
                    }
                }

                state.Usage = TryReadUsage(root, out var deltaUsage) ? MergeUsage(state.Usage, deltaUsage) : state.Usage;
                return string.Empty;
            }

            return string.Empty;
        }

        private static string ConvertResponsesStreamingEvent(JsonElement root, ApiRouteKind targetProtocol, StreamingProtocolConversionState state)
        {
            var eventType = ReadString(root, "type");
            if (eventType == "response.created" && root.TryGetProperty("response", out var responseElement))
            {
                state.Id = ReadString(responseElement, "id");
                state.Model = ReadString(responseElement, "model");
                return BuildStreamingCreatedEvent(targetProtocol, state);
            }

            if (root.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String)
            {
                return BuildStreamingTextDeltaEvent(targetProtocol, state, deltaElement.GetString() ?? string.Empty);
            }

            state.Usage = TryReadUsage(root, out var usage) ? MergeUsage(state.Usage, usage) : state.Usage;
            return string.Empty;
        }

        private static string ConvertOpenAiStreamingEvent(JsonElement root, ApiRouteKind targetProtocol, StreamingProtocolConversionState state)
        {
            if (string.IsNullOrWhiteSpace(state.Id))
            {
                state.Id = ReadString(root, "id");
            }

            var model = ReadString(root, "model");
            if (!string.IsNullOrWhiteSpace(model))
            {
                state.Model = model;
            }

            state.Usage = TryReadUsage(root, out var usage) ? MergeUsage(state.Usage, usage) : state.Usage;
            var created = state.Created ? string.Empty : BuildStreamingCreatedEvent(targetProtocol, state);
            var delta = ExtractOpenAiChoiceContent(root);
            return created + BuildStreamingTextDeltaEvent(targetProtocol, state, delta);
        }

        private static string BuildStreamingCreatedEvent(ApiRouteKind protocol, StreamingProtocolConversionState state)
        {
            if (state.Created)
            {
                return string.Empty;
            }

            state.Created = true;
            return protocol switch
            {
                ApiRouteKind.Responses => BuildResponsesSseCreatedEvent(state),
                ApiRouteKind.AnthropicMessages => BuildAnthropicSseStartEvents(state),
                _ => string.Empty
            };
        }

        private static string BuildStreamingTextDeltaEvent(ApiRouteKind protocol, StreamingProtocolConversionState state, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return protocol switch
            {
                ApiRouteKind.Responses => BuildResponsesTextDeltaEvent(state, text),
                ApiRouteKind.AnthropicMessages => BuildAnthropicSseEvent("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text } }),
                _ => BuildOpenAiTextDeltaEvent(state, text)
            };
        }

        private static string BuildOpenAiTextDeltaEvent(StreamingProtocolConversionState state, string text)
        {
            var includeRole = !state.OpenAiRoleSent;
            state.OpenAiRoleSent = true;
            return "data: " + BuildOpenAiStreamChunk(state.Id, state.Model, text, null, includeRole) + "\n\n";
        }

        private static string BuildStreamingToolCallStartEvent(ApiRouteKind protocol, StreamingProtocolConversionState state, ToolCallStreamState toolCall)
        {
            return protocol switch
            {
                ApiRouteKind.ChatCompletions => "data: " + BuildOpenAiToolCallStreamChunk(state, toolCall, includeIdentity: true, argumentsDelta: string.Empty) + "\n\n",
                ApiRouteKind.Responses => BuildResponsesToolCallStartEvent(state, toolCall),
                _ => string.Empty
            };
        }

        private static string BuildStreamingToolCallArgumentsDeltaEvent(ApiRouteKind protocol, StreamingProtocolConversionState state, ToolCallStreamState toolCall, string argumentsDelta)
        {
            if (string.IsNullOrEmpty(argumentsDelta))
            {
                return string.Empty;
            }

            toolCall.Arguments.Append(argumentsDelta);
            return protocol switch
            {
                ApiRouteKind.ChatCompletions => "data: " + BuildOpenAiToolCallStreamChunk(state, toolCall, includeIdentity: false, argumentsDelta) + "\n\n",
                ApiRouteKind.Responses => BuildResponsesToolCallArgumentsDeltaEvent(toolCall, argumentsDelta),
                _ => string.Empty
            };
        }

        private static string BuildStreamingCompletionEvent(ApiRouteKind protocol, StreamingProtocolConversionState state)
        {
            return protocol switch
            {
                ApiRouteKind.Responses => BuildResponsesCompletionEvents(state),
                ApiRouteKind.AnthropicMessages => BuildAnthropicSseEndEvents(state),
                _ => "data: " + BuildOpenAiStreamChunk(state.Id, state.Model, string.Empty, state.FinishReason, includeRole: false) + "\n\ndata: [DONE]\n\n"
            };
        }

        private static string BuildResponsesSseCreatedEvent(StreamingProtocolConversionState state)
        {
            var builder = new StringBuilder();
            AppendResponsesSseEvent(builder, "response.created", new
            {
                type = "response.created",
                response = BuildResponsesStreamResponseObject(state, "in_progress")
            });
            AppendResponsesSseEvent(builder, "response.in_progress", new
            {
                type = "response.in_progress",
                response = BuildResponsesStreamResponseObject(state, "in_progress")
            });
            return builder.ToString();
        }

        private static string BuildResponsesTextDeltaEvent(StreamingProtocolConversionState state, string text)
        {
            state.ResponsesText.Append(text);
            var builder = new StringBuilder();
            if (!state.ResponsesTextStarted)
            {
                state.ResponsesTextStarted = true;
                AppendResponsesSseEvent(builder, "response.output_item.added", new
                {
                    type = "response.output_item.added",
                    output_index = 0,
                    item = new { id = state.ResponsesTextItemId, type = "message", status = "in_progress", role = "assistant", content = Array.Empty<object>() }
                });
                AppendResponsesSseEvent(builder, "response.content_part.added", new
                {
                    type = "response.content_part.added",
                    item_id = state.ResponsesTextItemId,
                    output_index = 0,
                    content_index = 0,
                    part = new { type = "output_text", text = string.Empty }
                });
            }

            AppendResponsesSseEvent(builder, "response.output_text.delta", new
            {
                type = "response.output_text.delta",
                item_id = state.ResponsesTextItemId,
                output_index = 0,
                content_index = 0,
                delta = text
            });
            return builder.ToString();
        }

        private static string BuildResponsesToolCallStartEvent(StreamingProtocolConversionState state, ToolCallStreamState toolCall)
        {
            var builder = new StringBuilder();
            AppendResponsesSseEvent(builder, "response.output_item.added", new
            {
                type = "response.output_item.added",
                output_index = toolCall.OpenAiIndex,
                item = new
                {
                    id = toolCall.ResponsesItemId,
                    type = "function_call",
                    status = "in_progress",
                    call_id = toolCall.Id,
                    name = toolCall.Name,
                    arguments = string.Empty
                }
            });
            return builder.ToString();
        }

        private static string BuildResponsesToolCallArgumentsDeltaEvent(ToolCallStreamState toolCall, string argumentsDelta)
        {
            return BuildResponsesSseEvent("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta",
                item_id = toolCall.ResponsesItemId,
                output_index = toolCall.OpenAiIndex,
                delta = argumentsDelta
            });
        }

        private static string BuildResponsesCompletionEvents(StreamingProtocolConversionState state)
        {
            var builder = new StringBuilder();
            var outputIndex = 0;
            if (state.ResponsesTextStarted)
            {
                var text = state.ResponsesText.ToString();
                AppendResponsesSseEvent(builder, "response.output_text.done", new
                {
                    type = "response.output_text.done",
                    item_id = state.ResponsesTextItemId,
                    output_index = outputIndex,
                    content_index = 0,
                    text
                });
                AppendResponsesSseEvent(builder, "response.content_part.done", new
                {
                    type = "response.content_part.done",
                    item_id = state.ResponsesTextItemId,
                    output_index = outputIndex,
                    content_index = 0,
                    part = new { type = "output_text", text }
                });
                AppendResponsesSseEvent(builder, "response.output_item.done", new
                {
                    type = "response.output_item.done",
                    output_index = outputIndex,
                    item = BuildResponsesTextOutputItem(state)
                });
                outputIndex++;
            }

            foreach (var toolCall in state.ToolCalls)
            {
                var arguments = toolCall.Arguments.ToString();
                AppendResponsesSseEvent(builder, "response.function_call_arguments.done", new
                {
                    type = "response.function_call_arguments.done",
                    item_id = toolCall.ResponsesItemId,
                    output_index = outputIndex,
                    arguments
                });
                AppendResponsesSseEvent(builder, "response.output_item.done", new
                {
                    type = "response.output_item.done",
                    output_index = outputIndex,
                    item = BuildResponsesToolCallOutputItem(toolCall)
                });
                outputIndex++;
            }

            AppendResponsesSseEvent(builder, "response.completed", new
            {
                type = "response.completed",
                response = BuildResponsesStreamResponseObject(state, "completed")
            });
            return builder.ToString();
        }

        private static object BuildResponsesStreamResponseObject(StreamingProtocolConversionState state, string status)
        {
            var output = new List<object>();
            if (state.ResponsesTextStarted)
            {
                output.Add(BuildResponsesTextOutputItem(state));
            }

            output.AddRange(state.ToolCalls.Select(BuildResponsesToolCallOutputItem));
            return new
            {
                id = string.IsNullOrWhiteSpace(state.Id) ? "resp_" + Guid.NewGuid().ToString("N") : state.Id,
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status,
                model = state.Model,
                output,
                usage = new
                {
                    input_tokens = state.Usage.PromptTokens,
                    output_tokens = state.Usage.CompletionTokens,
                    total_tokens = state.Usage.TotalTokens > 0 ? state.Usage.TotalTokens : state.Usage.PromptTokens + state.Usage.CompletionTokens + state.Usage.CacheCreationTokens
                }
            };
        }

        private static object BuildResponsesTextOutputItem(StreamingProtocolConversionState state)
        {
            return new
            {
                id = state.ResponsesTextItemId,
                type = "message",
                status = "completed",
                role = "assistant",
                content = new[] { new { type = "output_text", text = state.ResponsesText.ToString() } }
            };
        }

        private static object BuildResponsesToolCallOutputItem(ToolCallStreamState toolCall)
        {
            return new
            {
                id = toolCall.ResponsesItemId,
                type = "function_call",
                status = "completed",
                call_id = toolCall.Id,
                name = toolCall.Name,
                arguments = toolCall.Arguments.ToString()
            };
        }

        private static string BuildResponsesSseEvent(string eventName, object payload)
        {
            var builder = new StringBuilder();
            AppendResponsesSseEvent(builder, eventName, payload);
            return builder.ToString();
        }

        private static string BuildAnthropicSseEvent(string eventName, object payload)
        {
            var builder = new StringBuilder();
            AppendAnthropicSseEvent(builder, eventName, payload);
            return builder.ToString();
        }

        private static string BuildAnthropicSseStartEvents(StreamingProtocolConversionState state)
        {
            var builder = new StringBuilder();
            var id = string.IsNullOrWhiteSpace(state.Id) ? "msg_" + Guid.NewGuid().ToString("N") : state.Id;
            AppendAnthropicSseEvent(builder, "message_start", new
            {
                type = "message_start",
                message = new { id, type = "message", role = "assistant", model = state.Model, content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = state.Usage.PromptTokens, output_tokens = 0 } }
            });
            AppendAnthropicSseEvent(builder, "content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = string.Empty } });
            return builder.ToString();
        }

        private static string BuildAnthropicSseEndEvents(StreamingProtocolConversionState state)
        {
            var builder = new StringBuilder();
            AppendAnthropicSseEvent(builder, "content_block_stop", new { type = "content_block_stop", index = 0 });
            AppendAnthropicSseEvent(builder, "message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn", stop_sequence = (string?)null }, usage = new { output_tokens = state.Usage.CompletionTokens } });
            AppendAnthropicSseEvent(builder, "message_stop", new { type = "message_stop" });
            return builder.ToString();
        }

        private static async Task WriteResponseBodyAsync(HttpListenerResponse localResponse, byte[] responseBytes, CancellationToken cancellationToken)
        {
            await localResponse.OutputStream.WriteAsync(responseBytes.AsMemory(0, responseBytes.Length), cancellationToken);
            await localResponse.OutputStream.FlushAsync(cancellationToken);
        }

        private HttpRequestMessage BuildProviderRequest(HttpListenerRequest request, byte[] requestBody, RelayRoute relayRoute, out byte[] providerRequestBody)
        {
            var config = activeConfig ?? throw new InvalidOperationException(GetText(TextId.Txt95));
            var endpoint = GetProviderEndpoint(relayRoute.ToProtocol);
            var targetUri = BuildTargetUri(request, endpoint);
            var providerRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetUri);
            providerRequestBody = Array.Empty<byte>();

            foreach (var headerName in request.Headers.AllKeys ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(headerName) || ShouldSkipRequestHeader(headerName, endpoint.ProviderType))
                {
                    continue;
                }

                var value = request.Headers[headerName];
                if (!string.IsNullOrEmpty(value))
                {
                    providerRequest.Headers.TryAddWithoutValidation(headerName, value);
                }
            }

            ApplyProviderAuthentication(providerRequest, endpoint, ExtractClientApiKey(request));

            if (requestBody.Length > 0)
            {
                providerRequestBody = TransformRequestBody(requestBody, relayRoute.FromProtocol, relayRoute.ToProtocol);
                providerRequest.Content = new ByteArrayContent(providerRequestBody);

                if (!string.IsNullOrEmpty(request.ContentType))
                {
                    providerRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
                }
            }

            return providerRequest;
        }

        private Uri BuildTargetUri(HttpListenerRequest request, ProviderEndpointConfig endpoint)
        {
            var query = request.Url?.Query ?? string.Empty;
            var providerUrl = IsModelListRequest(request)
                ? GetModelListUrl(endpoint)
                : endpoint.ProviderUrl.Trim();
            if (string.IsNullOrEmpty(query))
            {
                return new Uri(providerUrl);
            }

            var separator = providerUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return new Uri(providerUrl + separator + query.TrimStart('?'));
        }

        private static bool IsModelListRequest(HttpListenerRequest request)
        {
            if (!request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = NormalizePathSlashes(request.Url?.AbsolutePath ?? "/").TrimEnd('/');
            return path.EndsWith("/models", StringComparison.OrdinalIgnoreCase);
        }

        private string GetModelListUrl(ProviderEndpointConfig endpoint)
        {
            var modelListUrl = endpoint.ModelListUrlOverridden
                ? endpoint.ModelListUrl.Trim()
                : BuildModelListUrl(endpoint.RouteKind, endpoint.ProviderUrl);

            if (string.IsNullOrWhiteSpace(modelListUrl))
            {
                throw new InvalidOperationException(GetText(TextId.Txt96, GetRouteKindDisplayName(endpoint.RouteKind)));
            }

            if (!Uri.TryCreate(modelListUrl, UriKind.Absolute, out var modelListUri)
                || (modelListUri.Scheme != Uri.UriSchemeHttp && modelListUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(GetText(TextId.Txt97, GetRouteKindDisplayName(endpoint.RouteKind)));
            }

            return modelListUrl;
        }

        private static string NormalizePathSlashes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            var builder = new StringBuilder(path.Length);
            var previousWasSlash = false;

            foreach (var character in path)
            {
                if (character == '/')
                {
                    if (!previousWasSlash)
                    {
                        builder.Append(character);
                    }

                    previousWasSlash = true;
                    continue;
                }

                builder.Append(character);
                previousWasSlash = false;
            }

            var normalized = builder.ToString();
            return normalized.StartsWith('/') ? normalized : "/" + normalized;
        }

        private ProviderEndpointConfig GetProviderEndpoint(ApiRouteKind routeKind)
        {
            if (!providerConfigs.TryGetValue(routeKind, out var endpoint) || string.IsNullOrWhiteSpace(endpoint.ProviderUrl))
            {
                throw new InvalidOperationException(GetText(TextId.Txt98, GetRouteKindDisplayName(routeKind)));
            }

            endpoint.ProviderUrl = ValidateProviderUrl(routeKind, endpoint.ProviderUrl);
            return endpoint;
        }

        private string ValidateProviderUrl(ApiRouteKind routeKind, string providerUrl)
        {
            var trimmedProviderUrl = providerUrl.Trim();
            if (!Uri.TryCreate(trimmedProviderUrl, UriKind.Absolute, out var providerUri)
                || (providerUri.Scheme != Uri.UriSchemeHttp && providerUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(GetText(TextId.Txt99, GetRouteKindDisplayName(routeKind)));
            }

            if (!ProviderUrlMatchesFinalEndpoint(routeKind, providerUri.AbsolutePath))
            {
                throw new InvalidOperationException(GetText(TextId.Txt100, GetRouteKindDisplayName(routeKind)));
            }

            return trimmedProviderUrl;
        }

        private static bool ProviderUrlMatchesFinalEndpoint(ApiRouteKind routeKind, string absolutePath)
        {
            var path = NormalizePathSlashes(absolutePath).TrimEnd('/');
            return routeKind switch
            {
                ApiRouteKind.Responses => path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase),
                ApiRouteKind.ChatCompletions => path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase),
                ApiRouteKind.AnthropicMessages => path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private static RelayRoute ResolveRelayRoute(HttpListenerRequest request)
        {
            var segments = (request.Url?.AbsolutePath ?? "/")
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (segments.Length == 0 || !TryParseApiRouteKind(segments[0], out var toProtocol))
            {
                toProtocol = ApiRouteKind.ChatCompletions;
            }

            var fromProtocol = toProtocol;
            if (segments.Length > 1 && TryParseApiRouteKind(segments[1], out var parsedFromProtocol))
            {
                fromProtocol = parsedFromProtocol;
            }

            return new RelayRoute(toProtocol, fromProtocol);
        }

        private static bool TryParseApiRouteKind(string value, out ApiRouteKind routeKind)
        {
            if (value.Equals("compatible", StringComparison.OrdinalIgnoreCase)
                || value.Equals("chat", StringComparison.OrdinalIgnoreCase)
                || value.Equals("completions", StringComparison.OrdinalIgnoreCase)
                || value.Equals("chat-completions", StringComparison.OrdinalIgnoreCase)
                || value.Equals("chat_completions", StringComparison.OrdinalIgnoreCase))
            {
                routeKind = ApiRouteKind.ChatCompletions;
                return true;
            }

            if (value.Equals("responses", StringComparison.OrdinalIgnoreCase))
            {
                routeKind = ApiRouteKind.Responses;
                return true;
            }

            if (value.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            {
                routeKind = ApiRouteKind.AnthropicMessages;
                return true;
            }

            routeKind = ApiRouteKind.ChatCompletions;
            return false;
        }

        private static byte[] TransformRequestBody(byte[] requestBody, ApiRouteKind fromProtocol, ApiRouteKind toProtocol)
        {
            if (fromProtocol == toProtocol)
            {
                return requestBody;
            }

            var protocolRequest = ParseProtocolRequest(fromProtocol, requestBody);
            if (protocolRequest == null)
            {
                return requestBody;
            }

            return BuildProtocolRequest(toProtocol, protocolRequest);
        }

        private static ProtocolRequest? ParseProtocolRequest(ApiRouteKind protocol, byte[] requestBody)
        {
            try
            {
                using var document = JsonDocument.Parse(requestBody);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return protocol switch
                {
                    ApiRouteKind.AnthropicMessages => ParseAnthropicProtocolRequest(root),
                    ApiRouteKind.Responses => ParseResponsesProtocolRequest(root),
                    _ => ParseChatCompletionsProtocolRequest(root)
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static byte[] BuildProtocolRequest(ApiRouteKind protocol, ProtocolRequest request)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                switch (protocol)
                {
                    case ApiRouteKind.AnthropicMessages:
                        WriteAnthropicProtocolRequest(writer, request);
                        break;
                    case ApiRouteKind.Responses:
                        WriteResponsesProtocolRequest(writer, request);
                        break;
                    default:
                        WriteChatCompletionsProtocolRequest(writer, request);
                        break;
                }
            }

            return outputStream.ToArray();
        }

        private static ProtocolRequest ParseChatCompletionsProtocolRequest(JsonElement root)
        {
            return new ProtocolRequest(
                ReadString(root, "model"),
                root.TryGetProperty("messages", out var messagesElement) ? CloneElement(messagesElement) : CloneElement(default(JsonElement)),
                root.TryGetProperty("system", out var systemElement) ? CloneElement(systemElement) : ExtractSystemFromChatMessages(root),
                root.TryGetProperty("input", out var inputElement) ? CloneElement(inputElement) : null,
                root.TryGetProperty("tools", out var toolsElement) ? CloneElement(toolsElement) : null,
                ReadInt(root, "max_tokens", "max_completion_tokens", "maxOutputTokens", "max_output_tokens"),
                root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind is JsonValueKind.True or JsonValueKind.False ? streamElement.GetBoolean() : null,
                root.TryGetProperty("temperature", out var temperatureElement) ? CloneElement(temperatureElement) : null,
                root.TryGetProperty("top_p", out var topPElement) ? CloneElement(topPElement) : null);
        }

        private static JsonElement? ExtractSystemFromChatMessages(JsonElement root)
        {
            if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var systemParts = new List<string>();
            foreach (var message in messagesElement.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.Object
                    && string.Equals(ReadString(message, "role"), "system", StringComparison.OrdinalIgnoreCase))
                {
                    var text = ExtractContentText(message);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        systemParts.Add(text);
                    }
                }
            }

            return systemParts.Count == 0 ? null : JsonSerializer.SerializeToElement(string.Join("\n\n", systemParts));
        }

        private static ProtocolRequest ParseAnthropicProtocolRequest(JsonElement root)
        {
            JsonElement? systemElement = root.TryGetProperty("system", out var system) ? CloneElement(system) : null;
            var messagesElement = root.TryGetProperty("messages", out var messages) ? CloneElement(messages) : CloneElement(default(JsonElement));
            return new ProtocolRequest(
                ReadString(root, "model"),
                messagesElement,
                systemElement,
                null,
                root.TryGetProperty("tools", out var toolsElement) ? CloneElement(toolsElement) : null,
                ReadInt(root, "max_tokens"),
                root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind is JsonValueKind.True or JsonValueKind.False ? streamElement.GetBoolean() : null,
                root.TryGetProperty("temperature", out var temperatureElement) ? CloneElement(temperatureElement) : null,
                root.TryGetProperty("top_p", out var topPElement) ? CloneElement(topPElement) : null);
        }

        private static ProtocolRequest ParseResponsesProtocolRequest(JsonElement root)
        {
            return new ProtocolRequest(
                ReadString(root, "model"),
                CloneElement(default(JsonElement)),
                root.TryGetProperty("instructions", out var instructionsElement) ? CloneElement(instructionsElement) : null,
                root.TryGetProperty("input", out var inputElement) ? CloneElement(inputElement) : null,
                root.TryGetProperty("tools", out var toolsElement) ? CloneElement(toolsElement) : null,
                ReadInt(root, "max_output_tokens", "max_tokens"),
                root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind is JsonValueKind.True or JsonValueKind.False ? streamElement.GetBoolean() : null,
                root.TryGetProperty("temperature", out var temperatureElement) ? CloneElement(temperatureElement) : null,
                root.TryGetProperty("top_p", out var topPElement) ? CloneElement(topPElement) : null);
        }

        private static void WriteChatCompletionsProtocolRequest(Utf8JsonWriter writer, ProtocolRequest request)
        {
            writer.WriteStartObject();
            WriteCommonRequestProperties(writer, request, "max_tokens");
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            if (request.System is { } systemElement)
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WritePropertyName("content");
                systemElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            if (request.Messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in request.Messages.EnumerateArray())
                {
                    message.WriteTo(writer);
                }
            }
            else if (request.Input is { } inputElement)
            {
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WritePropertyName("content");
                inputElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteOpenAiCompatibleTools(writer, request.Tools);
            writer.WriteEndObject();
        }

        private static void WriteAnthropicProtocolRequest(Utf8JsonWriter writer, ProtocolRequest request)
        {
            writer.WriteStartObject();
            WriteCommonRequestProperties(writer, request, "max_tokens", DefaultAnthropicMaxTokens);
            if (request.System is { } systemElement)
            {
                writer.WritePropertyName("system");
                systemElement.WriteTo(writer);
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            if (request.Messages.ValueKind == JsonValueKind.Array)
            {
                WriteAnthropicMessagesFromChatMessages(writer, request.Messages);
            }
            else if (request.Input is { } inputElement)
            {
                WriteAnthropicMessagesFromInput(writer, inputElement);
            }

            writer.WriteEndArray();
            WriteAnthropicProtocolTools(writer, request.Tools);
            writer.WriteEndObject();
        }

        private static void WriteAnthropicMessagesFromChatMessages(Utf8JsonWriter writer, JsonElement messagesElement)
        {
            var pendingToolResults = new List<JsonElement>();

            foreach (var message in messagesElement.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.Object && ReadString(message, "role").Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (message.ValueKind == JsonValueKind.Object && ReadString(message, "role").Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    pendingToolResults.Add(message);
                    continue;
                }

                WritePendingAnthropicToolResults(writer, pendingToolResults);
                pendingToolResults.Clear();
                WriteAnthropicMessageFromChatMessage(writer, message);
            }

            WritePendingAnthropicToolResults(writer, pendingToolResults);
        }

        private static void WritePendingAnthropicToolResults(Utf8JsonWriter writer, List<JsonElement> toolMessages)
        {
            if (toolMessages.Count == 0)
            {
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            foreach (var toolMessage in toolMessages)
            {
                WriteAnthropicToolResultPart(writer, toolMessage);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteAnthropicMessageFromChatMessage(Utf8JsonWriter writer, JsonElement message)
        {
            if (message.ValueKind != JsonValueKind.Object)
            {
                message.WriteTo(writer);
                return;
            }

            var role = ReadString(message, "role");
            if (role.Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                WriteAnthropicToolResultPart(writer, message);
                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            }

            if (role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                && message.TryGetProperty("tool_calls", out var toolCallsElement)
                && toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                WriteAnthropicAssistantToolUseMessage(writer, message, toolCallsElement);
                return;
            }

            message.WriteTo(writer);
        }

        private static void WriteAnthropicToolResultPart(Utf8JsonWriter writer, JsonElement message)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "tool_result");
            writer.WriteString("tool_use_id", ReadString(message, "tool_call_id"));
            writer.WriteString("content", ExtractContentText(message));
            writer.WriteEndObject();
        }

        private static void WriteAnthropicAssistantToolUseMessage(Utf8JsonWriter writer, JsonElement message, JsonElement toolCallsElement)
        {
            writer.WriteStartObject();
            writer.WriteString("role", "assistant");
            writer.WritePropertyName("content");
            writer.WriteStartArray();

            var text = ExtractContentText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                WriteAnthropicTextPart(writer, text);
            }

            foreach (var toolCall in toolCallsElement.EnumerateArray())
            {
                if (toolCall.ValueKind != JsonValueKind.Object || !toolCall.TryGetProperty("function", out var functionElement))
                {
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteString("type", "tool_use");
                writer.WriteString("id", ReadString(toolCall, "id"));
                writer.WriteString("name", ReadString(functionElement, "name"));
                writer.WritePropertyName("input");
                WriteJsonStringAsObject(writer, ReadString(functionElement, "arguments"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteJsonStringAsObject(Utf8JsonWriter writer, string json)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    document.RootElement.WriteTo(writer);
                    return;
                }
                catch (JsonException)
                {
                }
            }

            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        private static void WriteAnthropicMessagesFromInput(Utf8JsonWriter writer, JsonElement inputElement)
        {
            if (inputElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inputElement.EnumerateArray())
                {
                    WriteAnthropicMessageFromResponsesItem(writer, item);
                }

                return;
            }

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            WriteAnthropicContentFromResponsesContent(writer, inputElement);
            writer.WriteEndObject();
        }

        private static void WriteAnthropicMessageFromResponsesItem(Utf8JsonWriter writer, JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var itemType = ReadString(item, "type");
                if (itemType.Equals("function_call", StringComparison.OrdinalIgnoreCase))
                {
                    WriteAnthropicAssistantToolUseMessageFromResponsesItem(writer, item);
                    return;
                }

                if (itemType.Equals("function_call_output", StringComparison.OrdinalIgnoreCase))
                {
                    WriteAnthropicToolResultMessageFromResponsesItem(writer, item);
                    return;
                }
            }

            writer.WriteStartObject();
            writer.WriteString("role", ReadResponsesRole(item));
            writer.WritePropertyName("content");

            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("content", out var contentElement))
            {
                WriteAnthropicContentFromResponsesContent(writer, contentElement);
            }
            else
            {
                WriteAnthropicContentFromResponsesContent(writer, item);
            }

            writer.WriteEndObject();
        }

        private static void WriteAnthropicAssistantToolUseMessageFromResponsesItem(Utf8JsonWriter writer, JsonElement item)
        {
            writer.WriteStartObject();
            writer.WriteString("role", "assistant");
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "tool_use");
            writer.WriteString("id", ReadString(item, "call_id", "id"));
            writer.WriteString("name", ReadString(item, "name"));
            writer.WritePropertyName("input");
            WriteJsonStringAsObject(writer, ReadString(item, "arguments"));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteAnthropicToolResultMessageFromResponsesItem(Utf8JsonWriter writer, JsonElement item)
        {
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "tool_result");
            writer.WriteString("tool_use_id", ReadString(item, "call_id", "id"));
            writer.WriteString("content", ExtractResponsesToolOutputText(item));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static string ExtractResponsesToolOutputText(JsonElement item)
        {
            foreach (var propertyName in new[] { "output", "content" })
            {
                if (item.TryGetProperty(propertyName, out var value))
                {
                    var text = ExtractElementText(value);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static string ReadResponsesRole(JsonElement item)
        {
            var role = ReadString(item, "role");
            return role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
        }

        private static void WriteAnthropicContentFromResponsesContent(Utf8JsonWriter writer, JsonElement contentElement)
        {
            if (contentElement.ValueKind == JsonValueKind.Array)
            {
                var parts = contentElement.EnumerateArray()
                    .Select(ExtractElementText)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToList();

                if (parts.Count == 0)
                {
                    writer.WriteStringValue(string.Empty);
                    return;
                }

                writer.WriteStartArray();
                foreach (var t in parts)
                {
                    WriteAnthropicTextPart(writer, t);
                }

                writer.WriteEndArray();
                return;
            }

            var text = ExtractElementText(contentElement);
            if (string.IsNullOrEmpty(text))
            {
                writer.WriteStringValue(string.Empty);
                return;
            }

            writer.WriteStringValue(text);
        }

        private static void WriteAnthropicTextPart(Utf8JsonWriter writer, string text)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        private static void WriteResponsesProtocolRequest(Utf8JsonWriter writer, ProtocolRequest request)
        {
            writer.WriteStartObject();
            WriteCommonRequestProperties(writer, request, "max_output_tokens");
            if (request.System is { } systemElement && systemElement.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                writer.WritePropertyName("instructions");
                systemElement.WriteTo(writer);
            }

            writer.WritePropertyName("input");
            if (request.Input is { } inputElement)
            {
                inputElement.WriteTo(writer);
            }
            else if (request.Messages.ValueKind != JsonValueKind.Undefined)
            {
                request.Messages.WriteTo(writer);
            }
            else
            {
                writer.WriteStringValue(string.Empty);
            }

            if (request.Tools is { } toolsElement)
            {
                writer.WritePropertyName("tools");
                toolsElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        private static void WriteCommonRequestProperties(Utf8JsonWriter writer, ProtocolRequest request, string maxTokensName, int defaultMaxTokens = 0)
        {
            if (!string.IsNullOrWhiteSpace(request.Model))
            {
                writer.WriteString("model", request.Model);
            }

            var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : defaultMaxTokens;
            if (maxTokens > 0)
            {
                writer.WriteNumber(maxTokensName, maxTokens);
            }

            if (request.Stream.HasValue)
            {
                writer.WriteBoolean("stream", request.Stream.Value);
            }

            if (request.Temperature is { } temperature)
            {
                writer.WritePropertyName("temperature");
                temperature.WriteTo(writer);
            }

            if (request.TopP is { } topP)
            {
                writer.WritePropertyName("top_p");
                topP.WriteTo(writer);
            }
        }

        private static void WriteOpenAiCompatibleTools(Utf8JsonWriter writer, JsonElement? tools)
        {
            if (tools is not { } toolsElement)
            {
                return;
            }

            writer.WritePropertyName("tools");
            if (toolsElement.ValueKind != JsonValueKind.Array)
            {
                toolsElement.WriteTo(writer);
                return;
            }

            writer.WriteStartArray();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                if (tool.ValueKind == JsonValueKind.Object && !tool.TryGetProperty("type", out _) && tool.TryGetProperty("input_schema", out var inputSchemaElement))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    CopyOptionalString(tool, writer, "name");
                    CopyOptionalString(tool, writer, "description");
                    writer.WritePropertyName("parameters");
                    inputSchemaElement.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    continue;
                }

                tool.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        private static void WriteAnthropicProtocolTools(Utf8JsonWriter writer, JsonElement? tools)
        {
            if (tools is not { } toolsElement)
            {
                return;
            }

            writer.WritePropertyName("tools");
            if (toolsElement.ValueKind != JsonValueKind.Array)
            {
                toolsElement.WriteTo(writer);
                return;
            }

            writer.WriteStartArray();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                if (tool.ValueKind == JsonValueKind.Object
                    && string.Equals(ReadString(tool, "type"), "function", StringComparison.OrdinalIgnoreCase)
                    && tool.TryGetProperty("name", out _))
                {
                    writer.WriteStartObject();
                    CopyOptionalString(tool, writer, "name");
                    CopyOptionalString(tool, writer, "description");
                    if (tool.TryGetProperty("parameters", out var parametersElement))
                    {
                        writer.WritePropertyName("input_schema");
                        parametersElement.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                    continue;
                }

                if (tool.ValueKind == JsonValueKind.Object
                    && string.Equals(ReadString(tool, "type"), "function", StringComparison.OrdinalIgnoreCase)
                    && tool.TryGetProperty("function", out var functionElement)
                    && functionElement.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    CopyOptionalString(functionElement, writer, "name");
                    CopyOptionalString(functionElement, writer, "description");
                    if (functionElement.TryGetProperty("parameters", out var parametersElement))
                    {
                        writer.WritePropertyName("input_schema");
                        parametersElement.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                    continue;
                }

                tool.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        private static string ReadString(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = ReadString(element, propertyName);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static JsonElement CloneElement(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Undefined ? default : element.Clone();
        }

        private static bool ShouldConvertResponse(RelayRoute relayRoute)
        {
            return relayRoute.FromProtocol != relayRoute.ToProtocol;
        }

        private static void CopyOptionalString(JsonElement source, Utf8JsonWriter writer, string propertyName)
        {
            if (source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                writer.WriteString(propertyName, value.GetString());
            }
        }

        private static bool ShouldSkipRequestHeader(string headerName, ProviderType providerType)
        {
            return headerName.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Expect", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyProviderAuthentication(HttpRequestMessage providerRequest, ProviderEndpointConfig config, string clientApiKey)
        {
            if (string.IsNullOrWhiteSpace(clientApiKey))
            {
                return;
            }

            var trimmedClientApiKey = clientApiKey.Trim();

            if (config.ProviderType == ProviderType.Anthropic)
            {
                providerRequest.Headers.Remove("x-api-key");
                providerRequest.Headers.TryAddWithoutValidation("x-api-key", trimmedClientApiKey);

                if (!providerRequest.Headers.Contains("anthropic-version"))
                {
                    providerRequest.Headers.TryAddWithoutValidation("anthropic-version", string.IsNullOrWhiteSpace(config.AnthropicVersion) ? "2023-06-01" : config.AnthropicVersion);
                }

                return;
            }

            providerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trimmedClientApiKey);
        }

        private static bool TryValidateClientApiKey(HttpListenerRequest request, ProviderEndpointConfig endpoint, out int statusCode, out byte[] errorBody)
        {
            statusCode = 200;
            errorBody = Array.Empty<byte>();

            var clientApiKey = ExtractClientApiKey(request);
            if (string.IsNullOrWhiteSpace(clientApiKey))
            {
                statusCode = 401;
                errorBody = BuildAuthErrorBody("客户端 API Key 缺失。", "api_relay_missing_client_api_key");
                return false;
            }

            return true;
        }

        private static string ExtractClientApiKey(HttpListenerRequest request)
        {
            var authorization = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                const string bearerPrefix = "Bearer ";
                return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                    ? authorization[bearerPrefix.Length..].Trim()
                    : authorization.Trim();
            }

            return request.Headers["x-api-key"]?.Trim() ?? string.Empty;
        }

        private static byte[] BuildAuthErrorBody(string message, string type)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                error = new
                {
                    message,
                    type
                }
            });
        }

        private static ClientResponseBody BuildClientResponse(byte[] responseBytes, string? mediaType, RelayRoute relayRoute, byte[] requestBody, bool isModelListRequest)
        {
            if (responseBytes.Length == 0)
            {
                return new ClientResponseBody(responseBytes, mediaType);
            }

            if (isModelListRequest && ShouldConvertResponse(relayRoute))
            {
                return BuildModelListClientResponse(responseBytes, relayRoute.FromProtocol, mediaType);
            }

            if (!ShouldConvertResponse(relayRoute))
            {
                return new ClientResponseBody(responseBytes, mediaType);
            }

            var providerResponse = ParseProtocolResponse(relayRoute.ToProtocol, responseBytes, requestBody, IsEventStream(mediaType));
            return FormatProtocolResponse(relayRoute.FromProtocol, providerResponse, IsEventStream(mediaType));
        }

        private static ClientResponseBody BuildModelListClientResponse(byte[] responseBytes, ApiRouteKind targetProtocol, string? mediaType)
        {
            try
            {
                var models = ParseModelListResponse(responseBytes);
                if (models.Count == 0)
                {
                    return new ClientResponseBody(responseBytes, mediaType);
                }

                return new ClientResponseBody(FormatModelListResponse(models, targetProtocol), "application/json; charset=utf-8");
            }
            catch (JsonException)
            {
                return new ClientResponseBody(responseBytes, mediaType);
            }
        }

        private static List<ModelListItem> ParseModelListResponse(byte[] responseBytes)
        {
            using var document = JsonDocument.Parse(responseBytes);
            var root = document.RootElement;
            var modelsElement = root.ValueKind switch
            {
                JsonValueKind.Array => root,
                JsonValueKind.Object when root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array => dataElement,
                JsonValueKind.Object when root.TryGetProperty("models", out var modelsProperty) && modelsProperty.ValueKind == JsonValueKind.Array => modelsProperty,
                _ => default
            };

            var models = new List<ModelListItem>();
            if (modelsElement.ValueKind != JsonValueKind.Array)
            {
                return models;
            }

            foreach (var item in modelsElement.EnumerateArray())
            {
                var model = ParseModelListItem(item);
                if (!string.IsNullOrWhiteSpace(model.Id))
                {
                    models.Add(model);
                }
            }

            return models;
        }

        private static ModelListItem ParseModelListItem(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var id = item.GetString() ?? string.Empty;
                return new ModelListItem(id, string.Empty, null);
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                return new ModelListItem(string.Empty, string.Empty, null);
            }

            var modelId = ReadString(item, "id", "model", "name");
            var displayName = ReadString(item, "display_name", "displayName", "name");
            long? created = null;
            if (item.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.Number && createdElement.TryGetInt64(out var createdValue))
            {
                created = createdValue;
            }

            if (!created.HasValue
                && item.TryGetProperty("created_at", out var createdAtElement)
                && createdAtElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(createdAtElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
            {
                created = createdAt.ToUnixTimeSeconds();
            }

            return new ModelListItem(modelId, displayName, created);
        }

        private static byte[] FormatModelListResponse(IReadOnlyList<ModelListItem> models, ApiRouteKind targetProtocol)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                if (targetProtocol == ApiRouteKind.AnthropicMessages)
                {
                    WriteAnthropicModelListResponse(writer, models);
                }
                else
                {
                    WriteOpenAiModelListResponse(writer, models);
                }
            }

            return outputStream.ToArray();
        }

        private static void WriteOpenAiModelListResponse(Utf8JsonWriter writer, IReadOnlyList<ModelListItem> models)
        {
            writer.WriteStartObject();
            writer.WriteString("object", "list");
            writer.WritePropertyName("data");
            writer.WriteStartArray();
            foreach (var model in models)
            {
                writer.WriteStartObject();
                writer.WriteString("id", model.Id);
                writer.WriteString("object", "model");
                writer.WriteNumber("created", model.Created ?? 0);
                writer.WriteString("owned_by", "provider");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteAnthropicModelListResponse(Utf8JsonWriter writer, IReadOnlyList<ModelListItem> models)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("data");
            writer.WriteStartArray();
            foreach (var model in models)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "model");
                writer.WriteString("id", model.Id);
                writer.WriteString("display_name", string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteBoolean("has_more", false);
            if (models.Count > 0)
            {
                writer.WriteString("first_id", models[0].Id);
                writer.WriteString("last_id", models[^1].Id);
            }

            writer.WriteEndObject();
        }

        private static ProtocolResponse ParseProtocolResponse(ApiRouteKind protocol, byte[] responseBytes, byte[] requestBody, bool isStream)
        {
            if (isStream)
            {
                return protocol switch
                {
                    ApiRouteKind.AnthropicMessages => ParseAnthropicStreamingResponse(responseBytes, requestBody),
                    ApiRouteKind.Responses => ParseResponsesStreamingResponse(responseBytes, requestBody),
                    _ => ParseOpenAiStreamingResponse(responseBytes, requestBody)
                };
            }

            return protocol switch
            {
                ApiRouteKind.AnthropicMessages => ParseAnthropicJsonResponse(responseBytes, requestBody),
                ApiRouteKind.Responses => ParseResponsesJsonResponse(responseBytes, requestBody),
                _ => ParseOpenAiJsonResponse(responseBytes, requestBody)
            };
        }

        private static ClientResponseBody FormatProtocolResponse(ApiRouteKind protocol, ProtocolResponse response, bool preferStream)
        {
            if (preferStream)
            {
                return protocol switch
                {
                    ApiRouteKind.AnthropicMessages => new ClientResponseBody(BuildAnthropicSseResponse(response.Content, response.Model), "text/event-stream"),
                    ApiRouteKind.Responses => new ClientResponseBody(BuildResponsesSseResponse(response), "text/event-stream"),
                    _ => new ClientResponseBody(BuildOpenAiSseResponse(response), "text/event-stream")
                };
            }

            return protocol switch
            {
                ApiRouteKind.AnthropicMessages => new ClientResponseBody(BuildAnthropicJsonResponse(response), "application/json; charset=utf-8"),
                ApiRouteKind.Responses => new ClientResponseBody(BuildResponsesJsonResponse(response), "application/json; charset=utf-8"),
                _ => new ClientResponseBody(BuildOpenAiJsonResponse(response), "application/json; charset=utf-8")
            };
        }

        private static ProtocolResponse ParseAnthropicJsonResponse(byte[] responseBytes, byte[] requestBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBytes);
                var root = document.RootElement;
                return new ProtocolResponse(
                    ReadString(root, "id"),
                    ReadModelOrRequestModel(root, requestBody),
                    ExtractAnthropicTextContent(root),
                    ConvertAnthropicStopReasonToOpenAi(ReadString(root, "stop_reason")),
                    TryReadUsage(root, out var usage) ? usage : UsageInfo.Empty);
            }
            catch (JsonException)
            {
                return CreateFallbackProtocolResponse(requestBody);
            }
        }

        private static ProtocolResponse ParseOpenAiJsonResponse(byte[] responseBytes, byte[] requestBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBytes);
                var root = document.RootElement;
                return new ProtocolResponse(
                    ReadString(root, "id"),
                    ReadModelOrRequestModel(root, requestBody),
                    ExtractOpenAiChoiceContent(root),
                    string.IsNullOrWhiteSpace(ExtractOpenAiFinishReason(root)) ? "stop" : ExtractOpenAiFinishReason(root),
                    TryReadUsage(root, out var usage) ? usage : UsageInfo.Empty);
            }
            catch (JsonException)
            {
                return CreateFallbackProtocolResponse(requestBody);
            }
        }

        private static ProtocolResponse ParseResponsesJsonResponse(byte[] responseBytes, byte[] requestBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBytes);
                var root = document.RootElement;
                return new ProtocolResponse(
                    ReadString(root, "id"),
                    ReadModelOrRequestModel(root, requestBody),
                    ExtractResponsesOutputText(root),
                    ConvertResponsesStatusToOpenAiFinishReason(ReadString(root, "status")),
                    TryReadUsage(root, out var usage) ? usage : UsageInfo.Empty);
            }
            catch (JsonException)
            {
                return CreateFallbackProtocolResponse(requestBody);
            }
        }

        private static ProtocolResponse ParseAnthropicStreamingResponse(byte[] responseBytes, byte[] requestBody)
        {
            var body = Encoding.UTF8.GetString(responseBytes);
            var content = new StringBuilder();
            var model = ExtractRequestModel(requestBody);
            var id = string.Empty;
            var finishReason = "stop";
            var usage = UsageInfo.Empty;
            var eventData = new StringBuilder();

            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Trim().Length == 0)
                {
                    ReadAnthropicStreamEvent(eventData.ToString(), content, ref id, ref model, ref finishReason, ref usage);
                    eventData.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    eventData.AppendLine(line[5..].TrimStart());
                }
            }

            ReadAnthropicStreamEvent(eventData.ToString(), content, ref id, ref model, ref finishReason, ref usage);
            return new ProtocolResponse(id, model, content.ToString(), finishReason, usage);
        }

        private static ProtocolResponse ParseOpenAiStreamingResponse(byte[] responseBytes, byte[] requestBody)
        {
            var body = Encoding.UTF8.GetString(responseBytes);
            var content = new StringBuilder();
            var model = ExtractRequestModel(requestBody);
            var id = string.Empty;
            var finishReason = "stop";
            var usage = UsageInfo.Empty;

            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[5..].TrimStart();
                if (data.Length == 0 || data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(data);
                    var root = document.RootElement;
                    id = string.IsNullOrWhiteSpace(id) ? ReadString(root, "id") : id;
                    var responseModel = ReadString(root, "model");
                    if (!string.IsNullOrWhiteSpace(responseModel))
                    {
                        model = responseModel;
                    }

                    content.Append(ExtractOpenAiChoiceContent(root));
                    var chunkFinishReason = ExtractOpenAiFinishReason(root);
                    if (!string.IsNullOrWhiteSpace(chunkFinishReason))
                    {
                        finishReason = chunkFinishReason;
                    }

                    usage = TryReadUsage(root, out var chunkUsage) ? MergeUsage(usage, chunkUsage) : usage;
                }
                catch (JsonException)
                {
                }
            }

            return new ProtocolResponse(id, model, content.ToString(), finishReason, usage);
        }

        private static ProtocolResponse ParseResponsesStreamingResponse(byte[] responseBytes, byte[] requestBody)
        {
            var body = Encoding.UTF8.GetString(responseBytes);
            var content = new StringBuilder();
            var model = ExtractRequestModel(requestBody);
            var id = string.Empty;
            var finishReason = "stop";
            var usage = UsageInfo.Empty;

            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[5..].TrimStart();
                if (data.Length == 0 || data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(data);
                    var root = document.RootElement;
                    if (ReadString(root, "type") == "response.created" && root.TryGetProperty("response", out var responseElement))
                    {
                        id = ReadString(responseElement, "id");
                        var responseModel = ReadString(responseElement, "model");
                        if (!string.IsNullOrWhiteSpace(responseModel))
                        {
                            model = responseModel;
                        }
                    }

                    if (root.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String)
                    {
                        content.Append(deltaElement.GetString());
                    }

                    if (ReadString(root, "type") is "response.completed" or "response.failed" or "response.incomplete")
                    {
                        finishReason = "stop";
                    }

                    usage = TryReadUsage(root, out var chunkUsage) ? MergeUsage(usage, chunkUsage) : usage;
                }
                catch (JsonException)
                {
                }
            }

            return new ProtocolResponse(id, model, content.ToString(), finishReason, usage);
        }

        private static void ReadAnthropicStreamEvent(string eventData, StringBuilder content, ref string id, ref string model, ref string finishReason, ref UsageInfo usage)
        {
            var data = eventData.Trim();
            if (data.Length == 0 || data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;
                var eventType = ReadString(root, "type");
                if (eventType == "message_start" && root.TryGetProperty("message", out var messageElement))
                {
                    id = ReadString(messageElement, "id");
                    model = ReadString(messageElement, "model");
                    usage = TryReadUsage(messageElement, out var startUsage) ? MergeUsage(usage, startUsage) : usage;
                    return;
                }

                if (eventType == "content_block_delta" && root.TryGetProperty("delta", out var deltaElement))
                {
                    content.Append(ReadString(deltaElement, "text"));
                    return;
                }

                if (eventType == "message_delta" && root.TryGetProperty("delta", out var messageDelta))
                {
                    finishReason = ConvertAnthropicStopReasonToOpenAi(ReadString(messageDelta, "stop_reason"));
                    usage = TryReadUsage(root, out var deltaUsage) ? MergeUsage(usage, deltaUsage) : usage;
                }
            }
            catch (JsonException)
            {
            }
        }

        private static byte[] BuildOpenAiJsonResponse(ProtocolResponse response)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                WriteOpenAiResponse(writer, response);
            }

            return outputStream.ToArray();
        }

        private static byte[] BuildResponsesJsonResponse(ProtocolResponse response)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                writer.WriteStartObject();
                writer.WriteString("id", string.IsNullOrWhiteSpace(response.Id) ? "resp_" + Guid.NewGuid().ToString("N") : response.Id);
                writer.WriteString("object", "response");
                writer.WriteNumber("created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                writer.WriteString("status", response.FinishReason == "length" ? "incomplete" : "completed");
                writer.WriteString("model", response.Model);
                writer.WritePropertyName("output");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("type", "message");
                writer.WriteString("role", "assistant");
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("type", "output_text");
                writer.WriteString("text", response.Content);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();
                WriteResponsesUsage(writer, response.Usage);
                writer.WriteEndObject();
            }

            return outputStream.ToArray();
        }

        private static byte[] BuildAnthropicJsonResponse(ProtocolResponse response)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                WriteAnthropicMessageResponse(writer, response.Id, response.Model, response.Content, ConvertOpenAiFinishReasonToAnthropic(response.FinishReason), ToUsageElement(response.Usage));
            }

            return outputStream.ToArray();
        }

        private static byte[] BuildOpenAiSseResponse(ProtocolResponse response)
        {
            var chunk = BuildOpenAiStreamChunk(response.Id, response.Model, response.Content, response.FinishReason, includeRole: true);
            return Encoding.UTF8.GetBytes("data: " + chunk + "\n\ndata: [DONE]\n\n");
        }

        private static byte[] BuildResponsesSseResponse(ProtocolResponse response)
        {
            var builder = new StringBuilder();
            AppendResponsesSseEvent(builder, "response.created", new
            {
                type = "response.created",
                response = new { id = string.IsNullOrWhiteSpace(response.Id) ? "resp_" + Guid.NewGuid().ToString("N") : response.Id, model = response.Model }
            });
            AppendResponsesSseEvent(builder, "response.output_text.delta", new { type = "response.output_text.delta", delta = response.Content });
            AppendResponsesSseEvent(builder, "response.completed", new { type = "response.completed" });
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static void AppendResponsesSseEvent(StringBuilder builder, string eventName, object payload)
        {
            builder.Append("event: ").AppendLine(eventName);
            builder.Append("data: ").AppendLine(JsonSerializer.Serialize(payload));
            builder.AppendLine();
        }

        private static void WriteOpenAiResponse(Utf8JsonWriter writer, ProtocolResponse response)
        {
            writer.WriteStartObject();
            writer.WriteString("id", string.IsNullOrWhiteSpace(response.Id) ? "chatcmpl-" + Guid.NewGuid().ToString("N") : response.Id);
            writer.WriteString("object", "chat.completion");
            writer.WriteNumber("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            writer.WriteString("model", response.Model);
            writer.WritePropertyName("choices");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteNumber("index", 0);
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("role", "assistant");
            writer.WriteString("content", response.Content);
            writer.WriteEndObject();
            writer.WriteString("finish_reason", string.IsNullOrWhiteSpace(response.FinishReason) ? "stop" : response.FinishReason);
            writer.WriteEndObject();
            writer.WriteEndArray();
            WriteOpenAiUsage(writer, response.Usage);
            writer.WriteEndObject();
        }

        private static void WriteOpenAiUsage(Utf8JsonWriter writer, UsageInfo usage)
        {
            writer.WritePropertyName("usage");
            writer.WriteStartObject();
            writer.WriteNumber("prompt_tokens", usage.PromptTokens);
            writer.WriteNumber("completion_tokens", usage.CompletionTokens);
            writer.WriteNumber("total_tokens", usage.TotalTokens > 0 ? usage.TotalTokens : usage.PromptTokens + usage.CompletionTokens + usage.CacheCreationTokens);
            writer.WriteEndObject();
        }

        private static void WriteResponsesUsage(Utf8JsonWriter writer, UsageInfo usage)
        {
            writer.WritePropertyName("usage");
            writer.WriteStartObject();
            writer.WriteNumber("input_tokens", usage.PromptTokens);
            writer.WriteNumber("output_tokens", usage.CompletionTokens);
            writer.WriteNumber("total_tokens", usage.TotalTokens > 0 ? usage.TotalTokens : usage.PromptTokens + usage.CompletionTokens + usage.CacheCreationTokens);
            writer.WriteEndObject();
        }

        private static JsonElement ToUsageElement(UsageInfo usage)
        {
            return JsonSerializer.SerializeToElement(new
            {
                prompt_tokens = usage.PromptTokens,
                completion_tokens = usage.CompletionTokens,
                input_tokens = usage.PromptTokens,
                output_tokens = usage.CompletionTokens
            });
        }

        private static string ReadModelOrRequestModel(JsonElement root, byte[] requestBody)
        {
            var model = ReadString(root, "model");
            return string.IsNullOrWhiteSpace(model) ? ExtractRequestModel(requestBody) : model;
        }

        private static ProtocolResponse CreateFallbackProtocolResponse(byte[] requestBody)
        {
            return new ProtocolResponse(string.Empty, ExtractRequestModel(requestBody), string.Empty, "stop", UsageInfo.Empty);
        }

        private static bool ShouldStreamProviderResponse(HttpResponseMessage providerResponse, RelayRoute relayRoute)
        {
            return !ShouldConvertResponse(relayRoute) && IsEventStream(providerResponse.Content.Headers.ContentType?.MediaType);
        }

        private static bool IsEventStream(string? mediaType)
        {
            return mediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static void WriteAnthropicMessageResponse(Utf8JsonWriter writer, string id, string model, string content, string stopReason, JsonElement usageElement)
        {
            writer.WriteStartObject();
            writer.WriteString("id", id.StartsWith("msg_", StringComparison.OrdinalIgnoreCase) ? id : "msg_" + id);
            writer.WriteString("type", "message");
            writer.WriteString("role", "assistant");
            writer.WriteString("model", model);
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", content);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("stop_reason", stopReason);
            writer.WriteNull("stop_sequence");
            writer.WritePropertyName("usage");
            writer.WriteStartObject();
            writer.WriteNumber("input_tokens", usageElement.ValueKind == JsonValueKind.Object ? ReadInt(usageElement, "prompt_tokens", "input_tokens") : 0);
            writer.WriteNumber("output_tokens", usageElement.ValueKind == JsonValueKind.Object ? ReadInt(usageElement, "completion_tokens", "output_tokens") : 0);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static string ExtractOpenAiChoiceContent(JsonElement root)
        {
            if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var choice in choicesElement.EnumerateArray())
            {
                if (choice.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (choice.TryGetProperty("message", out var messageElement))
                {
                    var content = ReadString(messageElement, "content");
                    if (!string.IsNullOrEmpty(content))
                    {
                        return content;
                    }
                }

                if (choice.TryGetProperty("delta", out var deltaElement))
                {
                    var content = ReadString(deltaElement, "content");
                    if (!string.IsNullOrEmpty(content))
                    {
                        return content;
                    }
                }
            }

            return string.Empty;
        }

        private static string ExtractOpenAiFinishReason(JsonElement root)
        {
            if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var choice in choicesElement.EnumerateArray())
            {
                if (choice.ValueKind == JsonValueKind.Object)
                {
                    var finishReason = ReadString(choice, "finish_reason");
                    if (!string.IsNullOrWhiteSpace(finishReason))
                    {
                        return finishReason;
                    }
                }
            }

            return string.Empty;
        }

        private static string ConvertOpenAiFinishReasonToAnthropic(string finishReason)
        {
            return finishReason switch
            {
                "length" => "max_tokens",
                "tool_calls" => "tool_use",
                _ => "end_turn"
            };
        }

        private static byte[] BuildAnthropicSseResponse(string text, string model)
        {
            var id = "msg_" + Guid.NewGuid().ToString("N");
            var builder = new StringBuilder();
            AppendAnthropicSseEvent(builder, "message_start", new
            {
                type = "message_start",
                message = new
                {
                    id,
                    type = "message",
                    role = "assistant",
                    model,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new { input_tokens = 0, output_tokens = 0 }
                }
            });
            AppendAnthropicSseEvent(builder, "content_block_start", new
            {
                type = "content_block_start",
                index = 0,
                content_block = new { type = "text", text = string.Empty }
            });
            AppendAnthropicSseEvent(builder, "content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new { type = "text_delta", text }
            });
            AppendAnthropicSseEvent(builder, "content_block_stop", new { type = "content_block_stop", index = 0 });
            AppendAnthropicSseEvent(builder, "message_delta", new
            {
                type = "message_delta",
                delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
                usage = new { output_tokens = 0 }
            });
            AppendAnthropicSseEvent(builder, "message_stop", new { type = "message_stop" });
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static void AppendAnthropicSseEvent(StringBuilder builder, string eventName, object payload)
        {
            builder.Append("event: ").AppendLine(eventName);
            builder.Append("data: ").AppendLine(JsonSerializer.Serialize(payload));
            builder.AppendLine();
        }

        private static string BuildOpenAiStreamChunk(string id, string model, string content, string? finishReason, bool includeRole)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                writer.WriteStartObject();
                writer.WriteString("id", string.IsNullOrWhiteSpace(id) ? "chatcmpl-" + Guid.NewGuid().ToString("N") : id);
                writer.WriteString("object", "chat.completion.chunk");
                writer.WriteNumber("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                writer.WriteString("model", model ?? string.Empty);
                writer.WritePropertyName("choices");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber("index", 0);
                writer.WritePropertyName("delta");
                writer.WriteStartObject();
                if (includeRole)
                {
                    writer.WriteString("role", "assistant");
                }

                if (!string.IsNullOrEmpty(content))
                {
                    writer.WriteString("content", content);
                }

                writer.WriteEndObject();
                if (finishReason == null)
                {
                    writer.WriteNull("finish_reason");
                }
                else
                {
                    writer.WriteString("finish_reason", finishReason);
                }

                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(outputStream.ToArray());
        }

        private static string BuildOpenAiToolCallStreamChunk(StreamingProtocolConversionState state, ToolCallStreamState toolCall, bool includeIdentity, string argumentsDelta)
        {
            using var outputStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outputStream))
            {
                writer.WriteStartObject();
                writer.WriteString("id", string.IsNullOrWhiteSpace(state.Id) ? "chatcmpl-" + Guid.NewGuid().ToString("N") : state.Id);
                writer.WriteString("object", "chat.completion.chunk");
                writer.WriteNumber("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                writer.WriteString("model", state.Model ?? string.Empty);
                writer.WritePropertyName("choices");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber("index", 0);
                writer.WritePropertyName("delta");
                writer.WriteStartObject();
                if (!state.OpenAiRoleSent)
                {
                    writer.WriteString("role", "assistant");
                    state.OpenAiRoleSent = true;
                }

                writer.WritePropertyName("tool_calls");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber("index", toolCall.OpenAiIndex);
                if (includeIdentity)
                {
                    writer.WriteString("id", string.IsNullOrWhiteSpace(toolCall.Id) ? "call_" + Guid.NewGuid().ToString("N") : toolCall.Id);
                    writer.WriteString("type", "function");
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    writer.WriteString("name", toolCall.Name ?? string.Empty);
                    if (!string.IsNullOrEmpty(argumentsDelta))
                    {
                        writer.WriteString("arguments", argumentsDelta);
                    }

                    writer.WriteEndObject();
                }
                else
                {
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    writer.WriteString("arguments", argumentsDelta ?? string.Empty);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteNull("finish_reason");
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(outputStream.ToArray());
        }

        private static string ExtractAnthropicTextContent(JsonElement root)
        {
            if (!root.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && string.Equals(ReadString(item, "type"), "text", StringComparison.OrdinalIgnoreCase))
                {
                    var text = ReadString(item, "text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
            }

            return string.Join(string.Empty, parts);
        }

        private static string ExtractResponsesOutputText(JsonElement root)
        {
            if (root.TryGetProperty("output_text", out var outputTextElement) && outputTextElement.ValueKind == JsonValueKind.String)
            {
                return outputTextElement.GetString() ?? string.Empty;
            }

            if (!root.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var outputItem in outputElement.EnumerateArray())
            {
                if (outputItem.ValueKind != JsonValueKind.Object
                    || !outputItem.TryGetProperty("content", out var contentElement)
                    || contentElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.ValueKind == JsonValueKind.Object)
                    {
                        var text = ReadString(contentItem, "text");
                        if (!string.IsNullOrEmpty(text))
                        {
                            parts.Add(text);
                        }
                    }
                }
            }

            return string.Join(string.Empty, parts);
        }

        private static string ConvertResponsesStatusToOpenAiFinishReason(string status)
        {
            return status switch
            {
                "incomplete" => "length",
                "failed" => "stop",
                _ => "stop"
            };
        }

        private static string ConvertAnthropicStopReasonToOpenAi(string stopReason)
        {
            return stopReason switch
            {
                "end_turn" => "stop",
                "max_tokens" => "length",
                "tool_use" => "tool_calls",
                "stop_sequence" => "stop",
                _ => string.IsNullOrWhiteSpace(stopReason) ? "stop" : stopReason
            };
        }

        private static void ApplyProviderResponse(HttpListenerResponse response, HttpResponseMessage providerResponse, long? contentLength, string? contentType)
        {
            response.StatusCode = (int)providerResponse.StatusCode;
            if (contentLength.HasValue)
            {
                response.ContentLength64 = contentLength.Value;
            }
            else
            {
                response.SendChunked = true;
            }

            foreach (var header in providerResponse.Headers)
            {
                TryAddResponseHeader(response, header.Key, header.Value);
            }

            foreach (var header in providerResponse.Content.Headers)
            {
                TryAddResponseHeader(response, header.Key, header.Value);
            }

            if (!string.IsNullOrWhiteSpace(contentType))
            {
                response.ContentType = contentType;
            }

            AddCorsHeaders(response);
        }

        private static void TryAddResponseHeader(HttpListenerResponse response, string headerName, IEnumerable<string> values)
        {
            if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                response.Headers[headerName] = string.Join(", ", values);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, OpenAI-Beta, OpenAI-Organization, x-api-key, anthropic-version, anthropic-beta";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
        }
    }
}

