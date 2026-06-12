using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace TimeTracker
{
    public partial class SettingsWindow : Window
    {
        public int TrackingIntervalSeconds { get; private set; } = 2;
        public int RetentionDays { get; private set; } = 90;
        public bool AutoStart { get; private set; } = false;
        public string TrackingMode { get; private set; } = "simple";
        public string ServerUrl { get; private set; } = "";
        public bool AutoSync { get; private set; } = false;
        public bool HostServer { get; private set; } = false;
        public int ServerPort { get; private set; } = 5080;
        public int MinPasswordLength { get; private set; } = 6;

        public event Action? CleanOldDataRequested;

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "settings.json");
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        public static SettingsWindow LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    int interval = root.TryGetProperty("interval", out var i) ? i.GetInt32() : 2;
                    int retention = root.TryGetProperty("retention", out var r) ? r.GetInt32() : 90;
                    bool autoStart = root.TryGetProperty("autoStart", out var a) && a.GetBoolean();
                    string mode = root.TryGetProperty("trackingMode", out var m) ? (m.GetString() ?? "simple") : "simple";
                    string surl = root.TryGetProperty("serverUrl", out var su) ? (su.GetString() ?? "") : "";
                    bool asy = root.TryGetProperty("autoSync", out var a2) && a2.GetBoolean();
                    bool hs = root.TryGetProperty("hostServer", out var hh) && hh.GetBoolean();
                    int sp = root.TryGetProperty("serverPort", out var spp) ? spp.GetInt32() : 5080;
                    int mpl = root.TryGetProperty("minPasswordLength", out var ml) ? Math.Max(1, ml.GetInt32()) : 6;
                    return new SettingsWindow(interval, retention, autoStart, mode, surl, asy, hs, sp, mpl);
                }
            }
            catch { }
            return new SettingsWindow();
        }

        public SettingsWindow() { InitializeComponent(); LoadDefaults(); }

        public SettingsWindow(int interval, int retention, bool autoStart, string mode = "simple",
            string serverUrl = "", bool autoSync = false, bool hostServer = false, int serverPort = 5080,
            int minPasswordLength = 6) : this()
        {
            TrackingIntervalSeconds = interval; RetentionDays = retention;
            AutoStart = autoStart; TrackingMode = mode;
            ServerUrl = serverUrl; AutoSync = autoSync;
            HostServer = hostServer; ServerPort = serverPort;
            MinPasswordLength = minPasswordLength;
            txtInterval.Text = interval.ToString(CultureInfo.InvariantCulture);
            txtRetentionDays.Text = retention.ToString(CultureInfo.InvariantCulture);
            chkAutoStart.IsChecked = autoStart;
            if (mode == "activity") rbActivity.IsChecked = true; else rbSimple.IsChecked = true;
            txtServerUrl.Text = serverUrl;
            chkAutoSync.IsChecked = autoSync;
            chkHostServer.IsChecked = hostServer;
            txtMinPwdLen.Text = minPasswordLength.ToString(CultureInfo.InvariantCulture);
        }

        private void LoadDefaults()
        {
            txtInterval.Text = TrackingIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            txtRetentionDays.Text = RetentionDays.ToString(CultureInfo.InvariantCulture);
            chkAutoStart.IsChecked = AutoStart;
            chkDarkMode.IsChecked = AppSettings.DarkMode;
            rbSimple.IsChecked = true;
            txtServerUrl.Text = AppSettings.ServerUrl;
            chkAutoSync.IsChecked = AppSettings.AutoSync;
            chkHostServer.IsChecked = AppSettings.HostServer;
            txtServerPort.Text = AppSettings.ServerPort.ToString(CultureInfo.InvariantCulture);
            txtMinPwdLen.Text = AppSettings.MinPasswordLength.ToString(CultureInfo.InvariantCulture);
            RefreshAccountStatus();
        }

        private void RefreshAccountStatus()
        {
            if (!string.IsNullOrEmpty(AppSettings.AuthToken))
            {
                lblAccountStatus.Text = "✅ 已登录云同步";
                lblAccountStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x10, 0xb9, 0x81));
            }
            else
            {
                lblAccountStatus.Text = "⚠ 未登录，注册或登录后启用";
                lblAccountStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xf5, 0x9e, 0x0b));
            }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            var regWin = new RegisterWindow { Owner = this };
            // 从当前设置或 AppSettings 获取最小长度
            int minLen = int.TryParse(txtMinPwdLen.Text, out int ml) ? Math.Max(1, ml) : AppSettings.MinPasswordLength;
            regWin.MinLength = minLen;

            // 预填用户名密码
            var regUserField = regWin.FindName("txtUsername") as System.Windows.Controls.TextBox;
            var regPassField = regWin.FindName("txtPassword") as System.Windows.Controls.TextBox;
            if (regUserField != null) regUserField.Text = txtUsername.Text.Trim();
            if (regPassField != null) regPassField.Text = txtPassword.Text.Trim();

            if (regWin.ShowDialog() == true)
            {
                RefreshAccountStatus();
                MessageBox.Show("注册成功！已自动登录", "云同步", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var user = txtUsername.Text.Trim();
            var pass = txtPassword.Text.Trim();
            int minLen = int.TryParse(txtMinPwdLen.Text, out int ml) ? Math.Max(1, ml) : 6;
            if (user.Length < minLen || pass.Length < minLen)
            {
                MessageBox.Show($"用户名和密码至少需要 {minLen} 位", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var (ok, error) = await ServerSyncClient.LoginAsync(user, pass);
            if (ok) { RefreshAccountStatus(); MessageBox.Show("登录成功！", "云同步", MessageBoxButton.OK, MessageBoxImage.Information); }
            else MessageBox.Show(error ?? "登录失败", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.AuthToken = "";
            AppSettings.Save();
            RefreshAccountStatus();
            MessageBox.Show("已注销", "云同步", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtInterval.Text, out int interval) && interval >= 1) TrackingIntervalSeconds = interval;
            if (int.TryParse(txtRetentionDays.Text, out int days) && days >= 1) RetentionDays = days;
            AutoStart = chkAutoStart.IsChecked ?? false;
            TrackingMode = rbActivity.IsChecked == true ? "activity" : "simple";
            ServerUrl = txtServerUrl.Text.Trim();
            AutoSync = chkAutoSync.IsChecked ?? false;
            HostServer = chkHostServer.IsChecked ?? false;
            if (int.TryParse(txtServerPort.Text, out int port) && port >= 1024) ServerPort = port;
            if (int.TryParse(txtMinPwdLen.Text, out int mpl) && mpl >= 1) MinPasswordLength = mpl;

            // 同步到全局 AppSettings
            AutoStart = chkAutoStart.IsChecked ?? false;
            AppSettings.DarkMode = chkDarkMode.IsChecked ?? false;
            AppSettings.MinPasswordLength = MinPasswordLength;

            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(new
                {
                    interval = TrackingIntervalSeconds,
                    retention = RetentionDays,
                    autoStart = AutoStart,
                    trackingMode = TrackingMode,
                    serverUrl = ServerUrl,
                    autoSync = AutoSync,
                    hostServer = HostServer,
                    serverPort = ServerPort,
                    minPasswordLength = MinPasswordLength,
                    currentActivityId = AppSettings.CurrentActivityId,
                    authToken = AppSettings.AuthToken
                }, _jsonOpts));
            }
            catch (Exception ex) { Logger.Error("Failed to save settings", ex); }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ChkDarkMode_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.DarkMode = chkDarkMode.IsChecked ?? false;
            ThemeHelper.Apply(AppSettings.DarkMode);
            AppSettings.Save();
        }

        private void BtnCleanOld_Click(object sender, RoutedEventArgs e)
        {
            CleanOldDataRequested?.Invoke();
            MessageBox.Show("旧数据已清理", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
