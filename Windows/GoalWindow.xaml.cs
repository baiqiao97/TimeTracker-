using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeTracker
{
    public partial class GoalWindow : Window
    {
        private readonly DatabaseManager _db;
        private bool _aiConfigured;

        public GoalWindow(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
            _aiConfigured = AIService.IsConfigured;
            borderApiHint.Visibility = _aiConfigured ? Visibility.Collapsed : Visibility.Visible;
            LoadGoals();
        }

        private void LoadGoals()
        {
            lstGoals.Items.Clear();
            var goals = _db.GetGoals();
            foreach (var g in goals)
            {
                var phases = _db.GetGoalPhases(g.Id);
                lstGoals.Items.Add(BuildGoalCard(g, phases));
            }
        }

        private Border BuildGoalCard(GoalData goal, List<GoalPhaseData> phases)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.06 }
            };

            var sp = new StackPanel();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock { Text = "🎯 " + goal.Title, FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e)) };
            Grid.SetColumn(title, 0); header.Children.Add(title);

            if (goal.Status == "active")
            {
                var btnComplete = new Button { Content = "完成目标", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81)),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Tag = goal.Id };
                btnComplete.Click += (_, _) => { _db.UpdateGoalStatus(goal.Id, "completed"); LoadGoals(); };
                Grid.SetColumn(btnComplete, 1); header.Children.Add(btnComplete);
            }
            sp.Children.Add(header);

            // 总体进度条
            var totalPlan = phases.Sum(p => p.EstimatedMinutes);
            var totalActual = phases.Sum(p => p.ActualMinutes);
            if (totalPlan > 0)
            {
                var ratio = Math.Min(1.0, (double)totalActual / totalPlan);
                var barBg = new Border { Height = 8, Background = new SolidColorBrush(Color.FromRgb(0xf3, 0xf4, 0xf6)),
                    CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 8) };
                var bar = new Border { Height = 8, Background = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)),
                    CornerRadius = new CornerRadius(4), Width = Double.NaN, HorizontalAlignment = HorizontalAlignment.Left };
                bar.Width = Math.Max(8, ratio * 600);
                barBg.Child = bar;
                sp.Children.Add(barBg);

                var stats = new TextBlock { Text = $"总进度 {ratio:P0}  |  累计有效 {totalActual}/{totalPlan} 分钟",
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)), Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(stats);
            }

            // 阶段列表
            foreach (var p in phases)
            {
                var row = new Border { Background = new SolidColorBrush(Color.FromRgb(0xfa, 0xfb, 0xfc)),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 4) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = $"阶段 {p.PhaseOrder}: {p.Title}", FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e)) });
                info.Children.Add(new TextBlock { Text = p.Description, FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)) });

                var stats2 = new StackPanel { Orientation = Orientation.Horizontal };
                double phaseRatio = p.EstimatedMinutes > 0 ? (double)p.ActualMinutes / p.EstimatedMinutes : 0;
                var badge = new Border { Background = p.Status == "completed"
                    ? new SolidColorBrush(Color.FromRgb(0xec, 0xfd, 0xf5)) : new SolidColorBrush(Color.FromRgb(0xf3, 0xf4, 0xf6)),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
                badge.Child = new TextBlock { Text = p.Status == "completed" ? $"✅ {p.ActualMinutes}/{p.EstimatedMinutes}min" : $"⏳ {p.EstimatedMinutes}min",
                    FontSize = 11, Foreground = p.Status == "completed" ? new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81)) : new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)) };
                stats2.Children.Add(badge);

                if (p.EffectiveRatio > 0)
                {
                    var erBadge = new Border { Background = new SolidColorBrush(Color.FromRgb(0xf0, 0xed, 0xff)),
                        CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2) };
                    erBadge.Child = new TextBlock { Text = $"有效比: {p.EffectiveRatio:P0}", FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)) };
                    stats2.Children.Add(erBadge);
                }
                info.Children.Add(stats2);
                Grid.SetColumn(info, 0); grid.Children.Add(info);

                if (p.Status != "completed")
                {
                    var btnCheck = new Button { Content = "📝 检测", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                        Tag = new Tuple<int, int, string, string>(goal.Id, p.Id, goal.Title, p.Title) };
                    btnCheck.Click += BtnCheckPhase_Click;
                    Grid.SetColumn(btnCheck, 1); grid.Children.Add(btnCheck);
                }
                row.Child = grid;
                sp.Children.Add(row);
            }

            // 有效率总结
            if (phases.Any(p => p.EffectiveRatio > 0))
            {
                var avgEff = phases.Where(p => p.EffectiveRatio > 0).Average(p => p.EffectiveRatio);
                var tip = avgEff < 0.3 ? "建议减少无关应用使用，专注目标" : avgEff < 0.5 ? "接近良好水平，继续加油" : "优秀！保持高效工作习惯";
                sp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xf0, 0xfd, 0xf4)),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 6, 0, 0),
                    Child = new TextBlock { Text = $"📈 有效工作比: {avgEff:P0} — {tip}", FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81)), TextWrapping = TextWrapping.Wrap }
                });
            }

            card.Child = sp;
            return card;
        }

        private void BtnCheckPhase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Tuple<int, int, string, string> tag)
            {
                var check = new GoalCheckWindow(_db, tag.Item1, tag.Item2, tag.Item3, tag.Item4);
                check.Owner = this;
                if (check.ShowDialog() == true) LoadGoals();
            }
        }

        private async void BtnAiDecompose_Click(object sender, RoutedEventArgs e)
        {
            var goal = txtGoal.Text.Trim();
            if (goal.Length < 3) { MessageBox.Show("请输入具体的目标描述", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!AIService.IsConfigured) { MessageBox.Show("请先在设置中配置 AI API Key", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            btnAiDecompose.IsEnabled = false;
            btnAiDecompose.Content = "拆解中...";
            try
            {
                var results = await AIService.DecomposeGoalAsync(goal);
                if (results == null || results.Length == 0) { MessageBox.Show("AI 拆解失败，请检查 API Key"); return; }

                var goalId = _db.InsertGoal(goal);
                for (int i = 0; i < results.Length; i++)
                {
                    var parts = results[i].Split('|');
                    var pTitle = parts.Length > 0 ? parts[0].Trim() : $"阶段{i + 1}";
                    var pDesc = parts.Length > 1 ? parts[1].Trim() : "";
                    var pMin = parts.Length > 2 && int.TryParse(parts[2].Trim(), out var m) ? m : 120;
                    _db.InsertGoalPhase(goalId, pTitle, pDesc, i + 1, pMin);
                }
                txtGoal.Text = "";
                LoadGoals();
            }
            catch (Exception ex) { MessageBox.Show($"AI 调用失败: {ex.Message}"); }
            finally { btnAiDecompose.IsEnabled = true; btnAiDecompose.Content = "🤖 AI 拆解"; }
        }

        private void BtnAddGoal_Click(object sender, RoutedEventArgs e)
        {
            var goal = txtGoal.Text.Trim();
            if (goal.Length < 3) { MessageBox.Show("请输入具体的目标描述"); return; }
            _db.InsertGoal(goal);
            txtGoal.Text = "";
            LoadGoals();
        }

        private void BtnConfigAi_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsWindow.LoadSettings();
            settings.Owner = this;
            settings.ShowDialog();
            _aiConfigured = AIService.IsConfigured;
            borderApiHint.Visibility = _aiConfigured ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
