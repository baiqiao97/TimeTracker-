using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TimeTracker
{
    /// <summary>
    /// 通知弹窗工具类（从 MainWindow 中提取）
    /// </summary>
    public static class NotificationHelper
    {
        public static async void Show(string title, string message, bool isSuccess = true)
        {
            try
            {
                var accentGreen = new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81));
                var accentRed = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
                var accent = isSuccess ? accentGreen : accentRed;
                var iconText = isSuccess ? "\u2713" : "\u2715";
                var textDark = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e));
                var textGray = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80));

                var icon = new TextBlock
                {
                    Text = iconText,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var iconCircle = new Border
                {
                    Width = 48, Height = 48,
                    CornerRadius = new CornerRadius(24),
                    Background = accent,
                    Child = icon,
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var titleBlock = new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = textDark,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                var msgBlock = new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    Foreground = textGray,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 280,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                // 底部渐变条
                var progressBar = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(0, 0, 12, 12),
                    Background = accentGreen,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0
                };

                var contentPanel = new StackPanel { Margin = new Thickness(28, 24, 28, 0) };
                contentPanel.Children.Add(iconCircle);
                contentPanel.Children.Add(titleBlock);
                contentPanel.Children.Add(msgBlock);
                contentPanel.Children.Add(progressBar);

                var cardBorder = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Background = Brushes.White,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 20,
                        ShadowDepth = 2,
                        Opacity = 0.15,
                        Color = Colors.Black
                    },
                    Child = contentPanel
                };

                // 入场动画准备
                var scaleT = new ScaleTransform(0.8, 0.8);
                cardBorder.RenderTransform = scaleT;
                cardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                cardBorder.Opacity = 0;

                var popup = new Window
                {
                    Width = 340,
                    Height = 210,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Content = cardBorder,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                // 入场动画
                popup.Loaded += (_, _) =>
                {
                    var sb = new Storyboard();
                    var fi = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    Storyboard.SetTarget(fi, cardBorder);
                    Storyboard.SetTargetProperty(fi, new PropertyPath(UIElement.OpacityProperty));
                    sb.Children.Add(fi);
                    var sx = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(350))
                        { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
                    Storyboard.SetTarget(sx, cardBorder);
                    Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                    sb.Children.Add(sx);
                    var sy = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(350))
                        { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
                    Storyboard.SetTarget(sy, cardBorder);
                    Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                    sb.Children.Add(sy);

                    // 绿色进度条展开
                    var barAnim = new DoubleAnimation(0, cardBorder.ActualWidth, TimeSpan.FromMilliseconds(1500))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                    Storyboard.SetTarget(barAnim, progressBar);
                    Storyboard.SetTargetProperty(barAnim, new PropertyPath(FrameworkElement.WidthProperty));
                    sb.Children.Add(barAnim);

                    sb.Begin();
                };

                popup.Show();

                // 2 秒后自动消失
                await Task.Delay(2000);
                var collapse = new Storyboard();
                var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                Storyboard.SetTarget(fo, cardBorder);
                Storyboard.SetTargetProperty(fo, new PropertyPath(UIElement.OpacityProperty));
                collapse.Children.Add(fo);
                collapse.Begin();
                await Task.Delay(220);
                popup.Close();
            }
            catch (Exception ex) { Logger.Error("Notification failed", ex); }
        }
    }
}
