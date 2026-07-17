using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace APIRelay
{
    public partial class Form1
    {
        private const string ManagedModelAliasPrefix = "apirelay-v1";
        private static readonly byte[] ProviderSecretEntropy = Encoding.UTF8.GetBytes("APIRelay.ProviderSecrets.v1");
        private GroupBox managedToolsGroupBox = null!;
        private Button claudeTabButton = null!;
        private Button codexTabButton = null!;
        private Button registerModelsButton = null!;
        private Panel managedToolContentPanel = null!;
        private TableLayoutPanel claudePanel = null!;
        private TableLayoutPanel codexPanel = null!;
        private ComboBox claudeHaikuComboBox = null!;
        private ComboBox claudeSonnetComboBox = null!;
        private ComboBox claudeOpusComboBox = null!;
        private CheckBox claudeToolSearchCheckBox = null!;
        private CheckBox claudeMaximumEffortCheckBox = null!;
        private CheckBox claudeEnabledCheckBox = null!;
        private ComboBox codexModelComboBox = null!;
        private TextBox codexEffortTextBox = null!;
        private CheckBox codexEnabledCheckBox = null!;
        private Label claudeHaikuLabel = null!;
        private Label claudeSonnetLabel = null!;
        private Label claudeOpusLabel = null!;
        private Label codexModelLabel = null!;
        private Label codexEffortLabel = null!;
        private bool updatingManagedToolControls;
        private bool previousClaudeEnabled;
        private bool previousCodexEnabled;

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
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                providerApiKeys.Remove(protocol);
            }
            else
            {
                providerApiKeys[protocol] = apiKey.Trim();
            }

            var secrets = providerApiKeys
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
                    return new ModelDiscoveryResult(new(), $"HTTP {(int)response.StatusCode}: {ReadProviderError(body)}");
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
                return new ModelDiscoveryResult(new(), ex.Message);
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

        private sealed record ModelDiscoveryResult(List<ModelListItem> Models, string Error);

        private void InitializeManagedToolControls()
        {
            managedToolsGroupBox = new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8) };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            claudeTabButton = CreateToolTabButton();
            codexTabButton = CreateToolTabButton();
            claudeTabButton.Click += (_, _) => SelectManagedToolTab(ManagedToolKind.Claude);
            codexTabButton.Click += (_, _) => SelectManagedToolTab(ManagedToolKind.Codex);
            registerModelsButton = new Button { Dock = DockStyle.Fill, Margin = new Padding(3, 1, 0, 3) };
            registerModelsButton.Click += (_, _) => ShowModelRegistrationDialog();
            header.Controls.Add(claudeTabButton, 0, 0);
            header.Controls.Add(codexTabButton, 1, 0);
            header.Controls.Add(registerModelsButton, 3, 0);
            root.Controls.Add(header, 0, 0);

            managedToolContentPanel = new Panel { Dock = DockStyle.Fill };
            claudePanel = CreateClaudeToolPanel();
            codexPanel = CreateCodexToolPanel();
            managedToolContentPanel.Controls.Add(codexPanel);
            managedToolContentPanel.Controls.Add(claudePanel);
            root.Controls.Add(managedToolContentPanel, 0, 1);
            managedToolsGroupBox.Controls.Add(root);

            mainLayout.SuspendLayout();
            for (var row = 6; row >= 2; row--)
            {
                foreach (Control control in mainLayout.Controls.Cast<Control>().Where(control => mainLayout.GetRow(control) == row).ToList())
                {
                    mainLayout.SetRow(control, row + 1);
                }
            }
            mainLayout.RowCount = 8;
            mainLayout.RowStyles.Insert(2, new RowStyle(SizeType.Absolute, 188));
            mainLayout.Controls.Add(managedToolsGroupBox, 0, 2);
            mainLayout.ResumeLayout(true);
            MinimumSize = new Size(MinimumSize.Width, 900);
            Height = Math.Min(Height + 188, Screen.FromControl(this).WorkingArea.Height);

            SelectManagedToolTab(ManagedToolKind.Claude);
        }

        private static Button CreateToolTabButton()
        {
            return new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 1, 3, 3),
                UseVisualStyleBackColor = false
            };
        }

        private TableLayoutPanel CreateClaudeToolPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Padding = new Padding(0, 4, 0, 0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            claudeHaikuLabel = CreateManagedLabel();
            claudeSonnetLabel = CreateManagedLabel();
            claudeOpusLabel = CreateManagedLabel();
            claudeHaikuComboBox = CreateModelComboBox();
            claudeSonnetComboBox = CreateModelComboBox();
            claudeOpusComboBox = CreateModelComboBox();
            panel.Controls.Add(claudeHaikuLabel, 0, 0);
            panel.Controls.Add(claudeHaikuComboBox, 1, 0);
            panel.Controls.Add(claudeSonnetLabel, 2, 0);
            panel.Controls.Add(claudeSonnetComboBox, 3, 0);
            panel.Controls.Add(claudeOpusLabel, 4, 0);
            panel.Controls.Add(claudeOpusComboBox, 5, 0);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            panel.SetColumnSpan(options, 6);
            claudeToolSearchCheckBox = new ThemeCheckBox { AutoSize = true, Margin = new Padding(3, 8, 18, 3) };
            claudeMaximumEffortCheckBox = new ThemeCheckBox { AutoSize = true, Margin = new Padding(3, 8, 18, 3) };
            claudeEnabledCheckBox = CreateEnableToggle();
            options.Controls.Add(claudeToolSearchCheckBox);
            options.Controls.Add(claudeMaximumEffortCheckBox);
            options.Controls.Add(claudeEnabledCheckBox);
            panel.Controls.Add(options, 0, 1);

            claudeHaikuComboBox.SelectedIndexChanged += (_, _) => ManagedToolControlChanged();
            claudeSonnetComboBox.SelectedIndexChanged += (_, _) => ManagedToolControlChanged();
            claudeOpusComboBox.SelectedIndexChanged += (_, _) => ManagedToolControlChanged();
            claudeToolSearchCheckBox.CheckedChanged += (_, _) => ManagedToolControlChanged();
            claudeMaximumEffortCheckBox.CheckedChanged += (_, _) => ManagedToolControlChanged();
            claudeEnabledCheckBox.CheckedChanged += (_, _) => ManagedToolControlChanged();
            return panel;
        }

        private TableLayoutPanel CreateCodexToolPanel()
        {
            // Two rows so the enabled toggle gets its own line and the model/effort controls
            // sit on a shared baseline. Labels fill their cell (vertically centered) while the
            // combo/text controls dock to fill so their baselines align instead of hugging the top.
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(0, 6, 0, 0),
                Visible = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            codexModelLabel = CreateManagedLabel();
            codexEffortLabel = CreateManagedLabel();
            codexModelComboBox = CreateModelComboBox();
            codexModelComboBox.Dock = DockStyle.Fill;
            codexModelComboBox.Margin = new Padding(3, 6, 14, 6);
            codexEffortTextBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 7, 3, 7) };
            codexEnabledCheckBox = CreateEnableToggle();
            codexEnabledCheckBox.Anchor = AnchorStyles.Left;
            codexEnabledCheckBox.Margin = new Padding(3, 4, 3, 0);

            panel.Controls.Add(codexModelLabel, 0, 0);
            panel.Controls.Add(codexModelComboBox, 1, 0);
            panel.Controls.Add(codexEffortLabel, 2, 0);
            panel.Controls.Add(codexEffortTextBox, 3, 0);
            panel.Controls.Add(codexEnabledCheckBox, 0, 1);
            panel.SetColumnSpan(codexEnabledCheckBox, 4);

            codexModelComboBox.SelectedIndexChanged += (_, _) => ManagedToolControlChanged();
            codexEffortTextBox.TextChanged += (_, _) => ManagedToolControlChanged();
            codexEnabledCheckBox.CheckedChanged += (_, _) => ManagedToolControlChanged();
            return panel;
        }

        private static Label CreateManagedLabel() => new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

        private ComboBox CreateModelComboBox()
        {
            var comboBox = new ThemeComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                // The owner-painted closed state renders GetItemText(); point it at DisplayName
                // so ManagedModelOption records don't fall back to their ToString().
                DisplayMember = nameof(ManagedModelOption.DisplayName),
                Margin = new Padding(3, 6, 12, 3),
                ItemHeight = 22
            };
            comboBox.DrawItem += ModelComboBox_DrawItem;
            comboBox.SelectionChangeCommitted += ModelComboBox_SelectionChangeCommitted;
            return comboBox;
        }

        private static CheckBox CreateEnableToggle()
        {
            return new CheckBox
            {
                Appearance = Appearance.Button,
                AutoSize = false,
                Size = new Size(86, 32),
                Margin = new Padding(3, 5, 3, 3),
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private void SelectManagedToolTab(ManagedToolKind tool)
        {
            var claudeSelected = tool == ManagedToolKind.Claude;
            claudePanel.Visible = claudeSelected;
            codexPanel.Visible = !claudeSelected;
            claudePanel.BringToFront();
            if (!claudeSelected)
            {
                codexPanel.BringToFront();
            }
            claudeTabButton.BackColor = claudeSelected ? UiTheme.PanelAlt : UiTheme.Surface;
            codexTabButton.BackColor = claudeSelected ? UiTheme.Surface : UiTheme.PanelAlt;
            claudeTabButton.ForeColor = claudeSelected ? UiTheme.Accent : UiTheme.TextSecondary;
            codexTabButton.ForeColor = claudeSelected ? UiTheme.TextSecondary : UiTheme.Accent;
        }

        private void RefreshManagedToolControls()
        {
            if (claudeHaikuComboBox == null)
            {
                return;
            }

            updatingManagedToolControls = true;
            PopulateModelComboBox(claudeHaikuComboBox, toolConfiguration.Claude.HaikuModelAlias);
            PopulateModelComboBox(claudeSonnetComboBox, toolConfiguration.Claude.SonnetModelAlias);
            PopulateModelComboBox(claudeOpusComboBox, toolConfiguration.Claude.OpusModelAlias);
            PopulateModelComboBox(codexModelComboBox, toolConfiguration.Codex.ModelAlias);
            claudeToolSearchCheckBox.Checked = toolConfiguration.Claude.EnableToolSearch;
            claudeMaximumEffortCheckBox.Checked = toolConfiguration.Claude.UseMaximumEffort;
            claudeEnabledCheckBox.Checked = toolConfiguration.Claude.Enabled;
            codexEffortTextBox.Text = toolConfiguration.Codex.ReasoningEffort;
            codexEnabledCheckBox.Checked = toolConfiguration.Codex.Enabled;
            var claudeSelectionsValid = IsValidModelSelection(claudeHaikuComboBox)
                && IsValidModelSelection(claudeSonnetComboBox)
                && IsValidModelSelection(claudeOpusComboBox);
            var codexSelectionValid = IsValidModelSelection(codexModelComboBox);
            var enabledStateChanged = false;
            if (!claudeSelectionsValid)
            {
                claudeEnabledCheckBox.Checked = false;
                enabledStateChanged = toolConfiguration.Claude.Enabled;
                toolConfiguration.Claude.Enabled = false;
            }
            if (!codexSelectionValid)
            {
                codexEnabledCheckBox.Checked = false;
                enabledStateChanged |= toolConfiguration.Codex.Enabled;
                toolConfiguration.Codex.Enabled = false;
            }
            previousClaudeEnabled = claudeEnabledCheckBox.Checked;
            previousCodexEnabled = codexEnabledCheckBox.Checked;
            updatingManagedToolControls = false;
            if ((!claudeSelectionsValid || !codexSelectionValid) && listener != null)
            {
                ReapplyManagedConfigurationsWhileRunning();
            }
            if (enabledStateChanged)
            {
                SaveSettings();
            }
            UpdateManagedToggleText();
        }

        private void DeactivateManagedToolRoutes()
        {
            if (claudeEnabledCheckBox == null)
            {
                return;
            }

            updatingManagedToolControls = true;
            claudeEnabledCheckBox.Checked = false;
            codexEnabledCheckBox.Checked = false;
            toolConfiguration.Claude.Enabled = false;
            toolConfiguration.Codex.Enabled = false;
            previousClaudeEnabled = false;
            previousCodexEnabled = false;
            updatingManagedToolControls = false;
            UpdateManagedToggleText();
        }

        private void PopulateModelComboBox(ComboBox comboBox, string selectedAlias)
        {
            comboBox.Items.Clear();
            foreach (var protocolGroup in registeredModels.OrderBy(model => model.Protocol).GroupBy(model => model.Protocol))
            {
                comboBox.Items.Add(new ManagedModelOption(GetRouteKindDisplayName(protocolGroup.Key), string.Empty, true, false));
                foreach (var model in protocolGroup.OrderBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase))
                {
                    comboBox.Items.Add(new ManagedModelOption(model.ModelId, CreateManagedModelAlias(model.Protocol, model.ModelId), false, false));
                }
            }

            var selected = comboBox.Items.Cast<ManagedModelOption>().FirstOrDefault(item => !item.IsHeading && item.Alias.Equals(selectedAlias, StringComparison.Ordinal));
            if (selected == null && !string.IsNullOrWhiteSpace(selectedAlias))
            {
                selected = new ManagedModelOption($"Invalid: {DescribeAlias(selectedAlias)}", selectedAlias, false, true);
                comboBox.Items.Insert(0, selected);
            }
            comboBox.SelectedItem = selected;
        }

        private static string DescribeAlias(string alias)
        {
            return TryParseManagedModelAlias(alias, out var protocol, out var modelId)
                ? $"{GetRouteKindDisplayName(protocol)} / {modelId}"
                : alias;
        }

        private void ModelComboBox_DrawItem(object? sender, DrawItemEventArgs e)
        {
            // Owner-draw the drop-down rows ourselves so they match the dark theme
            // (the system e.DrawBackground() would paint them white).
            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var back = new SolidBrush(isSelected ? UiTheme.GridSelection : UiTheme.Surface);
            e.Graphics.FillRectangle(back, e.Bounds);
            if (sender is ComboBox comboBox && e.Index >= 0 && comboBox.Items[e.Index] is ManagedModelOption item)
            {
                var baseFont = e.Font ?? comboBox.Font;
                var font = item.IsHeading ? new Font(baseFont, FontStyle.Bold) : baseFont;
                var color = item.IsInvalid ? UiTheme.Danger : item.IsHeading ? UiTheme.TextSecondary : (isSelected ? UiTheme.Text : UiTheme.Text);
                var bounds = item.IsHeading ? e.Bounds : new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, item.DisplayName, font, bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (item.IsHeading)
                {
                    font.Dispose();
                }
            }
            e.DrawFocusRectangle();
        }

        private void ModelComboBox_SelectionChangeCommitted(object? sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ManagedModelOption { IsHeading: true })
            {
                comboBox.SelectedIndex = -1;
            }
        }

        private void ManagedToolControlChanged()
        {
            if (updatingManagedToolControls)
            {
                return;
            }

            if ((claudeEnabledCheckBox.Checked || codexEnabledCheckBox.Checked) && listener == null)
            {
                updatingManagedToolControls = true;
                claudeEnabledCheckBox.Checked = false;
                codexEnabledCheckBox.Checked = false;
                updatingManagedToolControls = false;
                UpdateManagedToggleText();
                MessageBox.Show(currentLanguage == AppLanguage.Chinese ? "请先开启代理。" : "Start the relay first.", "APIRelay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var haikuValid = TryGetSelectedAlias(claudeHaikuComboBox, out var haiku);
            var sonnetValid = TryGetSelectedAlias(claudeSonnetComboBox, out var sonnet);
            var opusValid = TryGetSelectedAlias(claudeOpusComboBox, out var opus);
            var claudeValid = haikuValid && sonnetValid && opusValid;
            var codexValid = TryGetSelectedAlias(codexModelComboBox, out var codexModel);
            if (claudeEnabledCheckBox.Checked && !claudeValid)
            {
                updatingManagedToolControls = true;
                claudeEnabledCheckBox.Checked = false;
                updatingManagedToolControls = false;
                MessageBox.Show(currentLanguage == AppLanguage.Chinese ? "请先为 Claude 选择三个有效的已注册模型。" : "Select three valid registered models for Claude first.", "APIRelay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (codexEnabledCheckBox.Checked && !codexValid)
            {
                updatingManagedToolControls = true;
                codexEnabledCheckBox.Checked = false;
                updatingManagedToolControls = false;
                MessageBox.Show(currentLanguage == AppLanguage.Chinese ? "请先为 Codex 选择有效的已注册模型。" : "Select a valid registered model for Codex first.", "APIRelay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            toolConfiguration.Claude.HaikuModelAlias = haiku;
            toolConfiguration.Claude.SonnetModelAlias = sonnet;
            toolConfiguration.Claude.OpusModelAlias = opus;
            toolConfiguration.Claude.EnableToolSearch = claudeToolSearchCheckBox.Checked;
            toolConfiguration.Claude.UseMaximumEffort = claudeMaximumEffortCheckBox.Checked;
            toolConfiguration.Claude.Enabled = claudeEnabledCheckBox.Checked && claudeValid;
            toolConfiguration.Codex.ModelAlias = codexModel;
            toolConfiguration.Codex.ReasoningEffort = codexEffortTextBox.Text.Trim();
            toolConfiguration.Codex.Enabled = codexEnabledCheckBox.Checked && codexValid;
            SaveSettings();
            if (listener != null)
            {
                ReapplyManagedConfigurationsWhileRunning();
            }
            previousClaudeEnabled = toolConfiguration.Claude.Enabled;
            previousCodexEnabled = toolConfiguration.Codex.Enabled;
            UpdateManagedToggleText();
        }

        private static bool IsValidModelSelection(ComboBox comboBox)
        {
            return comboBox.SelectedItem is ManagedModelOption { IsHeading: false, IsInvalid: false };
        }

        private void ReapplyManagedConfigurationsWhileRunning()
        {
            if (activeConfig == null)
            {
                return;
            }

            try
            {
                RestoreManagedToolConfigurations();
                ApplyEnabledManagedToolConfigurations(activeConfig.LocalUri);
            }
            catch (Exception ex)
            {
                managedRuntimeRoutes = new Dictionary<string, ManagedRuntimeRoute>(StringComparer.Ordinal);
                AppendInternalException("Failed to update managed tool configuration while relay is running.", ex);
                MessageBox.Show(ex.Message, "APIRelay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool TryGetSelectedAlias(ComboBox comboBox, out string alias)
        {
            if (comboBox.SelectedItem is ManagedModelOption { IsHeading: false, IsInvalid: false } option)
            {
                alias = option.Alias;
                return true;
            }
            alias = comboBox.SelectedItem is ManagedModelOption selected ? selected.Alias : string.Empty;
            return false;
        }

        private void ApplyManagedToolLanguage()
        {
            if (managedToolsGroupBox == null)
            {
                return;
            }
            var chinese = currentLanguage == AppLanguage.Chinese;
            managedToolsGroupBox.Text = chinese ? "工具模型路由" : "Tool Model Routing";
            claudeTabButton.Text = "Claude";
            codexTabButton.Text = "Codex";
            registerModelsButton.Text = chinese ? "注册模型" : "Register Models";
            claudeHaikuLabel.Text = "HAIKU";
            claudeSonnetLabel.Text = "SONNET";
            claudeOpusLabel.Text = "OPUS";
            claudeToolSearchCheckBox.Text = chinese ? "启动工具搜索" : "Enable tool search";
            claudeMaximumEffortCheckBox.Text = chinese ? "最大档位思考" : "Maximum effort";
            codexModelLabel.Text = chinese ? "模型" : "Model";
            codexEffortLabel.Text = chinese ? "思考档位" : "Reasoning effort";
            UpdateManagedToggleText();
        }

        private void UpdateManagedToggleText()
        {
            if (claudeEnabledCheckBox == null)
            {
                return;
            }
            var chinese = currentLanguage == AppLanguage.Chinese;
            claudeEnabledCheckBox.Text = claudeEnabledCheckBox.Checked ? (chinese ? "已开启" : "Enabled") : (chinese ? "已关闭" : "Disabled");
            codexEnabledCheckBox.Text = codexEnabledCheckBox.Checked ? (chinese ? "已开启" : "Enabled") : (chinese ? "已关闭" : "Disabled");
            StyleEnableToggle(claudeEnabledCheckBox);
            StyleEnableToggle(codexEnabledCheckBox);
        }

        private static void StyleEnableToggle(CheckBox toggle)
        {
            toggle.BackColor = toggle.Checked ? UiTheme.Success : UiTheme.Surface;
            toggle.ForeColor = toggle.Checked ? Color.FromArgb(0x12, 0x33, 0x1F) : UiTheme.TextSecondary;
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 1;
            toggle.FlatAppearance.BorderColor = toggle.Checked ? UiTheme.Success : UiTheme.Border;
            toggle.TextAlign = ContentAlignment.MiddleCenter;
            toggle.Padding = Padding.Empty;
        }

        private sealed record ManagedModelOption(string DisplayName, string Alias, bool IsHeading, bool IsInvalid);

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
                    ApplyManagedConfiguration(ManagedToolKind.Codex, GetCodexSettingsPath(), BuildCodexSettings(localUri));
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
                aliases.Add(toolConfiguration.Codex.ModelAlias);
            }

            foreach (var alias in aliases.Distinct(StringComparer.Ordinal))
            {
                if (!TryParseManagedModelAlias(alias, out var protocol, out var modelId))
                {
                    throw new InvalidOperationException($"Managed model alias is invalid: {alias}");
                }

                var registration = registeredModels.FirstOrDefault(model => model.Protocol == protocol && StringComparer.Ordinal.Equals(model.ModelId, modelId))
                    ?? throw new InvalidOperationException($"Managed model is no longer registered: {GetRouteKindDisplayName(protocol)} / {modelId}");
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
                    registration.ModelId,
                    new Uri(providerUrl),
                    apiKey,
                    endpoint.AnthropicVersion,
                    endpoint.ForceCache,
                    endpoint.CacheOnConversion);
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

                resolvedRoute = new RelayRoute(managedRoute.Protocol, toolRoute.FromProtocol, true, managedRoute);
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Managed tool request JSON is invalid: {ex.Message}";
                return false;
            }
        }

        private void ApplyManagedConfiguration(ManagedToolKind tool, string targetPath, byte[] managedContent)
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
                WriteAllTextAtomically(markerPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }

            WriteAllBytesAtomically(targetPath, managedContent);
        }

        private void RestoreManagedToolConfigurations()
        {
            foreach (var tool in Enum.GetValues<ManagedToolKind>())
            {
                var markerPath = GetManagedMarkerPath(tool);
                if (File.Exists(markerPath))
                {
                    RestoreManagedToolConfiguration(markerPath);
                }
            }
        }

        private void RestoreManagedToolConfiguration(string markerPath)
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
                File.Delete(markerPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                AppendInternalException("Failed to restore managed tool configuration.", ex);
            }
        }

        private static void RestoreManagedConfigurationFiles(ManagedConfigurationApplyState state)
        {
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

        private static byte[] BuildCodexSettingsContent(string existingContent, Uri localUri, CodexToolSettings settings)
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
            root["model_reasoning_effort"] = settings.ReasoningEffort;
            var providers = root.TryGetValue("model_providers", out var providersValue) && providersValue is TomlTable existingProviders
                ? existingProviders
                : new TomlTable();
            root["model_providers"] = providers;
            var relayProvider = providers.TryGetValue("apirelay", out var relayValue) && relayValue is TomlTable existingRelay
                ? existingRelay
                : new TomlTable();
            providers["apirelay"] = relayProvider;
            relayProvider["name"] = "APIRelay";
            relayProvider["wire_api"] = "responses";
            relayProvider["requires_openai_auth"] = false;
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
        private static string GetCodexSettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");

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
}