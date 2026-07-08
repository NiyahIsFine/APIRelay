using System.Globalization;

namespace APIRelay
{
    public partial class Form1
    {
        private AppLanguage currentLanguage = AppLanguage.English;
        private bool updatingLanguageSelection;

        private string GetText(TextId id, params object[] args)
        {
            return AppTexts.GetText(currentLanguage, id, args);
        }

        private void InitializeLanguageSelector()
        {
            languageComboBox.Items.Clear();
            languageComboBox.Items.Add(new LanguageOption("English", AppLanguage.English));
            languageComboBox.Items.Add(new LanguageOption("中文", AppLanguage.Chinese));
            languageComboBox.SelectedIndex = 0;
            languageComboBox.SelectedIndexChanged += LanguageComboBox_SelectedIndexChanged;
        }

        private void LanguageComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (updatingLanguageSelection || languageComboBox.SelectedItem is not LanguageOption option)
            {
                return;
            }

            currentLanguage = option.Language;
            ApplyLanguage();
            SaveSettings();
        }

        private void SelectLanguage(AppLanguage language)
        {
            updatingLanguageSelection = true;
            foreach (var item in languageComboBox.Items)
            {
                if (item is LanguageOption option && option.Language == language)
                {
                    languageComboBox.SelectedItem = item;
                    break;
                }
            }
            updatingLanguageSelection = false;
        }

        private void ApplyLanguage()
        {
            Text = "APIRelay";
            providerSettingsButton.Text = GetText(TextId.Txt1);
            modelCostsButton.Text = GetText(TextId.Txt2);
            clearSelectedDateButton.Text = GetText(TextId.Txt3);
            clearAllDatesButton.Text = GetText(TextId.Txt4);
            languageLabel.Text = GetText(TextId.Txt5);
            configGroupBox.Text = GetText(TextId.Txt6);
            localUrlLabel.Text = GetText(TextId.Txt7);
            routeHelperLabel.Text = GetText(TextId.Txt8);
            serverProtocolLabel.Text = GetText(TextId.Txt9);
            toolProtocolLabel.Text = GetText(TextId.Txt10);
            copyRouteUrlButton.Text = GetText(TextId.Txt11);
            autoStartRelayCheckBox.Text = GetText(TextId.Txt12);
            openLogButton.Text = GetText(TextId.Txt13);
            openLogDirButton.Text = GetText(TextId.Txt130);
            protocolTraceCheckBox.Text = GetText(TextId.Txt128);
            openProtocolLogButton.Text = GetText(TextId.Txt129);
            startButton.Text = GetText(TextId.Txt14);
            stopButton.Text = GetText(TextId.Txt15);
            saveButton.Text = GetText(TextId.Txt16);
            statusLabel.Text = GetText(TextId.Txt17);
            usageGroupBox.Text = GetText(TextId.Txt18);
            promptTokensLabel.Text = GetText(TextId.Txt19);
            completionTokensLabel.Text = GetText(TextId.Txt20);
            cachedTokensLabel.Text = GetText(TextId.Txt21);
            totalCostLabel.Text = GetText(TextId.Txt22);
            dailyChartGroupBox.Text = GetText(TextId.Txt23);
            recordDateLabel.Text = GetText(TextId.Txt24);
            timeColumn.HeaderText = GetText(TextId.Txt25);
            modelColumn.HeaderText = GetText(TextId.Txt26);
            pathColumn.HeaderText = GetText(TextId.Txt27);
            promptColumn.HeaderText = GetText(TextId.Txt28);
            completionColumn.HeaderText = GetText(TextId.Txt20);
            costColumn.HeaderText = GetText(TextId.Txt22);
            durationColumn.HeaderText = GetText(TextId.Txt29);
            statusColumn.HeaderText = GetText(TextId.Txt17);

            SetRunningState(listener != null);
            UpdateStatsScopeButtonText();
            RefreshRouteProtocolOptions();
            UpdateTrayMenuText();
            usageBubble?.ApplyLanguage(currentLanguage);
            dailyChartPanel.Invalidate();
        }

        private void UpdateStatsScopeButtonText()
        {
            statsScopeButton.Text = statsAllDates
                ? GetText(TextId.Txt30)
                : GetText(TextId.Txt31);
        }

        private static bool TryParseLanguage(string? value, out AppLanguage language)
        {
            if (Enum.TryParse(value, ignoreCase: true, out language))
            {
                return true;
            }

            language = AppLanguage.English;
            return false;
        }

        private sealed record LanguageOption(string DisplayName, AppLanguage Language)
        {
            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
