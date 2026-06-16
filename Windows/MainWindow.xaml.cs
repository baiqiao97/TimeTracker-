using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TimeTracker
{
    public partial class MainWindow : Window
    {
        private TrackingService? _trackingService;
        private readonly DatabaseManager _databaseManager;
        private string _currentRange = "daily";
        private string _todoFilter = "all";
        private int _selectedPriority = 1;
        private string _selectedColor = "#6c5ce7";
        private DateTime? _selectedDate;

        public MainWindow()
        {
            InitializeComponent();
            _allPanels = new UIElement[] { statsPanel, ((UIElement)actionPanel), contentBorder, todoPanel, schedulePanel };
            AppSettings.Load();
            ThemeHelper.Apply(AppSettings.DarkMode);
            _databaseManager = new DatabaseManager();
            InitializeDefaultCategories();
            RegisterDevice();
            InitializeTracking();
            NavOverview_Click(null!, null!);
            SetupStartupAnimation();
            RefreshActivitySelector();
            try { SetupTrayIcon(); } catch (Exception ex) { Logger.Error("Failed to setup tray icon", ex); }
            try { AppSettings.ApplyAutoStart(); } catch (Exception ex) { Logger.Error("Failed to apply autostart", ex); }
            // 首次运行引导
            var settingsPath = AppSettings.IsPortable
                ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json")
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker", "settings.json");
            if (!File.Exists(settingsPath))
            {
                var onboarding = new OnboardingWindow();
                onboarding.ShowDialog();
            }

            try { GlobalHotkeyManager.Register(this,
                () => { if (Visibility == Visibility.Visible) { Hide(); } else { Show(); WindowState = WindowState.Normal; Activate(); } },
                () => _trackingService?.TogglePause()); } catch { }
            SetupServerAndSync();
        }

        // ======================== STARTUP ANIMATION ========================
        private void SetupStartupAnimation()
        {
            var scale = new ScaleTransform(0.95, 0.95);
            mainBorder.RenderTransform = scale;
            mainBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            mainBorder.Opacity = 0;

            Loaded += (_, _) =>
            {
                var sb = new Storyboard();

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(fadeIn, mainBorder);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(fadeIn);

                var sx = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(sx, mainBorder);
                Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                sb.Children.Add(sx);

                var sy = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(sy, mainBorder);
                Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                sb.Children.Add(sy);

                sb.Begin();
            };
        }

        // ======================== INIT ========================

        private void RegisterDevice()
        {
            string deviceId = Environment.MachineName + "-" + Environment.OSVersion.Version;
            string deviceName = Environment.MachineName;
            _databaseManager.RegisterDevice(deviceId, deviceName, "Windows");
            _databaseManager.DeleteOldRecords(DateTime.Now.AddDays(-AppSettings.RetentionDays));
        }

        private void InitializeDefaultCategories()
        {
            try
            {
                var categories = _databaseManager.GetCategories();
                if (categories.Count == 0)
                {
                    _databaseManager.AddCategory("浏览器", "#3498db", "网页浏览");
                    _databaseManager.AddCategory("社交媒体", "#e4405f", "社交应用");
                    _databaseManager.AddCategory("游戏", "#e74c3c", "游戏娱乐");
                    _databaseManager.AddCategory("办公软件", "#9b59b6", "办公应用");
                    _databaseManager.AddCategory("工具", "#2ecc71", "实用工具");
                    _databaseManager.AddCategory("通讯", "#1abc9c", "聊天通讯");
                    _databaseManager.AddCategory("视频", "#ff5722", "视频播放");
                    _databaseManager.AddCategory("其他", "#f39c12", "其他应用");
                }
            }
            catch (Exception ex) { Logger.Error("InitializeDefaultCategories error", ex); }
        }

        private void InitializeTracking()
        {
            _trackingService = new TrackingService(_databaseManager, AppSettings.TrackingIntervalSeconds);
            _trackingService.StatusUpdated += (status) =>
            {
                Dispatcher.Invoke(() =>
                {
                    lblCurrentApp.Text = status?.Replace("追踪中: ", "", StringComparison.Ordinal) ?? "就绪";
                });
            };
            _trackingService.PauseStateChanged += (paused) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (paused)
                    {
                        btnPause.Content = "▶";
                        lblStatus.Text = "已暂停";
                        lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
                    }
                    else
                    {
                        btnPause.Content = "⏸";
                        lblStatus.Text = "追踪中";
                        lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81));
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xb9, 0x81));
                    }
                });
            };
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _trackingService?.TogglePause();
        }

        // ======================== SIDEBAR NAVIGATION ========================

        private static void HighlightNav(Button active)
        {
            foreach (var child in ((StackPanel)active.Parent).Children)
            {
                if (child is Button btn)
                {
                    btn.Background = (btn == active) ? new SolidColorBrush(Color.FromRgb(0x33, 0x3a, 0x56))
                                                      : new SolidColorBrush(Colors.Transparent);
                    btn.Foreground = (btn == active) ? new SolidColorBrush(Colors.White)
                                                      : new SolidColorBrush(Color.FromRgb(0xa0, 0xae, 0xc0));
                }
            }
        }

        private static string DeviceId => Environment.MachineName + "-" + Environment.OSVersion.Version;

        private readonly UIElement[] _allPanels;
        private bool _isAnimating;

        private async Task AnimatePanelTransition(params UIElement[] showPanels)
        {
            if (_isAnimating) return;
            _isAnimating = true;

            var toHide = _allPanels.Where(p => p.Visibility == Visibility.Visible && !showPanels.Contains(p)).ToList();

            var fadeOutTasks = new List<Task>();
            foreach (var panel in toHide)
            {
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                var tcs = new TaskCompletionSource<bool>();
                var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                anim.Completed += (_, _) => { panel.Visibility = Visibility.Collapsed; tcs.SetResult(true); };
                panel.BeginAnimation(UIElement.OpacityProperty, anim);
                fadeOutTasks.Add(tcs.Task);
            }

            foreach (var panel in _allPanels.Except(showPanels).Except(toHide))
                panel.Visibility = Visibility.Collapsed;

            await Task.WhenAll(fadeOutTasks);

            foreach (var panel in showPanels)
            {
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                panel.Visibility = Visibility.Visible;
                panel.Opacity = 0;
                var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                panel.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            _isAnimating = false;
        }

        private Task ShowMainPanelsAsync() =>
            AnimatePanelTransition(statsPanel, actionPanel, contentBorder);

        private Task SwitchToPanelAsync(Grid panel) =>
            AnimatePanelTransition(panel);

        private async Task FadeRefreshContentAsync(Action loadAction)
        {
            contentBorder.BeginAnimation(UIElement.OpacityProperty, null);
            var tcs = new TaskCompletionSource<bool>();
            var outAnim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(80))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            outAnim.Completed += (_, _) => tcs.SetResult(true);
            contentBorder.BeginAnimation(UIElement.OpacityProperty, outAnim);
            await tcs.Task;

            loadAction();

            contentBorder.BeginAnimation(UIElement.OpacityProperty, null);
            var inAnim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(140))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            contentBorder.BeginAnimation(UIElement.OpacityProperty, inAnim);
        }

        private async void NavOverview_Click(object sender, RoutedEventArgs e)
        {
            _currentRange = "daily";
            SetPage("概览", "今日应用使用总览");
            HighlightNav(btnOverview);
            await ShowMainPanelsAsync();
            LoadStats();
            RefreshChartIfVisible();
        }

        private async void NavDaily_Click(object sender, RoutedEventArgs e)
        {
            _currentRange = "daily";
            SetPage("每日统计", "今日各应用使用详情");
            HighlightNav(btnDaily);
            await ShowMainPanelsAsync();
            LoadStats();
            ShowProcessStats();
            RefreshChartIfVisible();
        }

        private async void NavWeekly_Click(object sender, RoutedEventArgs e)
        {
            _currentRange = "weekly";
            SetPage("每周统计", "近7天各应用使用详情");
            HighlightNav(btnWeekly);
            await ShowMainPanelsAsync();
            LoadStats();
            ShowProcessStats();
            RefreshChartIfVisible();
        }

        private async void NavCategory_Click(object sender, RoutedEventArgs e)
        {
            _currentRange = "weekly";
            SetPage("分类统计", "按标签分类的使用时长");
            HighlightNav(btnCategory);
            await ShowMainPanelsAsync();
            LoadStats();
            ShowCategoryStats();
            RefreshChartIfVisible();
        }

        private async void NavActivity_Click(object sender, RoutedEventArgs e)
        {
            _currentRange = "weekly";
            SetPage("活动统计", "按活动分类查看使用时长");
            HighlightNav(btnActivity);
            await ShowMainPanelsAsync();
            LoadStats();
            ShowActivityStats();
            RefreshChartIfVisible();
        }

        private async void NavTodoList_Click(object sender, RoutedEventArgs e)
        {
            SetPage("待办清单", "管理你的待办事项");
            HighlightNav(btnTodoList);
            await SwitchToPanelAsync(todoPanel);
            LoadTodoItems();
        }

        private async void NavSchedule_Click(object sender, RoutedEventArgs e)
        {
            SetPage("日程安排", "管理你的日程计划");
            HighlightNav(btnSchedule);
            await SwitchToPanelAsync(schedulePanel);
            LoadSchedules();
        }

        private void NavImport_Click(object sender, RoutedEventArgs e) => DoImport();
        private void NavExport_Click(object sender, RoutedEventArgs e) => DoExport();
        private void NavSync_Click(object sender, RoutedEventArgs e) => DoSync();
        private void NavGoal_Click(object sender, RoutedEventArgs e)
        {
            var win = new GoalWindow(_databaseManager) { Owner = this };
            win.ShowDialog();
        }

        private void NavReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var report = ReportGenerator.GenerateHtmlReport(
                    DateTime.Now.AddDays(-7), DateTime.Now, _databaseManager);
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"TimeTracker_Report_{DateTime.Now:yyyyMMdd}.html");
                File.WriteAllText(path, report, Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                NotificationHelper.Show("报告已生成", $"保存到: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { NotificationHelper.Show("报告生成失败", ex.Message, false); }
        }

        private void NavManageActivities_Click(object sender, RoutedEventArgs e) => ManageActivities();
        private void NavManageCategories_Click(object sender, RoutedEventArgs e) => ManageCategories();
        private void NavSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        // ======================== ACTION BUTTONS ========================

        private async void ActionDaily_Click(object s, RoutedEventArgs e)
        {
            _currentRange = "daily"; SetPage("每日统计", "今日各应用使用详情");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowProcessStats(); RefreshChartIfVisible(); });
        }

        private async void ActionWeekly_Click(object s, RoutedEventArgs e)
        {
            _currentRange = "weekly"; SetPage("每周统计", "近7天各应用使用详情");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowProcessStats(); RefreshChartIfVisible(); });
        }

        private async void ActionMonthly_Click(object s, RoutedEventArgs e)
        {
            _currentRange = "monthly"; SetPage("每月统计", "近30天各应用使用详情");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowProcessStats(); RefreshChartIfVisible(); });
        }

        private async void ActionCategory_Click(object s, RoutedEventArgs e)
        {
            _currentRange = "weekly"; SetPage("分类统计", "按标签分类的使用时长");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowCategoryStats(); });
        }

        private async void ActionForeground_Click(object s, RoutedEventArgs e)
        {
            SetPage("前台/后台", "前台与后台使用时间对比");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowForegroundStats(); });
        }

        private async void ActionTimeline_Click(object s, RoutedEventArgs e)
        {
            SetPage("时间线", "今日各时段应用使用分布");
            await FadeRefreshContentAsync(() => { LoadStats(); ShowTimeline(); });
        }

        private async void ShowTimeline()
        {
            var hourly = await Task.Run(() => _databaseManager.GetHourlyBreakdown(DateTime.Now.Date));
            SetupColumns("时段", "HourLabel", "应用", "ProcessDisplay", "时长", "DurationText");
            var data = Enumerable.Range(0, 24).Select(h =>
            {
                var items = hourly.Where(x => x.Hour == h).ToList();
                var top = items.FirstOrDefault();
                var total = items.Sum(x => x.UsageMs);
                return new TimelineItem { HourLabel = $"{h:D2}:00-{h:D2}:59",
                    ProcessDisplay = top.ProcessName ?? "—",
                    DurationText = total > 0 ? FormatMs(total) : "—", TotalMs = total };
            }).ToList();
            _currentData = data; dataGrid.ItemsSource = data; AnimateRowsIn();
        }

        private System.Collections.IList? _currentData;
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = txtSearch.Text.Trim().ToLower();
            if (_currentData == null) return;

            if (string.IsNullOrEmpty(filter)) { dataGrid.ItemsSource = _currentData; return; }

            var filtered = _currentData.OfType<object>().Where(item =>
            {
                foreach (var prop in item.GetType().GetProperties())
                {
                    var val = prop.GetValue(item)?.ToString() ?? "";
                    if (val.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }).ToList();

            dataGrid.ItemsSource = filtered;
        }
        
        private void RefreshChartIfVisible()
        {
            if (chartBorder.Visibility == Visibility.Visible)
                DrawCharts();
        }

        // ======================== DATA LOADING ========================

        private int _rowAnimIndex;

        /// <summary>DataGrid 行从上到下依次淡入</summary>
        private void AnimateRowsIn()
        {
            _rowAnimIndex = 0;
            dataGrid.LoadingRow -= OnRowLoading;
            dataGrid.LoadingRow += OnRowLoading;
            // 延迟卸载事件，确保所有行列均已渲染
            Dispatcher.BeginInvoke(new Action(() => dataGrid.LoadingRow -= OnRowLoading),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnRowLoading(object? sender, DataGridRowEventArgs e)
        {
            var row = e.Row;
            if (row == null) return;

            int idx = _rowAnimIndex++;
            row.Opacity = 0;
            var translate = new TranslateTransform(0, 15);
            row.RenderTransform = translate;

            var sb = new Storyboard();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
            {
                BeginTime = TimeSpan.FromMilliseconds(idx * 45),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (_, _) => row.Opacity = 1;
            Storyboard.SetTarget(fade, row);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(450))
            {
                BeginTime = TimeSpan.FromMilliseconds(idx * 45),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            slide.Completed += (_, _) => translate.Y = 0;
            Storyboard.SetTarget(slide, row);
            Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            sb.Children.Add(slide);

            sb.Begin();
        }

        private void SetPage(string title, string subtitle)
        {
            lblPageTitle.Text = title;
            lblPageSubtitle.Text = subtitle;
            lblDateRange.Text = _currentRange switch
            {
                "weekly" => "近7天", "monthly" => "近30天", _ => "今日"
            };
        }

        private (DateTime start, DateTime end) GetDateRange()
        {
            var end = DateTime.Now;
            var start = _currentRange switch
            {
                "weekly" => end.AddDays(-7),
                "monthly" => end.AddMonths(-1),
                _ => end.Date
            };
            return (start, end);
        }

        private async void LoadStats()
        {
            var (start, end) = GetDateRange();
            try
            {
                var topApps = await Task.Run(() => _databaseManager.GetTopProcesses(start, end));
                var totalMs = topApps.Sum(a => a.TotalUsage);
                var ts = TimeSpan.FromMilliseconds(totalMs);

                Dispatcher.Invoke(() =>
                {
                    AnimateLabel(lblTotalTime, ts.TotalHours >= 1
                        ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
                        : $"{ts.Minutes}m {ts.Seconds}s");
                    AnimateLabel(lblAppCount, topApps.Count.ToString(CultureInfo.InvariantCulture));
                });
            }
            catch (Exception ex)
            {
                Logger.Error("LoadStats error", ex);
                Dispatcher.Invoke(() =>
                {
                    lblTotalTime.Text = "--";
                    lblAppCount.Text = "--";
                });
            }
        }

        private async void ShowProcessStats()
        {
            var (start, end) = GetDateRange();
            SetupColumns("应用名称", "DisplayName", "使用时长", "UsageTime", "标签", "Category");

            var stats = await Task.Run(() => _databaseManager.GetTopProcesses(start, end));
            RefreshColorCache();

            var data = stats.Select(s =>
            {
                var catName = s.CategoryName ?? "未分类";
                return new ProcessUsageItem
                {
                    ProcessName = s.ProcessName ?? "",
                    DisplayName = ProcessNameHelper.GetDisplayName(s.ProcessName),
                    UsageTime = FormatMs(s.TotalUsage),
                    Category = catName,
                    CatColor = GetCatColor(catName)
                };
            }).ToList();

            _currentData = data;
            dataGrid.ItemsSource = _currentData;
            AnimateRowsIn();
            dataGrid.SelectedIndex = 0;
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGrid.SelectedItem == null) return;
            var container = dataGrid.ItemContainerGenerator.ContainerFromItem(dataGrid.SelectedItem);
            if (container is not DataGridRow row) return;

            row.Background = Brushes.Transparent;
            var highlight = new SolidColorBrush(Color.FromRgb(0xed, 0xe9, 0xfe)) { Opacity = 0 };
            var da = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                  AutoReverse = true, Duration = TimeSpan.FromMilliseconds(600) };
            da.Completed += (_, _) =>
            {
                highlight.Opacity = 0;
                row.Background = Brushes.Transparent;
            };
            row.Background = highlight;
            highlight.BeginAnimation(SolidColorBrush.OpacityProperty, da);
        }

        private async void ShowCategoryStats()
        {
            var (start, end) = GetDateRange();
            SetupColumns("分类", "CategoryName", "使用时长", "UsageTime", null, null);

            var stats = await Task.Run(() =>
                _databaseManager.GetStatsByCategory(start, end));
            var data = stats.Select(s => new CategoryStatsItem
            {
                CategoryName = s.CategoryName ?? "未分类",
                UsageTime = FormatMs(s.TotalUsage)
            }).ToList();

            _currentData = data;
            dataGrid.ItemsSource = data;
            AnimateRowsIn();
        }

        private async void ShowActivityStats()
        {
            var (start, end) = GetDateRange();
            SetupColumns("活动名称", "Name", "使用时长", "UsageDisplay", null, null);

            var stats = await Task.Run(() => _databaseManager.GetStatsByActivity(start, end));
            var data = stats.Select(s => new ActivityStatsItem
            {
                Name = s.Name,
                Color = s.Color,
                UsageDisplay = FormatMs(s.TotalUsage)
            }).ToList();

            _currentData = data;
            dataGrid.ItemsSource = data;
            AnimateRowsIn();
        }

        private async void ShowForegroundStats()
        {
            var (start, end) = GetDateRange();
            SetupColumns("类型", "Type", "使用时长", "UsageTime", null, null);

            var stats = await Task.Run(() =>
                _databaseManager.GetStatsByForeground(start, end));
            var data = stats.Select(s => new ForegroundStatsItem
            {
                Type = s.Type ?? "未知",
                UsageTime = FormatMs(s.TotalUsage)
            }).ToList();

            _currentData = data;
            dataGrid.ItemsSource = data;
            AnimateRowsIn();
        }

        /// <summary>
        /// 缓存分类颜色映射，避免每次 DB 访问
        /// </summary>
        private Dictionary<string, SolidColorBrush> _colorCache = new();

        private void RefreshColorCache()
        {
            var dict = new Dictionary<string, SolidColorBrush>();
            foreach (var c in _databaseManager.GetCategories())
            {
                try
                {
                    dict[c.Name] = (SolidColorBrush)new BrushConverter().ConvertFrom(c.Color!)!;
                }
                catch { dict[c.Name] = Brushes.Gray; }
            }
            _colorCache = dict;
        }

        private SolidColorBrush GetCatColor(string? categoryName)
        {
            if (string.IsNullOrEmpty(categoryName) || categoryName == "未分类")
                return Brushes.Gray;
            return _colorCache.TryGetValue(categoryName, out var brush) ? brush : Brushes.Gray;
        }

        private SolidColorBrush GetCatColorBrush(string? categoryName)
        {
            if (_colorCache.Count == 0) RefreshColorCache();
            return GetCatColor(categoryName);
        }

        private void SetupColumns(string h1, string b1, string h2, string b2, string? h3, string? b3)
        {
            dataGrid.Columns.Clear();
            dataGrid.Columns.Add(new DataGridTextColumn { Header = h1, Binding = new System.Windows.Data.Binding(b1), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = h2, Binding = new System.Windows.Data.Binding(b2), Width = 140 });
            if (!string.IsNullOrEmpty(h3))
                dataGrid.Columns.Add(new DataGridTextColumn { Header = h3, Binding = new System.Windows.Data.Binding(b3), Width = 140 });
        }

        /// <summary>
        /// 双击DataGrid行：弹出快速类别分配
        /// </summary>
        private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dataGrid.SelectedItem is ProcessUsageItem item)
            {
                RefreshColorCache();
                var cats = _databaseManager.GetCategories();
                var displayName = ProcessNameHelper.GetDisplayName(item.ProcessName);
                int? selectedCatId = cats.FirstOrDefault(c => c.Name == item.Category)?.Id;

                // ===== 颜色 =====
                var accentPurple = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7));
                var accentHover = new SolidColorBrush(Color.FromRgb(0x5a, 0x4b, 0xd1));
                var textDark = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e));
                var textGray = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80));
                var bgLight = new SolidColorBrush(Color.FromRgb(0xf4, 0xf4, 0xfa));

                // ===== 标题栏 =====
                var headerTitle = new TextBlock
                {
                    Text = "分配标签",
                    FontSize = 15, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20, 0, 0, 0)
                };
                var closeBtn = MakeFlatButton("✕", 36, 36, 14,
                    new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xee)));
                closeBtn.VerticalAlignment = VerticalAlignment.Center;
                closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
                closeBtn.Margin = new Thickness(0, 0, 4, 0);
                closeBtn.MouseEnter += (_, _) => closeBtn.Foreground = Brushes.White;
                closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xee));

                var headerBar = new Grid { Height = 48, Background = Brushes.Transparent };
                headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(headerTitle, 0); Grid.SetColumn(closeBtn, 2);
                headerBar.Children.Add(headerTitle); headerBar.Children.Add(closeBtn);

                var headerBorder = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0),
                    Background = accentPurple, Child = headerBar };

                // ===== 应用信息行 =====
                var prefixDot = new Ellipse { Width = 10, Height = 10, Fill = item.CatColor,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                nameStack.Children.Add(new TextBlock { Text = displayName, FontSize = 15,
                    FontWeight = FontWeights.Bold, Foreground = textDark });
                var subText = !string.Equals(item.ProcessName, displayName, StringComparison.OrdinalIgnoreCase)
                    ? $"→ {item.ProcessName}" : "";
                if (subText.Length > 0)
                    nameStack.Children.Add(new TextBlock { Text = subText, FontSize = 11,
                        Foreground = textGray, Margin = new Thickness(0, 1, 0, 0) });

                var appRow = new StackPanel { Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 10) };
                appRow.Children.Add(prefixDot); appRow.Children.Add(nameStack);

                // 分隔线
                var divider = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xee, 0xee, 0xf2)),
                    Margin = new Thickness(0, 0, 0, 10) };

                // ===== 标签卡片选择器 =====
                var sectionTitle = new TextBlock { Text = "选择标签", FontSize = 12,
                    FontWeight = FontWeights.SemiBold, Foreground = textGray,
                    Margin = new Thickness(0, 0, 0, 8) };

                var tagWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                var catButtons = new List<(Border card, int catId)>();
                int selectedIdx = -1;

                foreach (var cat in cats)
                {
                    var catColor = GetCatColorBrush(cat.Name);
                    var dot = new Ellipse { Width = 10, Height = 10, Fill = catColor, Margin = new Thickness(0, 0, 6, 0) };
                    var label = new TextBlock { Text = cat.Name, FontSize = 12 };
                    var tagPanel = new StackPanel { Orientation = Orientation.Horizontal,
                        Margin = new Thickness(6, 0, 6, 0) };
                    tagPanel.Children.Add(dot); tagPanel.Children.Add(label);

                    var card = new Border
                    {
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
                        Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe8)),
                        BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8),
                        Cursor = Cursors.Hand, Child = tagPanel, Tag = cat.Id,
                        Focusable = false
                    };
                    card.MouseLeftButtonDown += (_, _) =>
                    {
                        selectedCatId = cat.Id;
                        foreach (var (c, _) in catButtons)
                        { c.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe8)); c.Background = Brushes.White; }
                        card.BorderBrush = accentPurple; card.BorderThickness = new Thickness(2);
                        card.Background = bgLight;
                    };
                    var cIdx = catButtons.Count;
                    if (cat.Id == selectedCatId) { selectedIdx = cIdx; }
                    catButtons.Add((card, cat.Id));
                    tagWrap.Children.Add(card);
                }

                // 初始选中
                if (selectedCatId.HasValue)
                {
                    var initCard = catButtons.FirstOrDefault(b => b.catId == selectedCatId.Value).card;
                    if (initCard != null) { initCard.BorderBrush = accentPurple; initCard.BorderThickness = new Thickness(2); initCard.Background = bgLight; }
                }

                // ===== 按钮行 =====
                var saveBtnBorder = new Border
                {
                    CornerRadius = new CornerRadius(10), Background = accentPurple,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.15, Color = Color.FromRgb(0x6c, 0x5c, 0xe7) },
                    Child = new TextBlock { Text = "保存", FontSize = 14, FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(32, 10, 32, 10) }
                };
                var saveBtn = MakeFlatButton(saveBtnBorder, 110, 42);
                saveBtn.Margin = new Thickness(0, 0, 0, 0);
                saveBtn.MouseEnter += (_, _) => saveBtnBorder.Background = accentHover;
                saveBtn.MouseLeave += (_, _) => saveBtnBorder.Background = accentPurple;

                var cancelBtnBorder = new Border
                {
                    CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Color.FromRgb(0xd1, 0xd5, 0xdb)),
                    BorderThickness = new Thickness(1), Background = Brushes.White,
                    Child = new TextBlock { Text = "取消", FontSize = 14,
                        Foreground = textGray, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(32, 10, 32, 10) }
                };
                var cancelBtn = MakeFlatButton(cancelBtnBorder, 110, 42);
                cancelBtn.Margin = new Thickness(12, 0, 0, 0);
                cancelBtn.MouseEnter += (_, _) => cancelBtnBorder.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf8));
                cancelBtn.MouseLeave += (_, _) => cancelBtnBorder.Background = Brushes.White;

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                btnRow.Children.Add(saveBtn); btnRow.Children.Add(cancelBtn);

                // ===== 组装 =====
                var contentPanel = new StackPanel { Margin = new Thickness(28, 20, 28, 16) };
                contentPanel.Children.Add(appRow);
                contentPanel.Children.Add(divider);
                contentPanel.Children.Add(sectionTitle);
                contentPanel.Children.Add(tagWrap);
                contentPanel.Children.Add(btnRow);

                var contentBorder = new Border { Background = Brushes.White,
                    CornerRadius = new CornerRadius(0, 0, 12, 12), Child = contentPanel };
                var rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(headerBorder, 0); Grid.SetRow(contentBorder, 1);
                rootGrid.Children.Add(headerBorder); rootGrid.Children.Add(contentBorder);

                var outerBorder = new Border
                {
                    CornerRadius = new CornerRadius(12), Background = Brushes.White,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.2, Color = Colors.Black },
                    Child = rootGrid
                };

                var scaleTransform = new ScaleTransform(0.85, 0.85);
                outerBorder.RenderTransform = scaleTransform;
                outerBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                outerBorder.Opacity = 0;

                var popup = new Window
                {
                    Width = 440,
                    Height = 330,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Content = outerBorder,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    ShowInTaskbar = false
                };

                // 入场动画（淡入 + 放大）
                popup.Loaded += (_, _) =>
                {
                    var storyboard = new Storyboard();

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    Storyboard.SetTarget(fadeIn, outerBorder);
                    Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
                    storyboard.Children.Add(fadeIn);

                    var scaleX = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(350))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    Storyboard.SetTarget(scaleX, outerBorder);
                    Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                    storyboard.Children.Add(scaleX);

                    var scaleY = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(350))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    Storyboard.SetTarget(scaleY, outerBorder);
                    Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                    storyboard.Children.Add(scaleY);

                    storyboard.Begin();
                };

                // 关闭动画辅助方法
                async Task AnimatedClose()
                {
                    var collapse = new Storyboard();

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(fadeOut, outerBorder);
                    Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
                    collapse.Children.Add(fadeOut);

                    var closeScaleX = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(closeScaleX, outerBorder);
                    Storyboard.SetTargetProperty(closeScaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                    collapse.Children.Add(closeScaleX);

                    var closeScaleY = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(closeScaleY, outerBorder);
                    Storyboard.SetTargetProperty(closeScaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                    collapse.Children.Add(closeScaleY);

                    collapse.Begin();
                    await Task.Delay(160);
                    popup.Close();
                }

                // 拖拽支持（仅在标题文字区域，避免干扰关闭按钮）
                headerTitle.MouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                        popup.DragMove();
                };
                headerBorder.MouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                        popup.DragMove();
                };

                // ===== 事件 =====
                closeBtn.Click += async (_, _) => await AnimatedClose();

                saveBtn.Click += async (_, _) =>
                {
                    if (selectedCatId is int catId && catId > 0)
                    {
                        var oldCategory = item.Category;
                        var oldColor = item.CatColor;
                        var oldCatId = cats.FirstOrDefault(c => c.Name == oldCategory)?.Id;
                        var cat = cats.FirstOrDefault(c => c.Id == catId);
                        item.Category = cat?.Name ?? "未分类";
                        item.CatColor = cat != null ? GetCatColor(cat.Name) : Brushes.Gray;

                        _trackingService?.AddProcessCategoryMapping(item.ProcessName, catId);
                        int updated = _databaseManager.UpdateProcessCategory(item.ProcessName, catId);
                        dataGrid.Items.Refresh();

                        await AnimatedClose();
                        NotificationHelper.Show("标签已分配",
                            $"已更新 {updated} 条记录\n\"{displayName}\" → {item.Category}",
                            onUndo: () =>
                            {
                                item.Category = oldCategory;
                                item.CatColor = oldColor;
                                if (oldCatId.HasValue)
                                    _databaseManager.UpdateProcessCategory(item.ProcessName, oldCatId.Value);
                                else
                                    _databaseManager.UpdateProcessCategory(item.ProcessName, 0);
                                dataGrid.Items.Refresh();
                                NotificationHelper.Show("已撤销", $"\"{displayName}\" 恢复为 {oldCategory}");
                            });
                    }
                    else
                    {
                        await AnimatedClose();
                    }
                };
                cancelBtn.Click += async (_, _) => await AnimatedClose();
                popup.ShowDialog();
            }
        }

        // ======================== IMPORT / EXPORT / SYNC ========================

        private async void DoExport()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出追踪数据",
                Filter = "JSON 文件|*.json|CSV 文件|*.csv",
                FileName = $"TimeTracker_{DateTime.Now:yyyyMMdd}.json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() != true) return;

            lblCurrentApp.Text = "导出中...";
            var (ok, count, error) = await Task.Run(
                () => DataSyncUtils.ExportData(_databaseManager, dialog.FileName));
            if (ok)
                NotificationHelper.Show("导出成功", $"已导出 {count} 条记录\n{dialog.FileName}");
            else
                NotificationHelper.Show("导出失败", error ?? "未知错误", false);
            lblCurrentApp.Text = ok ? $"导出完成 ✓" : "导出失败 ✗";
        }

        private async void DoImport()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入追踪数据",
                Filter = "JSON 文件|*.json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() != true) return;

            lblCurrentApp.Text = "导入中...";
            var (ok, newCount, skipped, error) = await Task.Run(
                () => DataSyncUtils.ImportData(_databaseManager, dialog.FileName));
            if (ok)
            {
                NotificationHelper.Show("导入成功",
                    $"新增 {newCount} 条，跳过 {skipped} 条重复记录");
                LoadStats();
            }
            else
            {
                NotificationHelper.Show("导入失败", error ?? "未知错误", false);
            }
            lblCurrentApp.Text = ok ? "导入完成" : "导入失败";
        }

        private async void DoSync()
        {
            lblCurrentApp.Text = "同步中...";
            var (ok, error) = await Task.Run(
                () => DataSyncUtils.SyncData(_databaseManager));
            if (ok)
            {
                NotificationHelper.Show("同步完成", "数据已导出并合并到本地");
                LoadStats();
            }
            else
            {
                NotificationHelper.Show("同步失败", error ?? "未知错误", false);
            }
            lblCurrentApp.Text = ok ? "同步完成" : "同步失败";
        }

        private void ManageActivities()
        {
            var dialog = new Window { Title = "管理活动", Width = 380, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.CanResize, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xf6, 0xfa)) };
            var sp = new StackPanel { Margin = new Thickness(20) };

            var title = new TextBlock { Text = "管理活动", FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e)), Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(title);

            var lb = new ListBox { Height = 240, BorderThickness = new Thickness(0),
                Background = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
            lb.ItemTemplate = new DataTemplate(() =>
            {
                var sp2 = new StackPanel();
                var tb = new TextBlock();
                tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
                tb.FontSize = 13; tb.FontWeight = FontWeights.SemiBold;
                sp2.Children.Add(tb);
                return new FrameworkElementFactory(typeof(StackPanel)) == null ? null : new FrameworkElementFactory(typeof(ContentPresenter));
            });
            // 手动构建 ListBox ItemTemplate
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
            textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            textFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            factory.AppendChild(textFactory);
            lb.ItemTemplate = new DataTemplate { VisualTree = factory };
            lb.DisplayMemberPath = "Name";
            sp.Children.Add(lb);

            var inputRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tbName = new TextBox { FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xd1, 0xd5, 0xdb)) };
            var btnAdd = new Button { Content = "添加", Width = 60, Height = 34, Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)), Foreground = Brushes.White,
                FontSize = 12, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            Grid.SetColumn(tbName, 0); Grid.SetColumn(btnAdd, 1);
            inputRow.Children.Add(tbName); inputRow.Children.Add(btnAdd);
            sp.Children.Add(inputRow);

            var btnDelete = new Button { Content = "删除选中活动", Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(0xfe, 0xf2, 0xf2)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)),
                FontSize = 12, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            sp.Children.Add(btnDelete);

            dialog.Content = sp;

            void RefreshList()
            {
                var acts = _databaseManager.GetActivities();
                lb.ItemsSource = null; lb.ItemsSource = acts;
            }
            RefreshList();

            btnAdd.Click += (_, _) =>
            {
                var name = tbName.Text.Trim();
                if (name.Length > 0) { _databaseManager.AddActivity(name); tbName.Text = ""; RefreshList(); RefreshActivitySelector(); }
            };
            tbName.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) { btnAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
            btnDelete.Click += (_, _) =>
            {
                if (lb.SelectedItem is ActivityData act)
                {
                    _databaseManager.DeleteActivity(act.Id);
                    RefreshList();
                    RefreshActivitySelector();
                }
            };

            dialog.ShowDialog();
            LoadStats();
        }

        private void ManageCategories()
        {
            var win = new CategoryManageWindow(_databaseManager, _trackingService);
            win.Owner = this;
            win.ShowDialog();
            LoadStats();
        }

        private void OpenSettings()
        {
            var win = SettingsWindow.LoadSettings();
            win.Owner = this;
            win.CleanOldDataRequested += () =>
                _databaseManager.DeleteOldRecords(DateTime.Now.AddDays(-AppSettings.RetentionDays));
            if (win.ShowDialog() == true)
            {
                AppSettings.TrackingIntervalSeconds = win.TrackingIntervalSeconds;
                AppSettings.RetentionDays = win.RetentionDays;
                AppSettings.AutoStart = win.AutoStart;
                AppSettings.TrackingMode = win.TrackingMode;
                AppSettings.ServerUrl = win.ServerUrl;
                AppSettings.AutoSync = win.AutoSync;
                AppSettings.HostServer = win.HostServer;
                AppSettings.ServerPort = win.ServerPort;
                AppSettings.Save();
                AppSettings.ApplyAutoStart();
                _databaseManager.DeleteOldRecords(DateTime.Now.AddDays(-AppSettings.RetentionDays));
                RefreshActivitySelector();
                SetupServerAndSync();
            }
        }

        // ======================== SERVER + SYNC ========================
        private System.Timers.Timer? _syncTimer;
        private System.Timers.Timer? _backupTimer;
        private System.Timers.Timer? _autoExportTimer;

        private void SetupServerAndSync()
        {
            _syncTimer?.Stop();
            _backupTimer?.Stop();
            _autoExportTimer?.Stop();

            // 每日自动备份（24小时）
            _backupTimer = new System.Timers.Timer(24 * 3600 * 1000);
            _backupTimer.Elapsed += (_, _) =>
            {
                try
                {
                    var path = _databaseManager.BackupDatabase();
                    if (!string.IsNullOrEmpty(path)) Logger.Info($"Database backed up: {path}");
                }
                catch (Exception ex) { Logger.Error("Auto backup failed", ex); }
            };
            _backupTimer.AutoReset = true;
            _backupTimer.Start();

            // 定时自动导出
            if (AppSettings.AutoExportEnabled && AppSettings.AutoExportIntervalMinutes > 0)
            {
                _autoExportTimer = new System.Timers.Timer(AppSettings.AutoExportIntervalMinutes * 60 * 1000);
                _autoExportTimer.Elapsed += (_, _) =>
                {
                    try
                    {
                        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TimeTracker", "Exports");
                        Directory.CreateDirectory(dir);
                        var path = System.IO.Path.Combine(dir, $"TimeTracker_auto_{DateTime.Now:yyyyMMdd_HHmm}.json");
                        DataSyncUtils.ExportData(_databaseManager, path);
                        Logger.Info($"Auto-exported to: {path}");
                    }
                    catch (Exception ex) { Logger.Error("Auto export failed", ex); }
                };
                _autoExportTimer.AutoReset = true;
                _autoExportTimer.Start();
            }

            _syncTimer?.Stop();

            // 1. 本机作为服务器
            if (AppSettings.HostServer)
            {
                EmbeddedServer.Start(_databaseManager, AppSettings.ServerPort);
            }
            else
            {
                EmbeddedServer.Stop();
            }

            // 2. 自动同步
            if ((AppSettings.AutoSync || AppSettings.HostServer) &&
                !string.IsNullOrEmpty(AppSettings.ServerUrl))
            {
                _ = DoServerSync();
            }
            if (AppSettings.AutoSync)
            {
                _syncTimer = new System.Timers.Timer(60_000);
                _syncTimer.Elapsed += async (_, _) => await DoServerSync();
                _syncTimer.AutoReset = true;
                _syncTimer.Start();
            }
        }

        private async Task DoServerSync()
        {
            try
            {
                var (ok, err) = await ServerSyncClient.SyncAsync(_databaseManager);
                if (ok)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadStats();
                        if (chartBorder.Visibility == Visibility.Visible) DrawCharts();
                    });
                }
            }
            catch { }
        }

        // ======================== ACTIVITY MODE ========================
        private void RefreshActivitySelector()
        {
            bool active = AppSettings.TrackingMode == "activity";
            activityPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            btnActivity.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            btnCategory.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            panelStandard.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            panelActivity.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active) return;

            var acts = _databaseManager.GetActivities();
            if (acts.Count == 0)
            {
                _databaseManager.AddActivity("学习", "#6c5ce7", "📚");
                _databaseManager.AddActivity("工作", "#10b981", "💼");
                _databaseManager.AddActivity("娱乐", "#f59e0b", "🎮");
                _databaseManager.AddActivity("社交", "#3b82f6", "💬");
                acts = _databaseManager.GetActivities();
            }
            cmbActivity.ItemsSource = acts;
            cmbActivity.DisplayMemberPath = "Name";

            if (AppSettings.CurrentActivityId.HasValue)
            {
                var selected = acts.FirstOrDefault(a => a.Id == AppSettings.CurrentActivityId.Value);
                if (selected != null) cmbActivity.SelectedItem = selected;
            }
            if (cmbActivity.SelectedItem == null && acts.Count > 0)
                cmbActivity.SelectedIndex = 0;
        }

        private void CmbActivity_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (cmbActivity.SelectedItem is ActivityData act)
            {
                AppSettings.CurrentActivityId = act.Id;
                AppSettings.Save();
            }
        }

        private void BtnAddActivity_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window { Title = "新建活动", Width = 280, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                Background = Brushes.White, WindowStyle = WindowStyle.ToolWindow };
            var sp = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
            var tb = new TextBox { Margin = new Thickness(0, 0, 0, 8), FontSize = 13 };

            // 先构建 template 树，再创建 ControlTemplate
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);

            var btn = new Button { Content = "添加", Height = 34, Background = new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                FontSize = 13, FontWeight = FontWeights.SemiBold };
            btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            sp.Children.Add(new TextBlock { Text = "活动名称:", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)), Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(tb);
            sp.Children.Add(btn);
            dialog.Content = sp;
            btn.Click += (_, _) =>
            {
                var name = tb.Text.Trim();
                if (name.Length > 0) { _databaseManager.AddActivity(name); RefreshActivitySelector(); }
                dialog.Close();
            };
            tb.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            dialog.ShowDialog();
        }

        // ======================== TRAY ICON ========================
        private void SetupTrayIcon()
        {
            NativeTray.Create(this,
                "TimeTracker - 时间追踪\n双击显示 | 右键暂停 | 中键退出",
                () => { Show(); WindowState = WindowState.Normal; Activate(); },
                () => _trackingService?.TogglePause(),
                () => Close());
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
                Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncTimer?.Stop();
            _backupTimer?.Stop();
            _autoExportTimer?.Stop();
            EmbeddedServer.Stop();
            GlobalHotkeyManager.Unregister();
            _trackingService?.Dispose();
            base.OnClosed(e);
        }

        // ======================== CHARTS ========================

        private void ActionChart_Click(object s, RoutedEventArgs e)
        {
            if (chartBorder.Visibility == Visibility.Visible)
            {
                chartBorder.Visibility = Visibility.Collapsed;
                return;
            }
            chartBorder.Opacity = 0;
            var scale = new ScaleTransform(0.92, 0.92);
            chartBorder.RenderTransform = scale;
            chartBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            chartBorder.Visibility = Visibility.Visible;

            var sb = new Storyboard();
            var fi = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fi, chartBorder);
            Storyboard.SetTargetProperty(fi, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fi);
            var sx = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(550))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sx, chartBorder);
            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            sb.Children.Add(sx);
            var sy = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(550))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sy, chartBorder);
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            sb.Children.Add(sy);
            sb.Completed += async (_, _) =>
            {
                await Task.Delay(120);
                DrawCharts();
            };
            sb.Begin();
        }

        private void ChartClose_Click(object s, RoutedEventArgs e)
        {
            var sb = new Storyboard();
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(fo, chartBorder);
            Storyboard.SetTargetProperty(fo, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fo);
            var sx = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(250))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(sx, chartBorder);
            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            sb.Children.Add(sx);
            var sy = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(250))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(sy, chartBorder);
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            sb.Children.Add(sy);
            sb.Completed += (_, _) => chartBorder.Visibility = Visibility.Collapsed;
            sb.Begin();
        }

        private async void DrawCharts()
        {
            // 两个 canvas 预设隐藏
            barChartCanvas.Opacity = 0;
            pieChartCanvas.Opacity = 0;

            var (start, end) = GetDateRange();
            var procTask = Task.Run(() => _databaseManager.GetTopProcesses(start, end));
            var catTask = Task.Run(() => _databaseManager.GetStatsByCategory(start, end));
            var procStats = await procTask;
            var catStats = await catTask;

            // 柱状图先画
            DrawBarChart(procStats.Take(10).ToList());
            await Task.Delay(120);
            // 饼图再画
            DrawPieChart(catStats);

            // 柱状图 canvas 整体淡入
            var barFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            barChartCanvas.BeginAnimation(UIElement.OpacityProperty, barFade);
            await Task.Delay(350);
            // 饼图 canvas 整体淡入
            var pieFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            pieChartCanvas.BeginAnimation(UIElement.OpacityProperty, pieFade);
        }

        private static readonly Color[] ChartColors = {
            Color.FromRgb(0x6c,0x5c,0xe7), Color.FromRgb(0x10,0xb9,0x81), Color.FromRgb(0xf5,0x9e,0x0b),
            Color.FromRgb(0x3b,0x82,0xf6), Color.FromRgb(0xef,0x44,0x44), Color.FromRgb(0x8b,0x5c,0xf6),
            Color.FromRgb(0x06,0xb6,0xd4), Color.FromRgb(0x84,0xcc,0x16), Color.FromRgb(0xf9,0x73,0x16),
            Color.FromRgb(0xec,0x48,0x99)
        };

        private void DrawBarChart(List<ProcessUsageData> data)
        {
            barChartCanvas.Children.Clear();
            if (data.Count == 0) return;

            double canvasW = Math.Max(barChartCanvas.ActualWidth, 300);
            double canvasH = Math.Max(barChartCanvas.ActualHeight, 180);
            double barAreaL = 80, barAreaR = 20, barAreaT = 10, barAreaB = 30;
            double chartW = canvasW - barAreaL - barAreaR;
            double chartH = canvasH - barAreaT - barAreaB;

            var maxMs = data.Max(d => d.TotalUsage);
            if (maxMs == 0) maxMs = 1;
            double barW = Math.Min(40, (chartW / data.Count) * 0.7);
            double gap = chartW / data.Count;

            for (int i = 0; i < data.Count; i++)
            {
                var d = data[i];
                double targetH = (d.TotalUsage / (double)maxMs) * chartH;
                double x = barAreaL + i * gap + (gap - barW) / 2;
                double baseY = barAreaT + chartH;

                var color = ChartColors[i % ChartColors.Length];
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = barW, Height = 0,
                    Fill = new SolidColorBrush(color),
                    RadiusX = 4, RadiusY = 4
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, baseY);
                barChartCanvas.Children.Add(rect);

                // 柱子生长动画
                var growAnim = new DoubleAnimation(0, targetH, TimeSpan.FromMilliseconds(500 + i * 70))
                    { EasingFunction = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut } };
                growAnim.Completed += (_, _) =>
                {
                    rect.Height = targetH;
                    Canvas.SetTop(rect, baseY - targetH);
                };
                rect.BeginAnimation(System.Windows.Shapes.Rectangle.HeightProperty, growAnim);

                var label = new TextBlock
                {
                    Text = Truncate(ProcessNameHelper.GetDisplayName(d.ProcessName), 6),
                    FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)),
                    TextAlignment = TextAlignment.Center, Width = gap, Opacity = 0
                };
                Canvas.SetLeft(label, barAreaL + i * gap);
                Canvas.SetTop(label, canvasH - 18);
                barChartCanvas.Children.Add(label);
                var lblAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                    { BeginTime = TimeSpan.FromMilliseconds(500 + i * 70), FillBehavior = FillBehavior.Stop };
                lblAnim.Completed += (_, _) => label.Opacity = 1;
                label.BeginAnimation(UIElement.OpacityProperty, lblAnim);

                var val = new TextBlock
                {
                    Text = FormatMsShort(d.TotalUsage),
                    FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(color),
                    TextAlignment = TextAlignment.Center, Width = gap, Opacity = 0
                };
                Canvas.SetLeft(val, barAreaL + i * gap);
                Canvas.SetTop(val, baseY - targetH - 14);
                barChartCanvas.Children.Add(val);
                val.BeginAnimation(UIElement.OpacityProperty, lblAnim);
            }
        }

        private void DrawPieChart(List<CategoryStatsData> data)
        {
            pieChartCanvas.Children.Clear();
            if (data.Count == 0) return;

            double pieW = Math.Max(pieChartCanvas.ActualWidth, 300);
            double pieH = Math.Max(pieChartCanvas.ActualHeight, 180);
            double cx = pieW / 2;
            double cy = pieH * 0.45;
            double radius = Math.Min(pieW, pieH) * 0.38;
            var totalMs = data.Sum(d => d.TotalUsage);
            if (totalMs == 0) return;

            double angle = 0;
            var legendY = 16;
            for (int i = 0; i < data.Count; i++)
            {
                var d = data[i];
                double sweep = (d.TotalUsage / (double)totalMs) * 360;
                if (sweep < 3) sweep = 3;
                if (sweep > 360 - angle) sweep = 360 - angle;
                if (sweep <= 0) continue;

                var color = ChartColors[i % ChartColors.Length];
                var arcPath = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(color),
                    Opacity = 0
                };
                arcPath.Data = BuildArcGeometry(cx, cy, radius, angle, sweep);
                pieChartCanvas.Children.Add(arcPath);

                // 扇区动画
                var arcAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350 + i * 100))
                    { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                      FillBehavior = FillBehavior.Stop };
                arcAnim.Completed += (_, _) => arcPath.Opacity = 1;
                arcPath.BeginAnimation(UIElement.OpacityProperty, arcAnim);

                angle += sweep;

                var pct = (int)Math.Round(d.TotalUsage / (double)totalMs * 100);
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0 };
                legend.Children.Add(new System.Windows.Shapes.Ellipse
                    { Width = 8, Height = 8, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 4, 0) });
                legend.Children.Add(new TextBlock
                {
                    Text = $"{d.CategoryName} {pct}%",
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80))
                });
                Canvas.SetLeft(legend, 0);
                Canvas.SetTop(legend, legendY);
                pieChartCanvas.Children.Add(legend);

                var legAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                    { BeginTime = TimeSpan.FromMilliseconds(350 + i * 100), FillBehavior = FillBehavior.Stop };
                legAnim.Completed += (_, _) => legend.Opacity = 1;
                legend.BeginAnimation(UIElement.OpacityProperty, legAnim);

                legendY += 20;
            }
        }

        private static PathGeometry BuildArcGeometry(double cx, double cy, double r,
            double start, double sweep)
        {
            double startRad = (start - 90) * Math.PI / 180;
            double endRad = (start + sweep - 90) * Math.PI / 180;
            double x1 = cx + r * Math.Cos(startRad);
            double y1 = cy + r * Math.Sin(startRad);
            double x2 = cx + r * Math.Cos(endRad);
            double y2 = cy + r * Math.Sin(endRad);
            bool largeArc = sweep > 180;

            return new PathGeometry(new PathFigure[] {
                new PathFigure(new Point(cx, cy), new PathSegment[] {
                    new LineSegment(new Point(x1, y1), true),
                    new ArcSegment(new Point(x2, y2), new Size(r, r), 0, largeArc,
                        SweepDirection.Clockwise, true)
                }, true)
            });
        }

        /// <summary>创建无 WPF 默认样式的扁平按钮（消除蓝色悬停）</summary>
        private static Button MakeFlatButton(object content, double w, double h)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var fef = new FrameworkElementFactory(typeof(ContentPresenter));
            tpl.VisualTree = fef;
            return new Button
            {
                Content = content, Width = w, Height = h,
                Template = tpl, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Padding = new Thickness(0), Focusable = false
            };
        }

        private static Button MakeFlatButton(string text, double w, double h, double fontSize, Brush foreground)
        {
            var btn = MakeFlatButton(new TextBlock { Text = text, FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                w, h);
            btn.Foreground = foreground;
            return btn;
        }

        private static void AnimateLabel(TextBlock label, string newText)
        {
            if (label.Text == newText) return;
            label.Opacity = 0;
            label.Text = newText;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            label.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text[..(maxLen - 1)] + "…";
        }

        private static string FormatMsShort(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m";
            return $"{ts.Seconds}s";
        }

        // ======================== HELPERS ========================

        private static string FormatMs(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        // ======================== TODO LIST ========================

        private void TodoAdd_Click(object sender, RoutedEventArgs e)
        {
            var title = txtTodoTitle.Text.Trim();
            if (string.IsNullOrEmpty(title)) return;

            _databaseManager.InsertTodo(title, "", _selectedPriority, null, DeviceId);
            txtTodoTitle.Clear();
            LoadTodoItems();
        }

        private void Priority_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var pri))
            {
                _selectedPriority = pri;
            }
            btnPrioHigh.Style = (Style)FindResource(_selectedPriority == 2 ? "PrioBtnHighActive" : "PrioBtnHigh");
            btnPrioMid.Style = (Style)FindResource(_selectedPriority == 1 ? "PrioBtnMidActive" : "PrioBtnMid");
            btnPrioLow.Style = (Style)FindResource(_selectedPriority == 0 ? "PrioBtnLowActive" : "PrioBtnLow");
        }

        private void TodoFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _todoFilter = "all";
            LoadTodoItems();
        }

        private void TodoFilterPending_Click(object sender, RoutedEventArgs e)
        {
            _todoFilter = "pending";
            LoadTodoItems();
        }

        private void TodoFilterDone_Click(object sender, RoutedEventArgs e)
        {
            _todoFilter = "done";
            LoadTodoItems();
        }

        private void ToggleTodo(int id, bool completed)
        {
            _databaseManager.ToggleTodo(id, completed);
            LoadTodoItems();
        }

        private void LoadTodoItems()
        {
            todoItemsControl.Items.Clear();

            List<TodoItemData> items;
            if (_todoFilter == "pending")
                items = _databaseManager.GetTodos(false);
            else if (_todoFilter == "done")
                items = _databaseManager.GetTodos(true).Where(t => t.IsCompleted).ToList();
            else
                items = _databaseManager.GetTodos(true);

            foreach (var item in items)
            {
                var checkBox = new CheckBox
                {
                    IsChecked = item.IsCompleted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                var itemId = item.Id;
                checkBox.Click += (s, ev) => ToggleTodo(itemId, checkBox.IsChecked == true);

                var titleBlock = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 14,
                    FontWeight = item.IsCompleted ? FontWeights.Normal : FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 280
                };
                if (item.IsCompleted)
                {
                    titleBlock.TextDecorations = TextDecorations.Strikethrough;
                    titleBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x9c, 0xa3, 0xaf));
                }

                var priorityText = item.Priority switch { 2 => "🔴 高", 0 => "🟢 低", _ => "🟡 中" };
                var priorityBadge = new TextBlock
                {
                    Text = priorityText,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };

                var dueText = !string.IsNullOrEmpty(item.DueDate) ? $"📅 {item.DueDate}" : "";
                var dueBlock = new TextBlock
                {
                    Text = dueText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };

                var delId = item.Id;
                var deleteBtn = new Button
                {
                    Content = "删除",
                    Background = new SolidColorBrush(Color.FromRgb(0xfe, 0xf2, 0xf2)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)),
                    FontSize = 11,
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(16, 0, 0, 0)
                };
                var dtpl = new ControlTemplate(typeof(Button));
                var dfef = new FrameworkElementFactory(typeof(Border));
                dfef.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                dfef.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                var dcp = new FrameworkElementFactory(typeof(ContentPresenter));
                dcp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                dcp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                dfef.AppendChild(dcp);
                dtpl.VisualTree = dfef;
                deleteBtn.Template = dtpl;
                deleteBtn.Click += (s, ev) => { _databaseManager.DeleteTodo(delId); LoadTodoItems(); };

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(checkBox);
                row.Children.Add(titleBlock);
                row.Children.Add(priorityBadge);
                row.Children.Add(dueBlock);
                row.Children.Add(deleteBtn);

                var border = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = Brushes.White,
                    Child = row
                };

                todoItemsControl.Items.Add(border);
            }
        }

        // ======================== SCHEDULE ========================

        private void ScheduleAdd_Click(object sender, RoutedEventArgs e)
        {
            var title = txtScheduleTitle.Text.Trim();
            if (string.IsNullOrEmpty(title)) return;

            var date = _selectedDate ?? DateTime.Today;
            var isAllDay = chkAllDay.IsChecked == true;

            string startTime;
            string? endTime = null;

            if (isAllDay)
            {
                startTime = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                endTime = date.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else
            {
                var timeInput = txtScheduleTime.Text.Trim();
                var parts = timeInput.Split('-');
                if (parts.Length == 2)
                {
                    var startStr = parts[0].Trim();
                    var endStr = parts[1].Trim();
                    if (TimeSpan.TryParse(startStr, out var startTs) && TimeSpan.TryParse(endStr, out var endTs))
                    {
                        var startDt = date + startTs;
                        var endDt = date + endTs;
                        startTime = startDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        endTime = endDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        startTime = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    startTime = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
            }

            _databaseManager.InsertSchedule(title, "", startTime, endTime, isAllDay, _selectedColor, DeviceId);
            txtScheduleTitle.Clear();
            txtScheduleTime.Text = "08:00 - 10:00";
            _selectedDate = null;
            UpdateDateButton(_selectedDate);
            chkAllDay.IsChecked = false;
            LoadSchedules();
        }

        private void DatePickerBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new Window
            {
                Title = "选择日期",
                Width = 260,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Content = new System.Windows.Controls.Calendar
                {
                    SelectedDate = _selectedDate ?? DateTime.Today,
                    DisplayMode = CalendarMode.Month,
                    IsTodayHighlighted = true,
                    Margin = new Thickness(10)
                }
            };
            var cal = (System.Windows.Controls.Calendar)win.Content;
            cal.SelectedDatesChanged += (s, ev) =>
            {
                _selectedDate = cal.SelectedDate;
                UpdateDateButton(_selectedDate);
                win.Close();
            };
            win.ShowDialog();
        }

        private void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (dpScheduleDate.SelectedDate.HasValue)
            {
                _selectedDate = dpScheduleDate.SelectedDate.Value;
                UpdateDateButton(_selectedDate);
            }
        }

        private void UpdateDateButton(DateTime? date)
        {
            var template = btnDatePicker.Template;
            var txt = template?.FindName("txt", btnDatePicker) as TextBlock;
            if (txt != null)
            {
                txt.Text = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "选择日期";
                txt.Foreground = date.HasValue
                    ? new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x2e))
                    : new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80));
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string color)
            {
                _selectedColor = color;
                foreach (Button child in colorPickerGroup.Children)
                {
                    child.Style = (Style)FindResource(
                        child.Tag?.ToString() == _selectedColor ? "ColorPickerBtnSelected" : "ColorPickerBtn");
                }
            }
        }

        private void LoadSchedules()
        {
            scheduleItemsControl.Items.Clear();
            var schedules = _databaseManager.GetSchedules();

            foreach (var item in schedules)
            {
                var colorBrush = ParseColorBrush(item.Color);

                var titleBlock = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                string timeText;
                try
                {
                    var startDt = DateTime.Parse(item.StartTime, CultureInfo.InvariantCulture);
                    if (item.IsAllDay)
                        timeText = $"📅 {startDt:yyyy-MM-dd} 全天";
                    else if (!string.IsNullOrEmpty(item.EndTime))
                    {
                        var endDt = DateTime.Parse(item.EndTime, CultureInfo.InvariantCulture);
                        timeText = $"📅 {startDt:yyyy-MM-dd}  {startDt:HH:mm} - {endDt:HH:mm}";
                    }
                    else
                        timeText = $"📅 {startDt:yyyy-MM-dd}  {startDt:HH:mm}";
                }
                catch
                {
                    timeText = item.StartTime;
                }

                var descText = !string.IsNullOrEmpty(item.Description) ? $" | {item.Description}" : "";
                var detailBlock = new TextBlock
                {
                    Text = timeText + descText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80))
                };

                var delId = item.Id;
                var deleteBtn = new Button
                {
                    Content = "删除",
                    Background = new SolidColorBrush(Color.FromRgb(0xfe, 0xf2, 0xf2)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)),
                    FontSize = 11,
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                var stpl = new ControlTemplate(typeof(Button));
                var sfef = new FrameworkElementFactory(typeof(Border));
                sfef.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                sfef.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                var scp = new FrameworkElementFactory(typeof(ContentPresenter));
                scp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                scp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                sfef.AppendChild(scp);
                stpl.VisualTree = sfef;
                deleteBtn.Template = stpl;
                deleteBtn.Click += (s, ev) => { _databaseManager.DeleteSchedule(delId); LoadSchedules(); };

                var textStack = new StackPanel();
                textStack.Children.Add(titleBlock);
                textStack.Children.Add(detailBlock);

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(textStack, 0);
                Grid.SetColumn(deleteBtn, 1);
                row.Children.Add(textStack);
                row.Children.Add(deleteBtn);

                var colorBar = new Border
                {
                    Width = 4,
                    Background = colorBrush,
                    CornerRadius = new CornerRadius(2, 0, 0, 2)
                };

                var outerRow = new Grid();
                outerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                outerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(colorBar, 0);
                Grid.SetColumn(row, 1);
                row.Margin = new Thickness(12, 0, 0, 0);
                outerRow.Children.Add(colorBar);
                outerRow.Children.Add(row);

                var border = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(0, 10, 16, 10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = Brushes.White,
                    Child = outerRow
                };

                scheduleItemsControl.Items.Add(border);
            }
        }

        private static SolidColorBrush ParseColorBrush(string hex)
        {
            try { return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; }
            catch { return new SolidColorBrush(Color.FromRgb(0x6c, 0x5c, 0xe7)); }
        }

        // ======================== WINDOW DRAG (borderless) ========================

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }


    }

    // ======================== VIEW MODELS ========================

    public class ProcessUsageItem
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string UsageTime { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public SolidColorBrush CatColor { get; set; } = Brushes.Gray;
    }

    public class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#3498db";
        public string Description { get; set; } = string.Empty;
        public SolidColorBrush ColorBrush
        {
            get
            {
                try { return (SolidColorBrush)new BrushConverter().ConvertFrom(Color)!; }
                catch { return Brushes.Gray; }
            }
        }
    }

    public class CategoryStatsItem
    {
        public string CategoryName { get; set; } = string.Empty;
        public string UsageTime { get; set; } = string.Empty;
    }

    public class ActivityStatsItem
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#6c5ce7";
        public string UsageDisplay { get; set; } = string.Empty;
    }

    public class ForegroundStatsItem
    {
        public string Type { get; set; } = string.Empty;
        public string UsageTime { get; set; } = string.Empty;
    }

    public class TimelineItem
    {
        public string HourLabel { get; set; } = string.Empty;
        public string ProcessDisplay { get; set; } = string.Empty;
        public string DurationText { get; set; } = string.Empty;
        public long TotalMs { get; set; }
    }
}
