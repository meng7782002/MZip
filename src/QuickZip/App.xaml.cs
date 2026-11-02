using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using QuickZip.Services;

namespace QuickZip;

public partial class App : Application
{
    public static string? StartupArchivePath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogHelper.Initialize();
        LogHelper.Info("========== QuickZip 启动 ==========");
        LogHelper.Info("程序目录：" + AppContext.BaseDirectory);

        if (e.Args.Length > 0)
        {
            StartupArchivePath = e.Args[0];
            LogHelper.Info("启动参数：" + StartupArchivePath);
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogHelper.Error("界面线程未处理异常", e.Exception);
        MessageBox.Show("程序发生异常：" + e.Exception.Message + "\n\n详细信息已写入日志。", "QuickZip",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogHelper.Error("后台任务未处理异常", e.Exception);
        e.SetObserved();
    }
}
