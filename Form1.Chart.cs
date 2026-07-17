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
        private void DailyChartPanel_Resize(object sender, EventArgs e)
        {
            dailyChartPanel.Invalidate();
        }

        private void DailyChartPanel_MouseMove(object sender, MouseEventArgs e)
        {
            var bucketIndex = GetDailyChartBucketIndex(e.Location, dailyChartPanel.ClientRectangle);
            if (bucketIndex == dailyChartHoverBucketIndex)
            {
                dailyChartMouseLocation = e.Location;
                return;
            }

            dailyChartMouseLocation = e.Location;
            dailyChartHoverBucketIndex = bucketIndex;
            dailyChartPanel.Invalidate();
        }

        private void DailyChartPanel_MouseLeave(object sender, EventArgs e)
        {
            dailyChartMouseLocation = null;
            dailyChartHoverBucketIndex = null;
            dailyChartPanel.Invalidate();
        }

        private void DailyChartPanel_Paint(object sender, PaintEventArgs e)
        {
            DrawDailyChart(e.Graphics, dailyChartPanel.ClientRectangle);
        }

        private void DrawDailyChart(Graphics graphics, Rectangle bounds)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(UiTheme.Panel);

            if (bounds.Width <= 40 || bounds.Height <= 40)
            {
                return;
            }

            var buckets = BuildDailyChartBuckets();
            var maxTokenValue = buckets.Max(bucket => Math.Max(bucket.InputTokens, Math.Max(bucket.OutputTokens, bucket.CachedTokens)));
            var maxCostValue = buckets.Max(bucket => bucket.Cost);
            var tokenScaleMax = RoundTokenScaleMax(maxTokenValue);
            var costScaleMax = RoundCostScaleMax(maxCostValue);
            var chartBounds = new Rectangle(bounds.Left + 70, bounds.Top + 16, bounds.Width - 140, bounds.Height - 48);
            if (chartBounds.Width <= 0 || chartBounds.Height <= 0)
            {
                return;
            }

            using var axisPen = new Pen(UiTheme.Border);
            using var gridPen = new Pen(UiTheme.BorderSoft);
            using var textBrush = new SolidBrush(UiTheme.TextSecondary);
            using var legendFont = new Font(Font.FontFamily, 8.5F);

            for (var index = 0; index <= 4; index++)
            {
                var y = chartBounds.Bottom - (chartBounds.Height * index / 4);
                graphics.DrawLine(gridPen, chartBounds.Left, y, chartBounds.Right, y);
                DrawYAxisLabel(graphics, FormatTokenTick(tokenScaleMax * index / 4), legendFont, textBrush, new Rectangle(bounds.Left, y - 8, chartBounds.Left - bounds.Left - 6, 18), ContentAlignment.MiddleRight);
                DrawYAxisLabel(graphics, FormatCostTick(costScaleMax * index / 4m), legendFont, textBrush, new Rectangle(chartBounds.Right + 6, y - 8, bounds.Right - chartBounds.Right - 8, 18), ContentAlignment.MiddleLeft);
            }

            graphics.DrawRectangle(axisPen, chartBounds);

            DrawLegend(graphics, bounds, legendFont);
            DrawXAxisLabels(graphics, chartBounds, legendFont, textBrush);

            if (buckets.All(bucket => bucket.InputTokens == 0 && bucket.OutputTokens == 0 && bucket.CachedTokens == 0 && bucket.Cost == 0m))
            {
                var emptyText = GetText(TextId.Txt72);
                var emptySize = TextRenderer.MeasureText(emptyText, Font);
                TextRenderer.DrawText(graphics, emptyText, Font, new Point(chartBounds.Left + (chartBounds.Width - emptySize.Width) / 2, chartBounds.Top + (chartBounds.Height - emptySize.Height) / 2), UiTheme.TextMuted);
                return;
            }

            DrawSeries(graphics, chartBounds, buckets.Select(bucket => (double)bucket.InputTokens).ToArray(), tokenScaleMax, UiTheme.SeriesInput);
            DrawSeries(graphics, chartBounds, buckets.Select(bucket => (double)bucket.OutputTokens).ToArray(), tokenScaleMax, UiTheme.SeriesOutput);
            DrawSeries(graphics, chartBounds, buckets.Select(bucket => (double)bucket.CachedTokens).ToArray(), tokenScaleMax, UiTheme.SeriesCache);
            DrawSeries(graphics, chartBounds, buckets.Select(bucket => (double)bucket.Cost).ToArray(), (double)costScaleMax, UiTheme.SeriesCost);
            DrawHoverDetails(graphics, bounds, chartBounds, buckets, legendFont);
        }

        private DailyChartBucket[] BuildDailyChartBuckets()
        {
            var selectedDate = GetSelectedRecordDate();
            var buckets = Enumerable.Range(0, 48).Select(_ => new DailyChartBucket()).ToArray();

            foreach (var record in visibleRecords.Where(record => record.Timestamp.Date == selectedDate))
            {
                var index = Math.Clamp(record.Timestamp.Hour * 2 + record.Timestamp.Minute / 30, 0, 47);
                var bucket = buckets[index];
                bucket.InputTokens += CalculateTotalInputTokens(record);
                bucket.OutputTokens += record.CompletionTokens;
                bucket.CachedTokens += record.CachedTokens;
                bucket.CacheCreationTokens += record.CacheCreationTokens;
                bucket.Cost += CalculateRecordCost(record);
            }

            return buckets;
        }

        private void DrawLegend(Graphics graphics, Rectangle bounds, Font font)
        {
            var items = new[]
            {
                (GetText(TextId.Txt19), UiTheme.SeriesInput),
                (GetText(TextId.Txt20), UiTheme.SeriesOutput),
                (GetText(TextId.Txt21), UiTheme.SeriesCache),
                (GetText(TextId.Txt22), UiTheme.SeriesCost)
            };

            var x = bounds.Left + 78;
            foreach (var (text, color) in items)
            {
                using var pen = new Pen(color, 2F);
                graphics.DrawLine(pen, x, bounds.Top + 9, x + 18, bounds.Top + 9);
                TextRenderer.DrawText(graphics, text, font, new Point(x + 22, bounds.Top + 2), color);
                x += text.Length > 2 ? 92 : 58;
            }
        }

        private static void DrawXAxisLabels(Graphics graphics, Rectangle chartBounds, Font font, Brush textBrush)
        {
            foreach (var (label, bucketIndex) in new[] { ("00:00", 0), ("06:00", 12), ("12:00", 24), ("18:00", 36), ("24:00", 47) })
            {
                var x = chartBounds.Left + (int)Math.Round(bucketIndex * chartBounds.Width / 47.0);
                graphics.DrawString(label, font, textBrush, x - 16, chartBounds.Bottom + 4);
            }
        }

        private static void DrawYAxisLabel(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds, ContentAlignment alignment)
        {
            var format = new StringFormat
            {
                Alignment = alignment == ContentAlignment.MiddleRight ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(text, font, brush, bounds, format);
        }

        private void DrawHoverDetails(Graphics graphics, Rectangle bounds, Rectangle chartBounds, DailyChartBucket[] buckets, Font font)
        {
            if (dailyChartMouseLocation == null || dailyChartHoverBucketIndex == null || buckets.Length == 0)
            {
                return;
            }

            var mouse = dailyChartMouseLocation.Value;
            if (mouse.X < chartBounds.Left || mouse.X > chartBounds.Right || mouse.Y < chartBounds.Top || mouse.Y > chartBounds.Bottom)
            {
                return;
            }

            var bucketIndex = dailyChartHoverBucketIndex.Value;
            var x = chartBounds.Left + (int)Math.Round(bucketIndex * chartBounds.Width / 47.0);
            var bucket = buckets[bucketIndex];
            var start = TimeSpan.FromMinutes(bucketIndex * 30);
            var end = start.Add(TimeSpan.FromMinutes(30));

            using var hoverPen = new Pen(UiTheme.TextMuted) { DashStyle = DashStyle.Dash };
            graphics.DrawLine(hoverPen, x, chartBounds.Top, x, chartBounds.Bottom);
            graphics.DrawLine(hoverPen, x, chartBounds.Bottom, x, chartBounds.Bottom + 18);

            var timeText = $"{start:hh\\:mm}-{end:hh\\:mm}";
            TextRenderer.DrawText(graphics, timeText, font, new Point(x - 28, chartBounds.Bottom + 20), UiTheme.TextSecondary);

            var lines = new[]
            {
                timeText,
                GetText(TextId.Txt73, bucket.InputTokens),
                GetText(TextId.Txt74, bucket.OutputTokens),
                GetText(TextId.Txt75, bucket.CachedTokens),
                GetText(TextId.Txt76, FormatCurrency(bucket.Cost))
            };

            var lineHeight = TextRenderer.MeasureText("Ag", font).Height + 2;
            var tooltipWidth = lines.Max(line => TextRenderer.MeasureText(line, font).Width) + 18;
            var tooltipHeight = lineHeight * lines.Length + 12;
            var tooltipX = Math.Min(Math.Max(x + 10, bounds.Left + 4), bounds.Right - tooltipWidth - 4);
            var tooltipY = Math.Min(Math.Max(mouse.Y + 10, bounds.Top + 4), bounds.Bottom - tooltipHeight - 4);
            var tooltipBounds = new Rectangle(tooltipX, tooltipY, tooltipWidth, tooltipHeight);

            using var backgroundBrush = new SolidBrush(Color.FromArgb(238, UiTheme.Surface));
            using var borderPen = new Pen(UiTheme.Border);
            graphics.FillRectangle(backgroundBrush, tooltipBounds);
            graphics.DrawRectangle(borderPen, tooltipBounds);

            for (var index = 0; index < lines.Length; index++)
            {
                var color = index switch
                {
                    1 => UiTheme.SeriesInput,
                    2 => UiTheme.SeriesOutput,
                    3 => UiTheme.SeriesCache,
                    4 => UiTheme.SeriesCost,
                    _ => UiTheme.TextSecondary
                };
                TextRenderer.DrawText(graphics, lines[index], font, new Point(tooltipBounds.Left + 8, tooltipBounds.Top + 6 + index * lineHeight), color);
            }
        }

        private static int? GetDailyChartBucketIndex(Point location, Rectangle bounds)
        {
            if (bounds.Width <= 40 || bounds.Height <= 40)
            {
                return null;
            }

            var chartBounds = new Rectangle(bounds.Left + 70, bounds.Top + 16, bounds.Width - 140, bounds.Height - 48);
            if (chartBounds.Width <= 0 || chartBounds.Height <= 0
                || location.X < chartBounds.Left
                || location.X > chartBounds.Right
                || location.Y < chartBounds.Top
                || location.Y > chartBounds.Bottom)
            {
                return null;
            }

            return Math.Clamp((int)Math.Round((location.X - chartBounds.Left) * 47.0 / chartBounds.Width), 0, 47);
        }

        private static long RoundTokenScaleMax(long maxValue)
        {
            if (maxValue <= 0)
            {
                return 4;
            }

            var roughStep = (long)Math.Ceiling(maxValue / 4.0);
            var magnitude = (long)Math.Pow(10, Math.Max(0, roughStep.ToString(CultureInfo.InvariantCulture).Length - 1));
            var step = (long)Math.Ceiling(roughStep / (double)magnitude) * magnitude;
            return step * 4;
        }

        private static decimal RoundCostScaleMax(decimal maxValue)
        {
            if (maxValue <= 0)
            {
                return 40m;
            }

            return Math.Ceiling(maxValue / 10m) * 10m;
        }

        private static string FormatTokenTick(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatCostTick(decimal value)
        {
            return "$" + value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void DrawSeries(Graphics graphics, Rectangle chartBounds, double[] values, double scaleMax, Color color)
        {
            if (values.Length == 0)
            {
                return;
            }

            if (scaleMax <= 0 || values.All(value => value <= 0))
            {
                return;
            }

            var points = values.Select((value, index) => new PointF(
                chartBounds.Left + (float)(index * chartBounds.Width / 47.0),
                chartBounds.Bottom - (float)(Math.Min(value, scaleMax) / scaleMax * chartBounds.Height))).ToArray();

            // Faint area fill beneath each line for depth.
            if (points.Length >= 2)
            {
                using var areaPath = new GraphicsPath();
                areaPath.AddLines(points);
                areaPath.AddLine(points[^1].X, chartBounds.Bottom, points[0].X, chartBounds.Bottom);
                areaPath.CloseFigure();
                using var areaBrush = new SolidBrush(Color.FromArgb(28, color));
                graphics.FillPath(areaBrush, areaPath);
            }

            using var pen = new Pen(color, 2.25F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLines(pen, points);
        }
    }
}

