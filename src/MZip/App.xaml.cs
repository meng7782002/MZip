using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MZip.Services;

namespace MZip;

public partial class App : Application
{
    public static string? StartupArchivePath { get; private set; }

    /// <summary>右键压缩类命令收集到的来源路径（--compress、/zip、/7zsolid）</summary>
    public static List<string>? StartupCompressPaths { get; private set; }

    /// <summary>右键压缩模式：default（按上次格式）/ zip / 7zsolid</summary>
    public static string StartupQuickMode { get; private set; } = "default";

    /// <summary>右键“添加到压缩文件…”收集到的来源路径（弹新建压缩对话框）</summary>
    public static List<string>? StartupAddPaths { get; private set; }

    /// <summary>右键批量解压收集到的压缩包路径</summary>
    public static List<string>? StartupBatchPaths { get; private set; }

    /// <summary>右键“解压到本目录”的压缩包路径</summary>
    public static string? StartupExtractHerePath { get; private set; }

    /// <summary>右键“解压到指定目录…”的压缩包路径</summary>
    public static string? StartupExtractToPath { get; private set; }

    /// <summary>右键“使用 MZip 修复”的压缩包路径</summary>
    public static string? StartupRepairPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogHelper.Initialize();
        // 注册代码页编码提供程序，支持 GBK/Big5/Shift-JIS 等历史编码（修复、文件名解码使用）
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        LogHelper.Info("========== MZip 启动 ==========");
        LogHelper.Info("程序目录：" + AppContext.BaseDirectory);

        bool exitAfterCollect = false;

        if (e.Args.Length > 0)
        {
            LogHelper.Info("启动参数：" + string.Join(" | ", e.Args));
            // 同时兼容 /cmd 与 --cmd 两种前缀
            string command = e.Args[0].TrimStart('/', '-').ToLowerInvariant();
            switch (command)
            {
                case "compress":
                case "zip":
                case "7zsolid":
                case "add":
                case "batch":
                {
                    List<string> sources = e.Args.Skip(1).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                    if (sources.Count == 0)
                    {
                        LogHelper.Warn("右键命令 " + command + "：未收到任何路径，程序退出");
                        exitAfterCollect = true;
                        break;
                    }
                    // 多选时资源管理器会为每个条目启动一个进程，这里按命令隔离汇总到首个实例
                    ShellCommandCollector.SetInstanceKey(command);
                    List<string>? collected = ShellCommandCollector.Collect(sources);
                    if (collected == null)
                    {
                        exitAfterCollect = true;
                        break;
                    }
                    switch (command)
                    {
                        case "add":
                            StartupAddPaths = collected;
                            break;
                        case "batch":
                            StartupBatchPaths = collected;
                            break;
                        default:
                            StartupCompressPaths = collected;
                            StartupQuickMode = command == "compress" ? "default" : command;
                            break;
                    }
                    break;
                }
                case "extract-here":
                case "extracthere":
                    StartupExtractHerePath = e.Args.Length > 1 ? e.Args[1] : null;
                    break;
                case "extract-to":
                case "extractto":
                    StartupExtractToPath = e.Args.Length > 1 ? e.Args[1] : null;
                    break;
                case "repair":
                    StartupRepairPath = e.Args.Length > 1 ? e.Args[1] : null;
                    break;
                case "register":
                case "unregister":
                    ApplyShellIntegration(string.Equals(command, "register", StringComparison.OrdinalIgnoreCase));
                    exitAfterCollect = true;
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
            // 辅助实例/静默注册实例不创建主窗口，处理完成后立即退出。
            // 此时 WPF 尚未进入消息循环，Shutdown() 不会结束进程，必须直接退出。
            LogHelper.Info("无需主窗口，程序退出");
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>安装/卸载时静默注册或清理文件关联与右键菜单，异常不影响安装流程</summary>
    private static void ApplyShellIntegration(bool register)
    {
        try
        {
            var shell = new ShellIntegrationService();
            bool cascaded = new SettingsService().Current.CascadedMenu;
            if (register)
            {
                shell.RegisterFileAssociation();
                shell.RegisterContextMenu(cascaded);
            }
            else
            {
                shell.UnregisterContextMenu();
                shell.UnregisterFileAssociation();
            }
            shell.NotifyShellChanged();
            LogHelper.Info(register
                ? "静默注册完成：文件关联与右键菜单已写入（级联=" + cascaded + "）"
                : "静默反注册完成：文件关联与右键菜单已移除");
        }
        catch (Exception ex)
        {
            // 注册失败不阻断安装/卸载，用户可在设置页手动重试
            LogHelper.Error(register ? "静默注册失败" : "静默反注册失败", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogHelper.Error("界面线程未处理异常", e.Exception);
        MessageBox.Show("程序发生异常：" + e.Exception.Message + "\n\n详细信息已写入日志。", "MZip",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogHelper.Error("后台任务未处理异常", e.Exception);
        e.SetObserved();
    }
}
