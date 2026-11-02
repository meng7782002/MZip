using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using QuickZip.Services;

namespace QuickZip;

public partial class App : Application
{
    public static string? StartupArchivePath { get; private set; }

    /// <summary>右键“使用 QuickZip 压缩”收集到的来源路径</summary>
    public static List<string>? StartupCompressPaths { get; private set; }

    /// <summary>右键“解压到本目录”的压缩包路径</summary>
    public static string? StartupExtractHerePath { get; private set; }

    /// <summary>右键“解压到指定目录…”的压缩包路径</summary>
    public static string? StartupExtractToPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogHelper.Initialize();
        LogHelper.Info("========== QuickZip 启动 ==========");
        LogHelper.Info("程序目录：" + AppContext.BaseDirectory);

        bool exitAfterCollect = false;

        if (e.Args.Length > 0)
        {
            LogHelper.Info("启动参数：" + string.Join(" | ", e.Args));
            switch (e.Args[0])
            {
                case "--compress":
                    List<string> sources = e.Args.Skip(1).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                    if (sources.Count == 0)
                    {
                        LogHelper.Warn("右键压缩：未收到任何路径，程序退出");
                        exitAfterCollect = true;
                        break;
                    }
                    // 多选时资源管理器会为每个条目启动一个进程，这里汇总到首个实例
                    List<string>? collected = ShellCommandCollector.Collect(sources);
                    if (collected == null)
                    {
                        exitAfterCollect = true;
                        break;
                    }
                    StartupCompressPaths = collected;
                    break;
                case "--extract-here":
                    StartupExtractHerePath = e.Args.Length > 1 ? e.Args[1] : null;
                    break;
                case "--extract-to":
                    StartupExtractToPath = e.Args.Length > 1 ? e.Args[1] : null;
                    break;

                default:
                    StartupArchivePath = e.Args[0];
                    break;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (exitAfterCollect)
        {
            // 辅助实例不创建主窗口，转发完成后立即退出
            LogHelper.Info("辅助实例已完成路径转发，退出");
            Shutdown();
            return;
        }

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
