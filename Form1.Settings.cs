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
        private void SetRunningState(bool running)
        {
            startButton.Enabled = !running;
            stopButton.Enabled = running;
            statusValueLabel.Text = running ? GetText(TextId.Txt32) : GetText(TextId.Txt33);
            statusValueLabel.ForeColor = running ? UiTheme.Success : UiTheme.Danger;
            localUrlTextBox.ReadOnly = running;
        }

        private void StopRelay(bool clearRunIntent = true)
        {
            if (clearRunIntent)
            {
                relayShouldRun = false;
                SaveSettings();
            }

            listenerCancellation?.Cancel();

            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            listener = null;
            activeConfig = null;
            listenerCancellation?.Dispose();
            listenerCancellation = null;
            RestoreManagedToolConfigurations();
            DeactivateManagedToolRoutes();
            managedRuntimeRoutes = new Dictionary<string, ManagedRuntimeRoute>(StringComparer.Ordinal);
            SetRunningState(false);
        }

        private void LoadSettings()
        {
            RelaySettings? settings = null;

            try
            {
                if (File.Exists(settingsPath))
                {
                    settings = JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(settingsPath));
                    if (settings == null)
                    {
                        AppendInternalLog("Settings file deserialized to null; using defaults.");
                    }
                    else
                    {
                        localUrlTextBox.Text = string.IsNullOrWhiteSpace(settings.LocalUrl) ? localUrlTextBox.Text : settings.LocalUrl;
                        SelectRouteProtocol(serverProtocolComboBox, settings.RouteHelperServerProtocol);
                        SelectRouteProtocol(toolProtocolComboBox, settings.RouteHelperToolProtocol);
                        UpdateRouteUrlPreview();
                        autoStartRelayCheckBox.Checked = false;
                        relayShouldRun = settings.RelayShouldRun;
                        SetProtocolTraceVisible(settings.ProtocolTraceVisible, saveSettings: false);
                        currentLanguage = TryParseLanguage(settings.Language, out var language) ? language : AppLanguage.English;
                        SelectLanguage(currentLanguage);
                        savedUsageBubbleLocation = settings.UsageBubbleLocationX.HasValue && settings.UsageBubbleLocationY.HasValue
                            ? new Point(settings.UsageBubbleLocationX.Value, settings.UsageBubbleLocationY.Value)
                            : null;
                        providerConfigs.Clear();
                        if (settings.ProviderConfigs?.Count > 0)
                        {
                            foreach (var config in settings.ProviderConfigs)
                            {
                                providerConfigs[config.RouteKind] = config;
                            }
                        }
                        else
                        {
                            MigrateLegacyProviderSettings(settings);
                        }

                        EnsureProviderConfigDefaults();

                        registeredModels.Clear();
                        registeredModels.AddRange((settings.RegisteredModels ?? new List<RegisteredModelConfig>())
                            .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
                            .GroupBy(model => (model.Protocol, model.ModelId), new RegisteredModelIdentityComparer())
                            .Select(group => group.First()));
                        toolConfiguration = settings.ToolConfiguration ?? new ToolConfigurationSettings();
                        toolConfiguration.Claude ??= new ClaudeToolSettings();
                        toolConfiguration.Codex ??= new CodexToolSettings();
                    }
                }
                else
                {
                    AppendInternalLog("Settings file not found; using defaults.");
                }

                EnsureProviderConfigDefaults();
                LoadModelCosts(settings?.ModelCosts);
                AppendInternalLog($"Settings loaded. ProviderConfigs={providerConfigs.Count}; ModelCosts={modelCosts.Count}; AutoStart={autoStartRelayCheckBox.Checked}");
            }
            catch (Exception ex)
            {
                AppendInternalException("Failed to load settings.", ex);
                AppendLog(GetText(TextId.Txt91, ex.Message), true);
                LoadModelCosts(settings?.ModelCosts);
            }
        }

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
                modelCosts.Clear();
                modelCosts.AddRange(CreateDefaultModelCosts());
                SaveModelCosts();
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
                if (persistedByName.TryGetValue(defaultCost.ModelName, out var persistedCost) && persistedCost.Overwrite)
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
                Overwrite = cost.Overwrite
            };
        }

        private void SaveModelCosts()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(modelCostsPath)!);
                var costs = modelCosts
                    .Where(cost => !string.IsNullOrWhiteSpace(cost.ModelName))
                    .Select(cost => new ModelCostConfig
                    {
                        ModelName = cost.ModelName.Trim(),
                        InputCostPerMillion = cost.InputCostPerMillion,
                        OutputCostPerMillion = cost.OutputCostPerMillion,
                        CacheHitCostPerMillion = cost.CacheHitCostPerMillion,
                        CacheCreationCostPerMillion = cost.CacheCreationCostPerMillion,
                        Overwrite = cost.Overwrite
                    })
                    .ToList();

                File.WriteAllText(modelCostsPath, JsonSerializer.Serialize(costs, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                AppendInternalLog($"Model costs saved. ModelCosts={costs.Count}");
            }
            catch (Exception ex)
            {
                AppendInternalException("Failed to save model costs.", ex);
                AppendLog(GetText(TextId.Txt93, ex.Message), true);
            }
        }

        private void InitializeStorage()
        {
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(recordsDirectory);
            File.WriteAllText(internalLogPath, string.Empty, Encoding.UTF8);
            TrimOldLogs();
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

        private void TrimOldLogs()
        {
            var logFiles = new DirectoryInfo(logsDirectory)
                .GetFiles("*.txt")
                .Where(file => !file.Name.Equals("internal.txt", StringComparison.OrdinalIgnoreCase)
                    && !IsProtocolLogFile(file.FullName));

            foreach (var logFile in logFiles)
            {
                try
                {
                    logFile.Delete();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                CaptureUsageBubbleLocation();
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                var settings = new RelaySettings
                {
                    LocalUrl = NormalizePrefix(localUrlTextBox.Text),
                    Language = currentLanguage.ToString(),
                    RouteHelperServerProtocol = GetSelectedRouteProtocol(serverProtocolComboBox) ?? ApiRouteKind.ChatCompletions,
                    RouteHelperToolProtocol = GetSelectedRouteProtocol(toolProtocolComboBox),
                    AutoStartRelay = false,
                    RelayShouldRun = relayShouldRun,
                    ProtocolTraceVisible = protocolTraceVisible,
                    UsageBubbleLocationX = savedUsageBubbleLocation?.X,
                    UsageBubbleLocationY = savedUsageBubbleLocation?.Y,
                    RegisteredModels = registeredModels
                        .OrderBy(model => model.Protocol)
                        .ThenBy(model => model.ModelId, StringComparer.Ordinal)
                        .Select(model => new RegisteredModelConfig
                        {
                            Protocol = model.Protocol,
                            ModelId = model.ModelId,
                            DisplayName = model.DisplayName,
                            RegisteredAtUtc = model.RegisteredAtUtc
                        })
                        .ToList(),
                    ToolConfiguration = toolConfiguration,
                    ProviderConfigs = providerConfigs.Values
                        .OrderBy(config => config.RouteKind)
                        .Select(config => new ProviderEndpointConfig
                        {
                            RouteKind = config.RouteKind,
                            ProviderType = config.ProviderType,
                            ProviderUrl = config.ProviderUrl.Trim(),
                            ModelListUrl = config.ModelListUrlOverridden
                                ? config.ModelListUrl.Trim()
                                : BuildModelListUrl(config.RouteKind, config.ProviderUrl),
                            ModelListUrlOverridden = config.ModelListUrlOverridden,
                            AnthropicVersion = string.IsNullOrWhiteSpace(config.AnthropicVersion) ? "2023-06-01" : config.AnthropicVersion.Trim(),
                            ForceCache = config.ForceCache,
                            CacheOnConversion = config.CacheOnConversion
                        })
                        .ToList()
                };

                WriteAllTextAtomically(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                AppendInternalLog($"Settings saved. ProviderConfigs={settings.ProviderConfigs.Count}; AutoStart={settings.AutoStartRelay}");
            }
            catch (Exception ex)
            {
                AppendInternalException("Failed to save settings.", ex);
                AppendLog(GetText(TextId.Txt94, ex.Message), true);
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

        private void AppendLog(string message, bool includeTime)
        {
            var line = includeTime ? $"[{DateTime.Now:HH:mm:ss}] {message}" : message;
            logTextBox.AppendText(line + Environment.NewLine);
        }

        private void InitializeProtocolTraceControls()
        {
            protocolTracePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(22, 2, 3, 0),
                Padding = new Padding(8, 0, 0, 0),
                BackColor = Color.FromArgb(245, 248, 255),
                BorderStyle = BorderStyle.FixedSingle,
                WrapContents = false,
                Visible = false
            };

            protocolTraceCheckBox = new ThemeCheckBox
            {
                AutoSize = true,
                Checked = true,
                Margin = new Padding(3, 5, 10, 3),
                UseVisualStyleBackColor = true
            };

            openProtocolLogButton = new Button
            {
                AutoSize = true,
                Height = 26,
                Margin = new Padding(0, 2, 4, 2),
                UseVisualStyleBackColor = true
            };

            protocolTraceCheckBox.Click += ProtocolTraceCheckBox_Click;
            openProtocolLogButton.Click += OpenProtocolLogButton_Click;
            openLogButton.MouseUp += OpenLogButton_MouseUp;

            protocolTracePanel.Controls.Add(protocolTraceCheckBox);
            protocolTracePanel.Controls.Add(openProtocolLogButton);
            logOptionsPanel.Controls.Add(protocolTracePanel);
        }

        private void SetProtocolTraceVisible(bool visible, bool saveSettings)
        {
            protocolTraceVisible = visible;
            openLogRightClickCount = 0;

            if (protocolTracePanel != null)
            {
                protocolTracePanel.Visible = visible;
            }

            if (protocolTraceCheckBox != null)
            {
                protocolTraceCheckBox.Checked = visible;
            }

            if (saveSettings)
            {
                SaveSettings();
            }
        }

        private string EnsureProtocolLogFile()
        {
            lock (protocolLogLock)
            {
                Directory.CreateDirectory(logsDirectory);
                var activePath = GetActiveProtocolLogPath(logsDirectory, ProtocolLogFileCount);
                if (activePath == null)
                {
                    activePath = GetProtocolLogPath(logsDirectory, 1);
                    File.WriteAllText(activePath, string.Empty, new UTF8Encoding(false));
                }

                return activePath;
            }
        }

        private void AppendProtocolLog(string requestId, ProtocolTraceDirection direction, ApiRouteKind protocol, byte[] body)
        {
            AppendProtocolLog(requestId, direction, protocol, Encoding.UTF8.GetString(body));
        }

        private void AppendProtocolLog(string requestId, ProtocolTraceDirection direction, ApiRouteKind protocol, string body)
        {
            if (!protocolTraceVisible)
            {
                return;
            }

            try
            {
                var builder = new StringBuilder();
                builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Request={requestId}; Direction={direction}; Protocol={protocol}");
                builder.AppendLine(body);
                builder.AppendLine();

                lock (protocolLogLock)
                {
                    AppendRotatingProtocolLog(
                        logsDirectory,
                        builder.ToString(),
                        ProtocolLogFileMaxBytes,
                        ProtocolLogFileCount);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
            }
        }

        private bool IsProtocolLogFile(string path)
        {
            if (path.Equals(protocolLogPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            return fileName.StartsWith("protocol-trace-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(fileName["protocol-trace-".Length..], out var index)
                && index is >= 2 and <= ProtocolLogFileCount;
        }

        private static void AppendRotatingProtocolLog(string directory, string content, long maxFileBytes, int maxFileCount)
        {
            Directory.CreateDirectory(directory);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            var activePath = GetActiveProtocolLogPath(directory, maxFileCount);
            var activeIndex = activePath == null ? 1 : GetProtocolLogIndex(activePath);
            activePath ??= GetProtocolLogPath(directory, activeIndex);
            var offset = 0;

            while (offset < bytes.Length)
            {
                var currentLength = File.Exists(activePath) ? new FileInfo(activePath).Length : 0;
                if (currentLength >= maxFileBytes)
                {
                    activeIndex = activeIndex % maxFileCount + 1;
                    activePath = GetProtocolLogPath(directory, activeIndex);
                    File.Delete(activePath);
                    currentLength = 0;
                }

                var writeCount = (int)Math.Min(bytes.Length - offset, maxFileBytes - currentLength);
                using (var stream = new FileStream(activePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes, offset, writeCount);
                }

                File.SetLastWriteTimeUtc(activePath, DateTime.UtcNow);
                offset += writeCount;
            }
        }

        private static string? GetActiveProtocolLogPath(string directory, int maxFileCount)
        {
            return Enumerable.Range(1, maxFileCount)
                .Select(index => GetProtocolLogPath(directory, index))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static string GetProtocolLogPath(string directory, int index)
        {
            return Path.Combine(directory, index == 1 ? "protocol-trace.txt" : $"protocol-trace-{index}.txt");
        }

        private static int GetProtocolLogIndex(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            return fileName.Equals("protocol-trace", StringComparison.OrdinalIgnoreCase)
                ? 1
                : int.Parse(fileName["protocol-trace-".Length..], CultureInfo.InvariantCulture);
        }

        private bool TryBeginInvoke(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }

        private static string NormalizePrefix(string value)
        {
            var normalized = value.Trim();
            return normalized.EndsWith('/') ? normalized : normalized + "/";
        }

        private void InitializeRouteHelper()
        {
            RefreshRouteProtocolOptions();
            UpdateRouteUrlPreview();
        }

        private void RefreshRouteProtocolOptions()
        {
            var selectedServerProtocol = GetSelectedRouteProtocol(serverProtocolComboBox) ?? ApiRouteKind.Responses;
            var selectedToolProtocol = GetSelectedRouteProtocol(toolProtocolComboBox);

            serverProtocolComboBox.Items.Clear();
            toolProtocolComboBox.Items.Clear();

            foreach (var option in CreateRouteProtocolOptions(includeEmpty: false))
            {
                serverProtocolComboBox.Items.Add(option);
            }

            foreach (var option in CreateRouteProtocolOptions(includeEmpty: true))
            {
                toolProtocolComboBox.Items.Add(option);
            }

            SelectRouteProtocol(serverProtocolComboBox, selectedServerProtocol);
            SelectRouteProtocol(toolProtocolComboBox, selectedToolProtocol);
            if (serverProtocolComboBox.SelectedIndex < 0)
            {
                serverProtocolComboBox.SelectedIndex = 0;
            }

            if (toolProtocolComboBox.SelectedIndex < 0)
            {
                toolProtocolComboBox.SelectedIndex = 0;
            }

            UpdateRouteUrlPreview();
        }

        private List<RouteProtocolOption> CreateRouteProtocolOptions(bool includeEmpty)
        {
            var options = new List<RouteProtocolOption>();
            if (includeEmpty)
            {
                options.Add(new RouteProtocolOption(GetText(TextId.Txt34), null));
            }

            options.Add(new RouteProtocolOption("Responses", ApiRouteKind.Responses));
            options.Add(new RouteProtocolOption("Chat Completions", ApiRouteKind.ChatCompletions));
            options.Add(new RouteProtocolOption("Anthropic", ApiRouteKind.AnthropicMessages));
            return options;
        }

        private void UpdateRouteUrlPreview()
        {
            if (routeUrlTextBox == null)
            {
                return;
            }

            routeUrlTextBox.Text = BuildRouteUrlPreview();
        }

        private string BuildRouteUrlPreview()
        {
            var baseUrl = NormalizePrefix(localUrlTextBox.Text);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                return string.Empty;
            }

            var serverProtocol = GetSelectedRouteProtocol(serverProtocolComboBox) ?? ApiRouteKind.ChatCompletions;
            var toolProtocol = GetSelectedRouteProtocol(toolProtocolComboBox);
            var sameProtocolSelected = toolProtocol.HasValue && toolProtocol.Value == serverProtocol;
            var routeSegments = new List<string> { GetRoutePathSegment(serverProtocol) };
            if (toolProtocol.HasValue && !sameProtocolSelected)
            {
                routeSegments.Add(GetRoutePathSegment(toolProtocol.Value));
            }

            var basePath = NormalizePathSlashes(baseUri.AbsolutePath).Trim('/');
            var routePath = string.Join('/', routeSegments);
            var previewPath = string.IsNullOrEmpty(basePath) ? routePath : basePath + "/" + routePath;
            if (sameProtocolSelected)
            {
                previewPath += "/";
            }

            var builder = new UriBuilder(baseUri)
            {
                Path = previewPath,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString();
        }

        private static ApiRouteKind? GetSelectedRouteProtocol(ComboBox comboBox)
        {
            return comboBox.SelectedItem is RouteProtocolOption option ? option.RouteKind : null;
        }

        private static void SelectRouteProtocol(ComboBox comboBox, ApiRouteKind? routeKind)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is RouteProtocolOption option && option.RouteKind == routeKind)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
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

        private ProviderType GetSelectedProviderType()
        {
            return ProviderType.OpenAICompatible;
        }

        private void SetSelectedProviderType(string? providerType)
        {
        }

        private static string GetProviderTypeDisplayName(ProviderType providerType)
        {
            return providerType == ProviderType.Anthropic ? "Anthropic" : "OpenAI compatible (Chat Completions / Responses)";
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

        private static List<ModelCostConfig> CreateDefaultModelCosts()
        {
            return new List<ModelCostConfig>
            {
                CreateModelCost("claude-3-5-haiku-20241022", 0.80m, 4m, 0.08m, 1m),
                CreateModelCost("claude-3-5-sonnet-20241022", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("claude-haiku-4-5", 1m, 5m, 0.10m, 1.25m),
                CreateModelCost("claude-haiku-4-5-20251001", 1m, 5m, 0.10m, 1.25m),
                CreateModelCost("claude-mythos-5", 10m, 50m, 1m, 12.50m),
                CreateModelCost("claude-opus-4-20250514", 15m, 75m, 1.50m, 18.75m),
                CreateModelCost("claude-opus-4-1-20250805", 15m, 75m, 1.50m, 18.75m),
                CreateModelCost("claude-opus-4-5", 5m, 25m, 0.50m, 6.25m),
                CreateModelCost("claude-opus-4-5-20251101", 5m, 25m, 0.50m, 6.25m),
                CreateModelCost("claude-opus-4-6", 5m, 25m, 0.50m, 6.25m),
                CreateModelCost("claude-opus-4-6-20260206", 5m, 25m, 0.50m, 6.25m),
                CreateModelCost("claude-opus-4-7", 5m, 25m, 0.50m, 6.25m),
                CreateModelCost("claude-sonnet-4-20250514", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("claude-sonnet-4-5-20250929", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("claude-sonnet-4-6", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("claude-sonnet-4-6-20260217", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("claude-sonnet-5", 3m, 15m, 0.30m, 3.75m),
                CreateModelCost("codestral-2508", 0.30m, 0.90m, 0.03m, 0m),
                CreateModelCost("codex-mini", 0.75m, 3m, 0.025m, 0m),
                CreateModelCost("command-a", 2.50m, 10m, 0m, 0m),
                CreateModelCost("command-r", 0.15m, 0.60m, 0m, 0m),
                CreateModelCost("command-r-plus", 2.50m, 10m, 0m, 0m),
                CreateModelCost("deepseek-chat", 0.14m, 0.28m, 0.0028m, 0m),
                CreateModelCost("deepseek-reasoner", 0.14m, 0.28m, 0.0028m, 0m),
                CreateModelCost("deepseek-v3", 0.28m, 1.11m, 0.028m, 0m),
                CreateModelCost("deepseek-v3.1", 0.55m, 1.67m, 0.055m, 0m),
                CreateModelCost("deepseek-v3.2", 0.28m, 0.42m, 0.028m, 0m),
                CreateModelCost("deepseek-v4-flash", 0.14m, 0.28m, 0.0028m, 0m),
                CreateModelCost("deepseek-v4-pro", 0.435m, 0.87m, 0.003625m, 0m),
                CreateModelCost("devstral-2-2512", 0.40m, 0.90m, 0.04m, 0m),
                CreateModelCost("devstral-medium", 0.40m, 2m, 0.04m, 0m),
                CreateModelCost("devstral-small-1.1", 0.07m, 0.28m, 0.01m, 0m),
                CreateModelCost("doubao-seed-2-0-code", 0.47m, 2.37m, 0m, 0m),
                CreateModelCost("doubao-seed-2-0-lite", 0.25m, 2m, 0m, 0m),
                CreateModelCost("doubao-seed-2-0-mini", 0.03m, 0.31m, 0m, 0m),
                CreateModelCost("doubao-seed-2-0-pro", 0.47m, 2.37m, 0m, 0m),
                CreateModelCost("doubao-seed-code", 0.17m, 1.11m, 0.02m, 0m),
                CreateModelCost("glm-4.6", 0.28m, 1.11m, 0.03m, 0m),
                CreateModelCost("glm-4.7", 0.39m, 1.75m, 0.04m, 0m),
                CreateModelCost("glm-5", 0.72m, 2.30m, 0m, 0m),
                CreateModelCost("glm-5.1", 0.95m, 3.15m, 0m, 0m),
                CreateModelCost("gpt-4.1", 2m, 8m, 0.50m, 0m),
                CreateModelCost("gpt-4.1-mini", 0.40m, 1.60m, 0.10m, 0m),
                CreateModelCost("gpt-4.1-nano", 0.10m, 0.40m, 0.025m, 0m),
                CreateModelCost("gpt-5", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-low", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-medium", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-high", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-minimal", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-low", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-medium", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-high", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-mini", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-mini-medium", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-codex-mini-high", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5-mini", 0.25m, 2m, 0.025m, 0m),
                CreateModelCost("gpt-5-nano", 0.05m, 0.40m, 0.005m, 0m),
                CreateModelCost("gpt-5.1", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-low", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-medium", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-high", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-minimal", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-codex", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-codex-mini", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-codex-max", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-codex-max-high", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.1-codex-max-xhigh", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gpt-5.2", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-low", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-medium", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-high", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-xhigh", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-codex", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-codex-low", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-codex-medium", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-codex-high", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.2-codex-xhigh", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.3-codex", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.3-codex-low", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.3-codex-medium", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.3-codex-high", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.3-codex-xhigh", 1.75m, 14m, 0.175m, 0m),
                CreateModelCost("gpt-5.4", 2.50m, 15m, 0.25m, 0m),
                CreateModelCost("gpt-5.4-mini", 0.75m, 4.50m, 0.075m, 0m),
                CreateModelCost("gpt-5.4-nano", 0.20m, 1.25m, 0.02m, 0m),
                CreateModelCost("gpt-5.4-pro", 30m, 180m, 3m, 0m),
                CreateModelCost("gpt-5.5", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gpt-5.5-pro", 30m, 180m, 3m, 0m),
                CreateModelCost("gpt-5.5-low", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gpt-5.5-medium", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gpt-5.5-high", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gpt-5.5-xhigh", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gpt-5.5-minimal", 5m, 30m, 0.50m, 0m),
                CreateModelCost("gemini-2.0-flash", 0.10m, 0.40m, 0.025m, 0m),
                CreateModelCost("gemini-2.5-flash", 0.3m, 2.5m, 0.03m, 0m),
                CreateModelCost("gemini-2.5-flash-lite", 0.10m, 0.40m, 0.01m, 0m),
                CreateModelCost("gemini-2.5-pro", 1.25m, 10m, 0.125m, 0m),
                CreateModelCost("gemini-3-flash-preview", 0.5m, 3m, 0.05m, 0m),
                CreateModelCost("gemini-3-pro-preview", 2m, 12m, 0.2m, 0m),
                CreateModelCost("gemini-3.1-flash-lite-preview", 0.25m, 1.50m, 0.025m, 0m),
                CreateModelCost("gemini-3.1-pro-preview", 2m, 12m, 0.20m, 0m),
                CreateModelCost("gemini-3.5-flash", 1.50m, 9m, 0m, 0m),
                CreateModelCost("grok-3", 3m, 15m, 0.75m, 0m),
                CreateModelCost("grok-3-mini", 0.25m, 0.50m, 0.075m, 0m),
                CreateModelCost("grok-4", 3m, 15m, 0.75m, 0m),
                CreateModelCost("grok-4-1-fast-non-reasoning", 0.20m, 0.50m, 0.05m, 0m),
                CreateModelCost("grok-4-1-fast-reasoning", 0.20m, 0.50m, 0.05m, 0m),
                CreateModelCost("grok-4.20-0309-non-reasoning", 1.25m, 2.50m, 0.20m, 0m),
                CreateModelCost("grok-4.20-0309-reasoning", 1.25m, 2.50m, 0.20m, 0m),
                CreateModelCost("grok-code-fast-1", 0.20m, 1.50m, 0.02m, 0m),
                CreateModelCost("kimi-k2-0905", 0.55m, 2.20m, 0.10m, 0m),
                CreateModelCost("kimi-k2-thinking", 0.55m, 2.20m, 0.10m, 0m),
                CreateModelCost("kimi-k2-turbo", 1.11m, 8.06m, 0.14m, 0m),
                CreateModelCost("kimi-k2.5", 0.60m, 2.50m, 0.10m, 0m),
                CreateModelCost("kimi-k2.6", 0.95m, 4.00m, 0.16m, 0m),
                CreateModelCost("magistral-medium", 2m, 5m, 0m, 0m),
                CreateModelCost("mimo-v2-flash", 0.09m, 0.29m, 0.009m, 0m),
                CreateModelCost("mimo-v2-pro", 1m, 3m, 0m, 0m),
                CreateModelCost("minimax-m2", 0.27m, 0.95m, 0.03m, 0m),
                CreateModelCost("minimax-m2.1", 0.27m, 0.95m, 0.03m, 0m),
                CreateModelCost("minimax-m2.1-lightning", 0.27m, 2.33m, 0.03m, 0m),
                CreateModelCost("minimax-m2.5", 0.12m, 0.95m, 0.03m, 0m),
                CreateModelCost("minimax-m2.5-lightning", 0.30m, 2.40m, 0.03m, 0m),
                CreateModelCost("minimax-m2.7", 0.30m, 1.20m, 0.06m, 0.375m),
                CreateModelCost("minimax-m2.7-highspeed", 0.60m, 2.40m, 0.06m, 0.375m),
                CreateModelCost("mistral-large-3-2512", 0.50m, 1.50m, 0.05m, 0m),
                CreateModelCost("mistral-medium-3.1", 0.40m, 2m, 0.04m, 0m),
                CreateModelCost("mistral-small-3.2-24b", 0.075m, 0.20m, 0.01m, 0m),
                CreateModelCost("o1", 15m, 60m, 7.50m, 0m),
                CreateModelCost("o1-mini", 0.55m, 2.20m, 0.55m, 0m),
                CreateModelCost("o1-pro", 15m, 60m, 1.50m, 0m),
                CreateModelCost("o3", 2m, 8m, 0.50m, 0m),
                CreateModelCost("o3-deep-research", 10m, 40m, 1m, 0m),
                CreateModelCost("o3-mini", 0.55m, 2.20m, 0.55m, 0m),
                CreateModelCost("o3-pro", 20m, 80m, 0m, 0m),
                CreateModelCost("o4-mini", 1.10m, 4.40m, 0.275m, 0m),
                CreateModelCost("o4-mini-deep-research", 2m, 8m, 0.20m, 0m),
                CreateModelCost("qwq-32b", 0.20m, 0.60m, 0m, 0m),
                CreateModelCost("qwq-plus", 0.80m, 2.40m, 0m, 0m),
                CreateModelCost("qwen3-235b-a22b", 0.70m, 8.40m, 0m, 0m),
                CreateModelCost("qwen3-32b", 0.16m, 0.64m, 0m, 0m),
                CreateModelCost("qwen3-coder-flash", 0.195m, 0.975m, 0m, 0m),
                CreateModelCost("qwen3-coder-next", 0.12m, 0.75m, 0m, 0m),
                CreateModelCost("qwen3-coder-plus", 0.65m, 3.25m, 0m, 0m),
                CreateModelCost("qwen3-max", 0.78m, 3.90m, 0m, 0m),
                CreateModelCost("qwen3.5-plus", 0.26m, 1.56m, 0m, 0m),
                CreateModelCost("qwen3.6-plus", 0.325m, 1.95m, 0m, 0m),
                CreateModelCost("step-3.5-flash", 0.10m, 0.30m, 0.02m, 0m),
                CreateModelCost("claude-opus-4-8", 5m, 25m, 0.5m, 6.25m),
                CreateModelCost("claude-fable-5", 10m, 50m, 1m, 12.5m),
                CreateModelCost("glm-5.2", 1.18m, 4.14m, 0.3m, 0m),
                CreateModelCost("gpt-5.6-luna", 1m, 6m, 0.1m, 1.25m),
                CreateModelCost("gpt-5.6-terra", 2.5m, 15m, 0.25m, 3.125m),
                CreateModelCost("gpt-5.6-sol", 5m, 30m, 0.5m, 6.25m)
            };
        }

        private static ModelCostConfig CreateModelCost(string modelName, decimal inputCost, decimal outputCost, decimal cacheHitCost, decimal cacheCreationCost)
        {
            return new ModelCostConfig
            {
                ModelName = modelName,
                InputCostPerMillion = inputCost,
                OutputCostPerMillion = outputCost,
                CacheHitCostPerMillion = cacheHitCost,
                CacheCreationCostPerMillion = cacheCreationCost
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
}

