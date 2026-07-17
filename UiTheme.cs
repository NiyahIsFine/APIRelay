using System.Drawing.Drawing2D;

namespace APIRelay
{
    /// <summary>Role a button plays in the theme (drives its colors).</summary>
    internal enum ButtonRole
    {
        Default,
        Primary,
        Danger,
        AccentOutline
    }

    /// <summary>
    /// Central dark-theme palette and shared control styling helpers used across
    /// the main window, dialogs, chart and floating bubble so every surface reads
    /// as one system.
    /// </summary>
    internal static class UiTheme
    {
        // ── Surface colors (dark) ──────────────────────────────────────────
        public static readonly Color Window = Color.FromArgb(0x16, 0x18, 0x1F);       // app background
        public static readonly Color Panel = Color.FromArgb(0x1C, 0x1F, 0x27);        // group boxes / cards
        public static readonly Color PanelAlt = Color.FromArgb(0x22, 0x26, 0x31);     // nested surfaces, hover
        public static readonly Color Surface = Color.FromArgb(0x2A, 0x2E, 0x3A);      // inputs, grid cells
        public static readonly Color SurfaceHover = Color.FromArgb(0x31, 0x36, 0x45);
        public static readonly Color Border = Color.FromArgb(0x34, 0x39, 0x46);
        public static readonly Color BorderSoft = Color.FromArgb(0x26, 0x2A, 0x35);
        public static readonly Color Divider = Color.FromArgb(0x2E, 0x33, 0x40);

        // ── Text ───────────────────────────────────────────────────────────
        public static readonly Color Text = Color.FromArgb(0xE6, 0xE8, 0xEE);
        public static readonly Color TextSecondary = Color.FromArgb(0x9A, 0xA0, 0xAD);
        public static readonly Color TextMuted = Color.FromArgb(0x6E, 0x74, 0x80);

        // ── Accents ────────────────────────────────────────────────────────
        public static readonly Color Accent = Color.FromArgb(0x6A, 0xA6, 0xFF);        // primary blue
        public static readonly Color AccentHover = Color.FromArgb(0x82, 0xB4, 0xFF);
        public static readonly Color AccentPress = Color.FromArgb(0x5A, 0x95, 0xF0);
        public static readonly Color AccentBand = Color.FromArgb(0x3D, 0x6B, 0xC2);    // selected tab underline

        // ── Semantic ───────────────────────────────────────────────────────
        public static readonly Color Success = Color.FromArgb(0x76, 0xDE, 0xA2);
        public static readonly Color Danger = Color.FromArgb(0xF0, 0x6A, 0x7A);
        public static readonly Color Warning = Color.FromArgb(0xE0, 0xA4, 0x58);

        // ── Chart series (kept in sync with legend) ────────────────────────
        public static readonly Color SeriesInput = Color.FromArgb(0x6A, 0xA6, 0xFF);
        public static readonly Color SeriesOutput = Color.FromArgb(0x76, 0xDE, 0xA2);
        public static readonly Color SeriesCache = Color.FromArgb(0xE0, 0xA4, 0x58);
        public static readonly Color SeriesCost = Color.FromArgb(0xE0, 0x7A, 0xB0);

        // ── Grid ───────────────────────────────────────────────────────────
        public static readonly Color GridHeaderBack = Color.FromArgb(0x22, 0x26, 0x31);
        public static readonly Color GridRowAlt = Color.FromArgb(0x1F, 0x22, 0x2B);
        public static readonly Color GridSelection = Color.FromArgb(0x2F, 0x3B, 0x52);

        // ── Fonts ──────────────────────────────────────────────────────────
        public const string FontFamily = "Microsoft YaHei UI";
        public static readonly Font DefaultFont = new(FontFamily, 9F);
        public static readonly Font ValueFont = new(FontFamily, 11F, FontStyle.Bold);
        public static readonly Font SmallFont = new(FontFamily, 8.5F);
        public static readonly Font StatusFont = new(FontFamily, 9F, FontStyle.Bold);

        // ── Layout metrics ─────────────────────────────────────────────────
        public const int Radius = 6;
        public const int ControlHeight = 30;
        private const int ButtonHorizontalPadding = 12;
        private const int ButtonVerticalAllowance = 12;

        /// <summary>Returns a button height based on the actual rendered font metrics.</summary>
        public static int GetButtonHeight(Font? font = null)
        {
            var textSize = TextRenderer.MeasureText("国Ag", font ?? DefaultFont, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return Math.Max(ControlHeight, textSize.Height + ButtonVerticalAllowance);
        }

        /// <summary>Returns the width needed to display a single-line button label.</summary>
        public static int GetButtonWidth(string text, Font? font = null, int minimumWidth = 0)
        {
            var textSize = TextRenderer.MeasureText(text, font ?? DefaultFont, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return Math.Max(minimumWidth, textSize.Width + ButtonHorizontalPadding * 2);
        }

        /// <summary>The shared dark base font used by the whole app.</summary>
        public static void ApplyFormTheme(Form form)
        {
            form.BackColor = Window;
            form.ForeColor = Text;
            // Intentionally leave form.Font alone: AutoScaleDimensions was computed by the
            // designer against the default font, and swapping it post-InitializeComponent would
            // shift the absolute-row layout. Child controls get the theme font individually.
        }

        /// <summary>Walks a dialog's control tree and applies the dark theme to every surface.</summary>
        public static void StyleDialog(Form dialog)
        {
            ApplyFormTheme(dialog);
            StyleTree(dialog);
            // Dark title bar: apply now if the handle exists, otherwise as soon as it's created.
            if (dialog.IsHandleCreated)
            {
                Form1.EnableDarkTitleBar(dialog.Handle);
            }
            else
            {
                dialog.HandleCreated += (_, _) => Form1.EnableDarkTitleBar(dialog.Handle);
            }
        }

        private static void StyleTree(Control root)
        {
            switch (root)
            {
                case GroupBox groupBox:
                    StyleGroupBox(groupBox);
                    foreach (Control child in groupBox.Controls) StyleTree(child);
                    break;
                case Button button:
                    StyleButton(button, ResolveDialogButtonRole(button));
                    foreach (Control child in button.Controls) StyleTree(child);
                    break;
                case TextBoxBase textBox:
                    StyleInput(textBox);
                    if (textBox.Multiline)
                    {
                        DarkScrollbars.ApplyWhenReady(textBox);
                    }
                    break;
                case ComboBox comboBox:
                    StyleComboBox(comboBox);
                    break;
                case CheckBox checkBox:
                    if (checkBox.Appearance == Appearance.Button)
                    {
                        StyleButtonCheckBox(checkBox);
                    }
                    else
                    {
                        StyleCheckBox(checkBox);
                    }
                    break;
                case Label label:
                    label.ForeColor = Text;
                    label.BackColor = Color.Transparent;
                    label.Font = DefaultFont;
                    break;
                case DataGridView grid:
                    StyleDataGridView(grid);
                    DarkScrollbars.ApplyWhenReady(grid);
                    break;
                case ListBox listBox:
                    listBox.BackColor = Surface;
                    listBox.ForeColor = Text;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    listBox.Font = DefaultFont;
                    DarkScrollbars.ApplyWhenReady(listBox);
                    break;
                default:
                    root.BackColor = Window;
                    root.ForeColor = Text;
                    root.Font = DefaultFont;
                    foreach (Control child in root.Controls) StyleTree(child);
                    break;
            }
        }

        private static ButtonRole ResolveDialogButtonRole(Button button)
        {
            var text = button.Text?.Trim();
            // Heuristic: OK/Save styled as primary (accent), Cancel/Delete as danger.
            if (button.DialogResult == DialogResult.OK || text is "Save" or "保存" or "OK" or "确定")
            {
                return ButtonRole.Primary;
            }
            if (text is "Delete" or "删除" or "Cancel" or "取消")
            {
                return ButtonRole.Danger;
            }
            return ButtonRole.Default;
        }

        private static void StyleDataGridView(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Panel;
            grid.GridColor = Divider;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Panel;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = GridSelection;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Font = DefaultFont;
            grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
            grid.ColumnHeadersDefaultCellStyle.BackColor = GridHeaderBack;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = StatusFont;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeaderBack;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 32;
        }

        /// <summary>Styles a CheckBox rendered as a button (Appearance.Button) as a theme toggle.</summary>
        public static void StyleButtonCheckBox(CheckBox toggle)
        {
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 1;
            toggle.FlatAppearance.BorderColor = toggle.Checked ? Accent : Border;
            toggle.BackColor = toggle.Checked ? Accent : Surface;
            toggle.ForeColor = toggle.Checked ? Color.White : TextSecondary;
            toggle.Font = DefaultFont;
            toggle.TextAlign = ContentAlignment.MiddleCenter;
            toggle.Padding = Padding.Empty;
            if (!toggle.AutoSize)
            {
                toggle.MinimumSize = new Size(toggle.MinimumSize.Width, GetButtonHeight(toggle.Font));
            }
            toggle.Cursor = Cursors.Hand;
        }

        /// <summary>Styles a context menu / toolstrip strip to match the dark theme.</summary>
        public static void StyleContextMenuStrip(ContextMenuStrip menu)
        {
            menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Panel;
            menu.ForeColor = Text;
            menu.Font = DefaultFont;
            menu.ShowImageMargin = false;
            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = Panel;
                item.ForeColor = Text;
                item.Font = DefaultFont;
            }
        }

        /// <summary>
        /// Custom renderer that paints menu items with the dark palette so the tray
        /// context menu no longer drops to a light system surface.
        /// </summary>
        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer()
                : base(new DarkColorTable())
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var rc = new Rectangle(Point.Empty, e.Item.Size);
                var back = e.Item.Selected ? SurfaceHover : Panel;
                using var brush = new SolidBrush(back);
                e.Graphics.FillRectangle(brush, rc);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : TextMuted;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using var pen = new Pen(BorderSoft);
                var y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 2, y, e.Item.Width - 2, y);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => SurfaceHover;
            public override Color MenuItemSelected => SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => SurfaceHover;
            public override Color MenuItemPressedGradientBegin => Surface;
            public override Color MenuItemPressedGradientEnd => Surface;
            public override Color MenuStripGradientBegin => Panel;
            public override Color MenuStripGradientEnd => Panel;
            public override Color ToolStripDropDownBackground => Panel;
            public override Color ImageMarginGradientBegin => Panel;
            public override Color ImageMarginGradientMiddle => Panel;
            public override Color ImageMarginGradientEnd => Panel;
            public override Color SeparatorDark => BorderSoft;
            public override Color SeparatorLight => BorderSoft;
            public override Color CheckBackground => Accent;
            public override Color CheckSelectedBackground => Accent;
            public override Color ButtonSelectedHighlight => SurfaceHover;
            public override Color ButtonSelectedGradientBegin => SurfaceHover;
            public override Color ButtonSelectedGradientEnd => SurfaceHover;
        }

        /// <summary>Styles a flat button. Primary buttons get the accent fill; others stay subtle.</summary>
        public static void StyleButton(Button button, ButtonRole role = ButtonRole.Default)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = Surface;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Font = DefaultFont;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(ButtonHorizontalPadding, 0, ButtonHorizontalPadding, 0);
            var preferredHeight = GetButtonHeight(button.Font);
            // Respect a deliberately compact designer size (the top toolbar is only 30px high).
            // Forcing a 30px button into that row, plus its margins, clips the bottom border.
            if (button.Height >= preferredHeight)
            {
                button.MinimumSize = new Size(button.MinimumSize.Width, preferredHeight);
            }

            ApplyButtonColors(button, role);

            // Keep the label readable when the button is disabled (WinForms otherwise paints
            // disabled text as system gray, which vanishes on the dark surface).
            button.EnabledChanged -= Button_EnabledChanged;
            button.EnabledChanged += Button_EnabledChanged;
            // Attach the role so the handler can re-derive the right disabled color.
            button.Tag = role;
        }

        private static void Button_EnabledChanged(object? sender, EventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var role = button.Tag is ButtonRole r ? r : ButtonRole.Default;
            ApplyButtonColors(button, role);
        }

        private static void ApplyButtonColors(Button button, ButtonRole role)
        {
            if (!button.Enabled)
            {
                // Disabled: dim the fill but keep a legible muted label instead of system gray.
                button.BackColor = Surface;
                button.ForeColor = TextMuted;
                button.FlatAppearance.BorderColor = BorderSoft;
                return;
            }

            switch (role)
            {
                case ButtonRole.Primary:
                    button.BackColor = Accent;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = Accent;
                    button.FlatAppearance.MouseOverBackColor = AccentHover;
                    button.FlatAppearance.MouseDownBackColor = AccentPress;
                    break;
                case ButtonRole.Danger:
                    button.BackColor = Surface;
                    button.ForeColor = Danger;
                    button.FlatAppearance.BorderColor = Color.FromArgb(0x5A, 0x3A, 0x40);
                    break;
                case ButtonRole.AccentOutline:
                    button.BackColor = Surface;
                    button.ForeColor = Accent;
                    button.FlatAppearance.BorderColor = Color.FromArgb(0x3A, 0x4A, 0x66);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x2F, 0x3A, 0x52);
                    break;
                default:
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                    button.FlatAppearance.BorderColor = Border;
                    break;
            }
        }

        /// <summary>Styles a TextBox/ComboBox surface to match the dark theme.</summary>
        public static void StyleInput(Control input)
        {
            input.BackColor = Surface;
            input.ForeColor = Text;
            input.Font = DefaultFont;
            if (input is TextBoxBase textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
        }

        /// <summary>Styles a normal checkbox, keeping its label legible even when disabled.</summary>
        public static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.BackColor = Window;
            checkBox.Font = DefaultFont;
            ApplyCheckBoxForeColor(checkBox);
            checkBox.EnabledChanged -= CheckBox_EnabledChanged;
            checkBox.EnabledChanged += CheckBox_EnabledChanged;
        }

        private static void CheckBox_EnabledChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox cb)
            {
                ApplyCheckBoxForeColor(cb);
            }
        }

        private static void ApplyCheckBoxForeColor(CheckBox checkBox)
        {
            checkBox.ForeColor = checkBox.Enabled ? Text : TextSecondary;
        }

        public static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = Surface;
            comboBox.ForeColor = Text;
            comboBox.Font = DefaultFont;
            // ThemeComboBox owner-paints the closed state entirely; the drop-down list is a
            // separate native window whose scrollbar still needs the dark window theme.
            DarkScrollbars.ApplyWhenReady(comboBox);
        }

        public static void StyleLabel(Label label, bool secondary = false)
        {
            label.ForeColor = secondary ? TextSecondary : Text;
            label.Font = DefaultFont;
        }

        public static void StyleGroupBox(GroupBox groupBox)
        {
            groupBox.BackColor = Panel;
            groupBox.ForeColor = Text;
            groupBox.Font = new Font(FontFamily, 9.5F, FontStyle.Bold);
            // Owner-paint so the frame matches the dark theme instead of the default ControlDark line.
            groupBox.Paint -= PaintGroupBox;
            groupBox.Paint += PaintGroupBox;
        }

        private static void PaintGroupBox(object? sender, PaintEventArgs e)
        {
            if (sender is not GroupBox groupBox)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Card background + border.
            using var back = new SolidBrush(Panel);
            g.FillRectangle(back, groupBox.ClientRectangle);
            using var borderPen = new Pen(Border);
            var r = groupBox.ClientRectangle;
            var titleSize = TextRenderer.MeasureText(g, groupBox.Text, groupBox.Font);
            var titleX = 12;
            var titleY = 0;
            // Draw a top border that breaks for the title.
            var midY = r.Top + groupBox.Font.Height / 2;
            g.DrawLine(borderPen, r.Left, midY, titleX - 4, midY);
            g.DrawLine(borderPen, titleX + titleSize.Width + 4, midY, r.Right - 1, midY);
            g.DrawLine(borderPen, r.Right - 1, midY, r.Right - 1, r.Bottom - 1);
            g.DrawLine(borderPen, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            g.DrawLine(borderPen, r.Left, midY, r.Left, r.Bottom - 1);

            // Accent dot + title text.
            using var accent = new SolidBrush(Accent);
            g.FillRectangle(accent, titleX, midY - 4, 3, 8);
            TextRenderer.DrawText(g, groupBox.Text, groupBox.Font, new Point(titleX + 8, titleY), Text);
        }

        /// <summary>Paints a rounded card background for a panel/container. Call from Paint or via a styled panel.</summary>
        public static void PaintCard(Graphics g, Rectangle bounds, Color back, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = BuildRoundedPath(bounds, Radius);
            using (var brush = new SolidBrush(back))
            {
                g.FillPath(brush, path);
            }
            if (border is { } bordercolor)
            {
                using var pen = new Pen(bordercolor);
                g.DrawPath(pen, path);
            }
        }

        public static GraphicsPath BuildRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            var d = radius * 2;
            if (d > bounds.Width)
            {
                d = bounds.Width;
            }
            if (d > bounds.Height)
            {
                d = bounds.Height;
            }

            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

    }
}
