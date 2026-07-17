namespace APIRelay
{
    public partial class Form1
    {
        private void ShowModelRegistrationDialog()
        {
            var availableConfigs = providerConfigs.Values
                .Where(config => Uri.TryCreate(config.ProviderUrl, UriKind.Absolute, out var providerUri)
                    && (providerUri.Scheme == Uri.UriSchemeHttp || providerUri.Scheme == Uri.UriSchemeHttps))
                .Where(config =>
                {
                    var modelListUrl = config.ModelListUrlOverridden ? config.ModelListUrl : BuildModelListUrl(config.RouteKind, config.ProviderUrl);
                    return Uri.TryCreate(modelListUrl, UriKind.Absolute, out var modelListUri)
                        && (modelListUri.Scheme == Uri.UriSchemeHttp || modelListUri.Scheme == Uri.UriSchemeHttps);
                })
                .OrderBy(config => config.RouteKind)
                .ToList();

            using var dialog = new ModelRegistrationForm(
                availableConfigs,
                registeredModels,
                currentLanguage,
                DiscoverModelsAsync,
                (protocol, apiKey) => SaveProviderApiKey(protocol, apiKey),
                models =>
                {
                    registeredModels.Clear();
                    registeredModels.AddRange(models);
                    SaveSettings();
                });
            dialog.ShowDialog(this);
            RefreshManagedToolControls();
        }

        private sealed class ModelRegistrationForm : Form
        {
            private readonly IReadOnlyList<ProviderEndpointConfig> configs;
            private readonly List<RegisteredModelConfig> registrations;
            private readonly Func<ProviderEndpointConfig, string, CancellationToken, Task<ModelDiscoveryResult>> discover;
            private readonly Action<ApiRouteKind, string> saveApiKey;
            private readonly Action<IReadOnlyList<RegisteredModelConfig>> saveRegistrations;
            private readonly ComboBox protocolComboBox = new ThemeComboBox();
            private readonly Button refreshButton = new();
            private readonly ListBox availableList = new();
            private readonly ListBox registeredList = new();
            private readonly Button addButton = new();
            private readonly Button removeButton = new();
            private readonly Label statusLabel = new();
            private List<ModelListItem> fetchedModels = new();
            private bool hasSuccessfulRefresh;
            private readonly AppLanguage language;

            public ModelRegistrationForm(
                IReadOnlyList<ProviderEndpointConfig> configs,
                IEnumerable<RegisteredModelConfig> registrations,
                AppLanguage language,
                Func<ProviderEndpointConfig, string, CancellationToken, Task<ModelDiscoveryResult>> discover,
                Action<ApiRouteKind, string> saveApiKey,
                Action<IReadOnlyList<RegisteredModelConfig>> saveRegistrations)
            {
                this.configs = configs;
                this.language = language;
                this.registrations = registrations.Select(CloneRegistration).ToList();
                this.discover = discover;
                this.saveApiKey = saveApiKey;
                this.saveRegistrations = saveRegistrations;

                Text = language == AppLanguage.Chinese ? "注册模型" : "Register Models";
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(760, 500);
                Size = new Size(900, 600);
                Padding = new Padding(12);

                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                header.Controls.Add(new Label
                {
                    AutoSize = true,
                    Margin = new Padding(0, 8, 8, 0),
                    Text = language == AppLanguage.Chinese ? "协议" : "Protocol"
                });
                protocolComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                protocolComboBox.Width = 220;
                protocolComboBox.DisplayMember = nameof(ProtocolChoice.DisplayName);
                protocolComboBox.SelectedIndexChanged += (_, _) => OnProtocolChanged();
                foreach (var config in configs)
                {
                    protocolComboBox.Items.Add(new ProtocolChoice(config.RouteKind, GetRouteKindDisplayName(config.RouteKind)));
                }
                header.Controls.Add(protocolComboBox);
                refreshButton.Text = language == AppLanguage.Chinese ? "刷新" : "Refresh";
                refreshButton.AutoSize = true;
                refreshButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                refreshButton.MinimumSize = new Size(
                    UiTheme.GetButtonWidth(refreshButton.Text),
                    UiTheme.GetButtonHeight());
                refreshButton.Click += async (_, _) => await RefreshModelsAsync(language);
                header.Controls.Add(refreshButton);
                root.Controls.Add(header, 0, 0);

                var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
                columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
                columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                columns.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                columns.Controls.Add(new Label { Dock = DockStyle.Fill, Text = language == AppLanguage.Chinese ? "可注册模型" : "Available Models", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                columns.Controls.Add(new Label { Dock = DockStyle.Fill, Text = language == AppLanguage.Chinese ? "已注册模型" : "Registered Models", TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
                availableList.Dock = DockStyle.Fill;
                availableList.DisplayMember = nameof(ModelListChoice.DisplayName);
                availableList.DrawMode = DrawMode.OwnerDrawFixed;
                availableList.DrawItem += AvailableList_DrawItem;
                availableList.DoubleClick += (_, _) => AddSelected();
                columns.Controls.Add(availableList, 0, 1);
                registeredList.Dock = DockStyle.Fill;
                registeredList.DrawMode = DrawMode.OwnerDrawFixed;
                registeredList.DrawItem += RegisteredList_DrawItem;
                registeredList.DoubleClick += (_, _) => RemoveSelected();
                columns.Controls.Add(registeredList, 2, 1);
                var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(9, 110, 9, 0), WrapContents = false };
                addButton.Text = "+";
                addButton.Size = new Size(44, UiTheme.GetButtonHeight());
                addButton.Click += (_, _) => AddSelected();
                removeButton.Text = "-";
                removeButton.Size = new Size(44, UiTheme.GetButtonHeight());
                removeButton.Click += (_, _) => RemoveSelected();
                commands.Controls.Add(addButton);
                commands.Controls.Add(removeButton);
                columns.Controls.Add(commands, 1, 1);
                root.Controls.Add(columns, 0, 1);

                statusLabel.Dock = DockStyle.Fill;
                statusLabel.AutoEllipsis = true;
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;
                root.Controls.Add(statusLabel, 0, 2);
                Controls.Add(root);

                if (protocolComboBox.Items.Count > 0)
                {
                    protocolComboBox.SelectedIndex = 0;
                }
                else
                {
                    refreshButton.Enabled = false;
                    statusLabel.Text = language == AppLanguage.Chinese ? "请先配置供应商 URL。" : "Configure a provider URL first.";
                }

                UiTheme.StyleDialog(this);
            }

            private ApiRouteKind? SelectedProtocol => protocolComboBox.SelectedItem is ProtocolChoice choice ? choice.Protocol : null;

            private void OnProtocolChanged()
            {
                fetchedModels.Clear();
                hasSuccessfulRefresh = false;
                statusLabel.Text = string.Empty;
                RefreshLists();
            }

            private async Task RefreshModelsAsync(AppLanguage language)
            {
                if (SelectedProtocol is not ApiRouteKind protocol)
                {
                    return;
                }

                using var prompt = new ApiKeyPromptForm(language);
                if (prompt.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                saveApiKey(protocol, prompt.ApiKey);
                refreshButton.Enabled = false;
                statusLabel.ForeColor = UiTheme.TextSecondary;
                statusLabel.Text = language == AppLanguage.Chinese ? "正在获取模型..." : "Loading models...";
                var result = await discover(configs.First(config => config.RouteKind == protocol), prompt.ApiKey, CancellationToken.None);
                refreshButton.Enabled = true;
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    statusLabel.ForeColor = UiTheme.Danger;
                    statusLabel.Text = result.Error;
                    return;
                }

                fetchedModels = result.Models;
                hasSuccessfulRefresh = true;
                statusLabel.ForeColor = UiTheme.Success;
                statusLabel.Text = language == AppLanguage.Chinese ? $"已获取 {fetchedModels.Count} 个模型。" : $"Loaded {fetchedModels.Count} models.";
                RefreshLists();
            }

            private void AddSelected()
            {
                if (SelectedProtocol is not ApiRouteKind protocol || availableList.SelectedItem is not ModelListChoice selected)
                {
                    return;
                }

                registrations.Add(new RegisteredModelConfig
                {
                    Protocol = protocol,
                    ModelId = selected.Model.Id,
                    DisplayName = selected.Model.DisplayName,
                    RegisteredAtUtc = DateTime.UtcNow
                });
                PersistAndRefresh();
            }

            private void RemoveSelected()
            {
                if (SelectedProtocol is not ApiRouteKind protocol || registeredList.SelectedItem is not RegisteredModelChoice selected)
                {
                    return;
                }

                registrations.RemoveAll(model => model.Protocol == protocol && StringComparer.Ordinal.Equals(model.ModelId, selected.Model.ModelId));
                PersistAndRefresh();
            }

            private void PersistAndRefresh()
            {
                saveRegistrations(registrations);
                RefreshLists();
            }

            private void RefreshLists()
            {
                availableList.Items.Clear();
                registeredList.Items.Clear();
                if (SelectedProtocol is not ApiRouteKind protocol)
                {
                    return;
                }

                var protocolRegistrations = registrations.Where(model => model.Protocol == protocol).OrderBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var model in FilterAvailableModels(protocol, fetchedModels, registrations))
                {
                    availableList.Items.Add(new ModelListChoice(model, string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : $"{model.Id} ({model.DisplayName})"));
                }

                foreach (var model in protocolRegistrations)
                {
                    registeredList.Items.Add(new RegisteredModelChoice(model, hasSuccessfulRefresh && IsRegistrationUnavailable(model, fetchedModels)));
                }
            }

            private static List<ModelListItem> FilterAvailableModels(
                ApiRouteKind protocol,
                IEnumerable<ModelListItem> fetched,
                IEnumerable<RegisteredModelConfig> registered)
            {
                var registeredIds = registered
                    .Where(model => model.Protocol == protocol)
                    .Select(model => model.ModelId)
                    .ToHashSet(StringComparer.Ordinal);
                return fetched.Where(model => !registeredIds.Contains(model.Id)).ToList();
            }

            private static bool IsRegistrationUnavailable(RegisteredModelConfig registration, IEnumerable<ModelListItem> fetched)
            {
                return !fetched.Any(model => StringComparer.Ordinal.Equals(model.Id, registration.ModelId));
            }

            private void RegisteredList_DrawItem(object? sender, DrawItemEventArgs e)
            {
                // Owner-draw so the listbox rows match the dark theme.
                var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using var back = new SolidBrush(isSelected ? UiTheme.GridSelection : UiTheme.Surface);
                e.Graphics.FillRectangle(back, e.Bounds);
                if (e.Index >= 0 && e.Index < registeredList.Items.Count && registeredList.Items[e.Index] is RegisteredModelChoice item)
                {
                    var unavailable = language == AppLanguage.Chinese ? "已失效" : "Unavailable";
                    var text = item.Unavailable ? $"{item.Model.ModelId}  [{unavailable}]" : item.Model.ModelId;
                    TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, item.Unavailable ? UiTheme.Danger : UiTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
            }

            private void AvailableList_DrawItem(object? sender, DrawItemEventArgs e)
            {
                // Owner-draw so the available listbox uses the dark selection color instead of
                // the system highlight blue.
                var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using var back = new SolidBrush(isSelected ? UiTheme.GridSelection : UiTheme.Surface);
                e.Graphics.FillRectangle(back, e.Bounds);
                if (e.Index >= 0 && e.Index < availableList.Items.Count && availableList.Items[e.Index] is ModelListChoice item)
                {
                    TextRenderer.DrawText(e.Graphics, item.DisplayName, e.Font, e.Bounds, UiTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            }

            private static RegisteredModelConfig CloneRegistration(RegisteredModelConfig model) => new()
            {
                Protocol = model.Protocol,
                ModelId = model.ModelId,
                DisplayName = model.DisplayName,
                RegisteredAtUtc = model.RegisteredAtUtc
            };

            private sealed record ProtocolChoice(ApiRouteKind Protocol, string DisplayName);
            private sealed record ModelListChoice(ModelListItem Model, string DisplayName);
            private sealed record RegisteredModelChoice(RegisteredModelConfig Model, bool Unavailable);
        }

        private sealed class ApiKeyPromptForm : Form
        {
            private readonly TextBox apiKeyTextBox = new();

            public ApiKeyPromptForm(AppLanguage language)
            {
                Text = language == AppLanguage.Chinese ? "输入 API 密钥" : "Enter API Key";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(460, 112);
                Padding = new Padding(12);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                apiKeyTextBox.Dock = DockStyle.Fill;
                apiKeyTextBox.UseSystemPasswordChar = true;
                layout.Controls.Add(apiKeyTextBox, 0, 0);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var okText = language == AppLanguage.Chinese ? "确定" : "OK";
                var cancelText = language == AppLanguage.Chinese ? "取消" : "Cancel";
                var buttonHeight = UiTheme.GetButtonHeight();
                var okButton = new Button
                {
                    Text = okText,
                    DialogResult = DialogResult.OK,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(UiTheme.GetButtonWidth(okText, minimumWidth: 80), buttonHeight)
                };
                var cancelButton = new Button
                {
                    Text = cancelText,
                    DialogResult = DialogResult.Cancel,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(UiTheme.GetButtonWidth(cancelText, minimumWidth: 80), buttonHeight)
                };
                okButton.Click += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(apiKeyTextBox.Text))
                    {
                        DialogResult = DialogResult.None;
                    }
                };
                buttons.Controls.Add(okButton);
                buttons.Controls.Add(cancelButton);
                layout.Controls.Add(buttons, 0, 1);
                Controls.Add(layout);
                AcceptButton = okButton;
                CancelButton = cancelButton;
                Shown += (_, _) => apiKeyTextBox.Focus();

                UiTheme.StyleDialog(this);
            }

            public string ApiKey => apiKeyTextBox.Text.Trim();
        }
    }
}