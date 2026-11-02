using System;
using System.Windows;
using System.Windows.Controls;
using QuickZip.Models;
using QuickZip.Services;

namespace QuickZip.Dialogs
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ShellIntegrationService _shell = new ShellIntegrationService();

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            FormatCombo.SelectedIndex = string.Equals(settings.LastCompressFormat, "zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LevelCombo.SelectedIndex = FindLevelIndex(settings.CompressionLevel);
            OverwriteCombo.SelectedIndex = FindOverwriteIndex(settings.OverwriteMode);
            RefreshIntegrationState();
        }

        private static int FindLevelIndex(int level)
        {
            int[] levels = { 0, 1, 3, 5, 7, 9 };
            int index = Array.IndexOf(levels, level);
            return index < 0 ? 3 : index;
        }

        private static int FindOverwriteIndex(string mode)
        {
            return mode switch
            {
                "Overwrite" => 1,
                "Skip" => 2,
                "Rename" => 3,
                _ => 0
            };
        }

        private void RefreshIntegrationState()
        {
            bool association = _shell.IsFileAssociationRegistered();
            bool menu = _shell.IsContextMenuRegistered();
            AssociationStatusText.Text =
                $"文件关联（.zip / .rar / .7z）：{(association ? "已注册" : "未注册")}；" +
                $"资源管理器右键菜单：{(menu ? "已注册" : "未注册")}";
        }

        private void Associate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shell.RegisterFileAssociation();
                _shell.NotifyShellChanged();
                RefreshIntegrationState();
                LogHelper.Info("用户操作：设为默认解压程序");
                MessageBox.Show(this,
                    "已注册 .zip / .rar / .7z 文件关联。\n\n如系统仍使用其他程序打开压缩包，" +
                    "请点击“打开默认应用设置”，在 Windows 默认应用中选择 QuickZip。",
                    "Windows 集成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogHelper.Error("注册文件关联失败", ex);
                MessageBox.Show(this, "注册失败：" + ex.Message, "Windows 集成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shell.OpenDefaultAppsSettings();
            }
            catch (Exception ex)
            {
                LogHelper.Error("打开默认应用设置失败", ex);
                MessageBox.Show(this, "无法打开默认应用设置：" + ex.Message, "Windows 集成",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Unassociate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shell.UnregisterFileAssociation();
                _shell.NotifyShellChanged();
                RefreshIntegrationState();
                LogHelper.Info("用户操作：取消文件关联");
            }
            catch (Exception ex)
            {
                LogHelper.Error("取消文件关联失败", ex);
                MessageBox.Show(this, "操作失败：" + ex.Message, "Windows 集成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RegisterMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shell.RegisterContextMenu();
                _shell.NotifyShellChanged();
                RefreshIntegrationState();
                LogHelper.Info("用户操作：注册右键菜单");
            }
            catch (Exception ex)
            {
                LogHelper.Error("注册右键菜单失败", ex);
                MessageBox.Show(this, "注册失败：" + ex.Message, "Windows 集成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UnregisterMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shell.UnregisterContextMenu();
                _shell.NotifyShellChanged();
                RefreshIntegrationState();
                LogHelper.Info("用户操作：移除右键菜单");
            }
            catch (Exception ex)
            {
                LogHelper.Error("移除右键菜单失败", ex);
                MessageBox.Show(this, "操作失败：" + ex.Message, "Windows 集成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _settings.LastCompressFormat = FormatCombo.SelectedIndex == 1 ? "zip" : "7z";

            if (LevelCombo.SelectedItem is ComboBoxItem levelItem && levelItem.Tag != null
                && int.TryParse(levelItem.Tag.ToString(), out int level))
            {
                _settings.CompressionLevel = level;
            }

            if (OverwriteCombo.SelectedItem is ComboBoxItem overwriteItem && overwriteItem.Tag != null)
            {
                _settings.OverwriteMode = overwriteItem.Tag.ToString()!;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
