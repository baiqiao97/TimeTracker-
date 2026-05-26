using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace TimeTracker
{
    public partial class CategoryManageWindow : Window
    {
        private readonly DatabaseManager _db;
        private readonly TrackingService? _tracking;
        private int _editingId = -1;
        private List<CategoryData> _allCategories = [];

        public CategoryManageWindow(DatabaseManager db, TrackingService? tracking = null)
        {
            InitializeComponent();
            _db = db;
            _tracking = tracking;
            LoadCategories();
            LoadProcessMappings();

            // 颜色输入框实时预览
            txtColor.TextChanged += (_, _) =>
            {
                try
                {
                    var c = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(txtColor.Text)!;
                    if (FindName("colorPreview") is System.Windows.Controls.Border preview)
                        preview.Background = c;
                }
                catch { }
            };
        }

        // ======================== 标签 CRUD ========================

        private void LoadCategories()
        {
            var cats = _db.GetCategories();
            var items = cats.Select(c => new CategoryItem
            {
                Id = c.Id,
                Name = c.Name ?? "",
                Color = c.Color ?? "#3498db",
                Description = c.Description ?? ""
            }).ToList();
            lstCategories.ItemsSource = items;
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string color)
                txtColor.Text = color;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入标签名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string color = txtColor.Text.Trim();
            if (string.IsNullOrEmpty(color)) color = "#3498db";
            string desc = txtDesc.Text.Trim();

            if (_editingId >= 0)
            {
                _db.UpdateCategory(_editingId, name, color, desc);
            }
            else
            {
                _db.AddCategory(name, color, desc);
            }

            ResetForm();
            LoadCategories();
            LoadProcessMappings(); // 刷新标签下拉框
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => ResetForm();

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var cats = _db.GetCategories();
                var cat = cats.FirstOrDefault(c => c.Id == id);
                if (cat != null)
                {
                    _editingId = cat.Id;
                    txtName.Text = cat.Name;
                    txtColor.Text = cat.Color;
                    txtDesc.Text = cat.Description;
                    lblFormTitle.Text = "编辑标签";
                    btnSave.Content = "保存修改";
                    btnCancel.Visibility = Visibility.Visible;
                }
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                if (MessageBox.Show("确定要删除此标签吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _db.DeleteCategory(id);
                    LoadCategories();
                    LoadProcessMappings();
                }
            }
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 列表选中不自动填充表单（用户必须点"编辑"按钮）
        }

        private void ResetForm()
        {
            _editingId = -1;
            txtName.Text = "";
            txtColor.Text = "";
            txtDesc.Text = "";
            lblFormTitle.Text = "添加新标签";
            btnSave.Content = "添加标签";
            btnCancel.Visibility = Visibility.Collapsed;
            lstCategories.SelectedItem = null;
        }

        // ======================== 应用标签分配 ========================

        private void LoadProcessMappings()
        {
            _allCategories = _db.GetCategories();

            // 添加"未分类"选项
            _allCategories.Insert(0, new CategoryData { Id = -1, Name = "未分类", Color = "#888888", Description = "" });

            var processes = _db.GetTopProcesses(DateTime.Now.AddDays(-30), DateTime.Now);

            // 一次查询获取所有进程的分类映射，避免 N+1
            var allRecords = _db.GetTimeRecords(DateTime.Now.AddDays(-30), DateTime.Now);
            var categoryMap = allRecords
                .GroupBy(r => r.ProcessName)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).First().CategoryId);

            var items = new ObservableCollection<ProcessMapItem>();
            foreach (var p in processes)
            {
                int? existingCatId = categoryMap.TryGetValue(p.ProcessName, out var cid) ? cid : null;

                items.Add(new ProcessMapItem
                {
                    ProcessName = p.ProcessName,
                    DisplayName = ProcessNameHelper.GetDisplayName(p.ProcessName),
                    CategoryId = existingCatId ?? -1,
                    CategoryName = existingCatId.HasValue
                        ? _allCategories.FirstOrDefault(c => c.Id == existingCatId)?.Name ?? "未分类"
                        : "未分类",
                    UsageDisplay = FormatMs(p.TotalUsage),
                    AvailableCategories = _allCategories
                });
            }

            lstProcessMap.ItemsSource = items;
        }

        private void ProcessCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.DataContext is ProcessMapItem item)
            {
                if (cb.SelectedValue is int catId && catId > 0)
                {
                    item.CategoryName = _allCategories.FirstOrDefault(c => c.Id == catId)?.Name ?? "未分类";

                    // 1. 持久化到追踪服务（新记录自动分类）
                    _tracking?.AddProcessCategoryMapping(item.ProcessName, catId);

                    // 2. 回写数据库已有记录
                    int updated = _db.UpdateProcessCategory(item.ProcessName, catId);

                    MainWindow.ShowSuccessNotification("标签已分配",
                        $"已更新 {updated} 条记录\n\"{item.DisplayName}\" → {item.CategoryName}");
                }
            }
        }

        private static string FormatMs(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    // ======================== 数据类 ========================

    public class ProcessMapItem : INotifyPropertyChanged
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string UsageDisplay { get; set; } = string.Empty;

        private int? _categoryId;
        public int? CategoryId
        {
            get => _categoryId;
            set { _categoryId = value; OnPropertyChanged(); }
        }

        private string _categoryName = "未分类";
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value; OnPropertyChanged(); }
        }

        public List<CategoryData>? AvailableCategories { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
