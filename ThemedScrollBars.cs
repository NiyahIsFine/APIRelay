using System.ComponentModel;
using System.Runtime.InteropServices;

namespace APIRelay
{
    /// <summary>
    /// A small owner-drawn scrollbar used where WinForms delegates its scrollbar colors to Windows.
    /// It intentionally has no arrows, leaving a compact track and a clearly visible draggable thumb.
    /// </summary>
    internal sealed class ThemedScrollBar : Control
    {
        private const int MinimumThumbLength = 28;
        private bool hovered;
        private bool dragging;
        private int dragOffset;
        private int maximum;
        private int largeChange = 1;
        private int value;

        public ThemedScrollBar(Orientation orientation)
        {
            Orientation = orientation;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Cursor = Cursors.Hand;
            BackColor = UiTheme.PanelAlt;
        }

        public Orientation Orientation { get; }

        [DefaultValue(0)]
        public int Maximum
        {
            get => maximum;
            set
            {
                maximum = Math.Max(0, value);
                this.value = Math.Min(this.value, maximum);
                Invalidate();
            }
        }

        [DefaultValue(1)]
        public int LargeChange
        {
            get => largeChange;
            set
            {
                largeChange = Math.Max(1, value);
                Invalidate();
            }
        }

        [DefaultValue(0)]
        public int Value
        {
            get => value;
            set
            {
                var clamped = Math.Clamp(value, 0, maximum);
                if (this.value == clamped)
                {
                    return;
                }

                this.value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var track = ClientRectangle;
            if (track.Width <= 0 || track.Height <= 0)
            {
                return;
            }

            using var trackBrush = new SolidBrush(UiTheme.PanelAlt);
            e.Graphics.FillRectangle(trackBrush, track);

            if (maximum <= 0)
            {
                return;
            }

            using var thumbBrush = new SolidBrush(dragging ? UiTheme.Accent : hovered ? UiTheme.SurfaceHover : UiTheme.Surface);
            var thumb = GetThumbBounds();
            e.Graphics.FillRectangle(thumbBrush, thumb);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!dragging)
            {
                hovered = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var thumb = GetThumbBounds();
            var coordinate = Orientation == Orientation.Vertical ? e.Y : e.X;
            if (thumb.Contains(e.Location))
            {
                dragging = true;
                dragOffset = coordinate - (Orientation == Orientation.Vertical ? thumb.Top : thumb.Left);
                Capture = true;
            }
            else
            {
                var thumbStart = Orientation == Orientation.Vertical ? thumb.Top : thumb.Left;
                Value += coordinate < thumbStart ? -largeChange : largeChange;
            }

            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var thumbBounds = GetThumbBounds();
                var length = Orientation == Orientation.Vertical ? Height : Width;
                var thumbLength = Orientation == Orientation.Vertical ? thumbBounds.Height : thumbBounds.Width;
                var coordinate = (Orientation == Orientation.Vertical ? e.Y : e.X) - dragOffset;
                var available = Math.Max(1, length - thumbLength);
                Value = (int)Math.Round(Math.Clamp(coordinate, 0, available) * (double)maximum / available);

                // A fast drag floods the queue with WM_MOUSEMOVE, which starves the WM_PAINT that
                // Invalidate() schedules until the mouse stops. Force the repaint through now.
                Update();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            hovered = ClientRectangle.Contains(e.Location);
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Orientation == Orientation.Vertical && maximum > 0 && e.Delta != 0)
            {
                var wheelNotches = e.Delta / SystemInformation.MouseWheelScrollDelta;
                if (wheelNotches != 0)
                {
                    Value -= wheelNotches * Math.Max(1, largeChange / 3);
                }
            }

            base.OnMouseWheel(e);
        }

        private Rectangle GetThumbBounds()
        {
            var length = Orientation == Orientation.Vertical ? Height : Width;
            var crossLength = Orientation == Orientation.Vertical ? Width : Height;
            var total = maximum + largeChange;
            var thumbLength = total <= 0 ? length : Math.Clamp((int)Math.Round(length * (double)largeChange / total), MinimumThumbLength, length);
            var available = Math.Max(0, length - thumbLength);
            var offset = maximum == 0 ? 0 : (int)Math.Round(available * (double)value / maximum);
            return Orientation == Orientation.Vertical
                ? new Rectangle(0, offset, crossLength, thumbLength)
                : new Rectangle(offset, 0, thumbLength, crossLength);
        }
    }

    /// <summary>
    /// DataGridView with compact, owner-drawn scrollbars. A DataGridView is a managed container so it
    /// can host the scrollbar children directly (unlike a native TextBox).
    /// </summary>
    internal sealed class ThemedDataGridView : DataGridView
    {
        private const int ScrollBarThickness = 10;
        private readonly ThemedScrollBar verticalScrollBar = new(Orientation.Vertical);
        private readonly ThemedScrollBar horizontalScrollBar = new(Orientation.Horizontal);
        private bool synchronizing;

        public ThemedDataGridView()
        {
            ScrollBars = ScrollBars.None;
            Controls.Add(verticalScrollBar);
            Controls.Add(horizontalScrollBar);
            verticalScrollBar.ValueChanged += (_, _) => SetVerticalOffset(verticalScrollBar.Value);
            horizontalScrollBar.ValueChanged += (_, _) => SetHorizontalOffset(horizontalScrollBar.Value);
            Scroll += (_, _) => SynchronizeScrollBars();
            RowsAdded += (_, _) => SynchronizeScrollBars();
            RowsRemoved += (_, _) => SynchronizeScrollBars();
            ColumnWidthChanged += (_, _) => SynchronizeScrollBars();
            RowHeightChanged += (_, _) => SynchronizeScrollBars();
            SizeChanged += (_, _) => SynchronizeScrollBars();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BeginInvoke(SynchronizeScrollBars);
        }

        protected override void OnDataBindingComplete(DataGridViewBindingCompleteEventArgs e)
        {
            base.OnDataBindingComplete(e);
            SynchronizeScrollBars();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // ScrollBars.None disables DataGridView's built-in wheel-scroll handling (it isn't just
            // hiding the native bars), so wheel scrolling has to be applied manually here.
            var wheelNotches = e.Delta / SystemInformation.MouseWheelScrollDelta;
            if (wheelNotches != 0 && Rows.Count > 0)
            {
                var linesPerNotch = SystemInformation.MouseWheelScrollLines <= 0 ? 3 : SystemInformation.MouseWheelScrollLines;
                var newIndex = Math.Clamp(FirstDisplayedScrollingRowIndex - wheelNotches * linesPerNotch, 0, Rows.Count - 1);
                try
                {
                    FirstDisplayedScrollingRowIndex = newIndex;
                }
                catch (InvalidOperationException)
                {
                    // The grid can reject a scroll request while its rows are changing.
                }

                SynchronizeScrollBars();
            }

            base.OnMouseWheel(e);
        }

        private void SynchronizeScrollBars()
        {
            if (IsDisposed || !IsHandleCreated || synchronizing)
            {
                return;
            }

            synchronizing = true;
            try
            {
                var headerHeight = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
                var availableHeight = Math.Max(1, ClientSize.Height - headerHeight);
                var contentHeight = Rows.GetRowsHeight(DataGridViewElementStates.Visible);
                var verticalMaximum = Math.Max(0, contentHeight - availableHeight);
                var horizontalVisible = Columns.GetColumnsWidth(DataGridViewElementStates.Visible) - ClientSize.Width > 0;

                verticalScrollBar.Visible = verticalMaximum > 0;
                verticalScrollBar.Bounds = new Rectangle(
                    Math.Max(0, ClientSize.Width - ScrollBarThickness),
                    headerHeight,
                    ScrollBarThickness,
                    Math.Max(0, ClientSize.Height - headerHeight - (horizontalVisible ? ScrollBarThickness : 0)));
                verticalScrollBar.Maximum = verticalMaximum;
                verticalScrollBar.LargeChange = availableHeight;
                verticalScrollBar.Value = Math.Min(verticalMaximum, VerticalScrollingOffset);

                var contentWidth = Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
                var horizontalMaximum = Math.Max(0, contentWidth - ClientSize.Width);
                horizontalScrollBar.Visible = horizontalMaximum > 0;
                horizontalScrollBar.Bounds = new Rectangle(
                    0,
                    Math.Max(0, ClientSize.Height - ScrollBarThickness),
                    Math.Max(0, ClientSize.Width - (verticalScrollBar.Visible ? ScrollBarThickness : 0)),
                    ScrollBarThickness);
                horizontalScrollBar.Maximum = horizontalMaximum;
                horizontalScrollBar.LargeChange = Math.Max(1, ClientSize.Width);
                horizontalScrollBar.Value = Math.Min(horizontalMaximum, HorizontalScrollingOffset);

                verticalScrollBar.BringToFront();
                horizontalScrollBar.BringToFront();
            }
            finally
            {
                synchronizing = false;
            }
        }

        private void SetVerticalOffset(int offset)
        {
            if (synchronizing || Rows.Count == 0)
            {
                return;
            }

            var accumulatedHeight = 0;
            foreach (DataGridViewRow row in Rows)
            {
                if (!row.Visible)
                {
                    continue;
                }

                if (accumulatedHeight + row.Height > offset)
                {
                    try
                    {
                        FirstDisplayedScrollingRowIndex = row.Index;
                    }
                    catch (InvalidOperationException)
                    {
                        // The grid can reject a scroll request while its rows are changing.
                    }
                    SynchronizeScrollBars();
                    return;
                }

                accumulatedHeight += row.Height;
            }
        }

        private void SetHorizontalOffset(int offset)
        {
            if (!synchronizing)
            {
                HorizontalScrollingOffset = offset;
            }
        }
    }

    /// <summary>
    /// Hosts a multiline <see cref="TextBox"/> alongside an owner-drawn dark scrollbar. A native
    /// TextBox cannot contain child controls, so the scrollbar lives on this panel as a sibling.
    /// </summary>
    internal static class ThemedScroll
    {
        /// <summary>
        /// Reparents <paramref name="textBox"/> into a themed host panel with a dark vertical
        /// scrollbar, preserving its position inside a parent TableLayoutPanel cell.
        /// </summary>
        public static void AttachVertical(TextBox textBox)
        {
            if (textBox.Parent is not TableLayoutPanel layout)
            {
                return;
            }

            var position = layout.GetPositionFromControl(textBox);
            if (position.Column < 0 || position.Row < 0)
            {
                return;
            }

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = textBox.Margin,
                BackColor = UiTheme.Surface
            };
            var scrollBar = new ThemedScrollBar(Orientation.Vertical)
            {
                Dock = DockStyle.Right,
                Width = 10,
                Visible = false
            };

            layout.Controls.Remove(textBox);
            textBox.Dock = DockStyle.Fill;
            textBox.ScrollBars = ScrollBars.None;
            textBox.BorderStyle = BorderStyle.FixedSingle;

            host.Controls.Add(textBox);
            host.Controls.Add(scrollBar);
            scrollBar.BringToFront();
            layout.Controls.Add(host, position.Column, position.Row);

            _ = new TextBoxScrollSync(textBox, scrollBar);
        }

        /// <summary>Keeps a themed scrollbar in sync with a multiline TextBox both ways.</summary>
        private sealed class TextBoxScrollSync
        {
            private const int EmGetLineCount = 0x00BA;
            private const int EmGetFirstVisibleLine = 0x00CE;
            private const int EmLineScroll = 0x00B6;

            private readonly TextBox textBox;
            private readonly ThemedScrollBar scrollBar;
            private ScrollWatcher? watcher;
            private bool syncing;

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            public TextBoxScrollSync(TextBox textBox, ThemedScrollBar scrollBar)
            {
                this.textBox = textBox;
                this.scrollBar = scrollBar;
                scrollBar.ValueChanged += (_, _) => Drive();
                textBox.TextChanged += (_, _) => Update();
                textBox.Resize += (_, _) => Update();

                if (textBox.IsHandleCreated)
                {
                    Hook();
                }
                else
                {
                    textBox.HandleCreated += (_, _) => Hook();
                }
            }

            private void Hook()
            {
                watcher?.ReleaseHandle();
                watcher = new ScrollWatcher(textBox.Handle, Update);
                Update();
            }

            private void Update()
            {
                if (syncing || !textBox.IsHandleCreated)
                {
                    return;
                }

                syncing = true;
                try
                {
                    var totalLines = (int)SendMessage(textBox.Handle, EmGetLineCount, IntPtr.Zero, IntPtr.Zero);
                    var lineHeight = Math.Max(1, textBox.Font.Height);
                    var visibleLines = Math.Max(1, textBox.ClientSize.Height / lineHeight);
                    var maximum = Math.Max(0, totalLines - visibleLines);
                    var firstVisible = (int)SendMessage(textBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);

                    scrollBar.Maximum = maximum;
                    scrollBar.LargeChange = visibleLines;
                    scrollBar.Value = Math.Min(maximum, firstVisible);
                    scrollBar.Visible = maximum > 0;
                }
                finally
                {
                    syncing = false;
                }
            }

            private void Drive()
            {
                if (syncing || !textBox.IsHandleCreated)
                {
                    return;
                }

                var firstVisible = (int)SendMessage(textBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
                var delta = scrollBar.Value - firstVisible;
                if (delta != 0)
                {
                    SendMessage(textBox.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(delta));
                }
            }

            /// <summary>Watches the TextBox for wheel / keyboard scrolling so the bar stays in sync.</summary>
            private sealed class ScrollWatcher : NativeWindow
            {
                private const int WmVScroll = 0x0115;
                private const int WmMouseWheel = 0x020A;
                private const int WmKeyUp = 0x0101;
                private const int WmLButtonUp = 0x0202;
                private readonly Action onScroll;

                public ScrollWatcher(IntPtr handle, Action onScroll)
                {
                    this.onScroll = onScroll;
                    AssignHandle(handle);
                }

                protected override void WndProc(ref Message m)
                {
                    base.WndProc(ref m);
                    if (m.Msg is WmVScroll or WmMouseWheel or WmKeyUp or WmLButtonUp)
                    {
                        onScroll();
                    }
                }
            }
        }
    }
}
