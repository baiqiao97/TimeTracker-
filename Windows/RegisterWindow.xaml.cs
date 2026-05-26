using System.Windows;
using System.Windows.Media;

namespace TimeTracker
{
    public partial class RegisterWindow : Window
    {
        private int _minLength;

        public RegisterWindow()
        {
            InitializeComponent();

            // 从配置读取最小长度
            _minLength = AppSettings.MinPasswordLength;
            if (_minLength < 1) _minLength = 6;

            runMinHint.Text = $"用户名和密码至少 {_minLength} 位";
        }

        /// <summary>
        /// 外部可修改的最小长度（用于设置页面传递配置值）
        /// </summary>
        public int MinLength
        {
            get => _minLength;
            set
            {
                _minLength = Math.Max(1, value);
                runMinHint.Text = $"用户名和密码至少 {_minLength} 位";
                ValidateInputs();
            }
        }

        private void ValidateInputs()
        {
            var user = txtUsername.Text.Trim();
            var pass = txtPassword.Text.Trim();

            lblUsernameHint.Text = user.Length < _minLength ? $"至少{_minLength}位" : "✓";
            lblUsernameHint.Foreground = user.Length < _minLength
                ? new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81));

            lblPasswordHint.Text = pass.Length < _minLength ? $"至少{_minLength}位" : "✓";
            lblPasswordHint.Foreground = pass.Length < _minLength
                ? new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81));

            btnRegister.IsEnabled = user.Length >= _minLength && pass.Length >= _minLength;
            lblError.Text = "";
        }

        private void TxtUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ValidateInputs();

        private void TxtPassword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ValidateInputs();

        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            var user = txtUsername.Text.Trim();
            var pass = txtPassword.Text.Trim();

            if (user.Length < _minLength || pass.Length < _minLength)
            {
                lblError.Text = $"用户名和密码至少需要 {_minLength} 位字符";
                return;
            }

            btnRegister.IsEnabled = false;
            btnRegister.Content = "注册中...";
            lblError.Text = "";

            (bool ok, string? error) = await ServerSyncClient.RegisterAsync(user, pass);

            if (ok)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                lblError.Text = error ?? "注册失败，请检查服务器连接";
                btnRegister.IsEnabled = true;
                btnRegister.Content = "注册";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
