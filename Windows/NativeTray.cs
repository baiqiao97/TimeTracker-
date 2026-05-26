using System.Runtime.InteropServices;
using System.Windows;

namespace TimeTracker
{
    public static class NativeTray
    {
        private static readonly Dictionary<IntPtr, (Action show, Action pause, Action exit)> _actions = new();

        public static void Create(Window owner, string title, Action show, Action pause, Action exit)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(owner);
            helper.EnsureHandle();
            var hwnd = helper.Handle;

            // 获取 exe 自身图标（无需 System.Drawing）
            var iconHandle = ExtractIcon(IntPtr.Zero,
                System.Reflection.Assembly.GetExecutingAssembly().Location, 0);

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
                uCallbackMessage = WM_TRAYICON,
                hIcon = iconHandle != IntPtr.Zero ? iconHandle : LoadIcon(IntPtr.Zero, IDI_APPLICATION),
                szTip = title
            };
            Shell_NotifyIcon(NIM_ADD, ref data);

            _actions[hwnd] = (show, pause, exit);

            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            source?.AddHook(TrayWndProc);
        }

        public static void Remove(IntPtr hwnd)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _actions.Remove(hwnd);
        }

        private static IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_TRAYICON) return IntPtr.Zero;
            if (!_actions.TryGetValue(hwnd, out var acts)) return IntPtr.Zero;

            switch ((uint)lParam)
            {
                case WM_LBUTTONDBLCLK: acts.show(); break;
                case WM_RBUTTONUP:     acts.pause(); break;
                case WM_MBUTTONUP:     acts.exit();  break;
            }
            handled = true;
            return IntPtr.Zero;
        }

        private const uint WM_TRAYICON = 0x8001;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONUP = 0x0209;
        private const uint NIM_ADD = 0;
        private const uint NIM_DELETE = 2;
        private const uint NIF_ICON = 2;
        private const uint NIF_TIP = 4;
        private const uint NIF_MESSAGE = 1;
        private static readonly IntPtr IDI_APPLICATION = new(32512);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string exePath, int iconIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
