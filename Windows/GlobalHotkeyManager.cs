using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TimeTracker
{
    public static class GlobalHotkeyManager
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public enum HotkeyId { ShowToggle = 1, PauseToggle = 2 }

        private static IntPtr _windowHandle;
        private static Action? _showToggle;
        private static Action? _pauseToggle;

        public static void Register(Window window, Action? showToggle = null, Action? pauseToggle = null)
        {
            _showToggle = showToggle;
            _pauseToggle = pauseToggle;
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.EnsureHandle();

            // Ctrl+Shift+T → 显示/隐藏
            RegisterHotKey(_windowHandle, (int)HotkeyId.ShowToggle, MOD_CONTROL | MOD_SHIFT, 0x54);
            // Ctrl+Shift+P → 暂停/恢复
            RegisterHotKey(_windowHandle, (int)HotkeyId.PauseToggle, MOD_CONTROL | MOD_SHIFT, 0x50);

            var source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(WndProc);
        }

        public static void Unregister()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, (int)HotkeyId.ShowToggle);
                UnregisterHotKey(_windowHandle, (int)HotkeyId.PauseToggle);
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                switch ((HotkeyId)wParam.ToInt32())
                {
                    case HotkeyId.ShowToggle:
                        _showToggle?.Invoke();
                        break;
                    case HotkeyId.PauseToggle:
                        _pauseToggle?.Invoke();
                        break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
