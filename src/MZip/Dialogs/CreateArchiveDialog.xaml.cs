using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MZip.Models;

namespace MZip.Dialogs
{
    public partial class CreateArchiveDialog : Window
    {
        private readonly ObservableCollection<string> _sources = new ObservableCollection<string>();
        private readonly AppSettings _settings;
        private readonly string? _currentArchivePath;
        private bool _targetManual;
        private bool _updatingTarget;

        public CreateArchiveOptions? Result { get; private set; }

        public CreateArchiveDialog(AppSettings settings, string? currentArchivePath)
        {
            InitializeComponent();
            _settings = settings;
            _currentArchivePath = currentArchivePath;
            SourcesList.ItemsSource = _sources;

            // 回填上次压缩参数（密码不记忆）
            var last = settings.LastCompress ?? new LastCompressOptions();
            string format = !string.IsNullOrEmpty(last.Format) ? last.Format : settings.LastCompressFormat;
            FormatCombo.SelectedIndex = format switch
            {
                "zip" => 1,
                "exe" => 2,
                _ => 0
            };
            LevelCombo.SelectedIndex = FindLevelIndex(last.Level);
            if (last.VolumeMB > 0) VolumeBox.Text = last.VolumeMB.ToString();
            SolidCheck.IsChecked = last.Solid;
            DeleteSourcesCheck.IsChecked = last.DeleteSources;
            EncryptNamesCheck.IsChecked = last.EncryptNames;

            UpdateFormatState();
            UpdateSourceSummary();
        }

        private string SelectedFormat => FormatCombo.SelectedIndex switch
        {
            1 => "zip",
            2 => "exe",
            _ => "7z"
        };

        private static string FormatExtension(string format) => format switch
        {
            "zip" => ".zip",
            "exe" => ".exe",
            _ => ".7z"
        };

        private int SelectedLevel
        {
            get
            {
                if (LevelCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    if (int.TryParse(item.Tag.ToString(), out int level)) return level;
                }
                return 5;
            }
        }

        private int FindLevelIndex(int level)
        {
            for (int i = 0; i < LevelCombo.Items.Count; i++)
            {
                if (LevelCombo.Items[i] is ComboBoxItem item && item.Tag != null
                    && int.TryParse(item.Tag.ToString(), out int value) && value == level)
                {
                    return i;
                }
            }
            return 3;
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "添加要压缩的文件",
                Filter = "所有文件|*.*",
                Multiselect = true,
                InitialDirectory = GetInitialDirectory()
            };
            if (dialog.ShowDialog(this) == true)
            {
                AddSources(dialog.FileNames);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "添加要压缩的文件夹",
                InitialDirectory = GetInitialDirectory()
            };
            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                AddSources(new[] { dialog.FolderName });
            }
        }

        private string GetInitialDirectory()
        {
            if (!string.IsNullOrEmpty(_currentArchivePath))
            {
                string? dir = Path.GetDirectoryName(_currentArchivePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            if (!string.IsNullOrEmpty(_settings.LastOutputDirectory) && Directory.Exists(_settings.LastOutputDirectory))
            {
                return _settings.LastOutputDirectory;
            }
            return AppContext.BaseDirectory;
        }

        private void AddSources(string[] paths)
        {
            foreach (string path in paths)
            {
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                if (_sources.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) continue;
                _sources.Add(path);
            }
            UpdateSourceSummary();
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SourcesList.SelectedItems.Count == 0) return;
            foreach (object? item in SourcesList.SelectedItems.Cast<object>().ToList())
            {
                if (item is string path) _sources.Remove(path);
            }
            UpdateSourceSummary();
        }

        private void ClearSources_Click(object sender, RoutedEventArgs e)
        {
            _sources.Clear();
            UpdateSourceSummary();
        }

        private void UpdateSourceSummary()
        {
            SourceCountText.Text = _sources.Count == 0 ? "尚未添加" : $"{_sources.Count} 项";
            if (!_targetManual)
            {
                UpdateSuggestedTarget();
            }
        }

        private void UpdateSuggestedTarget()
        {
            if (_sources.Count == 0)
            {
                SetTarget(string.Empty);
                return;
            }

            string first = _sources[0];
            string directory = Path.GetDirectoryName(first.TrimEnd('\\', '/')) ?? GetInitialDirectory();
            string name = _sources.Count > 1
                ? "新建压缩包"
                : (Directory.Exists(first) ? new DirectoryInfo(first).Name : Path.GetFileNameWithoutExtension(first));
            if (string.IsNullOrWhiteSpace(name)) name = "新建压缩包";

            string extension = FormatExtension(SelectedFormat);
            SetTarget(Path.Combine(directory, name + extension));
        }

        private void SetTarget(string path)
        {
            _updatingTarget = true;
            TargetBox.Text = path;
            TargetBox.CaretIndex = path.Length;
            _updatingTarget = false;
        }

        private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingTarget) return;
            _targetManual = true;
            ErrorText.Text = string.Empty;
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormatState();
            if (_targetManual) return;

            string extension = FormatExtension(SelectedFormat);
            string target = TargetBox.Text;
            if (!string.IsNullOrEmpty(target) && Path.GetExtension(target).ToLowerInvariant() != extension)
            {
                SetTarget(Path.ChangeExtension(target, extension));
            }
        }

        private void UpdateFormatState()
        {
            if (FormatCombo == null) return;
            string format = SelectedFormat;
            bool is7z = format == "7z";
            bool isExe = format == "exe";
            bool isZip = format == "zip";

            // 7Z 内核的格式支持固实与加密文件名
            EncryptNamesCheck.IsEnabled = is7z || isExe;
            if (!EncryptNamesCheck.IsEnabled) EncryptNamesCheck.IsChecked = false;
            SolidCheck.IsEnabled = is7z || isExe;
            if (!SolidCheck.IsEnabled) SolidCheck.IsChecked = false;

            // EXE 不支持分卷
            VolumeBox.IsEnabled = !isExe;
            if (isExe) VolumeBox.Text = string.Empty;

            // 仅 ZIP 支持注释
            CommentBox.IsEnabled = isZip;
            CommentLabel.IsEnabled = isZip;
            if (!isZip) CommentBox.Text = string.Empty;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            string format = SelectedFormat;
            string extension = FormatExtension(format);
            var dialog = new SaveFileDialog
            {
                Title = "保存压缩包",
                Filter = format switch
                {
                    "zip" => "ZIP 压缩包|*.zip",
                    "exe" => "自解压程序|*.exe",
                    _ => "7Z 压缩包|*.7z"
                },
                DefaultExt = extension,
                AddExtension = true
            };

            string current = TargetBox.Text.Trim();
            if (!string.IsNullOrEmpty(current))
            {
                dialog.FileName = Path.GetFileName(current);
                string? dir = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
            }
            else
            {
                dialog.InitialDirectory = GetInitialDirectory();
            }

            if (dialog.ShowDialog(this) == true)
            {
                SetTarget(dialog.FileName);
                _targetManual = true;
                ErrorText.Text = string.Empty;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            if (_sources.Count == 0)
            {
                ErrorText.Text = "请至少添加一个文件或文件夹。";
                return;
            }

            string target = TargetBox.Text.Trim().Trim('"');
            if (string.IsNullOrEmpty(target))
            {
                ErrorText.Text = "请选择压缩包保存位置。";
                return;
            }

            string format = SelectedFormat;
            string extension = FormatExtension(format);
            try
            {
                target = Path.GetFullPath(target);
                if (Path.GetExtension(target).ToLowerInvariant() != extension)
                {
                    target = Path.ChangeExtension(target, extension);
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = "保存路径无效：" + ex.Message;
                return;
            }

            string? targetDirectory = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(targetDirectory))
            {
                ErrorText.Text = "保存路径无效，请重新选择。";
                return;
            }

            foreach (string source in _sources)
            {
                if (Directory.Exists(source) && IsPathInside(targetDirectory, source))
                {
                    ErrorText.Text = "保存位置不能位于要压缩的文件夹内部，请更换保存位置。";
                    return;
                }
            }

            int volumeMB = 0;
            string volumeText = VolumeBox.Text.Trim();
            if (format != "exe" && volumeText.Length > 0)
            {
                if (!int.TryParse(volumeText, out volumeMB) || volumeMB < 1 || volumeMB > 102400)
                {
                    ErrorText.Text = "分卷大小无效，请填写 1 ~ 102400 之间的整数（MB），留空表示不分卷。";
                    return;
                }
            }

            Result = new CreateArchiveOptions
            {
                Format = format,
                Level = SelectedLevel,
                Password = PwdBox.Password,
                EncryptNames = (format == "7z" || format == "exe") && EncryptNamesCheck.IsChecked == true,
                VolumeMB = volumeMB,
                Solid = (format == "7z" || format == "exe") && SolidCheck.IsChecked == true,
                Comment = format == "zip" ? CommentBox.Text.Trim() : string.Empty,
                DeleteSources = DeleteSourcesCheck.IsChecked == true,
                TargetPath = target,
                Sources = _sources.ToList()
            };

            // 记忆本次参数（密码除外）
            _settings.LastCompress = new LastCompressOptions
            {
                Format = format,
                Level = SelectedLevel,
                VolumeMB = volumeMB,
                Solid = Result.Solid,
                DeleteSources = Result.DeleteSources,
                EncryptNames = Result.EncryptNames
            };
            _settings.LastCompressFormat = format;

            DialogResult = true;
        }

        /// <summary>
        /// 供命令行 /add 入口预填来源路径
        /// </summary>
        public void AddSourcePaths(IEnumerable<string> paths)
        {
            AddSources(paths.ToArray());
        }

        private static bool IsPathInside(string childPath, string parentDirectory)
        {
            try
            {
                string fullChild = Path.GetFullPath(childPath).TrimEnd('\\', '/');
                string fullParent = Path.GetFullPath(parentDirectory).TrimEnd('\\', '/');
                return fullChild.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fullChild, fullParent, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
