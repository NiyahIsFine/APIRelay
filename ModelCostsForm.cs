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
        private sealed class ModelCostsForm : Form
        {
            private readonly ThemedDataGridView costsGrid = new();
            private readonly AppLanguage language;

            public ModelCostsForm(IEnumerable<ModelCostConfig> modelCosts, AppLanguage language)
            {
                this.language = language;
                Text = AppTexts.GetText(language, TextId.Txt101);
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(620, 420);
                Size = new Size(720, 460);
                Padding = new Padding(12);

                ModelCosts = modelCosts
                    .Select(cost => new ModelCostConfig
                    {
                        ModelName = cost.ModelName,
                        InputCostPerMillion = cost.InputCostPerMillion,
                        OutputCostPerMillion = cost.OutputCostPerMillion,
                        CacheHitCostPerMillion = cost.CacheHitCostPerMillion,
                        CacheCreationCostPerMillion = cost.CacheCreationCostPerMillion
                    })
                    .ToList();

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3
                };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var headerPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    WrapContents = false
                };

                var addText = AppTexts.GetText(language, TextId.Txt102);
                var addButton = new Button
                {
                    Text = addText,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(UiTheme.GetButtonWidth(addText, minimumWidth: 80), UiTheme.GetButtonHeight()),
                    Margin = new Padding(0, 1, 8, 1)
                };
                addButton.Click += (_, _) => costsGrid.Rows.Add(string.Empty, "0", "0", "0", "0");
                headerPanel.Controls.Add(addButton);

                var hintLabel = new Label
                {
                    AutoSize = true,
                    Text = AppTexts.GetText(language, TextId.Txt103),
                    Margin = new Padding(0, 7, 0, 0)
                };
                headerPanel.Controls.Add(hintLabel);

                costsGrid.AllowUserToAddRows = false;
                costsGrid.AllowUserToDeleteRows = false;
                costsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                costsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                costsGrid.Dock = DockStyle.Fill;
                costsGrid.RowHeadersVisible = false;
                costsGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = AppTexts.GetText(language, TextId.Txt104),
                    Name = "modelNameColumn",
                    FillWeight = 160F
                });
                costsGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = AppTexts.GetText(language, TextId.Txt105),
                    Name = "inputCostColumn",
                    FillWeight = 80F
                });
                costsGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = AppTexts.GetText(language, TextId.Txt106),
                    Name = "outputCostColumn",
                    FillWeight = 75F
                });
                costsGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = AppTexts.GetText(language, TextId.Txt107),
                    Name = "cacheHitCostColumn",
                    FillWeight = 95F
                });
                costsGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = AppTexts.GetText(language, TextId.Txt108),
                    Name = "cacheCreationCostColumn",
                    FillWeight = 95F
                });
                costsGrid.Columns.Add(CreateGridButtonColumn(AppTexts.GetText(language, TextId.Txt109), "editColumn", UiTheme.Accent));
                costsGrid.Columns.Add(CreateGridButtonColumn(AppTexts.GetText(language, TextId.Txt110), "deleteColumn", UiTheme.Danger));
                costsGrid.CellContentClick += CostsGrid_CellContentClick;

                foreach (var cost in ModelCosts)
                {
                    costsGrid.Rows.Add(
                        cost.ModelName,
                        cost.InputCostPerMillion.ToString(CultureInfo.InvariantCulture),
                        cost.OutputCostPerMillion.ToString(CultureInfo.InvariantCulture),
                        cost.CacheHitCostPerMillion.ToString(CultureInfo.InvariantCulture),
                        cost.CacheCreationCostPerMillion.ToString(CultureInfo.InvariantCulture));
                }

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false
                };

                var buttonHeight = UiTheme.GetButtonHeight();
                var okText = AppTexts.GetText(language, TextId.Txt111);
                var okButton = new Button
                {
                    Text = okText,
                    DialogResult = DialogResult.OK,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(UiTheme.GetButtonWidth(okText, minimumWidth: 80), buttonHeight),
                    Margin = new Padding(8, 5, 0, 0)
                };
                okButton.Click += OkButton_Click;

                var cancelText = AppTexts.GetText(language, TextId.Txt112);
                var cancelButton = new Button
                {
                    Text = cancelText,
                    DialogResult = DialogResult.Cancel,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(UiTheme.GetButtonWidth(cancelText, minimumWidth: 80), buttonHeight),
                    Margin = new Padding(8, 5, 0, 0)
                };

                buttonPanel.Controls.Add(okButton);
                buttonPanel.Controls.Add(cancelButton);

                layout.Controls.Add(headerPanel, 0, 0);
                layout.Controls.Add(costsGrid, 0, 1);
                layout.Controls.Add(buttonPanel, 0, 2);
                Controls.Add(layout);

                AcceptButton = okButton;
                CancelButton = cancelButton;

                UiTheme.StyleDialog(this);
                costsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
                costsGrid.RowTemplate.Height = UiTheme.GetButtonHeight();
            }

            public List<ModelCostConfig> ModelCosts { get; private set; }

            private void CostsGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0)
                {
                    return;
                }

                var columnName = costsGrid.Columns[e.ColumnIndex].Name;
                if (columnName == "editColumn")
                {
                    costsGrid.CurrentCell = costsGrid.Rows[e.RowIndex].Cells["modelNameColumn"];
                    costsGrid.BeginEdit(true);
                    return;
                }

                if (columnName == "deleteColumn")
                {
                    costsGrid.Rows.RemoveAt(e.RowIndex);
                }
            }

            private void OkButton_Click(object? sender, EventArgs e)
            {
                costsGrid.EndEdit();
                var updatedCosts = new List<ModelCostConfig>();

                foreach (DataGridViewRow row in costsGrid.Rows)
                {
                    var modelName = Convert.ToString(row.Cells["modelNameColumn"].Value)?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(modelName))
                    {
                        continue;
                    }

                    if (!TryReadDecimal(row.Cells["inputCostColumn"].Value, out var inputCost)
                        || !TryReadDecimal(row.Cells["outputCostColumn"].Value, out var outputCost)
                        || !TryReadDecimal(row.Cells["cacheHitCostColumn"].Value, out var cacheHitCost)
                        || !TryReadDecimal(row.Cells["cacheCreationCostColumn"].Value, out var cacheCreationCost)
                        || inputCost < 0
                        || outputCost < 0
                        || cacheHitCost < 0
                        || cacheCreationCost < 0)
                    {
                        MessageBox.Show(AppTexts.GetText(language, TextId.Txt113), AppTexts.GetText(language, TextId.Txt114), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.None;
                        return;
                    }

                    updatedCosts.Add(new ModelCostConfig
                    {
                        ModelName = modelName,
                        InputCostPerMillion = inputCost,
                        OutputCostPerMillion = outputCost,
                        CacheHitCostPerMillion = cacheHitCost,
                        CacheCreationCostPerMillion = cacheCreationCost
                    });
                }

                ModelCosts = updatedCosts;
            }

            private static bool TryReadDecimal(object? value, out decimal result)
            {
                var text = Convert.ToString(value)?.Trim();
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
                    || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
            }

            private static DataGridViewButtonColumn CreateGridButtonColumn(string label, string name, Color foreColor)
            {
                var cell = new DataGridViewButtonCell { FlatStyle = FlatStyle.Flat };
                var column = new DataGridViewButtonColumn
                {
                    HeaderText = label,
                    Name = name,
                    Text = label,
                    UseColumnTextForButtonValue = true,
                    FillWeight = 56F,
                    CellTemplate = cell,
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = DataGridViewContentAlignment.MiddleCenter,
                        ForeColor = foreColor,
                        BackColor = UiTheme.Surface,
                        SelectionBackColor = UiTheme.Surface,
                        SelectionForeColor = foreColor,
                        Padding = new Padding(6, 0, 6, 0)
                    }
                };
                return column;
            }
        }
    }
}

