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
    public partial class Form1 : Form
    {
        private static readonly HttpClient HttpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });

        private readonly string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APIRelay",
            "settings.json");

        private readonly string modelCostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APIRelay",
            "model-costs.json");

        private readonly string appDataRoot;
        private readonly string logsDirectory;
        private readonly string recordsDirectory;
        private readonly string internalLogPath;
        private readonly object internalLogLock = new();
        private readonly object recordsLock = new();
        private readonly List<RequestRecord> visibleRecords = new();
        private readonly List<ModelCostConfig> modelCosts = new();
        private readonly Dictionary<ApiRouteKind, ProviderEndpointConfig> providerConfigs = CreateDefaultProviderConfigs();
        private const string AnthropicMessagesRelayPath = "/anthropic/v1/messages";
        private const int DefaultAnthropicMaxTokens = 8192;
        private const long StreamingProgressLogIntervalMs = 5000;
        private const long StreamingProgressLogBytes = 256 * 1024;
        private bool loadingRecordDates;
        private bool statsAllDates;
        private bool allowExit;
        private bool usageBubbleVisible = true;
        private bool autoStartRelayQueued;
        private Point? savedUsageBubbleLocation;
        private Point? dailyChartMouseLocation;
        private int? dailyChartHoverBucketIndex;
        private NotifyIcon? trayIcon;
        private ToolStripMenuItem? toggleBubbleMenuItem;
        private FloatingUsageBubble? usageBubble;

        private CancellationTokenSource? listenerCancellation;
        private HttpListener? listener;
        private RelayConfig? activeConfig;
        private long promptTokens;
        private long completionTokens;
        private long cachedTokens;
        private long cacheCreationTokens;
        private long totalTokens;

        public Form1()
        {
            appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "APIRelay");
            logsDirectory = Path.Combine(appDataRoot, "Logs");
            recordsDirectory = Path.Combine(appDataRoot, "Records");
            internalLogPath = Path.Combine(logsDirectory, "internal.txt");

            InitializeComponent();
            ApplyApplicationIcon();
            requestGrid.ShowCellToolTips = true;
            InitializeLanguageSelector();
            InitializeRouteHelper();
            InitializeStorage();
            AppendInternalLog("Application initialized.");
            LoadSettings();
            ApplyLanguage();
            LoadRecordDates(DateTime.Today);
            LoadRecordsForSelectedDate();
            UpdateTotals();
            InitializeTrayIcon();
            InitializeUsageBubble();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AppendInternalLog("Main form shown.");
            StartRelayOnLaunchIfEnabled();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                HideMainWindow();
                return;
            }

            CaptureUsageBubbleLocation();
            AppendInternalLog($"Form closing. Reason={e.CloseReason}; AllowExit={allowExit}.");
            StopRelay();
            SaveSettings();
            trayIcon?.Dispose();
            usageBubble?.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (WindowState == FormWindowState.Minimized)
            {
                HideMainWindow();
            }
        }

        private async void StartButton_Click(object sender, EventArgs e)
        {
            SaveSettings();
            await StartRelayAsync();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopRelay();
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            SaveSettings();
            AppendLog(GetText(TextId.Txt35), true);
        }

        private void RouteHelperInput_Changed(object sender, EventArgs e)
        {
            UpdateRouteUrlPreview();
        }

        private void CopyRouteUrlButton_Click(object sender, EventArgs e)
        {
            var url = routeUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Clipboard.SetText(url);
                AppendLog(GetText(TextId.Txt36, url), true);
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                MessageBox.Show(GetText(TextId.Txt37, ex.Message), GetText(TextId.Txt38), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ClearSelectedDateButton_Click(object sender, EventArgs e)
        {
            var selectedDate = GetSelectedRecordDate();
            if (MessageBox.Show(
                GetText(TextId.Txt39, selectedDate),
                GetText(TextId.Txt40),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            var recordPath = GetRecordPath(selectedDate);
            lock (recordsLock)
            {
                if (File.Exists(recordPath))
                {
                    File.Delete(recordPath);
                }
            }

            visibleRecords.Clear();
            requestGrid.Rows.Clear();
            LoadRecordDates(DateTime.Today == selectedDate ? DateTime.Today : selectedDate);
            LoadRecordsForSelectedDate();
            UpdateTotals();
            UpdateUsageBubble();
            dailyChartPanel.Invalidate();
            AppendLog(GetText(TextId.Txt41, selectedDate), true);
        }

        private void ClearAllDatesButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                GetText(TextId.Txt42),
                GetText(TextId.Txt43),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            lock (recordsLock)
            {
                foreach (var recordFile in Directory.GetFiles(recordsDirectory, "*.json"))
                {
                    try
                    {
                        File.Delete(recordFile);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AppendLog(GetText(TextId.Txt44, Path.GetFileName(recordFile), ex.Message), true);
                    }
                }
            }

            visibleRecords.Clear();
            requestGrid.Rows.Clear();
            LoadRecordDates(DateTime.Today);
            LoadRecordsForSelectedDate();
            UpdateTotals();
            UpdateUsageBubble();
            dailyChartPanel.Invalidate();
            AppendLog(GetText(TextId.Txt45), true);
        }

        private void OpenLogButton_Click(object sender, EventArgs e)
        {
            try
            {
                EnsureInternalLogFile();
                Process.Start(new ProcessStartInfo(internalLogPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetText(TextId.Txt46, ex.Message), GetText(TextId.Txt47), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ModelCostsButton_Click(object sender, EventArgs e)
        {
            using var dialog = new ModelCostsForm(modelCosts, currentLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            modelCosts.Clear();
            modelCosts.AddRange(dialog.ModelCosts);
            SaveModelCosts();
            SaveSettings();
            RefreshRecordGrid();
            UpdateTotals();
            UpdateUsageBubble();
            dailyChartPanel.Invalidate();
            AppendLog(GetText(TextId.Txt48), true);
        }

        private void ProviderSettingsButton_Click(object sender, EventArgs e)
        {
            if (listener != null)
            {
                MessageBox.Show(
                    GetText(TextId.Txt49),
                    GetText(TextId.Txt50),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var dialog = new ProviderSettingsForm(providerConfigs.Values.OrderBy(config => config.RouteKind), currentLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            providerConfigs.Clear();
            foreach (var config in dialog.ProviderConfigs)
            {
                providerConfigs[config.RouteKind] = config;
            }

            EnsureProviderConfigDefaults();
            SaveSettings();
            AppendLog(GetText(TextId.Txt51), true);
        }

        private void StatsScopeButton_Click(object sender, EventArgs e)
        {
            statsAllDates = !statsAllDates;
            UpdateStatsScopeButtonText();
            UpdateTotals();
        }
    }
}

