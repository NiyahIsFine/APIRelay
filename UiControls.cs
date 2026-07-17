using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace APIRelay
{
    /// <summary>Panel that paints as a rounded card on the dark theme.</summary>
    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Panel;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintCard(e.Graphics, ClientRectangle, BackColor, UiTheme.Border);
            base.OnPaint(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT — let the rounded card show
                return cp;
            }
        }
    }

    /// <summary>
    /// A flat, owner-drawn button that renders as a rounded pill/card matching the dark theme,
    /// with primary / default / danger roles. Replaces WinForms' 3D button chrome everywhere.
    /// </summary>
    internal sealed class ThemeButton : Button
    {
        private ButtonRole role = ButtonRole.Default;
        private bool hovered;
        private bool pressed;

        public ThemeButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            Font = UiTheme.DefaultFont;
            Cursor = Cursors.Hand;
            Height = UiTheme.ControlHeight;
        }

        [DefaultValue(ButtonRole.Default)]
        public ButtonRole Role
        {
            get => role;
            set
            {
                role = value;
                Invalidate();
            }
        }

        [DefaultValue(true)]
        public new bool DoubleBuffered
        {
            get => true;
            set { }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = ClientRectangle;
            var paintBounds = Rectangle.Inflate(bounds, -1, -1);
            if (paintBounds.Width <= 0 || paintBounds.Height <= 0)
            {
                return;
            }

            var (back, fore) = GetColors();

            // Keep the entire one-pixel stroke inside the client area; drawing on ClientRectangle
            // clips the bottom/right half of the border on WinForms.
            using var path = UiTheme.BuildRoundedPath(paintBounds, UiTheme.Radius);
            using (var brush = new SolidBrush(back))
            {
                g.FillPath(brush, path);
            }

            if (role == ButtonRole.Primary && Enabled)
            {
                // subtle top highlight for primary buttons
                using var hl = new LinearGradientBrush(bounds, Color.FromArgb(40, Color.White), Color.FromArgb(0, Color.White), 90F);
                using var hlPath = UiTheme.BuildRoundedPath(bounds, UiTheme.Radius);
                g.FillPath(hl, hlPath);
            }

            if (!Enabled)
            {
                using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                g.FillPath(dim, path);
            }

            // Text
            TextRenderer.DrawText(g, Text, Font, bounds, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        private (Color back, Color fore) GetColors()
        {
            if (!Enabled)
            {
                return (UiTheme.Surface, UiTheme.TextMuted);
            }

            switch (role)
            {
                case ButtonRole.Primary:
                    if (pressed) return (UiTheme.AccentPress, Color.White);
                    if (hovered) return (UiTheme.AccentHover, Color.White);
                    return (UiTheme.Accent, Color.White);
                case ButtonRole.Danger:
                    if (pressed) return (Color.FromArgb(0x4A, 0x2C, 0x32), UiTheme.Danger);
                    if (hovered) return (Color.FromArgb(0x5A, 0x3A, 0x40), UiTheme.Danger);
                    return (UiTheme.Surface, UiTheme.Danger);
                default:
                    if (pressed) return (UiTheme.Surface, UiTheme.Text);
                    if (hovered) return (UiTheme.SurfaceHover, UiTheme.Text);
                    return (UiTheme.Surface, UiTheme.Text);
            }
        }
    }

    /// <summary>
    /// A ComboBox whose closed state is fully owner-painted (UserPaint), so the native control
    /// never draws its light frame or arrow — eliminating hover flicker on the dark theme.
    /// Requires DropDownStyle.DropDownList (all combos in this app use it).
    /// </summary>
    internal sealed class ThemeComboBox : ComboBox
    {
        private const int ArrowWidth = 22;
        private bool hovered;

        public ThemeComboBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            DropDownStyle = ComboBoxStyle.DropDownList;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var active = hovered || DroppedDown;
            var back = !Enabled ? UiTheme.PanelAlt : active ? UiTheme.SurfaceHover : UiTheme.Surface;
            var border = !Enabled ? UiTheme.BorderSoft : (Focused || DroppedDown) ? UiTheme.Accent : UiTheme.Border;
            var textColor = !Enabled ? UiTheme.TextMuted : UiTheme.Text;
            var arrowColor = !Enabled ? UiTheme.TextMuted : active ? UiTheme.Text : UiTheme.TextSecondary;

            using (var backBrush = new SolidBrush(back))
            {
                g.FillRectangle(backBrush, bounds);
            }
            using (var borderPen = new Pen(border))
            {
                g.DrawRectangle(borderPen, new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1));
            }

            // Chevron on the right.
            var arrowBounds = new Rectangle(bounds.Right - ArrowWidth, bounds.Top, ArrowWidth, bounds.Height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var arrowPen = new Pen(arrowColor, 1.8F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                var centerX = arrowBounds.Left + arrowBounds.Width / 2;
                var centerY = arrowBounds.Top + arrowBounds.Height / 2;
                g.DrawLines(arrowPen, new[]
                {
                    new Point(centerX - 4, centerY - 2),
                    new Point(centerX, centerY + 2),
                    new Point(centerX + 4, centerY - 2)
                });
            }
            g.SmoothingMode = SmoothingMode.None;

            // Selected item text.
            var text = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text;
            if (!string.IsNullOrEmpty(text))
            {
                var textBounds = Rectangle.FromLTRB(bounds.Left + 8, bounds.Top, arrowBounds.Left - 2, bounds.Bottom);
                TextRenderer.DrawText(g, text, Font, textBounds, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// A compact owner-drawn checkbox that keeps its box, checkmark and label within the dark palette.
    /// </summary>
    internal sealed class ThemeCheckBox : CheckBox
    {
        private const int BoxSize = 16;
        private const int TextGap = 7;

        public ThemeCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var textSize = TextRenderer.MeasureText(Text, Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            var width = Padding.Horizontal + BoxSize + TextGap + textSize.Width;
            var height = Math.Max(BoxSize, textSize.Height) + Padding.Vertical;
            return new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(BackColor);
            g.FillRectangle(background, ClientRectangle);

            var boxY = Math.Max(Padding.Top, (Height - BoxSize) / 2);
            var boxX = RightToLeft == RightToLeft.Yes
                ? Width - Padding.Right - BoxSize
                : Padding.Left;
            var box = new Rectangle(boxX, boxY, BoxSize - 1, BoxSize - 1);
            var enabled = Enabled;
            var selected = CheckState != CheckState.Unchecked;
            var fill = !enabled ? UiTheme.Surface : selected ? UiTheme.Accent : UiTheme.Surface;
            var border = !enabled ? UiTheme.BorderSoft : selected ? UiTheme.Accent : UiTheme.Border;

            using (var path = UiTheme.BuildRoundedPath(box, 3))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(borderPen, path);
            }

            if (CheckState == CheckState.Checked)
            {
                using var checkPen = new Pen(Color.White, 2F)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawLines(checkPen, new[]
                {
                    new Point(box.Left + 3, box.Top + 8),
                    new Point(box.Left + 6, box.Bottom - 3),
                    new Point(box.Right - 3, box.Top + 4)
                });
            }
            else if (CheckState == CheckState.Indeterminate)
            {
                using var markBrush = new SolidBrush(enabled ? Color.White : UiTheme.TextMuted);
                g.FillRectangle(markBrush, box.Left + 4, box.Top + 7, box.Width - 7, 2);
            }

            var textLeft = RightToLeft == RightToLeft.Yes ? Padding.Left : box.Right + TextGap;
            var textRight = RightToLeft == RightToLeft.Yes ? box.Left - TextGap : Width - Padding.Right;
            var textBounds = Rectangle.FromLTRB(textLeft, Padding.Top, textRight, Height - Padding.Bottom);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            flags |= RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            TextRenderer.DrawText(g, Text, Font, textBounds, enabled ? ForeColor : UiTheme.TextSecondary, flags);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, textBounds, ForeColor, BackColor);
            }
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); PerformLayout(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); PerformLayout(); Invalidate(); }
        protected override void OnRightToLeftChanged(EventArgs e) { base.OnRightToLeftChanged(e); Invalidate(); }
    }

    /// a title, then hosts child controls. Avoids the noisy GroupBox border on dark.
    /// </summary>
    internal sealed class SectionHeader : Panel
    {
        private string title = string.Empty;

        public SectionHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Panel;
            Height = 28;
        }

        [DefaultValue("")]
        public string Title
        {
            get => title;
            set { title = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var accent = new SolidBrush(UiTheme.Accent);
            g.FillRectangle(accent, 0, 8, 3, Height - 16);

            if (!string.IsNullOrEmpty(title))
            {
                TextRenderer.DrawText(g, title, new Font(UiTheme.FontFamily, 9.5F, FontStyle.Bold),
                    new Rectangle(10, 0, Width - 14, Height), UiTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            base.OnPaint(e);
        }
    }
}
