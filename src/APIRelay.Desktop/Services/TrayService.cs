using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace APIRelay.Desktop.Services;

internal sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly Window owner;
    private readonly HwndSource source;
    private readonly Action show;
    private readonly Action toggleBubble;
    private readonly Action quit;
    private readonly Func<bool> bubbleVisible;
    private readonly uint taskbarCreated;
    private NotifyIconData data;
    private bool disposed;
    private ContextMenu? menu;

    public TrayService(Window owner, Action show, Action toggleBubble, Action quit, Func<bool> bubbleVisible)
    {
        this.owner = owner;
        this.show = show;
        this.toggleBubble = toggleBubble;
        this.quit = quit;
        this.bubbleVisible = bubbleVisible;
        var hwnd = new WindowInteropHelper(owner).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(WindowProc);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        data = new()
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = hwnd,
            Id = 1,
            Flags = 1 | 2 | 4,
            Callback = CallbackMessage,
            Icon = ExtractIcon(IntPtr.Zero, Environment.ProcessPath!, 0),
            Tip = "APIRelay",
            Info = "",
            InfoTitle = ""
        };
        AddIcon();
    }
    public void Refresh(bool running)
    {
        data.Tip = "APIRelay · " + TextCatalog.Current[running ? "Running" : "Stopped"];
        ShellNotifyIcon(1, ref data);
    }
    private void AddIcon()
    {
        if (!ShellNotifyIcon(0, ref data))
            throw new InvalidOperationException("The notification area is unavailable.");
    }
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated) { ShellNotifyIcon(0, ref data); return IntPtr.Zero; }
        if (message != CallbackMessage) return IntPtr.Zero;
        switch ((int)lParam)
        {
            case 0x203:
            case 0x400:
            case 0x401:
                show();
                break;
            case 0x205:
            case 0x7B:
                SetForegroundWindow(hwnd);
                menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = owner };
                AddMenu(TextCatalog.Current["ShowWindow"], show);
                AddMenu(TextCatalog.Current[bubbleVisible() ? "HideBubble" : "ShowBubble"], toggleBubble);
                menu.Items.Add(new Separator());
                AddMenu(TextCatalog.Current["Quit"], quit);
                menu.IsOpen = true;
                break;
        }
        handled = true;
        return IntPtr.Zero;
    }
    private void AddMenu(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        menu!.Items.Add(item);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (menu != null) menu.IsOpen = false;
        ShellNotifyIcon(2, ref data);
        source.RemoveHook(WindowProc);
        if (data.Icon != IntPtr.Zero) DestroyIcon(data.Icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr instance, string file, int index);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
