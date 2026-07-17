using System.Runtime.InteropServices;

namespace APIRelay
{
    public partial class Form1
    {
        // ── Dark title bar (ImmersiveDarkMode) via DWM ───────────────────────
        // WinForms draws the non-client area using the OS; on Windows 10/11 the only way to get
        // a dark title bar is to ask DWM for the immersive dark attribute. Must be (re)applied
        // whenever the HWND is created, so it lives in OnHandleCreated.

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int cbAttribute);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;       // Win10 2004+
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19; // older builds

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnableDarkTitleBar(Handle);
        }

        internal static void EnableDarkTitleBar(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var on = 1;
            // Try the modern attribute first, fall back to the pre-20H1 id on older Windows 10.
            if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref on, sizeof(int));
            }
        }
    }
}
