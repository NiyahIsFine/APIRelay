using System.Drawing.Drawing2D;

namespace APIRelay
{
    public partial class Form1
    {
        /// <summary>
        /// Applies the dark theme to the main window and all of its descendants.
        /// Layout is left to the designer; this only restyles surfaces, fonts and colors.
        /// </summary>
        private void ApplyTheme()
        {
            UiTheme.ApplyFormTheme(this);
            StyleControlTree(mainLayout);

            // Value labels (token totals / cost) use the bold accent treatment.
            StyleStatValueLabel(promptTokensValueLabel, UiTheme.SeriesInput);
            StyleStatValueLabel(completionTokensValueLabel, UiTheme.SeriesOutput);
            StyleStatValueLabel(cachedTokensValueLabel, UiTheme.SeriesCache);
            StyleStatValueLabel(cacheCreationTokensValueLabel, UiTheme.SeriesCacheCreation);
            StyleStatValueLabel(totalCostValueLabel, UiTheme.SeriesCost);

            // Status pill color is driven by running state in SetRunningState; just neutralize here.
            statusValueLabel.Font = UiTheme.StatusFont;
            statusValueLabel.ForeColor = UiTheme.Danger;

            StyleGrid();
            StyleChartPanel();
            StyleProtocolTracePanel();

            // Dark scrollbars for the scrolling surfaces once their handles exist.
            DarkScrollbars.ApplyWhenReady(requestGrid);
            ThemedScroll.AttachVertical(logTextBox);

            // Re-apply the managed-tool tab selection so its accent coloring survives the
            // control-tree restyle that just ran over the tab buttons. Default to Claude.
            SelectManagedToolTab(ManagedToolKind.Claude);
            UpdateManagedToggleText();
        }

        private void StyleControlTree(Control root)
        {
            root.BackColor = UiTheme.Window;
            root.ForeColor = UiTheme.Text;
            root.Font = UiTheme.DefaultFont;

            foreach (Control child in root.Controls)
            {
                StyleControl(child);
                StyleControlTree(child);
            }
        }

        private void StyleControl(Control control)
        {
            control.Font = UiTheme.DefaultFont;
            switch (control)
            {
                case GroupBox groupBox:
                    UiTheme.StyleGroupBox(groupBox);
                    break;
                case Button button:
                    UiTheme.StyleButton(button, ResolveButtonRole(button));
                    break;
                case TextBoxBase textBox:
                    UiTheme.StyleInput(textBox);
                    break;
                case ComboBox comboBox:
                    UiTheme.StyleComboBox(comboBox);
                    break;
                case CheckBox checkBox:
                    if (checkBox.Appearance == Appearance.Button)
                    {
                        StyleEnableToggle(checkBox);
                    }
                    else
                    {
                        UiTheme.StyleCheckBox(checkBox);
                    }
                    break;
                case Label label:
                    label.ForeColor = UiTheme.Text;
                    label.BackColor = Color.Transparent;
                    label.Font = UiTheme.DefaultFont;
                    break;
                // Panel covers FlowLayoutPanel and TableLayoutPanel (both derive from it).
                case Panel panel:
                    panel.BackColor = UiTheme.Window;
                    panel.ForeColor = UiTheme.Text;
                    break;
            }
        }

        private ButtonRole ResolveButtonRole(Button button)
        {
            if (button == startButton || button == copyRouteUrlButton)
            {
                return ButtonRole.Primary;
            }
            if (button == stopButton || button == clearSelectedDateButton || button == clearAllDatesButton)
            {
                return ButtonRole.Danger;
            }
            if (button == providerSettingsButton || button == modelCostsButton)
            {
                return ButtonRole.AccentOutline;
            }
            return ButtonRole.Default;
        }

        private static void StyleStatValueLabel(Label label, Color accent)
        {
            if (label == null)
            {
                return;
            }
            label.Font = UiTheme.ValueFont;
            label.ForeColor = accent;
            label.BackColor = Color.Transparent;
        }

        private void StyleGrid()
        {
            requestGrid.EnableHeadersVisualStyles = false;
            requestGrid.BorderStyle = BorderStyle.None;
            requestGrid.BackgroundColor = UiTheme.Panel;
            requestGrid.GridColor = UiTheme.Divider;
            requestGrid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            requestGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            requestGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            requestGrid.DefaultCellStyle.BackColor = UiTheme.Panel;
            requestGrid.DefaultCellStyle.ForeColor = UiTheme.Text;
            requestGrid.DefaultCellStyle.SelectionBackColor = UiTheme.GridSelection;
            requestGrid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            requestGrid.DefaultCellStyle.Font = UiTheme.DefaultFont;
            requestGrid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
            requestGrid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.GridHeaderBack;
            requestGrid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextSecondary;
            requestGrid.ColumnHeadersDefaultCellStyle.Font = UiTheme.StatusFont;
            requestGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.GridHeaderBack;
            requestGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.Text;
            requestGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
            requestGrid.RowTemplate.Height = 30;
            requestGrid.RowHeadersVisible = false;

            // Zebra striping via the built-in style properties (set once) rather than per-paint
            // DefaultCellstyle writes, which can re-trigger formatting and raise DataError dialogs.
            requestGrid.RowsDefaultCellStyle.BackColor = UiTheme.Panel;
            requestGrid.RowsDefaultCellStyle.ForeColor = UiTheme.Text;
            requestGrid.RowsDefaultCellStyle.SelectionBackColor = UiTheme.GridSelection;
            requestGrid.RowsDefaultCellStyle.SelectionForeColor = UiTheme.Text;
            requestGrid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.GridRowAlt;
            requestGrid.AlternatingRowsDefaultCellStyle.ForeColor = UiTheme.Text;
            requestGrid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UiTheme.GridSelection;
            requestGrid.AlternatingRowsDefaultCellStyle.SelectionForeColor = UiTheme.Text;

            requestGrid.CellFormatting -= RequestGrid_CellFormatting;
            requestGrid.CellFormatting += RequestGrid_CellFormatting;
            // Swallow any formatting/dialog errors so a stray value can never raise a modal box.
            requestGrid.DataError -= RequestGrid_DataError;
            requestGrid.DataError += RequestGrid_DataError;
        }

        private void RequestGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= requestGrid.Rows.Count || e.CellStyle == null)
            {
                return;
            }

            // Tint the status column by HTTP status code (value is an int).
            if (e.ColumnIndex == statusColumn.Index && e.Value is int code)
            {
                e.CellStyle.ForeColor = code >= 200 && code < 300
                    ? UiTheme.Success
                    : code >= 400
                        ? UiTheme.Danger
                        : UiTheme.Warning;
            }

            // Cost column emphasized.
            if (e.ColumnIndex == costColumn.Index && e.Value != null)
            {
                e.CellStyle.ForeColor = UiTheme.SeriesCost;
                e.CellStyle.Font = UiTheme.StatusFont;
            }
        }

        private void RequestGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
        {
            // Keep theme/formatting glitches from surfacing as a modal dialog.
            e.ThrowException = false;
            e.Cancel = false;
        }

        private void StyleChartPanel()
        {
            dailyChartPanel.BackColor = UiTheme.Panel;
        }

        private void StyleProtocolTracePanel()
        {
            if (protocolTracePanel == null)
            {
                return;
            }

            protocolTracePanel.BackColor = UiTheme.PanelAlt;
            protocolTracePanel.BorderStyle = BorderStyle.FixedSingle;

            if (protocolTraceCheckBox != null)
            {
                protocolTraceCheckBox.ForeColor = UiTheme.Text;
                protocolTraceCheckBox.BackColor = UiTheme.PanelAlt;
            }

            if (openProtocolLogButton != null)
            {
                UiTheme.StyleButton(openProtocolLogButton);
            }
        }
    }
}
