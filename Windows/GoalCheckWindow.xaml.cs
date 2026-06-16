using System.Windows;
using System.Windows.Media;

namespace TimeTracker
{
    public partial class GoalCheckWindow : Window
    {
        private readonly DatabaseManager _db;
        private readonly int _goalId, _phaseId;
        private readonly string _goalTitle, _phaseTitle;

        public GoalCheckWindow(DatabaseManager db, int goalId, int phaseId, string goalTitle, string phaseTitle)
        {
            InitializeComponent();
            _db = db; _goalId = goalId; _phaseId = phaseId;
            _goalTitle = goalTitle; _phaseTitle = phaseTitle;
            lblGoalInfo.Text = $"目标: {goalTitle} → 阶段: {phaseTitle}";
            btnAiEval.IsEnabled = AIService.IsConfigured;
            LoadTrackingData();
        }

        private void LoadTrackingData()
        {
            // 获取最近30天该目标相关时段的追踪数据
            var week = _db.GetTimeRecords(DateTime.Now.AddDays(-30), DateTime.Now);
            var totalMs = week.Sum(r => r.UsageTime);
            var ts = TimeSpan.FromMilliseconds(totalMs);

            // 简单估算：工作相关应用（带分类标签）的总时长
            var categorized = week.Where(r => r.CategoryId.HasValue).Sum(r => r.UsageTime);
            var catTs = TimeSpan.FromMilliseconds(categorized);

            var autoRatio = totalMs > 0 ? Math.Round((double)categorized / totalMs, 2) : 0;
            lblTrackingData.Text = $"近30天总追踪 {ts.TotalHours:F1}h | 已分类相关应用 {catTs.TotalHours:F1}h | 系统估算有效比 ≈ {autoRatio:P0}";

            // 自动填充有效比
            if (double.TryParse(txtRatio.Text, out _)) { }
            txtRatio.Text = autoRatio.ToString("F2");
        }

        private async void BtnAiEval_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtMinutes.Text, out var actualMin)) return;
            if (!double.TryParse(txtRatio.Text, out var ratio)) return;

            btnAiEval.IsEnabled = false;
            btnAiEval.Content = "评价中...";
            var result = await AIService.EvaluatePhaseAsync(_goalTitle, _phaseTitle, 120, actualMin, ratio);
            if (!string.IsNullOrEmpty(result))
            {
                borderAiResult.Visibility = Visibility.Visible;
                lblAiResult.Text = "🤖 " + result;
            }
            btnAiEval.IsEnabled = true;
            btnAiEval.Content = "🤖 AI 评价";
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtMinutes.Text, out var actualMin)) return;
            if (!double.TryParse(txtRatio.Text, out var ratio)) return;
            var notes = txtNotes.Text.Trim();

            _db.CompletePhase(_phaseId, actualMin, ratio, notes);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
