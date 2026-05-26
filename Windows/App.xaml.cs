using System.Windows;

namespace TimeTracker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 全局未捕获异常处理 — 防止静默崩溃
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"应用发生错误:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    MessageBox.Show($"后台线程错误:\n{ex.Message}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
                Console.WriteLine($"Unobserved task exception: {args.Exception?.Message}");
            };
        }
    }
}