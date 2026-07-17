namespace APIRelay
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainLayout = new TableLayoutPanel();
            topToolBar = new Panel();
            providerSettingsButton = new Button();
            modelCostsButton = new Button();
            clearSelectedDateButton = new Button();
            clearAllDatesButton = new Button();
            languageLabel = new Label();
            languageComboBox = new ThemeComboBox();
            configGroupBox = new GroupBox();
            configLayout = new TableLayoutPanel();
            localUrlLabel = new Label();
            localUrlTextBox = new TextBox();
            routeHelperLabel = new Label();
            routeHelperPanel = new Panel();
            serverProtocolLabel = new Label();
            serverProtocolComboBox = new ThemeComboBox();
            toolProtocolLabel = new Label();
            toolProtocolComboBox = new ThemeComboBox();
            routeUrlTextBox = new TextBox();
            copyRouteUrlButton = new Button();
            logOptionsPanel = new FlowLayoutPanel();
            autoStartRelayCheckBox = new ThemeCheckBox();
            openLogButton = new Button();
            openLogDirButton = new Button();
            buttonPanel = new FlowLayoutPanel();
            startButton = new Button();
            stopButton = new Button();
            saveButton = new Button();
            statusLabel = new Label();
            statusValueLabel = new Label();
            usageGroupBox = new GroupBox();
            usageLayout = new TableLayoutPanel();
            promptTokensLabel = new Label();
            promptTokensValueLabel = new Label();
            completionTokensLabel = new Label();
            completionTokensValueLabel = new Label();
            cachedTokensLabel = new Label();
            cachedTokensValueLabel = new Label();
            totalCostLabel = new Label();
            totalCostValueLabel = new Label();
            statsScopeButton = new Button();
            dailyChartGroupBox = new GroupBox();
            dailyChartPanel = new BufferedChartPanel();
            recordFilterPanel = new FlowLayoutPanel();
            recordDateLabel = new Label();
            recordDateComboBox = new ThemeComboBox();
            requestGrid = new ThemedDataGridView();
            timeColumn = new DataGridViewTextBoxColumn();
            modelColumn = new DataGridViewTextBoxColumn();
            pathColumn = new DataGridViewTextBoxColumn();
            promptColumn = new DataGridViewTextBoxColumn();
            completionColumn = new DataGridViewTextBoxColumn();
            costColumn = new DataGridViewTextBoxColumn();
            durationColumn = new DataGridViewTextBoxColumn();
            statusColumn = new DataGridViewTextBoxColumn();
            logTextBox = new TextBox();
            mainLayout.SuspendLayout();
            topToolBar.SuspendLayout();
            configGroupBox.SuspendLayout();
            configLayout.SuspendLayout();
            routeHelperPanel.SuspendLayout();
            buttonPanel.SuspendLayout();
            usageGroupBox.SuspendLayout();
            usageLayout.SuspendLayout();
            dailyChartGroupBox.SuspendLayout();
            recordFilterPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)requestGrid).BeginInit();
            SuspendLayout();
            // 
            // mainLayout
            // 
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(topToolBar, 0, 0);
            mainLayout.Controls.Add(configGroupBox, 0, 1);
            mainLayout.Controls.Add(usageGroupBox, 0, 2);
            mainLayout.Controls.Add(dailyChartGroupBox, 0, 3);
            mainLayout.Controls.Add(recordFilterPanel, 0, 4);
            mainLayout.Controls.Add(requestGrid, 0, 5);
            mainLayout.Controls.Add(logTextBox, 0, 6);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(12, 12);
            mainLayout.Name = "mainLayout";
            mainLayout.RowCount = 7;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 194F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            mainLayout.Size = new Size(1060, 796);
            mainLayout.TabIndex = 0;
            // 
            // topToolBar
            // 
            topToolBar.Controls.Add(providerSettingsButton);
            topToolBar.Controls.Add(modelCostsButton);
            topToolBar.Controls.Add(clearSelectedDateButton);
            topToolBar.Controls.Add(clearAllDatesButton);
            topToolBar.Controls.Add(languageLabel);
            topToolBar.Controls.Add(languageComboBox);
            topToolBar.Dock = DockStyle.Fill;
            topToolBar.Location = new Point(3, 3);
            topToolBar.Name = "topToolBar";
            topToolBar.Size = new Size(1054, 30);
            topToolBar.TabIndex = 0;
            // 
            // providerSettingsButton
            // 
            providerSettingsButton.Location = new Point(3, 1);
            providerSettingsButton.Margin = new Padding(3, 1, 3, 1);
            providerSettingsButton.Name = "providerSettingsButton";
            providerSettingsButton.Size = new Size(126, 28);
            providerSettingsButton.TabIndex = 0;
            providerSettingsButton.Text = "配置供应商";
            providerSettingsButton.UseVisualStyleBackColor = true;
            providerSettingsButton.Click += ProviderSettingsButton_Click;
            // 
            // modelCostsButton
            // 
            modelCostsButton.Location = new Point(135, 1);
            modelCostsButton.Margin = new Padding(3, 1, 3, 1);
            modelCostsButton.Name = "modelCostsButton";
            modelCostsButton.Size = new Size(126, 28);
            modelCostsButton.TabIndex = 1;
            modelCostsButton.Text = "配置模型价格";
            modelCostsButton.UseVisualStyleBackColor = true;
            modelCostsButton.Click += ModelCostsButton_Click;
            // 
            // clearSelectedDateButton
            // 
            clearSelectedDateButton.Location = new Point(267, 1);
            clearSelectedDateButton.Margin = new Padding(3, 1, 3, 1);
            clearSelectedDateButton.Name = "clearSelectedDateButton";
            clearSelectedDateButton.Size = new Size(150, 28);
            clearSelectedDateButton.TabIndex = 2;
            clearSelectedDateButton.Text = "清空当前日期统计";
            clearSelectedDateButton.UseVisualStyleBackColor = true;
            clearSelectedDateButton.Click += ClearSelectedDateButton_Click;
            // 
            // clearAllDatesButton
            // 
            clearAllDatesButton.Location = new Point(423, 1);
            clearAllDatesButton.Margin = new Padding(3, 1, 3, 1);
            clearAllDatesButton.Name = "clearAllDatesButton";
            clearAllDatesButton.Size = new Size(138, 28);
            clearAllDatesButton.TabIndex = 3;
            clearAllDatesButton.Text = "清空全部统计";
            clearAllDatesButton.UseVisualStyleBackColor = true;
            clearAllDatesButton.Click += ClearAllDatesButton_Click;
            // 
            // languageLabel
            // 
            languageLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            languageLabel.AutoSize = true;
            languageLabel.Location = new Point(873, 7);
            languageLabel.Margin = new Padding(3, 7, 6, 0);
            languageLabel.Name = "languageLabel";
            languageLabel.Size = new Size(68, 17);
            languageLabel.TabIndex = 5;
            languageLabel.Text = "Language:";
            // 
            // languageComboBox
            // 
            languageComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageComboBox.FormattingEnabled = true;
            languageComboBox.Location = new Point(950, 3);
            languageComboBox.Margin = new Padding(3, 3, 3, 3);
            languageComboBox.Name = "languageComboBox";
            languageComboBox.Size = new Size(98, 25);
            languageComboBox.TabIndex = 6;
            // 
            // configGroupBox
            // 
            configGroupBox.Controls.Add(configLayout);
            configGroupBox.Dock = DockStyle.Fill;
            configGroupBox.Location = new Point(3, 39);
            configGroupBox.Name = "configGroupBox";
            configGroupBox.Size = new Size(1054, 188);
            configGroupBox.TabIndex = 1;
            configGroupBox.TabStop = false;
            configGroupBox.Text = "代理配置";
            // 
            // configLayout
            // 
            configLayout.ColumnCount = 2;
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.Controls.Add(localUrlLabel, 0, 0);
            configLayout.Controls.Add(localUrlTextBox, 1, 0);
            configLayout.Controls.Add(routeHelperLabel, 0, 1);
            configLayout.Controls.Add(routeHelperPanel, 1, 1);
            configLayout.Controls.Add(logOptionsPanel, 1, 2);
            configLayout.Controls.Add(buttonPanel, 1, 3);
            configLayout.Dock = DockStyle.Fill;
            configLayout.Location = new Point(3, 19);
            configLayout.Name = "configLayout";
            configLayout.Padding = new Padding(10);
            configLayout.RowCount = 4;
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            configLayout.Size = new Size(1048, 166);
            configLayout.TabIndex = 0;
            // 
            // localUrlLabel
            // 
            localUrlLabel.AutoSize = true;
            localUrlLabel.Dock = DockStyle.Fill;
            localUrlLabel.Location = new Point(13, 10);
            localUrlLabel.Name = "localUrlLabel";
            localUrlLabel.Size = new Size(114, 34);
            localUrlLabel.TabIndex = 0;
            localUrlLabel.Text = "本地监听地址";
            localUrlLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // localUrlTextBox
            // 
            localUrlTextBox.Dock = DockStyle.Fill;
            localUrlTextBox.Location = new Point(133, 15);
            localUrlTextBox.Margin = new Padding(3, 5, 3, 3);
            localUrlTextBox.Name = "localUrlTextBox";
            localUrlTextBox.Size = new Size(902, 23);
            localUrlTextBox.TabIndex = 1;
            localUrlTextBox.Text = "http://127.0.0.1:14556/";
            localUrlTextBox.TextChanged += RouteHelperInput_Changed;
            // 
            // routeHelperLabel
            // 
            routeHelperLabel.AutoSize = true;
            routeHelperLabel.Dock = DockStyle.Fill;
            routeHelperLabel.Location = new Point(13, 44);
            routeHelperLabel.Name = "routeHelperLabel";
            routeHelperLabel.Size = new Size(114, 34);
            routeHelperLabel.TabIndex = 2;
            routeHelperLabel.Text = "调用地址";
            routeHelperLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // routeHelperPanel
            // 
            routeHelperPanel.Controls.Add(serverProtocolLabel);
            routeHelperPanel.Controls.Add(serverProtocolComboBox);
            routeHelperPanel.Controls.Add(toolProtocolLabel);
            routeHelperPanel.Controls.Add(toolProtocolComboBox);
            routeHelperPanel.Controls.Add(routeUrlTextBox);
            routeHelperPanel.Controls.Add(copyRouteUrlButton);
            routeHelperPanel.Dock = DockStyle.Fill;
            routeHelperPanel.Location = new Point(130, 44);
            routeHelperPanel.Margin = new Padding(0);
            routeHelperPanel.Name = "routeHelperPanel";
            routeHelperPanel.Size = new Size(908, 34);
            routeHelperPanel.TabIndex = 2;
            // 
            // serverProtocolLabel
            // 
            serverProtocolLabel.AutoSize = true;
            serverProtocolLabel.Location = new Point(3, 8);
            serverProtocolLabel.Margin = new Padding(3, 8, 6, 0);
            serverProtocolLabel.Name = "serverProtocolLabel";
            serverProtocolLabel.Size = new Size(80, 17);
            serverProtocolLabel.TabIndex = 0;
            serverProtocolLabel.Text = "发往服务器";
            // 
            // serverProtocolComboBox
            // 
            serverProtocolComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            serverProtocolComboBox.FormattingEnabled = true;
            serverProtocolComboBox.Location = new Point(92, 4);
            serverProtocolComboBox.Margin = new Padding(3, 4, 12, 3);
            serverProtocolComboBox.Name = "serverProtocolComboBox";
            serverProtocolComboBox.Size = new Size(132, 25);
            serverProtocolComboBox.TabIndex = 1;
            serverProtocolComboBox.SelectedIndexChanged += RouteHelperInput_Changed;
            // 
            // toolProtocolLabel
            // 
            toolProtocolLabel.AutoSize = true;
            toolProtocolLabel.Location = new Point(239, 8);
            toolProtocolLabel.Margin = new Padding(3, 8, 6, 0);
            toolProtocolLabel.Name = "toolProtocolLabel";
            toolProtocolLabel.Size = new Size(80, 17);
            toolProtocolLabel.TabIndex = 2;
            toolProtocolLabel.Text = "发给工具";
            // 
            // toolProtocolComboBox
            // 
            toolProtocolComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            toolProtocolComboBox.FormattingEnabled = true;
            toolProtocolComboBox.Location = new Point(328, 4);
            toolProtocolComboBox.Margin = new Padding(3, 4, 12, 3);
            toolProtocolComboBox.Name = "toolProtocolComboBox";
            toolProtocolComboBox.Size = new Size(132, 25);
            toolProtocolComboBox.TabIndex = 3;
            toolProtocolComboBox.SelectedIndexChanged += RouteHelperInput_Changed;
            // 
            // routeUrlTextBox
            // 
            routeUrlTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            routeUrlTextBox.Location = new Point(475, 5);
            routeUrlTextBox.Margin = new Padding(3, 5, 6, 3);
            routeUrlTextBox.Name = "routeUrlTextBox";
            routeUrlTextBox.ReadOnly = true;
            routeUrlTextBox.Size = new Size(330, 23);
            routeUrlTextBox.TabIndex = 4;
            // 
            // copyRouteUrlButton
            // 
            copyRouteUrlButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copyRouteUrlButton.Location = new Point(814, 3);
            copyRouteUrlButton.Margin = new Padding(3, 3, 3, 3);
            copyRouteUrlButton.Name = "copyRouteUrlButton";
            copyRouteUrlButton.Size = new Size(72, 28);
            copyRouteUrlButton.TabIndex = 5;
            copyRouteUrlButton.Text = "复制";
            copyRouteUrlButton.UseVisualStyleBackColor = true;
            copyRouteUrlButton.Click += CopyRouteUrlButton_Click;
            // 
            // logOptionsPanel
            // 
            logOptionsPanel.Controls.Add(openLogButton);
            logOptionsPanel.Controls.Add(openLogDirButton);
            logOptionsPanel.Dock = DockStyle.Fill;
            logOptionsPanel.Location = new Point(130, 78);
            logOptionsPanel.Margin = new Padding(0);
            logOptionsPanel.Name = "logOptionsPanel";
            logOptionsPanel.Size = new Size(908, 34);
            logOptionsPanel.TabIndex = 3;
            logOptionsPanel.WrapContents = false;
            // 
            // autoStartRelayCheckBox
            // 
            autoStartRelayCheckBox.AutoSize = true;
            autoStartRelayCheckBox.Location = new Point(3, 7);
            autoStartRelayCheckBox.Margin = new Padding(3, 7, 16, 3);
            autoStartRelayCheckBox.Name = "autoStartRelayCheckBox";
            autoStartRelayCheckBox.Size = new Size(135, 21);
            autoStartRelayCheckBox.TabIndex = 0;
            autoStartRelayCheckBox.Text = "启动时自动启动代理";
            autoStartRelayCheckBox.UseVisualStyleBackColor = true;
            // 
            // openLogButton
            // 
            openLogButton.Location = new Point(157, 3);
            openLogButton.Margin = new Padding(3, 3, 3, 3);
            openLogButton.Name = "openLogButton";
            openLogButton.Size = new Size(110, 28);
            openLogButton.TabIndex = 1;
            openLogButton.Text = "打开日志";
            openLogButton.UseVisualStyleBackColor = true;
            openLogButton.Click += OpenLogButton_Click;
            // 
            // openLogDirButton
            // 
            openLogDirButton.Location = new Point(273, 3);
            openLogDirButton.Margin = new Padding(3, 3, 3, 3);
            openLogDirButton.Name = "openLogDirButton";
            openLogDirButton.Size = new Size(110, 28);
            openLogDirButton.TabIndex = 2;
            openLogDirButton.Text = "打开日志目录";
            openLogDirButton.UseVisualStyleBackColor = true;
            openLogDirButton.Click += OpenLogDirButton_Click;
            // 
            // buttonPanel
            // 
            buttonPanel.Controls.Add(startButton);
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Controls.Add(saveButton);
            buttonPanel.Controls.Add(statusLabel);
            buttonPanel.Controls.Add(statusValueLabel);
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Location = new Point(130, 112);
            buttonPanel.Margin = new Padding(0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new Size(908, 42);
            buttonPanel.TabIndex = 4;
            buttonPanel.WrapContents = false;
            // 
            // startButton
            // 
            startButton.Location = new Point(3, 6);
            startButton.Margin = new Padding(3, 6, 3, 3);
            startButton.Name = "startButton";
            startButton.Size = new Size(90, 28);
            startButton.TabIndex = 0;
            startButton.Text = "启动代理";
            startButton.UseVisualStyleBackColor = true;
            startButton.Click += StartButton_Click;
            // 
            // stopButton
            // 
            stopButton.Enabled = false;
            stopButton.Location = new Point(99, 6);
            stopButton.Margin = new Padding(3, 6, 3, 3);
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(90, 28);
            stopButton.TabIndex = 1;
            stopButton.Text = "停止代理";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            // 
            // saveButton
            // 
            saveButton.Location = new Point(195, 6);
            saveButton.Margin = new Padding(3, 6, 3, 3);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(90, 28);
            saveButton.TabIndex = 2;
            saveButton.Text = "保存配置";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += SaveButton_Click;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(304, 12);
            statusLabel.Margin = new Padding(16, 12, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(44, 17);
            statusLabel.TabIndex = 3;
            statusLabel.Text = "状态：";
            // 
            // statusValueLabel
            // 
            statusValueLabel.AutoSize = true;
            statusValueLabel.ForeColor = Color.Firebrick;
            statusValueLabel.Location = new Point(354, 12);
            statusValueLabel.Margin = new Padding(3, 12, 3, 0);
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Size = new Size(44, 17);
            statusValueLabel.TabIndex = 4;
            statusValueLabel.Text = "已停止";
            // 
            // usageGroupBox
            // 
            usageGroupBox.Controls.Add(usageLayout);
            usageGroupBox.Dock = DockStyle.Fill;
            usageGroupBox.Location = new Point(3, 199);
            usageGroupBox.Name = "usageGroupBox";
            usageGroupBox.Size = new Size(1054, 76);
            usageGroupBox.TabIndex = 2;
            usageGroupBox.TabStop = false;
            usageGroupBox.Text = "Token 使用统计";
            // 
            // usageLayout
            // 
            usageLayout.ColumnCount = 9;
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            usageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
            usageLayout.Controls.Add(promptTokensLabel, 0, 0);
            usageLayout.Controls.Add(promptTokensValueLabel, 1, 0);
            usageLayout.Controls.Add(completionTokensLabel, 2, 0);
            usageLayout.Controls.Add(completionTokensValueLabel, 3, 0);
            usageLayout.Controls.Add(cachedTokensLabel, 4, 0);
            usageLayout.Controls.Add(cachedTokensValueLabel, 5, 0);
            usageLayout.Controls.Add(totalCostLabel, 6, 0);
            usageLayout.Controls.Add(totalCostValueLabel, 7, 0);
            usageLayout.Controls.Add(statsScopeButton, 8, 0);
            usageLayout.Dock = DockStyle.Fill;
            usageLayout.Location = new Point(3, 19);
            usageLayout.Name = "usageLayout";
            usageLayout.Padding = new Padding(10);
            usageLayout.RowCount = 1;
            usageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            usageLayout.Size = new Size(1048, 54);
            usageLayout.TabIndex = 0;
            // 
            // promptTokensLabel
            // 
            promptTokensLabel.AutoSize = true;
            promptTokensLabel.Dock = DockStyle.Fill;
            promptTokensLabel.Location = new Point(13, 10);
            promptTokensLabel.Name = "promptTokensLabel";
            promptTokensLabel.Size = new Size(84, 34);
            promptTokensLabel.TabIndex = 0;
            promptTokensLabel.Text = "总输入";
            promptTokensLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // promptTokensValueLabel
            // 
            promptTokensValueLabel.AutoSize = true;
            promptTokensValueLabel.Dock = DockStyle.Fill;
            promptTokensValueLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            promptTokensValueLabel.Location = new Point(103, 10);
            promptTokensValueLabel.Name = "promptTokensValueLabel";
            promptTokensValueLabel.Size = new Size(125, 34);
            promptTokensValueLabel.TabIndex = 1;
            promptTokensValueLabel.Text = "0";
            promptTokensValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // completionTokensLabel
            // 
            completionTokensLabel.AutoSize = true;
            completionTokensLabel.Dock = DockStyle.Fill;
            completionTokensLabel.Location = new Point(234, 10);
            completionTokensLabel.Name = "completionTokensLabel";
            completionTokensLabel.Size = new Size(84, 34);
            completionTokensLabel.TabIndex = 2;
            completionTokensLabel.Text = "总输出";
            completionTokensLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // completionTokensValueLabel
            // 
            completionTokensValueLabel.AutoSize = true;
            completionTokensValueLabel.Dock = DockStyle.Fill;
            completionTokensValueLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            completionTokensValueLabel.Location = new Point(324, 10);
            completionTokensValueLabel.Name = "completionTokensValueLabel";
            completionTokensValueLabel.Size = new Size(125, 34);
            completionTokensValueLabel.TabIndex = 3;
            completionTokensValueLabel.Text = "0";
            completionTokensValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cachedTokensLabel
            // 
            cachedTokensLabel.AutoSize = true;
            cachedTokensLabel.Dock = DockStyle.Fill;
            cachedTokensLabel.Location = new Point(455, 10);
            cachedTokensLabel.Name = "cachedTokensLabel";
            cachedTokensLabel.Size = new Size(104, 34);
            cachedTokensLabel.TabIndex = 4;
            cachedTokensLabel.Text = "总缓存命中";
            cachedTokensLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cachedTokensValueLabel
            // 
            cachedTokensValueLabel.AutoSize = true;
            cachedTokensValueLabel.Dock = DockStyle.Fill;
            cachedTokensValueLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            cachedTokensValueLabel.Location = new Point(565, 10);
            cachedTokensValueLabel.Name = "cachedTokensValueLabel";
            cachedTokensValueLabel.Size = new Size(125, 34);
            cachedTokensValueLabel.TabIndex = 5;
            cachedTokensValueLabel.Text = "0";
            cachedTokensValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // totalCostLabel
            // 
            totalCostLabel.AutoSize = true;
            totalCostLabel.Dock = DockStyle.Fill;
            totalCostLabel.Location = new Point(696, 10);
            totalCostLabel.Name = "totalCostLabel";
            totalCostLabel.Size = new Size(104, 34);
            totalCostLabel.TabIndex = 6;
            totalCostLabel.Text = "总花费";
            totalCostLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // totalCostValueLabel
            // 
            totalCostValueLabel.AutoSize = true;
            totalCostValueLabel.Dock = DockStyle.Fill;
            totalCostValueLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            totalCostValueLabel.Location = new Point(806, 10);
            totalCostValueLabel.Name = "totalCostValueLabel";
            totalCostValueLabel.Size = new Size(113, 34);
            totalCostValueLabel.TabIndex = 7;
            totalCostValueLabel.Text = "$0.000000";
            totalCostValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // statsScopeButton
            // 
            statsScopeButton.Dock = DockStyle.Fill;
            statsScopeButton.Location = new Point(925, 13);
            statsScopeButton.Margin = new Padding(3, 3, 3, 3);
            statsScopeButton.Name = "statsScopeButton";
            statsScopeButton.Size = new Size(110, 28);
            statsScopeButton.TabIndex = 8;
            statsScopeButton.Text = "统计：当前日期";
            statsScopeButton.UseVisualStyleBackColor = true;
            statsScopeButton.Click += StatsScopeButton_Click;
            // 
            // dailyChartGroupBox
            // 
            dailyChartGroupBox.Controls.Add(dailyChartPanel);
            dailyChartGroupBox.Dock = DockStyle.Fill;
            dailyChartGroupBox.Location = new Point(3, 281);
            dailyChartGroupBox.Name = "dailyChartGroupBox";
            dailyChartGroupBox.Size = new Size(1054, 164);
            dailyChartGroupBox.TabIndex = 3;
            dailyChartGroupBox.TabStop = false;
            dailyChartGroupBox.Text = "当前日期半小时趋势（输入/输出/缓存命中/花费）";
            // 
            // dailyChartPanel
            // 
            dailyChartPanel.BackColor = Color.White;
            dailyChartPanel.Dock = DockStyle.Fill;
            dailyChartPanel.Location = new Point(3, 19);
            dailyChartPanel.Name = "dailyChartPanel";
            dailyChartPanel.Size = new Size(1048, 142);
            dailyChartPanel.TabIndex = 0;
            dailyChartPanel.MouseLeave += DailyChartPanel_MouseLeave;
            dailyChartPanel.MouseMove += DailyChartPanel_MouseMove;
            dailyChartPanel.Paint += DailyChartPanel_Paint;
            dailyChartPanel.Resize += DailyChartPanel_Resize;
            // 
            // 
            // recordFilterPanel
            // 
            recordFilterPanel.Controls.Add(recordDateLabel);
            recordFilterPanel.Controls.Add(recordDateComboBox);
            recordFilterPanel.Dock = DockStyle.Fill;
            recordFilterPanel.Location = new Point(3, 451);
            recordFilterPanel.Name = "recordFilterPanel";
            recordFilterPanel.Size = new Size(1054, 30);
            recordFilterPanel.TabIndex = 3;
            recordFilterPanel.WrapContents = false;
            // 
            // recordDateLabel
            // 
            recordDateLabel.AutoSize = true;
            recordDateLabel.Location = new Point(3, 7);
            recordDateLabel.Margin = new Padding(3, 7, 8, 0);
            recordDateLabel.Name = "recordDateLabel";
            recordDateLabel.Size = new Size(68, 17);
            recordDateLabel.TabIndex = 0;
            recordDateLabel.Text = "记录日期：";
            // 
            // recordDateComboBox
            // 
            recordDateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            recordDateComboBox.FormattingEnabled = true;
            recordDateComboBox.Location = new Point(82, 3);
            recordDateComboBox.Name = "recordDateComboBox";
            recordDateComboBox.Size = new Size(140, 25);
            recordDateComboBox.TabIndex = 1;
            recordDateComboBox.SelectedIndexChanged += RecordDateComboBox_SelectedIndexChanged;
            // 
            // requestGrid
            // 
            requestGrid.AllowUserToAddRows = false;
            requestGrid.AllowUserToDeleteRows = false;
            requestGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            requestGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            requestGrid.Columns.AddRange(new DataGridViewColumn[] { timeColumn, modelColumn, pathColumn, promptColumn, completionColumn, costColumn, durationColumn, statusColumn });
            requestGrid.Dock = DockStyle.Fill;
            requestGrid.Location = new Point(3, 487);
            requestGrid.Name = "requestGrid";
            requestGrid.ReadOnly = true;
            requestGrid.RowHeadersVisible = false;
            requestGrid.RowTemplate.Height = 25;
            requestGrid.Size = new Size(1054, 231);
            requestGrid.TabIndex = 4;
            // 
            // timeColumn
            // 
            timeColumn.FillWeight = 62F;
            timeColumn.HeaderText = "时间";
            timeColumn.Name = "timeColumn";
            timeColumn.ReadOnly = true;
            // 
            // modelColumn
            // 
            modelColumn.FillWeight = 110F;
            modelColumn.HeaderText = "模型";
            modelColumn.Name = "modelColumn";
            modelColumn.ReadOnly = true;
            // 
            // pathColumn
            // 
            pathColumn.FillWeight = 110F;
            pathColumn.HeaderText = "路径";
            pathColumn.Name = "pathColumn";
            pathColumn.ReadOnly = true;
            // 
            // 
            // promptColumn
            // 
            promptColumn.FillWeight = 85F;
            promptColumn.HeaderText = "输入/缓存读取";
            promptColumn.Name = "promptColumn";
            promptColumn.ReadOnly = true;
            // 
            // completionColumn
            // 
            completionColumn.FillWeight = 35F;
            completionColumn.HeaderText = "输出";
            completionColumn.Name = "completionColumn";
            completionColumn.ReadOnly = true;
            // 
            // costColumn
            // 
            costColumn.FillWeight = 60F;
            costColumn.HeaderText = "成本";
            costColumn.Name = "costColumn";
            costColumn.ReadOnly = true;
            // 
            // durationColumn
            // 
            durationColumn.FillWeight = 90F;
            durationColumn.HeaderText = "用时/首字";
            durationColumn.Name = "durationColumn";
            durationColumn.ReadOnly = true;
            // 
            // statusColumn
            // 
            statusColumn.FillWeight = 28F;
            statusColumn.HeaderText = "状态";
            statusColumn.Name = "statusColumn";
            statusColumn.ReadOnly = true;
            // 
            // logTextBox
            // 
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Location = new Point(3, 724);
            logTextBox.Multiline = true;
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.Size = new Size(1054, 90);
            logTextBox.TabIndex = 5;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1084, 820);
            Controls.Add(mainLayout);
            MinimumSize = new Size(900, 760);
            Name = "Form1";
            Padding = new Padding(12);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "APIRelay";
            mainLayout.ResumeLayout(false);
            mainLayout.PerformLayout();
            topToolBar.ResumeLayout(false);
            configGroupBox.ResumeLayout(false);
            configLayout.ResumeLayout(false);
            configLayout.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            usageGroupBox.ResumeLayout(false);
            usageLayout.ResumeLayout(false);
            usageLayout.PerformLayout();
            dailyChartGroupBox.ResumeLayout(false);
            recordFilterPanel.ResumeLayout(false);
            recordFilterPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)requestGrid).EndInit();
            routeHelperPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel mainLayout;
        private Panel topToolBar;
        private Button providerSettingsButton;
        private Button modelCostsButton;
        private Button clearSelectedDateButton;
        private Button clearAllDatesButton;
        private Label languageLabel;
        private ComboBox languageComboBox;
        private GroupBox configGroupBox;
        private TableLayoutPanel configLayout;
        private Label localUrlLabel;
        private TextBox localUrlTextBox;
        private Label routeHelperLabel;
        private Panel routeHelperPanel;
        private Label serverProtocolLabel;
        private ComboBox serverProtocolComboBox;
        private Label toolProtocolLabel;
        private ComboBox toolProtocolComboBox;
        private TextBox routeUrlTextBox;
        private Button copyRouteUrlButton;
        private FlowLayoutPanel logOptionsPanel;
        private CheckBox autoStartRelayCheckBox;
        private Button openLogButton;
        private Button openLogDirButton;
        private FlowLayoutPanel buttonPanel;
        private Button startButton;
        private Button stopButton;
        private Button saveButton;
        private Label statusLabel;
        private Label statusValueLabel;
        private GroupBox usageGroupBox;
        private TableLayoutPanel usageLayout;
        private Label promptTokensLabel;
        private Label promptTokensValueLabel;
        private Label completionTokensLabel;
        private Label completionTokensValueLabel;
        private Label cachedTokensLabel;
        private Label cachedTokensValueLabel;
        private Label totalCostLabel;
        private Label totalCostValueLabel;
        private Button statsScopeButton;
        private GroupBox dailyChartGroupBox;
        private BufferedChartPanel dailyChartPanel;
        private FlowLayoutPanel recordFilterPanel;
        private Label recordDateLabel;
        private ComboBox recordDateComboBox;
        private ThemedDataGridView requestGrid;
        private DataGridViewTextBoxColumn timeColumn;
        private DataGridViewTextBoxColumn modelColumn;
        private DataGridViewTextBoxColumn pathColumn;
        private DataGridViewTextBoxColumn promptColumn;
        private DataGridViewTextBoxColumn completionColumn;
        private DataGridViewTextBoxColumn costColumn;
        private DataGridViewTextBoxColumn durationColumn;
        private DataGridViewTextBoxColumn statusColumn;
        private TextBox logTextBox;
    }
}
