using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MZip.Models;
using MZip.Services;

namespace MZip.Dialogs
{
    /// <summary>
    /// 批量解压窗口（FR-95）：队列依次解压多个压缩包，单包失败不中断，完成后汇总
    /// </summary>
    public partial class BatchExtractWindow : Window
    {
        private static readonly string[] ArchiveExtensions =
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso", ".cab", ".jar", ".apk", ".001"
        };

        private readonly SevenZipEngine _engine = new SevenZipEngine();
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly ObservableCollection<BatchItem> _items = new ObservableCollection<BatchItem>();
        private CancellationTokenSource? _cts;
        private bool _running;

        public BatchExtractWindow()
        {
            InitializeComponent();
            PackageGrid.ItemsSource = _items;
            OverwriteCombo.SelectedIndex = 0;
        }

        public BatchExtractWindow(IEnumerable<string> archivePaths) : this()
        {
            AddPaths(archivePaths);
        }

        // ==================== 列表管理 ====================

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要批量解压的压缩包",
                Filter = "压缩包文件|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.iso;*.cab;*.jar;*.apk;*.001|所有文件|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                AddPaths(dialog.FileNames);
            }
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            int added = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                if (!IsArchiveFile(path)) continue;
                if (_items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;

                _items.Add(new BatchItem
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    Directory = Path.GetDirectoryName(path) ?? string.Empty,
                    Status = "等待"
                });
                added++;
            }

            if (added > 0)
            {
                LogHelper.Info($"批量解压：添加 {added} 个压缩包，当前共 {_items.Count} 个");
                ProgressText.Text = $"共 {_items.Count} 个压缩包";
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var selected = PackageGrid.SelectedItems.Cast<BatchItem>().ToList();
            foreach (BatchItem item in selected)
            {
                _items.Remove(item);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            ProgressText.Text = "就绪";
        }

        private void OutputMode_Changed(object sender, RoutedEventArgs e)
        {
            UnifiedDirBox.IsEnabled = UnifiedRadio.IsChecked == true;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "统一输出目录" };
            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                UnifiedDirBox.Text = dialog.FolderName;
                UnifiedRadio.IsChecked = true;
            }
        }

        // ==================== 拖拽 ====================

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (_running || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (_running || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (_running) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                AddPaths(files);
            }
        }

        // ==================== 队列执行 ====================

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "请先添加要解压的压缩包。", "批量解压", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? unifiedDir = null;
            if (UnifiedRadio.IsChecked == true)
            {
                unifiedDir = UnifiedDirBox.Text.Trim();
                if (string.IsNullOrEmpty(unifiedDir))
                {
                    MessageBox.Show(this, "请选择统一输出目录。", "批量解压", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            string mode = ((ComboBoxItem)OverwriteCombo.SelectedItem).Tag?.ToString() ?? "Overwrite";
            bool deleteSource = DeleteSourceCheck.IsChecked == true;

            LogHelper.Info($"批量解压开始：共 {_items.Count} 个，模式 {(unifiedDir == null ? "各包同名文件夹" : "统一目录 " + unifiedDir)}，覆盖 {mode}，删除源包 {deleteSource}");

            _running = true;
            _cts = new CancellationTokenSource();
            SetRunningState(true);

            var succeeded = new List<string>();
            var failed = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < _items.Count; i++)
            {
                if (_cts.IsCancellationRequested) break;

                BatchItem item = _items[i];
                item.Status = "解压中…";
                int index = i;
                var progress = new Progress<OperationProgress>(p =>
                {
                    double total = (index + Math.Clamp(p.Percent, 0, 100) / 100.0) / _items.Count * 100.0;
                    TotalProgressBar.Value = total;
                    PercentText.Text = $"{total:0}%";
                    ProgressText.Text = $"({index + 1}/{_items.Count}) {item.Name} {p.Percent:0}%";
                });

                try
                {
                    string outputDir = BuildOutputDirectory(item, unifiedDir);
                    await ExtractWithPasswordRetryAsync(item, outputDir, mode, progress);
                    item.Status = "成功";
                    succeeded.Add(item.FullPath);
                    LogHelper.Info($"批量解压成功：{item.FullPath} → {outputDir}");

                    if (deleteSource)
                    {
                        DeleteSourceArchive(item);
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = "已取消";
                    LogHelper.Info("批量解压：用户取消队列");
                    break;
                }
                catch (Exception ex)
                {
                    item.Status = "失败：" + ex.Message;
                    failed.Add(new KeyValuePair<string, string>(item.FullPath, ex.Message));
                    LogHelper.Error("批量解压失败：" + item.FullPath, ex);
                }
            }

            TotalProgressBar.Value = 100;
            _running = false;
            _cts?.Dispose();
            _cts = null;
            SetRunningState(false);

            string summary = $"批量解压完成：成功 {succeeded.Count} 个，失败 {failed.Count} 个。";
            if (failed.Count > 0)
            {
                summary += "\n\n失败清单：\n" + string.Join("\n", failed.Select(f => $"{Path.GetFileName(f.Key)}：{f.Value}"));
            }
            LogHelper.Info(summary.Replace("\n", " "));
            ProgressText.Text = summary.Split('\n')[0];
            MessageBox.Show(this, summary, "批量解压完成", MessageBoxButton.OK,
                failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        /// <summary>计算单个压缩包的输出目录（各包同名文件夹或统一目录下同名文件夹）</summary>
        private static string BuildOutputDirectory(BatchItem item, string? unifiedDir)
        {
            string baseDir = unifiedDir ?? item.Directory;
            string name = Path.GetFileNameWithoutExtension(item.Name);
            if (string.IsNullOrWhiteSpace(name)) name = "解压结果";
            // 分卷包 .001 去掉分卷扩展名
            if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
            string dir = Path.Combine(baseDir, name);
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>解压单个包，密码错误时弹窗重试，用户取消输入则中止该包（不中断队列）</summary>
        private async Task ExtractWithPasswordRetryAsync(BatchItem item, string outputDir, string mode,
            IProgress<OperationProgress> progress)
        {
            string? password = null;
            while (true)
            {
                try
                {
                    await _engine.ExtractAsync(item.FullPath, outputDir, password, null, mode, progress, _cts!.Token);
                    return;
                }
                catch (SevenZipException ex) when (ex.IsPasswordProblem)
                {
                    var dialog = new PasswordDialog(item.Name, password != null) { Owner = this };
                    if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.Password))
                    {
                        throw new SevenZipException("需要密码，用户取消输入。", true);
                    }
                    password = dialog.Password;
                }
            }
        }

        /// <summary>解压成功后删除源压缩包（含分卷序列），失败仅记录日志</summary>
        private void DeleteSourceArchive(BatchItem item)
        {
            try
            {
                File.Delete(item.FullPath);
                LogHelper.Info("批量解压后删除源压缩包：" + item.FullPath);

                // 分卷序列：x.7z.001 → 删除 x.7z.002..nnn
                if (item.FullPath.EndsWith(".001", StringComparison.OrdinalIgnoreCase))
                {
                    string basePath = item.FullPath.Substring(0, item.FullPath.Length - 4);
                    for (int v = 2; v <= 999; v++)
                    {
                        string vol = basePath + "." + v.ToString("000");
                        if (!File.Exists(vol)) break;
                        File.Delete(vol);
                        LogHelper.Info("批量解压后删除分卷：" + vol);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("批量解压后删除源压缩包失败：" + item.FullPath, ex);
            }
        }

        private void CancelOp_Click(object sender, RoutedEventArgs e)
        {
            if (_cts == null) return;
            LogHelper.Info("批量解压：用户请求取消");
            ProgressText.Text = "正在取消…";
            _cts.Cancel();
        }

        private void SetRunningState(bool running)
        {
            StartButton.IsEnabled = !running;
            CancelButton.IsEnabled = running;
            PackageGrid.IsEnabled = !running;
            UnifiedDirBox.IsEnabled = !running && UnifiedRadio.IsChecked == true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_running)
            {
                MessageBox.Show(this, "正在批量解压，请先取消或等待完成后再关闭。", "批量解压",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
            }
        }

        private static bool IsArchiveFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return ArchiveExtensions.Contains(extension);
        }

        private class BatchItem : INotifyPropertyChanged
        {
            public string FullPath { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Directory { get; set; } = string.Empty;

            private string _status = string.Empty;
            public string Status
            {
                get => _status;
                set
                {
                    _status = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
