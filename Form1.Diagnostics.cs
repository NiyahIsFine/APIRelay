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
        private static string GetApplicationRootDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (directory.GetFiles("*.csproj").Length > 0)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory;
        }

        private static string BuildRequestSummary(HttpListenerRequest request, byte[] requestBody)
        {
            var content = ExtractRequestContent(requestBody);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return TruncateForLog(content, 20);
            }

            return TruncateForLog($"{request.HttpMethod} {request.Url?.AbsolutePath ?? "/"}", 20);
        }

        private static string BuildRequestDiagnostics(HttpListenerRequest request, byte[] requestBody)
        {
            var diagnostics = new List<string>
            {
                $"BodyBytes={requestBody.Length}",
                $"ContentType={request.ContentType ?? string.Empty}",
                $"Path={request.Url?.PathAndQuery ?? string.Empty}"
            };

            if (TryReadRequestDiagnostics(requestBody, out var model, out var stream, out var messageCount, out var inputItemCount, out var maxTokens))
            {
                diagnostics.Add($"Model={model}");
                diagnostics.Add($"Stream={stream}");
                diagnostics.Add($"Messages={messageCount}");
                diagnostics.Add($"InputItems={inputItemCount}");
                diagnostics.Add($"MaxTokens={maxTokens}");
            }

            return string.Join("; ", diagnostics);
        }

        private static bool TryReadRequestDiagnostics(byte[] requestBody, out string model, out string stream, out int messageCount, out int inputItemCount, out int maxTokens)
        {
            model = string.Empty;
            stream = "unknown";
            messageCount = 0;
            inputItemCount = 0;
            maxTokens = 0;

            if (requestBody.Length == 0)
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(requestBody);
                var root = document.RootElement;
                model = ReadString(root, "model");
                stream = ReadBoolDiagnostic(root, "stream");
                messageCount = ReadArrayCount(root, "messages");
                inputItemCount = ReadArrayCount(root, "input");
                maxTokens = ReadInt(root, "max_tokens", "max_completion_tokens", "maxOutputTokens", "max_output_tokens");
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string BuildProviderResponseHeaderDiagnostics(HttpResponseMessage providerResponse)
        {
            var diagnostics = new List<string>
            {
                $"ContentLength={providerResponse.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
                $"TransferEncoding={string.Join(",", providerResponse.Headers.TransferEncoding.Select(value => value.Value))}",
                $"ProviderRequestId={ReadHeader(providerResponse, "x-request-id", "request-id", "cf-ray")}",
                $"RateLimitRemaining={ReadHeader(providerResponse, "x-ratelimit-remaining", "x-ratelimit-remaining-requests", "anthropic-ratelimit-requests-remaining")}",
                $"RateLimitReset={ReadHeader(providerResponse, "x-ratelimit-reset", "x-ratelimit-reset-requests", "anthropic-ratelimit-requests-reset")}",
            };

            return string.Join("; ", diagnostics);
        }

        private static string ReadBoolDiagnostic(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            {
                return "unknown";
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => "unknown"
            };
        }

        private static int ReadArrayCount(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Array
                    ? value.GetArrayLength()
                    : 0;
        }

        private static string ReadHeader(HttpResponseMessage response, params string[] headerNames)
        {
            foreach (var headerName in headerNames)
            {
                if (response.Headers.TryGetValues(headerName, out var values) || response.Content.Headers.TryGetValues(headerName, out values))
                {
                    return string.Join(",", values);
                }
            }

            return string.Empty;
        }

        private string BuildResponseSummary(int statusCode, UsageInfo usage, long elapsedMs)
        {
            var text = HasBillableUsage(usage)
                ? GetText(TextId.Txt77, statusCode, FormatDuration(elapsedMs), usage.CompletionTokens)
                : $"{statusCode} {FormatDuration(elapsedMs)}";

            return TruncateForLog(text, 20);
        }

        private static string ExtractRequestContent(byte[] requestBody)
        {
            if (requestBody.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(requestBody);
                var root = document.RootElement;

                if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
                {
                    var model = modelElement.GetString();
                    var content = ExtractPromptText(root);
                    return string.IsNullOrWhiteSpace(content) ? model ?? string.Empty : $"{model} {content}";
                }

                return ExtractPromptText(root);
            }
            catch (JsonException)
            {
                return Encoding.UTF8.GetString(requestBody);
            }
            catch (DecoderFallbackException)
            {
                return string.Empty;
            }
        }

        private static string ExtractRequestModel(byte[] requestBody)
        {
            if (requestBody.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(requestBody);
                return ReadString(document.RootElement, "model");
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ExtractPromptText(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray().Reverse())
                {
                    var text = ExtractContentText(message);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            if (root.TryGetProperty("input", out var input))
            {
                var text = ExtractElementText(input);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (root.TryGetProperty("prompt", out var prompt))
            {
                return ExtractElementText(prompt);
            }

            return string.Empty;
        }

        private static string ExtractContentText(JsonElement message)
        {
            if (message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content))
            {
                return ExtractElementText(content);
            }

            return ExtractElementText(message);
        }

        private static string ExtractElementText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(" ", element.EnumerateArray().Select(ExtractElementText).Where(text => !string.IsNullOrWhiteSpace(text))),
                JsonValueKind.Object => ExtractObjectText(element),
                _ => string.Empty
            };
        }

        private static string ExtractObjectText(JsonElement element)
        {
            foreach (var propertyName in new[] { "text", "input_text", "output_text", "content" })
            {
                if (element.TryGetProperty(propertyName, out var value))
                {
                    var text = ExtractElementText(value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static string TruncateForLog(string value, int maxLength)
        {
            var compact = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= maxLength ? compact : compact[..maxLength];
        }

        private static Encoding TryGetResponseEncoding(HttpResponseMessage response)
        {
            var charset = response.Content.Headers.ContentType?.CharSet;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    return Encoding.GetEncoding(charset.Trim('"'));
                }
                catch (ArgumentException)
                {
                }
            }

            return Encoding.UTF8;
        }

        private static void TryClose(HttpListenerResponse response, int statusCode, byte[] body, string contentType = "text/plain; charset=utf-8")
        {
            try
            {
                AddCorsHeaders(response);
                response.StatusCode = statusCode;
                response.ContentType = contentType;
                response.ContentLength64 = body.Length;
                response.OutputStream.Write(body);
                response.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}

