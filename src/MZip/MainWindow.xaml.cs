using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MZip.Dialogs;
using MZip.Models;
using MZip.Services;

namespace MZip;

public partial class MainWindow : Window
{
    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso", ".cab", ".jar", ".apk", ".001"
    };

    private const string OpenFileFilter =
        "压缩包文件|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.iso;*.cab;*.jar;*.apk;*.001|所有文件|*.*";

    private readonly SevenZipEngine _engine = new SevenZipEngine();
    private readonly SettingsService _settingsService = new SettingsService();
    private readonly ObservableCollection<ArchiveItem> _items = new ObservableCollection<ArchiveItem>();

    private CancellationTokenSource? _cts;
    private string? _archivePath;
    private string? _password;
    private ArchiveInfo? _archiveInfo;
    private bool _busy;

    // 程序内部剪贴板：复制条目及来源压缩包信息
    private List<string>? _copiedEntries;
    private string? _copiedArchivePath;
    private string? _copiedPassword;

    private AppSettings Settings => _settingsService.Current;

    public MainWindow()
    {
        InitializeComponent();
        EntriesGrid.ItemsSource = _items;
        UpdateCommandState();
        UpdateEmptyState();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogHelper.Info("主窗口已加载");
        EngineText.Text = $"7-Zip {_engine.EngineVersion}";
        BuildRecentMenu();
        UpdateCommandState();

        if (!_engine.IsAvailable)
        {
            LogHelper.Error("压缩引擎缺失：" + _engine.SevenZipPath);
            MessageBox.Show(this, $"未找到压缩引擎 7z.exe。\n请确认程序 tools 目录完整。\n\n查找路径：{_engine.SevenZipPath}",
                "压缩引擎缺失", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (App.StartupCompressPaths is { Count: > 0 } compressPaths)
        {
            Dispatcher.BeginInvoke(new Action(async () => await QuickCompressAsync(compressPaths)));
            return;
        }

        if (!string.IsNullOrWhiteSpace(App.StartupExtractHerePath))
        {
            string path = App.StartupExtractHerePath!;
            Dispatcher.BeginInvoke(new Action(async () => await QuickExtractHereAsync(path)));
            return;
        }

        if (!string.IsNullOrWhiteSpace(App.StartupExtractToPath))
        {
            string path = App.StartupExtractToPath!;
            Dispatcher.BeginInvoke(new Action(async () => await QuickExtractToAsync(path)));
            return;
        }

        if (!string.IsNullOrWhiteSpace(App.StartupArchivePath) && File.Exists(App.StartupArchivePath))
        {
            string path = App.StartupArchivePath!;
            Dispatcher.BeginInvoke(new Action(async () => await OpenArchiveAsync(path)));
        }
    }

    // ==================== 打开 / 关闭 ====================

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog
        {
            Title = "打开压缩包",
            Filter = OpenFileFilter,
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenArchiveAsync(dialog.FileName);
        }
    }

    private async Task OpenArchiveAsync(string path, string? presetPassword = null)
    {
        if (_busy) return;

        string? password = presetPassword;
        while (true)
        {
            BeginBusy("正在读取压缩包…");
            try
            {
                ArchiveInfo info = await _engine.ListAsync(path, password, _cts!.Token);
                ApplyArchive(info, password);
                _settingsService.AddRecentArchive(path);
                BuildRecentMenu();
                SetStatus($"已打开：{info.FileCount} 个文件，{info.FolderCount} 个目录，压缩包 {FormatBytes(info.ArchiveSize)}");
                EndBusy();
                return;
            }
            catch (OperationCanceledException)
            {
                EndBusy();
                SetStatus("已取消");
                return;
            }
            catch (SevenZipException ex) when (ex.IsPasswordProblem)
            {
                EndBusy();
                var dialog = new PasswordDialog(Path.GetFileName(path), password != null) { Owner = this };
                if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.Password))
                {
                    SetStatus("已取消打开");
                    return;
                }
                password = dialog.Password;
            }
            catch (Exception ex)
            {
                EndBusy();
                LogHelper.Error("打开压缩包失败：" + path, ex);
                MessageBox.Show(this, ex.Message, "打开压缩包失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("打开失败");
                return;
            }
        }
    }

    private void ApplyArchive(ArchiveInfo info, string? password)
    {
        _archivePath = info.ArchivePath;
        _archiveInfo = info;
        _password = password;

        _items.Clear();
        foreach (ArchiveItem item in info.Entries)
        {
            _items.Add(item);
        }

        ArchiveNameText.Text = Path.GetFileName(info.ArchivePath);
        ArchivePathText.Text = info.ArchivePath;
        FormatText.Text = FormatDisplay(info.Format);
        FileCountText.Text = info.FileCount.ToString();
        FolderCountText.Text = info.FolderCount.ToString();
        TotalSizeText.Text = FormatBytes(info.TotalSize);
        PackedSizeText.Text = FormatBytes(info.ArchiveSize);
        RatioText.Text = info.RatioText;

        Title = $"{Path.GetFileName(info.ArchivePath)} - MZip";
        UpdateEmptyState();
        UpdateCommandState();
    }

    private async Task ReloadArchiveAsync()
    {
        if (string.IsNullOrEmpty(_archivePath)) return;
        await OpenArchiveAsync(_archivePath, _password);
    }

    private void CloseArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        CloseArchive();
    }

    private void CloseArchive()
    {
        _archivePath = null;
        _archiveInfo = null;
        _password = null;
        _items.Clear();
        Title = "MZip 压缩解压工具";
        UpdateEmptyState();
        UpdateCommandState();
        SetStatus("就绪");
        LogHelper.Info("已关闭压缩包");
    }

    private void UpdateEmptyState()
    {
        bool hasArchive = _archivePath != null;
        InfoCard.Visibility = hasArchive ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasArchive || _busy ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==================== 解压 ====================

    private async void ExtractAll_Click(object sender, RoutedEventArgs e)
    {
        await ExtractAsync(null);
    }

    private async void ExtractSelected_Click(object sender, RoutedEventArgs e)
    {
        List<ArchiveItem> selected = GetSelectedEntries();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在列表中选择要解压的条目。", "解压选中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ExtractAsync(selected);
    }

    private async Task ExtractAsync(List<ArchiveItem>? selectedItems)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath)) return;

        string defaultDirectory = GetDefaultOutputDirectory();
        try
        {
            Directory.CreateDirectory(defaultDirectory);
        }
        catch (Exception ex)
        {
            LogHelper.Error("创建默认解压目录失败：" + defaultDirectory, ex);
        }

        var dialog = new OpenFolderDialog
        {
            Title = selectedItems == null ? "解压全部到" : "解压选中到",
            InitialDirectory = defaultDirectory
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        string outputDirectory = dialog.FolderName;
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            LogHelper.Error("创建解压目录失败：" + outputDirectory, ex);
            MessageBox.Show(this, "无法创建解压目录：" + ex.Message, "解压", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string mode = Settings.OverwriteMode;
        if (mode == "Ask")
        {
            var overwriteDialog = new OverwriteDialog { Owner = this };
            if (overwriteDialog.ShowDialog() != true)
            {
                return;
            }
            mode = overwriteDialog.Mode;
        }

        List<string>? entries = selectedItems?.Select(i => i.FullPath).ToList();
        await RunExtractionAsync(outputDirectory, entries, selectedItems?.Count, mode);
    }

    /// <summary>执行解压（含密码重试、进度、取消），供界面解压、右键解压与粘贴复用</summary>
    /// <param name="sourceArchivePath">来源压缩包；为空时使用当前打开的压缩包</param>
    /// <param name="presetPassword">来源压缩包对应的密码（粘贴其他压缩包条目时使用）</param>
    private async Task RunExtractionAsync(string outputDirectory, List<string>? entries, int? selectedCount, string mode,
        string? sourceArchivePath = null, string? presetPassword = null)
    {
        string archivePath = sourceArchivePath ?? _archivePath ?? string.Empty;
        if (string.IsNullOrEmpty(archivePath))
        {
            LogHelper.Warn("解压失败：未指定来源压缩包");
            return;
        }

        var progress = new Progress<OperationProgress>(OnProgress);
        string? password = presetPassword ?? (sourceArchivePath == null ? _password : null);

        while (true)
        {
            BeginBusy(selectedCount == null ? "正在解压全部内容…" : $"正在解压 {selectedCount} 个条目…");
            try
            {
                await _engine.ExtractAsync(archivePath, outputDirectory, password, entries, mode, progress, _cts!.Token);
                if (sourceArchivePath == null)
                {
                    _password = password;
                }
                Settings.LastOutputDirectory = outputDirectory;
                _settingsService.Save();
                EndBusy();
                SetStatus("解压完成：" + outputDirectory);
                LogHelper.Info($"解压完成，输出目录：{outputDirectory}（来源 {archivePath}）");

                MessageBoxResult ask = MessageBox.Show(this, "解压完成，是否打开输出目录？", "解压完成",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (ask == MessageBoxResult.Yes)
                {
                    OpenInExplorer(outputDirectory);
                }
                return;
            }
            catch (OperationCanceledException)
            {
                EndBusy();
                SetStatus("已取消解压");
                LogHelper.Info("用户取消解压");
                return;
            }
            catch (SevenZipException ex) when (ex.IsPasswordProblem)
            {
                EndBusy();
                var passwordDialog = new PasswordDialog(Path.GetFileName(archivePath), password != null) { Owner = this };
                if (passwordDialog.ShowDialog() != true || string.IsNullOrEmpty(passwordDialog.Password))
                {
                    SetStatus("已取消解压");
                    return;
                }
                password = passwordDialog.Password;
            }
            catch (Exception ex)
            {
                EndBusy();
                LogHelper.Error("解压失败：" + archivePath, ex);
                MessageBox.Show(this, ex.Message, "解压失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("解压失败");
                return;
            }
        }
    }

    private string GetDefaultOutputDirectory()
    {
        string baseDirectory = Path.GetDirectoryName(_archivePath) ?? AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(Settings.LastOutputDirectory) && Directory.Exists(Settings.LastOutputDirectory))
        {
            baseDirectory = Settings.LastOutputDirectory;
        }
        string name = Path.GetFileNameWithoutExtension(_archivePath!);
        if (string.IsNullOrWhiteSpace(name)) name = "解压结果";
        return Path.Combine(baseDirectory, name);
    }

    // ==================== Windows 右键菜单入口 ====================

    /// <summary>右键“使用 MZip 压缩”：在来源所在目录生成压缩包并打开</summary>
    private async Task QuickCompressAsync(IReadOnlyList<string> sourcePaths)
    {
        if (_busy) return;

        List<string> sources = sourcePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sources.Count == 0)
        {
            LogHelper.Warn("右键压缩：来源均不存在，已取消");
            SetStatus("没有可压缩的文件");
            return;
        }

        string first = sources[0];
        string? parentDirectory = Directory.Exists(first)
            ? Path.GetDirectoryName(first.TrimEnd(Path.DirectorySeparatorChar))
            : Path.GetDirectoryName(first);
        if (string.IsNullOrWhiteSpace(parentDirectory)) parentDirectory = AppContext.BaseDirectory;

        string name;
        if (sources.Count == 1)
        {
            name = Directory.Exists(first)
                ? new DirectoryInfo(first).Name
                : Path.GetFileNameWithoutExtension(first);
        }
        else
        {
            name = new DirectoryInfo(first).Parent?.Name ?? "压缩包";
        }
        if (string.IsNullOrWhiteSpace(name)) name = "压缩包";

        string format = string.Equals(Settings.LastCompressFormat, "zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "7z";
        // 记忆的格式可能是 exe，右键快速压缩固定用 zip/7z
        string targetPath = BuildUniqueFilePath(Path.Combine(parentDirectory, name + "." + format));

        var options = new CreateArchiveOptions
        {
            Format = format,
            Level = Settings.CompressionLevel,
            TargetPath = targetPath,
            Sources = sources
        };

        LogHelper.Info($"右键压缩：{sources.Count} 个来源 → {targetPath}");
        var progress = new Progress<OperationProgress>(OnProgress);
        BeginBusy("正在创建压缩包…");
        try
        {
            await _engine.CreateAsync(options, progress, _cts!.Token);
            EndBusy();
            SetStatus("压缩包创建完成：" + targetPath);
            LogHelper.Info("右键压缩完成：" + targetPath);
            await OpenArchiveAsync(targetPath);
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            SetStatus("已取消压缩");
            LogHelper.Info("用户取消右键压缩");
        }
        catch (Exception ex)
        {
            EndBusy();
            LogHelper.Error("右键压缩失败：" + targetPath, ex);
            MessageBox.Show(this, ex.Message, "创建压缩包失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("创建压缩包失败");
        }
    }

    /// <summary>右键“解压到本目录”：解压到压缩包所在目录</summary>
    private async Task QuickExtractHereAsync(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            LogHelper.Warn("右键解压：文件不存在 " + archivePath);
            MessageBox.Show(this, "找不到压缩包文件：\n" + archivePath, "解压", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await OpenArchiveAsync(archivePath);
        if (string.IsNullOrEmpty(_archivePath)) return;

        string archiveDirectory = Path.GetDirectoryName(_archivePath) ?? AppContext.BaseDirectory;
        string folderName = Path.GetFileNameWithoutExtension(_archivePath);
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = Path.GetFileName(_archivePath);
        }
        string outputDirectory = Path.Combine(archiveDirectory, folderName);

        if (File.Exists(outputDirectory))
        {
            LogHelper.Warn("右键解压：目标文件夹路径已被文件占用 " + outputDirectory);
            MessageBox.Show(this, $"无法创建解压文件夹，同名文件已存在：\n{outputDirectory}", "解压",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            LogHelper.Error("创建解压文件夹失败：" + outputDirectory, ex);
            MessageBox.Show(this, "创建解压文件夹失败：" + ex.Message, "解压",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string mode = Settings.OverwriteMode;
        if (string.Equals(mode, "Ask", StringComparison.OrdinalIgnoreCase))
        {
            // 一键解压不打断用户，覆盖策略为“询问”时按直接覆盖处理
            mode = "Overwrite";
        }
        LogHelper.Info($"右键解压到本目录：{_archivePath} → {outputDirectory}（覆盖策略 {mode}）");
        await RunExtractionAsync(outputDirectory, null, null, mode);
    }

    /// <summary>右键“解压到指定目录…”：打开压缩包后选择目录并解压</summary>
    private async Task QuickExtractToAsync(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            LogHelper.Warn("右键解压：文件不存在 " + archivePath);
            MessageBox.Show(this, "找不到压缩包文件：\n" + archivePath, "解压", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await OpenArchiveAsync(archivePath);
        if (string.IsNullOrEmpty(_archivePath)) return;
        await ExtractAsync(null);
    }

    /// <summary>同名文件已存在时追加 (1)、(2) 等序号</summary>
    private static string BuildUniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    // ==================== 新建 / 添加 / 删除 ====================

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new CreateArchiveDialog(Settings, _archivePath) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;

        CreateArchiveOptions options = dialog.Result;
        if (File.Exists(options.TargetPath))
        {
            MessageBoxResult confirm = MessageBox.Show(this,
                $"文件已存在：\n{options.TargetPath}\n\n继续将覆盖该文件，是否继续？",
                "新建压缩包", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var progress = new Progress<OperationProgress>(OnProgress);
        BeginBusy("正在创建压缩包…");
        try
        {
            if (options.SelfExtracting)
            {
                var sfx = new SfxService(_engine);
                await sfx.CreateSfxAsync(options, progress, _cts!.Token);
            }
            else
            {
                await _engine.CreateAsync(options, progress, _cts!.Token);
            }
            Settings.LastCompressFormat = options.Format == "exe" ? "7z" : options.Format;
            Settings.CompressionLevel = options.Level;
            _settingsService.Save();

            if (options.DeleteSources)
            {
                await DeleteSourcesAfterCompressAsync(options, progress);
            }

            EndBusy();
            SetStatus("压缩包创建完成：" + options.TargetPath);

            string openPath = options.TargetPath;
            if (options.VolumeMB > 0 && !File.Exists(openPath) && File.Exists(openPath + ".001"))
            {
                openPath += ".001";
            }
            await OpenArchiveAsync(openPath, string.IsNullOrEmpty(options.Password) ? null : options.Password);
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            SetStatus("已取消压缩");
            LogHelper.Info("用户取消创建压缩包");
        }
        catch (Exception ex)
        {
            EndBusy();
            LogHelper.Error("创建压缩包失败：" + options.TargetPath, ex);
            MessageBox.Show(this, ex.Message, "创建压缩包失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("创建压缩包失败");
        }
    }

    /// <summary>
    /// 压缩成功且校验通过后删除源文件（FR-94）
    /// </summary>
    private async Task DeleteSourcesAfterCompressAsync(CreateArchiveOptions options, IProgress<OperationProgress> progress)
    {
        string testPath = options.TargetPath;
        if (options.VolumeMB > 0 && !File.Exists(testPath) && File.Exists(testPath + ".001"))
        {
            testPath += ".001";
        }

        ArchiveTestResult test = await _engine.TestAsync(testPath,
            string.IsNullOrEmpty(options.Password) ? null : options.Password, progress, _cts!.Token);
        if (!test.Success)
        {
            LogHelper.Warn("压缩后校验未通过，保留源文件：" + test.Message);
            MessageBox.Show(this, "压缩包校验未通过，已保留源文件。\n" + test.Message,
                "删除源文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var failed = new List<string>();
        foreach (string source in options.Sources)
        {
            try
            {
                if (File.Exists(source)) File.Delete(source);
                else if (Directory.Exists(source)) Directory.Delete(source, true);
                LogHelper.Info("压缩后删除源文件：" + source);
            }
            catch (Exception ex)
            {
                failed.Add(source);
                LogHelper.Error("删除源文件失败：" + source, ex);
            }
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this, "以下源文件删除失败，请手动处理：\n" + string.Join("\n", failed),
                "删除源文件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CanEditArchive()) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择要添加到压缩包的文件",
            Filter = "所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await AddSourcesAsync(dialog.FileNames);
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CanEditArchive()) return;
        var dialog = new OpenFolderDialog { Title = "选择要添加到压缩包的文件夹" };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            await AddSourcesAsync(new[] { dialog.FolderName });
        }
    }

    private async Task AddSourcesAsync(IEnumerable<string> sourcePaths)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath) || _archiveInfo == null) return;

        List<string> sources = sourcePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sources.Count == 0) return;
        if (!CanEditArchive())
        {
            MessageBox.Show(this, "当前压缩包格式不支持添加（仅支持 ZIP / 7Z）。", "添加文件",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var progress = new Progress<OperationProgress>(OnProgress);
        BeginBusy($"正在向压缩包添加 {sources.Count} 项…");
        try
        {
            await _engine.AddAsync(_archivePath!, _archiveInfo.Format, sources, _password, Settings.CompressionLevel, progress, _cts!.Token);
            EndBusy();
            SetStatus("添加完成，正在刷新列表…");
            await ReloadArchiveAsync();
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            SetStatus("已取消添加");
            LogHelper.Info("用户取消添加文件");
        }
        catch (Exception ex)
        {
            EndBusy();
            LogHelper.Error("添加文件失败：" + _archivePath, ex);
            MessageBox.Show(this, ex.Message, "添加文件失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("添加失败");
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath)) return;
        if (!CanEditArchive())
        {
            MessageBox.Show(this, "当前压缩包格式不支持删除（仅支持 ZIP / 7Z）。", "删除条目",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        List<ArchiveItem> selected = GetSelectedEntries();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在列表中选择要删除的条目。", "删除条目",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(this,
            $"确定要从压缩包中删除选中的 {selected.Count} 个条目吗？此操作不可撤销。",
            "删除条目", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        BeginBusy("正在删除条目…");
        try
        {
            await _engine.DeleteAsync(_archivePath!, _password, selected.Select(i => i.FullPath).ToList(), _cts!.Token);
            EndBusy();
            SetStatus("删除完成，正在刷新列表…");
            await ReloadArchiveAsync();
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            SetStatus("已取消删除");
        }
        catch (Exception ex)
        {
            EndBusy();
            LogHelper.Error("删除条目失败：" + _archivePath, ex);
            MessageBox.Show(this, ex.Message, "删除条目失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("删除失败");
        }
    }

    // ==================== 测试 / 刷新 ====================

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath)) return;

        var progress = new Progress<OperationProgress>(OnProgress);
        BeginBusy("正在校验压缩包…");
        try
        {
            ArchiveTestResult result = await _engine.TestAsync(_archivePath!, _password, progress, _cts!.Token);
            EndBusy();
            SetStatus(result.Message);
            MessageBox.Show(this, result.Message, result.Success ? "校验通过" : "校验失败",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            SetStatus("已取消校验");
        }
        catch (Exception ex)
        {
            EndBusy();
            LogHelper.Error("测试压缩包失败：" + _archivePath, ex);
            MessageBox.Show(this, ex.Message, "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("校验失败");
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath)) return;
        await ReloadArchiveAsync();
    }

    // ==================== 其他界面事件 ====================

    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandState();
    }

    private async void EntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy) return;
        if (EntriesGrid.SelectedItems.Count > 0)
        {
            await ExtractAsync(GetSelectedEntries());
        }
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        List<ArchiveItem> selected = GetSelectedEntries();
        if (selected.Count == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(i => i.FullPath)));
            SetStatus("已复制条目路径");
        }
        catch (Exception ex)
        {
            LogHelper.Error("复制到剪贴板失败", ex);
        }
    }

    // ==================== 复制 / 粘贴 ====================

    private void SelectAllEntries_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _archivePath == null) return;
        EntriesGrid.SelectAll();
        SetStatus($"已全选 {_items.Count} 个条目");
    }

    private void CopyEntries_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_archivePath)) return;

        List<ArchiveItem> selected = GetSelectedEntries();
        if (selected.Count == 0)
        {
            SetStatus("请先选择要复制的条目");
            return;
        }

        _copiedEntries = selected.Select(i => i.FullPath).ToList();
        _copiedArchivePath = _archivePath;
        _copiedPassword = _password;
        SetStatus($"已复制 {selected.Count} 个条目，按 Ctrl+V 可粘贴到指定文件夹");
        LogHelper.Info($"复制条目：{selected.Count} 个，来源 {_archivePath}");
    }

    private async void PasteTo_Click(object sender, RoutedEventArgs e)
    {
        await PasteToAsync();
    }

    /// <summary>把内部剪贴板中的条目解压到用户选择的文件夹</summary>
    private async Task PasteToAsync()
    {
        LogHelper.Info($"粘贴请求：条目 {_copiedEntries?.Count ?? 0} 个");
        if (_busy) return;

        if (_copiedEntries == null || _copiedEntries.Count == 0 || string.IsNullOrEmpty(_copiedArchivePath))
        {
            MessageBox.Show(this, "剪贴板中没有压缩包条目。\n请先在压缩包中选中条目并复制。", "粘贴",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(_copiedArchivePath))
        {
            LogHelper.Warn("粘贴失败：来源压缩包不存在 " + _copiedArchivePath);
            MessageBox.Show(this, $"来源压缩包已不存在：\n{_copiedArchivePath}", "粘贴",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string initialDirectory = Settings.LastOutputDirectory;
        if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
        {
            initialDirectory = Path.GetDirectoryName(_copiedArchivePath) ?? AppContext.BaseDirectory;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "粘贴到文件夹",
            InitialDirectory = initialDirectory
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;

        string outputDirectory = dialog.FolderName;
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            LogHelper.Error("创建粘贴目标目录失败：" + outputDirectory, ex);
            MessageBox.Show(this, "无法创建目标目录：" + ex.Message, "粘贴",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string mode = Settings.OverwriteMode;
        if (mode == "Ask")
        {
            var overwriteDialog = new OverwriteDialog { Owner = this };
            if (overwriteDialog.ShowDialog() != true) return;
            mode = overwriteDialog.Mode;
        }

        LogHelper.Info($"粘贴条目：{_copiedArchivePath} → {outputDirectory}，共 {_copiedEntries.Count} 个，覆盖策略 {mode}");
        await RunExtractionAsync(outputDirectory, _copiedEntries, _copiedEntries.Count, mode, _copiedArchivePath, _copiedPassword);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts == null) return;
        LogHelper.Info("用户请求取消当前操作");
        SetStatus("正在取消…");
        _cts.Cancel();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new SettingsWindow(Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settingsService.Save();
            SetStatus("设置已保存");
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        LogHelper.Info("打开关于窗口");
        var dialog = new AboutWindow(_engine.EngineVersion) { Owner = this };
        dialog.ShowDialog();
    }

    private void BatchExtract_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        LogHelper.Info("打开批量解压窗口");
        var window = new BatchExtractWindow { Owner = this };
        window.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (!_busy) Open_Click(sender, e);
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.A && _archivePath != null && !_busy)
            {
                e.Handled = true;
                SelectAllEntries_Click(sender, e);
                return;
            }
            if (e.Key == Key.C && _archivePath != null && !_busy)
            {
                e.Handled = true;
                CopyEntries_Click(sender, e);
                return;
            }
            if (e.Key == Key.V && _copiedEntries is { Count: > 0 })
            {
                e.Handled = true;
                await PasteToAsync();
                return;
            }
        }
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (!_busy && _archivePath != null) await ReloadArchiveAsync();
            return;
        }
        if (e.Key == Key.Escape && _busy)
        {
            e.Handled = true;
            Cancel_Click(sender, e);
            return;
        }
        if (e.Key == Key.Delete && EntriesGrid.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            DeleteSelected_Click(sender, e);
            return;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            MessageBox.Show(this, "正在执行操作，请先取消当前操作或等待其完成后再退出。", "MZip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }
        LogHelper.Info("程序退出");
    }

    // ==================== 拖拽 ====================

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_busy || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }
        DropOverlay.Visibility = Visibility.Visible;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_busy || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_busy) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        if (string.IsNullOrEmpty(_archivePath))
        {
            string? archive = files.FirstOrDefault(IsArchiveFile);
            if (archive == null)
            {
                MessageBox.Show(this, "请拖入压缩包文件（支持 ZIP / 7Z / RAR 等格式）。", "打开压缩包",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await OpenArchiveAsync(archive);
            return;
        }

        if (!CanEditArchive())
        {
            MessageBox.Show(this, "当前压缩包格式不支持添加（仅支持 ZIP / 7Z）。", "添加文件",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await AddSourcesAsync(files);
    }

    // ==================== 状态与命令 ====================

    private void BeginBusy(string status)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        StatusText.Text = status;
        OperationProgressBar.Value = 0;
        OperationProgressBar.Visibility = Visibility.Visible;
        ProgressText.Text = string.Empty;
        ProgressText.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        UpdateEmptyState();
        UpdateCommandState();
    }

    private void EndBusy()
    {
        _busy = false;
        _cts?.Dispose();
        _cts = null;
        OperationProgressBar.Value = 0;
        OperationProgressBar.Visibility = Visibility.Collapsed;
        ProgressText.Text = string.Empty;
        ProgressText.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
        UpdateCommandState();
    }

    private void OnProgress(OperationProgress progress)
    {
        if (progress == null) return;
        OperationProgressBar.Value = Math.Min(100, Math.Max(0, progress.Percent));
        ProgressText.Text = $"{progress.Percent:0}%";
        if (!string.IsNullOrEmpty(progress.CurrentItem))
        {
            string stage = string.IsNullOrEmpty(progress.Stage) ? "处理中" : progress.Stage;
            SetStatus($"{stage}：{progress.CurrentItem}");
        }
    }

    private void UpdateCommandState()
    {
        bool hasArchive = _archivePath != null && _archiveInfo != null;
        bool canEdit = hasArchive && CanEditArchive();
        bool hasSelection = hasArchive && EntriesGrid.SelectedItems.Count > 0;

        MainMenu.IsEnabled = !_busy;
        ToolbarButtons.IsEnabled = !_busy;

        ExtractAllButton.IsEnabled = hasArchive;
        ExtractSelectedButton.IsEnabled = hasSelection;
        CreateArchiveButton.IsEnabled = true;
        AddButton.IsEnabled = canEdit && !_busy;
        DeleteButton.IsEnabled = canEdit && hasSelection && !_busy;
        TestButton.IsEnabled = hasArchive && !_busy;
        RefreshButton.IsEnabled = hasArchive && !_busy;

        EntriesGrid.IsEnabled = !_busy;
        EmptyState.IsHitTestVisible = !_busy;
        EmptyOpenButton.IsEnabled = !_busy;
    }

    private bool CanEditArchive()
    {
        if (_archiveInfo == null) return false;
        string format = _archiveInfo.Format;
        return format.Equals("7z", StringComparison.OrdinalIgnoreCase)
            || format.Equals("zip", StringComparison.OrdinalIgnoreCase);
    }

    private List<ArchiveItem> GetSelectedEntries()
    {
        return EntriesGrid.SelectedItems.Cast<ArchiveItem>().ToList();
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (Settings.RecentArchives.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "（暂无记录）", IsEnabled = false });
            return;
        }

        foreach (string path in Settings.RecentArchives.ToList())
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path
            };
            string captured = path;
            item.Click += async (_, _) =>
            {
                if (!File.Exists(captured))
                {
                    MessageBox.Show(this, "文件不存在：" + captured, "打开压缩包",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    _settingsService.RemoveRecentArchive(captured);
                    BuildRecentMenu();
                    return;
                }
                await OpenArchiveAsync(captured);
            };
            RecentMenu.Items.Add(item);
        }

        RecentMenu.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "清除最近记录" };
        clearItem.Click += (_, _) =>
        {
            Settings.RecentArchives.Clear();
            _settingsService.Save();
            BuildRecentMenu();
        };
        RecentMenu.Items.Add(clearItem);
    }

    private static bool IsArchiveFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return ArchiveExtensions.Contains(extension);
    }

    private static string FormatDisplay(string format)
    {
        if (string.IsNullOrEmpty(format)) return "-";
        string lower = format.ToLowerInvariant();
        return lower switch
        {
            "7z" => "7Z",
            "zip" => "ZIP",
            "rar5" => "RAR5",
            "rar" => "RAR",
            _ => format.ToUpperInvariant()
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogHelper.Error("打开资源管理器失败：" + path, ex);
        }
    }
}
