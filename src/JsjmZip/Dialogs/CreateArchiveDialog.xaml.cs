using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using JsjmZip.Models;

namespace JsjmZip.Dialogs
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

            FormatCombo.SelectedIndex = string.Equals(settings.LastCompressFormat, "zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LevelCombo.SelectedIndex = FindLevelIndex(settings.CompressionLevel);
            UpdateFormatState();
            UpdateSourceSummary();
        }

        private string SelectedFormat => FormatCombo.SelectedIndex == 1 ? "zip" : "7z";

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

            string extension = SelectedFormat == "zip" ? ".zip" : ".7z";
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

            string extension = SelectedFormat == "zip" ? ".zip" : ".7z";
            string target = TargetBox.Text;
            if (!string.IsNullOrEmpty(target) && Path.GetExtension(target).ToLowerInvariant() != extension)
            {
                SetTarget(Path.ChangeExtension(target, extension));
            }
        }

        private void UpdateFormatState()
        {
            bool is7z = SelectedFormat == "7z";
            EncryptNamesCheck.IsEnabled = is7z;
            if (!is7z) EncryptNamesCheck.IsChecked = false;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            string format = SelectedFormat;
            string extension = format == "zip" ? ".zip" : ".7z";
            var dialog = new SaveFileDialog
            {
                Title = "保存压缩包",
                Filter = format == "zip" ? "ZIP 压缩包|*.zip" : "7Z 压缩包|*.7z",
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
            string extension = format == "zip" ? ".zip" : ".7z";
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

            Result = new CreateArchiveOptions
            {
                Format = format,
                Level = SelectedLevel,
                Password = PwdBox.Password,
                EncryptNames = format == "7z" && EncryptNamesCheck.IsChecked == true,
                TargetPath = target,
                Sources = _sources.ToList()
            };
            DialogResult = true;
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
