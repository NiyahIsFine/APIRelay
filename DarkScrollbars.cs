using System.Runtime.InteropServices;

namespace APIRelay
{
    /// <summary>
    /// Forces the classic Win32 scrollbars hosted inside DataGridView/TextBox/ListBox/etc. to
    /// render dark so they stop dropping to a bright system surface on the dark theme.
    ///
    /// Uses the undocumented but stable DarkMode_Explorer window theme + PreferredAppMode that
    /// the OS itself uses for dark File Explorer. Applied per-window after its handle exists.
    /// </summary>
    internal static class DarkScrollbars
    {
        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

        [DllImport("uxtheme.dll", ExactSpelling = true)]
        private static extern int SetPreferredAppMode(int appMode);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr hWnd, GetWindowCmd uCmd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private enum GetWindowCmd : uint { Child = 5 }

        private static bool _preferredAppModeSet;

        /// <summary>Enable dark scrollbars for the given control and its scrollbars.</summary>
        public static void Apply(Control control)
        {
            if (control == null || !control.IsHandleCreated)
            {
                return;
            }

            EnsurePreferredAppMode();
            ApplyDarkToWindow(control.Handle);
            foreach (Control child in control.Controls)
            {
                Apply(child);
            }
        }

        /// <summary>Apply once the control's handle is created (use from HandleCreated).</summary>
        public static void ApplyWhenReady(Control control)
        {
            if (control.IsHandleCreated)
            {
                Apply(control);
            }
            else
            {
                control.HandleCreated += (_, _) => Apply(control);
            }
        }

        private static void EnsurePreferredAppMode()
        {
            if (_preferredAppModeSet)
            {
                return;
            }

            try
            {
                // 1 = allow dark mode app-wide for themed controls.
                SetPreferredAppMode(1);
            }
            catch
            {
                // Older Windows without the export — scrollbars just stay light; not fatal.
            }

            _preferredAppModeSet = true;
        }

        private static void ApplyDarkToWindow(IntPtr handle)
        {
            try
            {
                SetWindowTheme(handle, "DarkMode_Explorer", null);
            }
            catch
            {
            }

            // DataGridView/ListBox embed their scrollbar as a child window — theme that too.
            EnumChildWindows(handle, EnumChild, IntPtr.Zero);
        }

        private static bool EnumChild(IntPtr hWnd, IntPtr _)
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(hWnd, sb, 64);
            var name = sb.ToString();
            if (name == "ScrollBar" || name.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("DataGridView", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    SetWindowTheme(hWnd, "DarkMode_Explorer", null);
                }
                catch
                {
                }
            }

            return true;
        }
    }
}
