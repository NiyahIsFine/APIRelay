using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APIRelay
{
    public partial class Form1
    {
        private void ApplyApplicationIcon()
        {
            try
            {
                using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("APIRelay.app.ico");
                if (iconStream != null)
                {
                    Icon = new Icon(iconStream);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException)
            {
                AppendInternalException("Failed to apply application icon.", ex);
            }
        }

        private void InitializeTrayIcon()
        {
            toggleBubbleMenuItem = new ToolStripMenuItem(GetText(TextId.Txt52), null, (_, _) => ToggleUsageBubble());
            var showWindowMenuItem = new ToolStripMenuItem(GetText(TextId.Txt53), null, (_, _) => ShowMainWindow());
            var exitMenuItem = new ToolStripMenuItem(GetText(TextId.Txt54), null, (_, _) => ExitFromTray());
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(showWindowMenuItem);
            contextMenu.Items.Add(toggleBubbleMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            trayIcon = new NotifyIcon
            {
                ContextMenuStrip = contextMenu,
                Icon = Icon,
                Text = GetText(TextId.Txt55),
                Visible = true
            };

            trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        }

        private void InitializeUsageBubble()
        {
            usageBubble = new FloatingUsageBubble(currentLanguage);
            usageBubble.BubbleDoubleClicked += UsageBubble_BubbleDoubleClicked;
            RestoreUsageBubbleLocation(usageBubble);
            UpdateUsageBubble();
            usageBubble.Show();
            usageBubble.KeepAboveNormalWindows();
        }

        private void StartRelayOnLaunchIfEnabled()
        {
            if (autoStartRelayQueued || !autoStartRelayCheckBox.Checked)
            {
                return;
            }

            autoStartRelayQueued = true;
            TryBeginInvoke(async () => await StartRelayAsync());
        }

        private void HideMainWindow()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowMainWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            ScrollRequestGridToTop();
            Activate();
            usageBubble?.KeepAboveNormalWindows();
        }

        private void ToggleUsageBubble()
        {
            usageBubbleVisible = !usageBubbleVisible;

            if (usageBubbleVisible)
            {
                if (usageBubble == null || usageBubble.IsDisposed)
                {
                    usageBubble = new FloatingUsageBubble(currentLanguage);
                    usageBubble.BubbleDoubleClicked += UsageBubble_BubbleDoubleClicked;
                    RestoreUsageBubbleLocation(usageBubble);
                }

                UpdateUsageBubble();
                usageBubble.Show();
                usageBubble.KeepAboveNormalWindows();
            }
            else
            {
                CaptureUsageBubbleLocation();
                usageBubble?.Hide();
            }

            UpdateTrayMenuText();
        }

        private void UpdateTrayMenuText()
        {
            if (toggleBubbleMenuItem != null)
            {
                toggleBubbleMenuItem.Text = usageBubbleVisible ? GetText(TextId.Txt52) : GetText(TextId.Txt56);
            }

            if (trayIcon != null)
            {
                trayIcon.Text = GetText(TextId.Txt55);
                if (trayIcon.ContextMenuStrip?.Items.Count >= 4)
                {
                    trayIcon.ContextMenuStrip.Items[0].Text = GetText(TextId.Txt53);
                    trayIcon.ContextMenuStrip.Items[3].Text = GetText(TextId.Txt54);
                }
            }

            usageBubble?.KeepAboveNormalWindows();
        }

        private void ExitFromTray()
        {
            allowExit = true;
            Close();
        }

        private void UsageBubble_BubbleDoubleClicked(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void RestoreUsageBubbleLocation(FloatingUsageBubble bubble)
        {
            if (savedUsageBubbleLocation == null)
            {
                return;
            }

            var location = savedUsageBubbleLocation.Value;
            var bounds = new Rectangle(location, bubble.Size);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
            {
                bubble.SetStartupLocation(location);
            }
        }

        private void CaptureUsageBubbleLocation()
        {
            if (usageBubble is { IsDisposed: false })
            {
                savedUsageBubbleLocation = usageBubble.Location;
            }
        }
    }
}

