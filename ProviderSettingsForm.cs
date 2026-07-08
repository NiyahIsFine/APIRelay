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
        private sealed class ProviderSettingsForm : Form
        {
            private readonly Dictionary<ApiRouteKind, TextBox> urlTextBoxes = new();
            private readonly Dictionary<ApiRouteKind, TextBox> modelListUrlTextBoxes = new();
            private readonly Dictionary<ApiRouteKind, CheckBox> modelListOverwriteCheckBoxes = new();
            private readonly Dictionary<ApiRouteKind, TextBox> anthropicVersionTextBoxes = new();
            private readonly Dictionary<ApiRouteKind, CheckBox> forceCacheCheckBoxes = new();
            private readonly Dictionary<ApiRouteKind, CheckBox> cacheOnConversionCheckBoxes = new();
            private readonly AppLanguage language;

            public ProviderSettingsForm(IEnumerable<ProviderEndpointConfig> providerConfigs, AppLanguage language)
            {
                this.language = language;
                Text = AppTexts.GetText(language, TextId.Txt115);
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(760, 520);
                Size = new Size(900, 600);
                Padding = new Padding(12);

                ProviderConfigs = providerConfigs.Select(CloneProviderConfig).ToList();

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 134F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

                layout.Controls.Add(CreateRouteGroup(ApiRouteKind.Responses, ProviderType.OpenAICompatible, "Responses", false), 0, 0);
                layout.Controls.Add(CreateRouteGroup(ApiRouteKind.ChatCompletions, ProviderType.OpenAICompatible, "Chat Completions", false), 0, 1);
                layout.Controls.Add(CreateRouteGroup(ApiRouteKind.AnthropicMessages, ProviderType.Anthropic, "Anthropic Messages", true), 0, 2);

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false
                };

                var okButton = new Button
                {
                    Text = AppTexts.GetText(language, TextId.Txt111),
                    DialogResult = DialogResult.OK,
                    Size = new Size(80, 28),
                    Margin = new Padding(8, 7, 0, 0)
                };
                okButton.Click += OkButton_Click;

                var cancelButton = new Button
                {
                    Text = AppTexts.GetText(language, TextId.Txt112),
                    DialogResult = DialogResult.Cancel,
                    Size = new Size(80, 28),
                    Margin = new Padding(8, 7, 0, 0)
                };

                buttonPanel.Controls.Add(okButton);
                buttonPanel.Controls.Add(cancelButton);
                layout.Controls.Add(buttonPanel, 0, 3);
                Controls.Add(layout);

                AcceptButton = okButton;
                CancelButton = cancelButton;
            }

            public List<ProviderEndpointConfig> ProviderConfigs { get; private set; }

            private GroupBox CreateRouteGroup(ApiRouteKind routeKind, ProviderType providerType, string title, bool showAnthropicVersion)
            {
                var config = ProviderConfigs.FirstOrDefault(item => item.RouteKind == routeKind) ?? new ProviderEndpointConfig
                {
                    RouteKind = routeKind,
                    ProviderType = providerType,
                    AnthropicVersion = "2023-06-01"
                };

                var groupBox = new GroupBox
                {
                    Dock = DockStyle.Fill,
                    Text = title
                };

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = showAnthropicVersion ? 3 : 2,
                    Padding = new Padding(8)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                if (showAnthropicVersion)
                {
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                }

                var urlTextBox = new TextBox { Dock = DockStyle.Fill, Text = config.ProviderUrl, PlaceholderText = GetProviderUrlPlaceholder(routeKind, language) };
                var modelListUrlTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Text = string.IsNullOrWhiteSpace(config.ModelListUrl) || !config.ModelListUrlOverridden
                        ? BuildModelListUrl(routeKind, config.ProviderUrl)
                        : config.ModelListUrl,
                    PlaceholderText = AppTexts.GetText(language, TextId.Txt116),
                    Enabled = config.ModelListUrlOverridden
                };
                var overwriteCheckBox = new CheckBox
                {
                    Appearance = Appearance.Button,
                    AutoSize = false,
                    Checked = config.ModelListUrlOverridden,
                    Dock = DockStyle.Fill,
                    Text = "Overwrite",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                urlTextBoxes[routeKind] = urlTextBox;
                modelListUrlTextBoxes[routeKind] = modelListUrlTextBox;
                modelListOverwriteCheckBoxes[routeKind] = overwriteCheckBox;

                urlTextBox.TextChanged += (_, _) => UpdateModelListUrl(routeKind);
                overwriteCheckBox.CheckedChanged += (_, _) => UpdateModelListUrl(routeKind);

                layout.Controls.Add(CreateMiddleLeftLabel(AppTexts.GetText(language, TextId.Txt117)), 0, 0);
                layout.Controls.Add(urlTextBox, 1, 0);
                layout.Controls.Add(CreateMiddleLeftLabel(AppTexts.GetText(language, TextId.Txt118)), 0, 1);
                layout.Controls.Add(CreateModelListUrlPanel(modelListUrlTextBox, overwriteCheckBox), 1, 1);

                if (showAnthropicVersion)
                {
                    var versionTextBox = new TextBox { Width = 180, Margin = new Padding(0, 3, 12, 3), Text = string.IsNullOrWhiteSpace(config.AnthropicVersion) ? "2023-06-01" : config.AnthropicVersion };
                    anthropicVersionTextBoxes[routeKind] = versionTextBox;

                    var forceCacheCheckBox = new CheckBox
                    {
                        AutoSize = true,
                        Checked = config.ForceCache,
                        Margin = new Padding(0, 5, 12, 3),
                        Text = AppTexts.GetText(language, TextId.Txt131)
                    };
                    var cacheOnConversionCheckBox = new CheckBox
                    {
                        AutoSize = true,
                        Checked = config.CacheOnConversion,
                        Margin = new Padding(0, 5, 0, 3),
                        Text = AppTexts.GetText(language, TextId.Txt132)
                    };
                    forceCacheCheckBoxes[routeKind] = forceCacheCheckBox;
                    cacheOnConversionCheckBoxes[routeKind] = cacheOnConversionCheckBox;
                    forceCacheCheckBox.CheckedChanged += (_, _) => cacheOnConversionCheckBox.Enabled = !forceCacheCheckBox.Checked;
                    cacheOnConversionCheckBox.Enabled = !forceCacheCheckBox.Checked;

                    var versionPanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0),
                        WrapContents = false
                    };
                    versionPanel.Controls.Add(versionTextBox);
                    versionPanel.Controls.Add(forceCacheCheckBox);
                    versionPanel.Controls.Add(cacheOnConversionCheckBox);

                    layout.Controls.Add(CreateMiddleLeftLabel(AppTexts.GetText(language, TextId.Txt119)), 0, 2);
                    layout.Controls.Add(versionPanel, 1, 2);
                }

                groupBox.Controls.Add(layout);
                return groupBox;
            }

            private void OkButton_Click(object? sender, EventArgs e)
            {
                var updated = new List<ProviderEndpointConfig>();
                foreach (var routeKind in new[] { ApiRouteKind.Responses, ApiRouteKind.ChatCompletions, ApiRouteKind.AnthropicMessages })
                {
                    var providerType = routeKind == ApiRouteKind.AnthropicMessages ? ProviderType.Anthropic : ProviderType.OpenAICompatible;
                    var providerUrl = urlTextBoxes[routeKind].Text.Trim();
                    if (!string.IsNullOrWhiteSpace(providerUrl)
                        && (!Uri.TryCreate(providerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                    {
                        MessageBox.Show(AppTexts.GetText(language, TextId.Txt99, GetRouteKindDisplayName(routeKind)), AppTexts.GetText(language, TextId.Txt127), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.None;
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(providerUrl)
                        && Uri.TryCreate(providerUrl, UriKind.Absolute, out var finalUri)
                        && !ProviderUrlMatchesFinalEndpoint(routeKind, finalUri.AbsolutePath))
                    {
                        MessageBox.Show(AppTexts.GetText(language, TextId.Txt120, GetRouteKindDisplayName(routeKind), StripExamplePrefix(GetProviderUrlPlaceholder(routeKind, language), language)), AppTexts.GetText(language, TextId.Txt127), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.None;
                        return;
                    }

                    var modelListUrl = modelListUrlTextBoxes[routeKind].Text.Trim();
                    if (!string.IsNullOrWhiteSpace(modelListUrl)
                        && (!Uri.TryCreate(modelListUrl, UriKind.Absolute, out var modelListUri) || (modelListUri.Scheme != Uri.UriSchemeHttp && modelListUri.Scheme != Uri.UriSchemeHttps)))
                    {
                        MessageBox.Show(AppTexts.GetText(language, TextId.Txt97, GetRouteKindDisplayName(routeKind)), AppTexts.GetText(language, TextId.Txt127), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.None;
                        return;
                    }

                    updated.Add(new ProviderEndpointConfig
                    {
                        RouteKind = routeKind,
                        ProviderType = providerType,
                        ProviderUrl = providerUrl,
                        ModelListUrl = modelListUrl,
                        ModelListUrlOverridden = modelListOverwriteCheckBoxes[routeKind].Checked,
                        AnthropicVersion = anthropicVersionTextBoxes.TryGetValue(routeKind, out var versionTextBox) && !string.IsNullOrWhiteSpace(versionTextBox.Text)
                            ? versionTextBox.Text.Trim()
                            : "2023-06-01",
                        ForceCache = forceCacheCheckBoxes.TryGetValue(routeKind, out var forceCacheCheckBox) && forceCacheCheckBox.Checked,
                        CacheOnConversion = !cacheOnConversionCheckBoxes.TryGetValue(routeKind, out var cacheOnConversionCheckBox) || cacheOnConversionCheckBox.Checked
                    });
                }

                ProviderConfigs = updated;
            }

            private static ProviderEndpointConfig CloneProviderConfig(ProviderEndpointConfig config)
            {
                return new ProviderEndpointConfig
                {
                    RouteKind = config.RouteKind,
                    ProviderType = config.ProviderType,
                    ProviderUrl = config.ProviderUrl,
                    ModelListUrl = config.ModelListUrl,
                    ModelListUrlOverridden = config.ModelListUrlOverridden,
                    AnthropicVersion = string.IsNullOrWhiteSpace(config.AnthropicVersion) ? "2023-06-01" : config.AnthropicVersion,
                    ForceCache = config.ForceCache,
                    CacheOnConversion = config.CacheOnConversion
                };
            }

            private void UpdateModelListUrl(ApiRouteKind routeKind)
            {
                var modelListUrlTextBox = modelListUrlTextBoxes[routeKind];
                var overwriteCheckBox = modelListOverwriteCheckBoxes[routeKind];
                modelListUrlTextBox.Enabled = overwriteCheckBox.Checked;

                if (!overwriteCheckBox.Checked)
                {
                    modelListUrlTextBox.Text = BuildModelListUrl(routeKind, urlTextBoxes[routeKind].Text);
                }
            }

            private static Control CreateModelListUrlPanel(TextBox modelListUrlTextBox, CheckBox overwriteCheckBox)
            {
                var panel = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    RowCount = 1
                };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
                panel.Controls.Add(modelListUrlTextBox, 0, 0);
                panel.Controls.Add(overwriteCheckBox, 1, 0);
                return panel;
            }

            private static string GetProviderUrlPlaceholder(ApiRouteKind routeKind, AppLanguage language)
            {
                return routeKind switch
                {
                    ApiRouteKind.Responses => AppTexts.GetText(language, TextId.Txt121),
                    ApiRouteKind.ChatCompletions => AppTexts.GetText(language, TextId.Txt122),
                    ApiRouteKind.AnthropicMessages => AppTexts.GetText(language, TextId.Txt123),
                    _ => AppTexts.GetText(language, TextId.Txt124)
                };
            }

            private static string StripExamplePrefix(string value, AppLanguage language)
            {
                var prefix = language == AppLanguage.Chinese ? "例如：" : "Example: ";
                return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
            }

            private static Label CreateMiddleLeftLabel(string text)
            {
                return new Label
                {
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Text = text,
                    TextAlign = ContentAlignment.MiddleLeft
                };
            }
        }
    }
}

