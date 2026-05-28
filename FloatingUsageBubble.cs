using System.Drawing.Drawing2D;
using System.Globalization;

namespace APIRelay
{
    internal sealed class FloatingUsageBubble : Form
    {
        private const int BubbleCornerRadius = 24;
        private static readonly IntPtr HwndTopMost = new(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private readonly Label titleLabel = new();
        private readonly Label inputValueLabel = new();
        private readonly Label outputValueLabel = new();
        private readonly Label costValueLabel = new();
        private readonly Label inputLabel = new();
        private readonly Label outputLabel = new();
        private readonly Label costLabel = new();
        private readonly Label toastLabel = new();
        private readonly System.Windows.Forms.Timer toastTimer = new();
        private int toastTicksRemaining;
        private bool dragging;
        private bool hasStartupLocation;
        private Point dragOffset;

        public event EventHandler? BubbleDoubleClicked;

        public FloatingUsageBubble(AppLanguage language = AppLanguage.English)
        {
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(26, 29, 36);
            ClientSize = new Size(232, 124);
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0.86;
            Padding = new Padding(14, 10, 14, 12);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = AppTexts.GetText(language, TextId.Txt125);
            TopMost = true;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                RowCount = 4
            };
            layout.BackColor = Color.Transparent;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));

            titleLabel.AutoSize = true;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(titleLabel, 0, 0);
            layout.SetColumnSpan(titleLabel, 2);

            AddStatRow(layout, 1, inputLabel, inputValueLabel, Color.FromArgb(126, 174, 255));
            AddStatRow(layout, 2, outputLabel, outputValueLabel, Color.FromArgb(116, 222, 162));
            AddStatRow(layout, 3, costLabel, costValueLabel, Color.FromArgb(255, 160, 207));

            toastLabel.AutoSize = true;
            toastLabel.BackColor = Color.FromArgb(255, 226, 243);
            toastLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            toastLabel.ForeColor = Color.FromArgb(190, 34, 111);
            toastLabel.Padding = new Padding(10, 4, 10, 4);
            toastLabel.Visible = false;

            Controls.Add(layout);
            Controls.Add(toastLabel);

            AttachDragHandlers(this);
            AttachDragHandlers(layout);
            foreach (Control control in layout.Controls)
            {
                AttachDragHandlers(control);
            }

            toastTimer.Interval = 80;
            toastTimer.Tick += ToastTimer_Tick;
            ApplyLanguage(language);
            UpdateStats(0, 0, 0m);
        }

        public void ApplyLanguage(AppLanguage language)
        {
            Text = AppTexts.GetText(language, TextId.Txt125);
            titleLabel.Text = AppTexts.GetText(language, TextId.Txt126);
            inputLabel.Text = AppTexts.GetText(language, TextId.Txt19);
            outputLabel.Text = AppTexts.GetText(language, TextId.Txt20);
            costLabel.Text = AppTexts.GetText(language, TextId.Txt22);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int wsExToolWindow = 0x00000080;
                const int wsExNoActivate = 0x08000000;
                var createParams = base.CreateParams;
                createParams.ExStyle |= wsExToolWindow | wsExNoActivate;
                return createParams;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!hasStartupLocation)
            {
                PlaceNearBottomRight();
            }

            KeepAboveNormalWindows();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                KeepAboveNormalWindows();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using var path = new GraphicsPath();
            var bounds = new Rectangle(Point.Empty, ClientSize);
            var radius = BubbleCornerRadius;
            path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var glowPen = new Pen(Color.FromArgb(36, 255, 255, 255), 4F);
            using var borderPen = new Pen(Color.FromArgb(86, 255, 255, 255));
            e.Graphics.DrawRoundedRectangle(glowPen, new Rectangle(2, 2, Width - 5, Height - 5), BubbleCornerRadius);
            e.Graphics.DrawRoundedRectangle(borderPen, new Rectangle(0, 0, Width - 1, Height - 1), BubbleCornerRadius);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toastTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        public void UpdateStats(long inputTokens, long outputTokens, decimal totalCost)
        {
            inputValueLabel.Text = inputTokens.ToString("N0", CultureInfo.InvariantCulture);
            outputValueLabel.Text = outputTokens.ToString("N0", CultureInfo.InvariantCulture);
            costValueLabel.Text = FormatCurrency(totalCost);
        }

        public void KeepAboveNormalWindows()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            TopMost = true;
            SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        public void ShowCostToast(decimal cost)
        {
            if (!Visible)
            {
                return;
            }

            toastLabel.Text = "+" + FormatCurrency(cost);
            toastLabel.Left = Width - toastLabel.Width - 14;
            toastLabel.Top = 10;
            toastLabel.Region?.Dispose();
            toastLabel.Region = CreateRoundedRegion(toastLabel.ClientSize, 16);
            toastLabel.Visible = true;
            toastLabel.BringToFront();
            KeepAboveNormalWindows();
            toastTicksRemaining = 24;
            toastTimer.Stop();
            toastTimer.Start();
        }

        public void PlaceNearBottomRight()
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            hasStartupLocation = true;
            Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
        }

        public void SetStartupLocation(Point location)
        {
            hasStartupLocation = true;
            Location = location;
        }

        private static void AddStatRow(TableLayoutPanel layout, int row, Label label, Label valueLabel, Color valueColor)
        {
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.ForeColor = Color.FromArgb(190, 196, 210);
            label.TextAlign = ContentAlignment.MiddleLeft;

            valueLabel.AutoSize = true;
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            valueLabel.ForeColor = valueColor;
            valueLabel.TextAlign = ContentAlignment.MiddleRight;

            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(valueLabel, 1, row);
        }

        private static string FormatCurrency(decimal value)
        {
            return "$" + value.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        private void ToastTimer_Tick(object? sender, EventArgs e)
        {
            toastTicksRemaining--;
            toastLabel.Top = Math.Max(0, toastLabel.Top - 1);

            if (toastTicksRemaining <= 0)
            {
                toastTimer.Stop();
                toastLabel.Visible = false;
            }
        }

        private void AttachDragHandlers(Control control)
        {
            control.DoubleClick += (_, _) => BubbleDoubleClicked?.Invoke(this, EventArgs.Empty);

            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                dragging = true;
                dragOffset = e.Location;
            };

            control.MouseMove += (_, e) =>
            {
                if (!dragging)
                {
                    return;
                }

                Location = new Point(Location.X + e.X - dragOffset.X, Location.Y + e.Y - dragOffset.Y);
            };

            control.MouseUp += (_, _) => dragging = false;
        }

        private static Region CreateRoundedRegion(Size size, int radius)
        {
            using var path = new GraphicsPath();
            var bounds = new Rectangle(Point.Empty, size);
            path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }

    internal static class GraphicsExtensions
    {
        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            graphics.DrawPath(pen, path);
        }
    }
}
