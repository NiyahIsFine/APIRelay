namespace APIRelay
{
    internal sealed class ProtocolTraceViewerForm : Form
    {
        private const int BodyCacheCapacity = 10;

        private readonly string logsDirectory;
        private readonly int maxFileCount;
        private readonly ComboBox fileComboBox = new();
        private readonly Button refreshButton = new();
        private readonly TreeView messageTreeView = new();
        private readonly TextBox bodyTextBox = new();
        private readonly Label fileLabel = new();
        private readonly Label statusLabel = new();
        private readonly CachedBody[] bodyCache = new CachedBody[BodyCacheCapacity];
        private readonly Font requestGroupFont = new(UiTheme.FontFamily, 9F, FontStyle.Bold);
        private AppLanguage language;
        private bool updatingFiles;
        private int messageCount;
        private long bodyCacheUseCounter;

        public ProtocolTraceViewerForm(string logsDirectory, int maxFileCount, string initialPath, AppLanguage language)
        {
            this.logsDirectory = logsDirectory;
            this.maxFileCount = maxFileCount;
            this.language = language;

            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 520);
            Size = new Size(1100, 720);
            Padding = new Padding(12);

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var toolbar = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 10)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            fileLabel.AutoSize = true;
            fileLabel.Anchor = AnchorStyles.Left;
            fileLabel.Margin = new Padding(0, 7, 8, 0);
            fileComboBox.Dock = DockStyle.Fill;
            fileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fileComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (!updatingFiles)
                {
                    LoadSelectedFile();
                }
            };
            refreshButton.AutoSize = true;
            refreshButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            refreshButton.MinimumSize = new Size(80, UiTheme.GetButtonHeight());
            refreshButton.Margin = new Padding(8, 0, 0, 0);
            refreshButton.Click += (_, _) => RefreshFilesAndContent(GetSelectedPath());

            toolbar.Controls.Add(fileLabel, 0, 0);
            toolbar.Controls.Add(fileComboBox, 1, 0);
            toolbar.Controls.Add(refreshButton, 2, 0);

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 6
            };
            splitContainer.Panel1.Padding = new Padding(0, 0, 5, 0);
            splitContainer.Panel2.Padding = new Padding(5, 0, 0, 0);

            messageTreeView.Dock = DockStyle.Fill;
            messageTreeView.BorderStyle = BorderStyle.FixedSingle;
            messageTreeView.FullRowSelect = true;
            messageTreeView.HideSelection = false;
            messageTreeView.HotTracking = true;
            messageTreeView.Indent = 20;
            messageTreeView.ItemHeight = 28;
            messageTreeView.ShowLines = false;
            messageTreeView.ShowPlusMinus = false;
            messageTreeView.ShowRootLines = false;
            messageTreeView.AfterSelect += (_, e) => ShowSelectedMessage(e.Node);
            messageTreeView.NodeMouseClick += MessageTreeViewNodeMouseClick;
            messageTreeView.BeforeCollapse += (_, e) =>
            {
                if (e.Node is { Parent: null })
                {
                    bodyTextBox.Clear();
                }
            };

            bodyTextBox.Dock = DockStyle.Fill;
            bodyTextBox.Multiline = true;
            bodyTextBox.ReadOnly = true;
            bodyTextBox.ScrollBars = ScrollBars.Both;
            bodyTextBox.WordWrap = false;
            bodyTextBox.Font = new Font(FontFamily.GenericMonospace, 10F);

            splitContainer.Panel1.Controls.Add(messageTreeView);
            splitContainer.Panel2.Controls.Add(bodyTextBox);

            statusLabel.AutoSize = true;
            statusLabel.Margin = new Padding(0, 8, 0, 0);

            rootLayout.Controls.Add(toolbar, 0, 0);
            rootLayout.Controls.Add(splitContainer, 0, 1);
            rootLayout.Controls.Add(statusLabel, 0, 2);
            Controls.Add(rootLayout);
            Shown += (_, _) =>
            {
                const int desiredPanel1MinSize = 260;
                const int desiredPanel2MinSize = 320;
                const int desiredSplitterDistance = 360;
                var availableWidth = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
                if (availableWidth <= 0)
                {
                    return;
                }

                var splitterDistance = Math.Clamp(
                    desiredSplitterDistance,
                    splitContainer.Panel1MinSize,
                    availableWidth - splitContainer.Panel2MinSize);
                splitContainer.SplitterDistance = splitterDistance;
                splitContainer.Panel1MinSize = Math.Min(desiredPanel1MinSize, splitterDistance);
                splitContainer.Panel2MinSize = Math.Min(desiredPanel2MinSize, availableWidth - splitterDistance);
            };

            UiTheme.StyleDialog(this);
            StyleMessageTree();
            bodyTextBox.Font = new Font(FontFamily.GenericMonospace, 10F);
            ApplyLanguage(language);
            RefreshFilesAndContent(initialPath);
        }

        public void ApplyLanguage(AppLanguage newLanguage)
        {
            language = newLanguage;
            Text = AppTexts.GetText(language, TextId.Txt139);
            fileLabel.Text = AppTexts.GetText(language, TextId.Txt140);
            refreshButton.Text = AppTexts.GetText(language, TextId.Txt141);
            UpdateStatus();
        }

        public void RefreshLatest(string preferredPath)
        {
            RefreshFilesAndContent(preferredPath);
        }

        private void RefreshFilesAndContent(string? preferredPath)
        {
            var files = EnumerateLogFiles(logsDirectory, maxFileCount);
            var selectedPath = preferredPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !files.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
            {
                selectedPath = files.FirstOrDefault();
            }

            updatingFiles = true;
            try
            {
                fileComboBox.Items.Clear();
                foreach (var path in files)
                {
                    fileComboBox.Items.Add(new ProtocolLogFileOption(path));
                }

                fileComboBox.SelectedItem = fileComboBox.Items
                    .Cast<ProtocolLogFileOption>()
                    .FirstOrDefault(item => item.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                updatingFiles = false;
            }

            LoadSelectedFile();
        }

        private void LoadSelectedFile()
        {
            messageTreeView.BeginUpdate();
            try
            {
                messageTreeView.Nodes.Clear();
                bodyTextBox.Clear();
                messageCount = 0;

                var selectedPath = GetSelectedPath();
                ClearBodyCache();
                if (selectedPath == null)
                {
                    UpdateStatus();
                    return;
                }

                var groups = GroupEntries(ProtocolTraceParser.IndexFile(selectedPath));
                foreach (var group in groups)
                {
                    var groupNode = new TreeNode(FormatGroupText(group))
                    {
                        NodeFont = requestGroupFont,
                        Tag = group
                    };

                    for (var index = 0; index < group.Entries.Count; index++)
                    {
                        var entry = group.Entries[index];
                        groupNode.Nodes.Add(new TreeNode(FormatMessageText(index + 1, entry))
                        {
                            Tag = entry
                        });
                    }

                    messageTreeView.Nodes.Add(groupNode);
                    messageCount += group.Entries.Count;
                }

                UpdateStatus();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                statusLabel.Text = AppTexts.GetText(language, TextId.Txt143, ex.Message);
            }
            finally
            {
                messageTreeView.EndUpdate();
            }
        }

        private void MessageTreeViewNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            messageTreeView.SelectedNode = e.Node;
            if (e.Node.Parent != null)
            {
                return;
            }

            if (e.Node.IsExpanded)
            {
                e.Node.Collapse();
            }
            else
            {
                e.Node.Expand();
            }
        }

        private void ShowSelectedMessage(TreeNode? node)
        {
            try
            {
                bodyTextBox.Text = node?.Tag is ProtocolTraceEntry entry
                    ? GetFormattedBody(entry)
                    : string.Empty;
                bodyTextBox.SelectionStart = 0;
                bodyTextBox.SelectionLength = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bodyTextBox.Clear();
                statusLabel.Text = AppTexts.GetText(language, TextId.Txt143, ex.Message);
            }
        }

        private void StyleMessageTree()
        {
            messageTreeView.BackColor = UiTheme.Panel;
            messageTreeView.ForeColor = UiTheme.Text;
            messageTreeView.Font = UiTheme.DefaultFont;
            messageTreeView.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            messageTreeView.DrawNode += DrawMessageTreeNode;
            DarkScrollbars.ApplyWhenReady(messageTreeView);
        }

        private void DrawMessageTreeNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null)
            {
                return;
            }

            var selected = (e.State & TreeNodeStates.Selected) != 0;
            var isGroup = e.Node.Parent == null;
            var rowBounds = new Rectangle(0, e.Bounds.Y, messageTreeView.ClientSize.Width, e.Bounds.Height);
            var backColor = selected
                ? UiTheme.GridSelection
                : isGroup ? UiTheme.PanelAlt : UiTheme.Panel;

            using (var backgroundBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, rowBounds);
            }

            if (selected)
            {
                using var accentBrush = new SolidBrush(UiTheme.Accent);
                e.Graphics.FillRectangle(accentBrush, 0, rowBounds.Top, 3, rowBounds.Height);
            }

            var textLeft = isGroup ? 28 : 38;
            if (isGroup)
            {
                DrawExpansionArrow(e.Graphics, rowBounds, e.Node.IsExpanded);
            }
            else
            {
                using var branchPen = new Pen(UiTheme.Divider);
                var branchX = 19;
                var centerY = rowBounds.Top + rowBounds.Height / 2;
                e.Graphics.DrawLine(branchPen, branchX, rowBounds.Top, branchX, centerY);
                e.Graphics.DrawLine(branchPen, branchX, centerY, 29, centerY);
            }

            var textBounds = new Rectangle(
                textLeft,
                rowBounds.Top,
                Math.Max(0, rowBounds.Width - textLeft - 6),
                rowBounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                e.Node.NodeFont ?? messageTreeView.Font,
                textBounds,
                isGroup ? UiTheme.Text : UiTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static void DrawExpansionArrow(Graphics graphics, Rectangle rowBounds, bool expanded)
        {
            var centerX = 14;
            var centerY = rowBounds.Top + rowBounds.Height / 2;
            Point[] points = expanded
                ? [new(centerX - 4, centerY - 2), new(centerX + 4, centerY - 2), new(centerX, centerY + 3)]
                : [new(centerX - 2, centerY - 4), new(centerX - 2, centerY + 4), new(centerX + 3, centerY)];
            using var arrowBrush = new SolidBrush(UiTheme.TextSecondary);
            graphics.FillPolygon(arrowBrush, points);
        }

        internal static IReadOnlyList<ProtocolTraceGroup> GroupEntries(IReadOnlyList<ProtocolTraceEntry> entries)
        {
            var groups = new List<ProtocolTraceGroup>();
            var groupsById = new Dictionary<uint, ProtocolTraceGroup>();

            foreach (var entry in entries)
            {
                if (!groupsById.TryGetValue(entry.RequestId, out var group))
                {
                    group = new ProtocolTraceGroup(entry.RequestId);
                    groupsById.Add(entry.RequestId, group);
                    groups.Add(group);
                }

                group.Entries.Add(entry);
            }

            return groups;
        }

        private static string FormatGroupText(ProtocolTraceGroup group)
        {
            var first = group.Entries[0].Timestamp;
            var last = group.Entries[^1].Timestamp;
            return $"{group.RequestId:x8}   {first:HH:mm:ss.fff}  →  {last:HH:mm:ss.fff}   ({group.Entries.Count})";
        }

        private static string FormatMessageText(int number, ProtocolTraceEntry entry)
        {
            return $"{number:D3}  {entry.Timestamp:HH:mm:ss.fff}  {entry.DirectionDisplay}  {entry.ProtocolDisplay}";
        }

        private string GetFormattedBody(ProtocolTraceEntry entry)
        {
            bodyCacheUseCounter++;
            var emptyIndex = -1;
            var leastRecentlyUsedIndex = 0;

            for (var index = 0; index < bodyCache.Length; index++)
            {
                ref var cachedBody = ref bodyCache[index];
                if (cachedBody.Content != null && cachedBody.BodyOffset == entry.BodyOffset)
                {
                    cachedBody.LastUsed = bodyCacheUseCounter;
                    return cachedBody.Content;
                }

                if (cachedBody.Content == null)
                {
                    emptyIndex = index;
                }
                else if (bodyCache[leastRecentlyUsedIndex].Content == null
                    || cachedBody.LastUsed < bodyCache[leastRecentlyUsedIndex].LastUsed)
                {
                    leastRecentlyUsedIndex = index;
                }
            }

            var selectedPath = GetSelectedPath();
            if (selectedPath == null)
            {
                return string.Empty;
            }

            var content = ProtocolTraceParser.FormatBody(ProtocolTraceParser.ReadBody(selectedPath, entry));
            var cacheIndex = emptyIndex >= 0 ? emptyIndex : leastRecentlyUsedIndex;
            bodyCache[cacheIndex] = new CachedBody(entry.BodyOffset, content, bodyCacheUseCounter);
            return content;
        }

        private void ClearBodyCache()
        {
            Array.Clear(bodyCache);
            bodyCacheUseCounter = 0;
        }

        private void UpdateStatus()
        {
            statusLabel.Text = messageCount == 0
                ? AppTexts.GetText(language, TextId.Txt142)
                : AppTexts.GetText(language, TextId.Txt144, messageCount);
        }

        private string? GetSelectedPath()
        {
            return (fileComboBox.SelectedItem as ProtocolLogFileOption)?.Path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearBodyCache();
                requestGroupFont.Dispose();
            }

            base.Dispose(disposing);
        }

        internal static IReadOnlyList<string> EnumerateLogFiles(string directory, int maxFileCount)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Enumerable.Range(1, maxFileCount)
                .Select(index => Path.Combine(directory, index == 1 ? "protocol-trace.txt" : $"protocol-trace-{index}.txt"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }

        private sealed record ProtocolLogFileOption(string Path)
        {
            public override string ToString()
            {
                return System.IO.Path.GetFileName(Path);
            }
        }

        private struct CachedBody
        {
            public CachedBody(long bodyOffset, string content, long lastUsed)
            {
                BodyOffset = bodyOffset;
                Content = content;
                LastUsed = lastUsed;
            }

            public long BodyOffset { get; }
            public string? Content { get; }
            public long LastUsed { get; set; }
        }

        internal sealed class ProtocolTraceGroup
        {
            public ProtocolTraceGroup(uint requestId)
            {
                RequestId = requestId;
            }

            public uint RequestId { get; }
            public List<ProtocolTraceEntry> Entries { get; } = new();
        }
    }
}
