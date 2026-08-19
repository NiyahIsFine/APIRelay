using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APIRelay
{
    public partial class Form1
    {
        private Task StartRelayAsync()
        {
            if (listener != null)
            {
                AppendInternalLog("Start requested but listener is already running.");
                return Task.CompletedTask;
            }

            if (!Uri.TryCreate(NormalizePrefix(localUrlTextBox.Text), UriKind.Absolute, out var localUri) || localUri.Scheme != Uri.UriSchemeHttp)
            {
                AppendInternalLog($"Start rejected because local URL is invalid. Value={localUrlTextBox.Text}");
                relayShouldRun = false;
                SaveSettings();
                MessageBox.Show(GetText(TextId.Txt57), GetText(TextId.Txt58), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.CompletedTask;
            }

            if (!IsLocalPortAvailable(localUri.Port))
            {
                AppendInternalLog($"Start rejected because port {localUri.Port} is already in use.");
                relayShouldRun = false;
                SaveSettings();
                MessageBox.Show(GetText(TextId.Txt133, localUri.Port), GetText(TextId.Txt63), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.CompletedTask;
            }

            activeConfig = new RelayConfig(localUri);

            listenerCancellation = new CancellationTokenSource();
            listener = new HttpListener();
            listener.Prefixes.Add(activeConfig.LocalUri.ToString());

            try
            {
                ApplyEnabledManagedToolConfigurations(activeConfig.LocalUri);
                listener.Start();
                relayShouldRun = true;
                SaveSettings();
                AppendInternalLog($"Listener started. LocalUri={activeConfig.LocalUri}");
                SetRunningState(true);
                AppendLog(GetText(TextId.Txt59, activeConfig.LocalUri), true);
                AppendLog(GetText(TextId.Txt60), true);
                AppendLog(GetText(TextId.Txt61, internalLogPath), true);
                _ = Task.Run(() => ListenLoopAsync(listener, listenerCancellation.Token));
            }
            catch (HttpListenerException ex)
            {
                AppendInternalException("Listener failed to start.", ex);
                StopRelay();
                MessageBox.Show(GetText(TextId.Txt62, ex.Message), GetText(TextId.Txt63), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ObjectDisposedException)
            {
                AppendInternalLog("Listener start ignored because listener was disposed.");
                StopRelay();
            }
            catch (Exception ex)
            {
                AppendInternalException("Unexpected listener startup failure.", ex);
                StopRelay();
                MessageBox.Show(GetText(TextId.Txt64, ex.Message), GetText(TextId.Txt65), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return Task.CompletedTask;
        }

        private static bool IsLocalPortAvailable(int port)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private async Task ListenLoopAsync(HttpListener activeListener, CancellationToken cancellationToken)
        {
            AppendInternalLog("Listen loop entered.");
            while (!cancellationToken.IsCancellationRequested && activeListener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await activeListener.GetContextAsync();
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    AppendInternalLog("Listen loop stopping after listener cancellation.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    AppendInternalLog("Listen loop stopping because listener was disposed.");
                    break;
                }

                AppendInternalLog($"Accepted request. Method={context.Request.HttpMethod}; Path={context.Request.Url?.PathAndQuery ?? string.Empty}");
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            }

            AppendInternalLog("Listen loop exited.");
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;
            var response = context.Response;
            var requestId = Guid.NewGuid().ToString("N")[..8];
            AppendInternalLog($"Request {requestId} started. Method={request.HttpMethod}; Path={request.Url?.PathAndQuery ?? string.Empty}");

            try
            {
                AddCorsHeaders(response);

                if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    AppendInternalLog($"Request {requestId} completed as CORS preflight.");
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                var requestBody = await ReadRequestBodyAsync(request, cancellationToken);
                var relayRoute = ResolveRelayRoute(request);
                if (relayRoute.IsManagedTool)
                {
                    if (!TryResolveManagedRoute(requestBody, relayRoute, out relayRoute, out var managedError))
                    {
                        TryClose(response, 400, BuildAuthErrorBody(managedError, "api_relay_invalid_managed_model"), "application/json; charset=utf-8");
                        return;
                    }
                }
                AppendInternalLog($"Request {requestId} route resolved. To={relayRoute.ToProtocol}; From={relayRoute.FromProtocol}; Managed={relayRoute.IsManagedTool}");

                var endpoint = GetEndpointForRoute(relayRoute);
                if (!relayRoute.IsManagedTool && !TryValidateClientApiKey(request, endpoint, out var authStatusCode, out var authErrorBody))
                {
                    AppendInternalLog($"Request {requestId} rejected by API key validation. Status={authStatusCode}; Route={relayRoute.ToProtocol}");
                    TryClose(response, authStatusCode, authErrorBody, "application/json; charset=utf-8");
                    TryBeginInvoke(() => AppendLog(GetText(TextId.Txt66), true));
                    return;
                }

                var requestSummary = BuildRequestSummary(request, requestBody);
                AppendInternalLog($"Request {requestId} client request read. {BuildRequestDiagnostics(request, requestBody)}");
                TryBeginInvoke(() => AppendLog(GetText(TextId.Txt67, requestSummary), true));

                using var providerRequest = BuildProviderRequest(request, requestBody, relayRoute, out var providerRequestBody);
                var providerUri = providerRequest.RequestUri!;
                AppendInternalLog($"Request {requestId} provider request built. Target={providerUri}; BodyBytes={providerRequestBody.Length}");
                if (requestBody.Length > 0)
                {
                    AppendProtocolLog(requestId, ProtocolTraceDirection.ClientToTool, relayRoute.FromProtocol, requestBody);
                    if (relayRoute.FromProtocol != relayRoute.ToProtocol)
                    {
                        AppendProtocolLog(requestId, ProtocolTraceDirection.ToolToServer, relayRoute.ToProtocol, providerRequestBody);
                    }
                }

                var stopwatch = Stopwatch.StartNew();
                AppendInternalLog($"Request {requestId} sending provider request.");
                using var providerResponse = await HttpClient.SendAsync(providerRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var headerWaitMs = stopwatch.ElapsedMilliseconds;
                AppendInternalLog($"Request {requestId} provider response headers received. Status={(int)providerResponse.StatusCode}; MediaType={providerResponse.Content.Headers.ContentType?.MediaType ?? string.Empty}; HeaderWaitMs={headerWaitMs}; {BuildProviderResponseHeaderDiagnostics(providerResponse)}");
                var responseMediaType = providerResponse.Content.Headers.ContentType?.MediaType;
                byte[] responseBytes = Array.Empty<byte>();
                ClientResponseBody? convertedResponse = null;
                long firstResponseMs;
                var usage = UsageInfo.Empty;
                if (ShouldConvertResponse(relayRoute) && IsEventStream(responseMediaType))
                {
                    AppendInternalLog($"Request {requestId} streaming with protocol conversion started.");
                    ApplyProviderResponse(response, providerResponse, null, "text/event-stream; charset=utf-8");
                    var streamingResult = await StreamConvertedProviderResponseToClientAsync(requestId, providerResponse, response, relayRoute, requestBody, stopwatch, cancellationToken);
                    firstResponseMs = streamingResult.FirstByteMs;
                    usage = streamingResult.Usage;
                    AppendInternalLog($"Request {requestId} streaming conversion completed. ProviderBytes={streamingResult.ProviderBytes}; ClientBytes={streamingResult.ClientBytes}; HeaderWaitMs={headerWaitMs}; FirstResponseMs={firstResponseMs}");
                }
                else if (ShouldStreamProviderResponse(providerResponse, relayRoute))
                {
                    AppendInternalLog($"Request {requestId} passthrough streaming started.");
                    ApplyProviderResponse(response, providerResponse, null, responseMediaType);
                    var streamingResult = await StreamProviderResponseToClientAsync(requestId, providerResponse, response, relayRoute, stopwatch, cancellationToken);
                    firstResponseMs = streamingResult.FirstByteMs;
                    usage = streamingResult.Usage;
                    AppendInternalLog($"Request {requestId} passthrough streaming completed. ProviderBytes={streamingResult.ProviderBytes}; HeaderWaitMs={headerWaitMs}; FirstResponseMs={firstResponseMs}");
                }
                else
                {
                    AppendInternalLog($"Request {requestId} non-stream response reading started.");
                    (responseBytes, firstResponseMs) = await ReadProviderResponseWithFirstByteTimingAsync(providerResponse, stopwatch, cancellationToken);
                    AppendInternalLog($"Request {requestId} provider body read. ProviderBytes={responseBytes.Length}; HeaderWaitMs={headerWaitMs}; FirstResponseMs={firstResponseMs}");
                    if (responseBytes.Length > 0)
                    {
                        AppendProtocolLog(requestId, ProtocolTraceDirection.ServerToTool, relayRoute.ToProtocol, responseBytes);
                    }

                    var clientResponse = BuildClientResponse(responseBytes, responseMediaType, relayRoute, requestBody, IsModelListRequest(request));
                    AppendInternalLog($"Request {requestId} client response built. ClientBytes={clientResponse.Body.Length}; ContentType={clientResponse.ContentType ?? string.Empty}");
                    convertedResponse = ShouldConvertResponse(relayRoute) ? clientResponse : null;
                    if (relayRoute.FromProtocol != relayRoute.ToProtocol && clientResponse.Body.Length > 0)
                    {
                        AppendProtocolLog(requestId, ProtocolTraceDirection.ToolToClient, relayRoute.FromProtocol, clientResponse.Body);
                    }

                    ApplyProviderResponse(response, providerResponse, clientResponse.Body.Length, clientResponse.ContentType);
                    await WriteResponseBodyAsync(response, clientResponse.Body, cancellationToken);
                    AppendInternalLog($"Request {requestId} client response body written.");
                    usage = ExtractUsage(responseBytes, providerResponse.Content.Headers.ContentType?.MediaType);
                }

                var elapsedMs = stopwatch.ElapsedMilliseconds;
                AppendInternalLog($"Request {requestId} usage extracted. Prompt={usage.PromptTokens}; Completion={usage.CompletionTokens}; Total={usage.TotalTokens}; HeaderWaitMs={headerWaitMs}; FirstResponseMs={firstResponseMs}; ElapsedMs={elapsedMs}");
                response.Close();
                AppendInternalLog($"Request {requestId} response closed. Status={(int)providerResponse.StatusCode}; ElapsedMs={elapsedMs}");

                RecordRequest(request, providerUri.PathAndQuery, (int)providerResponse.StatusCode, usage, elapsedMs, firstResponseMs, requestBody, relayRoute.ManagedRoute?.ModelId);
                TryBeginInvoke(() => AppendLog(GetText(TextId.Txt68, BuildResponseSummary((int)providerResponse.StatusCode, usage, elapsedMs)), true));
            }
            catch (OperationCanceledException)
            {
                AppendInternalLog($"Request {requestId} canceled.");
                TryClose(response, 499, Encoding.UTF8.GetBytes(GetText(TextId.Txt69)));
            }
            catch (Exception ex)
            {
                AppendInternalException($"Request {requestId} failed.", ex);
                var body = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    error = new
                    {
                        message = GetText(TextId.Txt70, ex.Message),
                        type = "api_relay_error"
                    }
                });

                TryClose(response, 502, body, "application/json; charset=utf-8");
                TryBeginInvoke(() => AppendLog(GetText(TextId.Txt71, ex.Message), true));
            }
        }

        private static async Task<byte[]> ReadRequestBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
        {
            if (!request.HasEntityBody)
            {
                return Array.Empty<byte>();
            }

            using var memoryStream = new MemoryStream();
            await request.InputStream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        private static async Task<(byte[] Body, long FirstByteMs)> ReadProviderResponseWithFirstByteTimingAsync(HttpResponseMessage providerResponse, Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            await using var responseStream = await providerResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long firstByteMs = 0;

            while (true)
            {
                var read = await responseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (firstByteMs == 0)
                {
                    firstByteMs = stopwatch.ElapsedMilliseconds;
                }

                memoryStream.Write(buffer, 0, read);
            }

            return (memoryStream.ToArray(), firstByteMs);
        }

        private async Task<StreamingRelayResult> StreamProviderResponseToClientAsync(string requestId, HttpResponseMessage providerResponse, HttpListenerResponse localResponse, RelayRoute relayRoute, Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            try
            {
                await using var responseStream = await providerResponse.Content.ReadAsStreamAsync(cancellationToken);
                var responseEncoding = TryGetResponseEncoding(providerResponse);
                var buffer = new byte[81920];
                var usageAccumulator = new StreamingUsageAccumulator(responseEncoding);
                var traceAccumulator = new StreamingProtocolTraceAccumulator(responseEncoding, body => AppendProtocolLog(requestId, ProtocolTraceDirection.ServerToTool, relayRoute.ToProtocol, body));
                long firstByteMs = 0;
                long providerBytes = 0;
                var progress = new StreamingProgressLogger(this, requestId, "passthrough", stopwatch);

                while (true)
                {
                    var read = await responseStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (firstByteMs == 0)
                    {
                        firstByteMs = stopwatch.ElapsedMilliseconds;
                    }

                    await localResponse.OutputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    await localResponse.OutputStream.FlushAsync(cancellationToken);
                    providerBytes += read;
                    traceAccumulator.AppendBytes(buffer, read);
                    usageAccumulator.AppendBytes(buffer, read);
                    progress.Report(providerBytes, providerBytes, force: false);
                }

                progress.Report(providerBytes, providerBytes, force: true);
                traceAccumulator.Complete();
                usageAccumulator.Complete();
                return new StreamingRelayResult(firstByteMs, usageAccumulator.Usage, providerBytes, providerBytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppendInternalException("Passthrough streaming failed while reading provider or writing client.", ex);
                throw;
            }
        }

        private async Task<StreamingRelayResult> StreamConvertedProviderResponseToClientAsync(string requestId, HttpResponseMessage providerResponse, HttpListenerResponse localResponse, RelayRoute relayRoute, byte[] requestBody, Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            try
            {
                await using var responseStream = await providerResponse.Content.ReadAsStreamAsync(cancellationToken);
                var responseEncoding = TryGetResponseEncoding(providerResponse);
                using var reader = new StreamReader(responseStream, responseEncoding);
                var eventData = new StringBuilder();
                var state = new StreamingProtocolConversionState(ExtractRequestModel(requestBody));
                var usageAccumulator = new StreamingUsageAccumulator(responseEncoding);
                long firstByteMs = 0;
                long providerBytes = 0;
                long clientBytes = 0;
                var progress = new StreamingProgressLogger(this, requestId, "conversion", stopwatch);

                while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
                {
                    var line = rawLine.TrimEnd('\r');
                    providerBytes += responseEncoding.GetByteCount(line) + 1;
                    usageAccumulator.AppendLine(line);

                    if (line.Length == 0)
                    {
                        var writeResult = await WriteConvertedSseEventAsync(eventData.ToString(), relayRoute, state, localResponse, stopwatch, firstByteMs, cancellationToken);
                        firstByteMs = writeResult.FirstByteMs;
                        clientBytes += writeResult.BytesWritten;
                        if (eventData.Length > 0)
                        {
                            AppendProtocolLog(requestId, ProtocolTraceDirection.ServerToTool, relayRoute.ToProtocol, eventData.ToString());
                        }

                        if (writeResult.Payload.Length > 0)
                        {
                            AppendProtocolLog(requestId, ProtocolTraceDirection.ToolToClient, relayRoute.FromProtocol, writeResult.Payload);
                        }

                        progress.Report(providerBytes, clientBytes, force: false);
                        eventData.Clear();
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventData.AppendLine(line[5..].TrimStart());
                    }
                }

                var finalWriteResult = await WriteConvertedSseEventAsync(eventData.ToString(), relayRoute, state, localResponse, stopwatch, firstByteMs, cancellationToken);
                firstByteMs = finalWriteResult.FirstByteMs;
                clientBytes += finalWriteResult.BytesWritten;
                if (eventData.Length > 0)
                {
                    AppendProtocolLog(requestId, ProtocolTraceDirection.ServerToTool, relayRoute.ToProtocol, eventData.ToString());
                }

                if (finalWriteResult.Payload.Length > 0)
                {
                    AppendProtocolLog(requestId, ProtocolTraceDirection.ToolToClient, relayRoute.FromProtocol, finalWriteResult.Payload);
                }

                var completionWriteResult = await WriteConvertedSseCompletionAsync(relayRoute, state, localResponse, cancellationToken);
                clientBytes += completionWriteResult.BytesWritten;
                if (completionWriteResult.Payload.Length > 0)
                {
                    AppendProtocolLog(requestId, ProtocolTraceDirection.ToolToClient, relayRoute.FromProtocol, completionWriteResult.Payload);
                }

                progress.Report(providerBytes, clientBytes, force: true);
                usageAccumulator.Complete();
                return new StreamingRelayResult(firstByteMs, MergeUsage(usageAccumulator.Usage, state.Usage), providerBytes, clientBytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppendInternalException("Converted streaming failed while reading provider, converting protocol, or writing client.", ex);
                throw;
            }
        }

        private async Task<(long FirstByteMs, long BytesWritten, string Payload)> WriteConvertedSseEventAsync(string eventData, RelayRoute relayRoute, StreamingProtocolConversionState state, HttpListenerResponse localResponse, Stopwatch stopwatch, long firstByteMs, CancellationToken cancellationToken)
        {
            string payload;
            try
            {
                payload = ConvertStreamingEvent(eventData, relayRoute, state);
            }
            catch (Exception ex)
            {
                AppendInternalException("Streaming event conversion threw unexpectedly.", ex);
                throw;
            }

            if (payload.Length == 0)
            {
                return (firstByteMs, 0, string.Empty);
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                await localResponse.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
                await localResponse.OutputStream.FlushAsync(cancellationToken);
                return (firstByteMs == 0 ? stopwatch.ElapsedMilliseconds : firstByteMs, bytes.Length, payload);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppendInternalException("Writing converted streaming event to client failed.", ex);
                throw;
            }
        }

        private async Task<(long BytesWritten, string Payload)> WriteConvertedSseCompletionAsync(RelayRoute relayRoute, StreamingProtocolConversionState state, HttpListenerResponse localResponse, CancellationToken cancellationToken)
        {
            var payload = BuildStreamingCompletionEvent(relayRoute.FromProtocol, state);
            if (payload.Length == 0)
            {
                return (0, string.Empty);
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            await localResponse.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
            await localResponse.OutputStream.FlushAsync(cancellationToken);
            return (bytes.Length, payload);
        }

    }
}

