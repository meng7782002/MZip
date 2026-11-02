using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MZip.Services
{
    /// <summary>
    /// Windows 系统集成：当前用户的文件关联与资源管理器右键菜单（仅写 HKCU，不修改 HKLM）
    /// </summary>
    public class ShellIntegrationService
    {
        private const string ProgId = "MZip.Archive";
        private const string ProgIdDescription = "MZip 压缩包";
        private const string ClassesRoot = @"Software\Classes";
        private const string StarCompressKey = @"Software\Classes\*\shell\MZip.Compress";
        private const string DirectoryCompressKey = @"Software\Classes\Directory\shell\MZip.Compress";
        private const string CapabilitiesKey = @"Software\MZip\Capabilities";
        private const string IconBackupKey = @"Software\MZip\IconBackup";
        private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

        private static readonly string[] ArchiveExtensions = { ".zip", ".rar", ".7z" };

        private static string ExePath =>
            Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "MZip.exe");

        private static string QuotedExe => "\"" + ExePath + "\"";

        private static string QuotedExeWithIcon => "\"" + ExePath + "\",0";

        /// <summary>文件关联是否已注册（以 .zip 为准）</summary>
        public bool IsFileAssociationRegistered()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\.zip");
            if (key == null) return false;
            object? value = key.GetValue(null);
            return value != null && string.Equals(value.ToString(), ProgId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>右键菜单是否已注册</summary>
        public bool IsContextMenuRegistered()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StarCompressKey);
            return key != null;
        }

        /// <summary>注册 .zip/.rar/.7z 文件关联与默认应用能力信息</summary>
        public void RegisterFileAssociation()
        {
            string progIdPath = ClassesRoot + @"\" + ProgId;
            using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey(progIdPath)!)
            {
                progIdKey.SetValue(null, ProgIdDescription);
                progIdKey.SetValue("DefaultIcon", QuotedExeWithIcon);
                using RegistryKey commandKey = progIdKey.CreateSubKey(@"shell\open\command")!;
                commandKey.SetValue(null, QuotedExe + " \"%1\"");
            }

            foreach (string ext in ArchiveExtensions)
            {
                string extPath = ClassesRoot + @"\" + ext;
                using RegistryKey extKey = Registry.CurrentUser.CreateSubKey(extPath)!;
                extKey.SetValue(null, ProgId);
                using RegistryKey openWithKey = extKey.CreateSubKey("OpenWithProgids")!;
                openWithKey.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                WriteExtensionIcon(extKey, ext);
            }

            using (RegistryKey capabilitiesKey = Registry.CurrentUser.CreateSubKey(CapabilitiesKey)!)
            {
                capabilitiesKey.SetValue("ApplicationName", "MZip");
                capabilitiesKey.SetValue("ApplicationDescription", "支持 ZIP / 7Z / RAR 的压缩解压工具");
                capabilitiesKey.SetValue("ApplicationIcon", QuotedExeWithIcon);
                using RegistryKey associationsKey = capabilitiesKey.CreateSubKey("FileAssociations")!;
                foreach (string ext in ArchiveExtensions)
                {
                    associationsKey.SetValue(ext, ProgId);
                }
            }

            using (RegistryKey registeredKey = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey)!)
            {
                registeredKey.SetValue("MZip", @"Software\MZip\Capabilities");
            }

            LogHelper.Info("已注册文件关联：.zip/.rar/.7z → " + ProgId);
        }

        /// <summary>取消文件关联（仅清理本程序写入项）</summary>
        public void UnregisterFileAssociation()
        {
            DeleteKeyTree(ClassesRoot + @"\" + ProgId);

            foreach (string ext in ArchiveExtensions)
            {
                using RegistryKey? extKey = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\" + ext, writable: true);
                if (extKey != null)
                {
                    object? value = extKey.GetValue(null);
                    if (value != null && string.Equals(value.ToString(), ProgId, StringComparison.OrdinalIgnoreCase))
                    {
                        extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                    }
                    using RegistryKey? openWithKey = extKey.OpenSubKey("OpenWithProgids", writable: true);
                    openWithKey?.DeleteValue(ProgId, throwOnMissingValue: false);
                    RestoreExtensionIcon(extKey, ext);
                }
            }

            DeleteKeyTree(IconBackupKey);
            DeleteKeyTree(CapabilitiesKey);
            using (RegistryKey? registeredKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true))
            {
                registeredKey?.DeleteValue("MZip", throwOnMissingValue: false);
            }

            LogHelper.Info("已取消文件关联：.zip/.rar/.7z");
        }

        /// <summary>注册资源管理器右键菜单（压缩 + 解压）</summary>
        public void RegisterContextMenu()
        {
            WriteCompressVerb(StarCompressKey);
            WriteCompressVerb(DirectoryCompressKey);

            foreach (string ext in ArchiveExtensions)
            {
                string basePath = @"Software\Classes\SystemFileAssociations\" + ext + @"\shell";
                WriteExtractVerb(basePath + @"\MZip.ExtractHere", "解压到本目录", " --extract-here");
                WriteExtractVerb(basePath + @"\MZip.ExtractTo", "解压到指定目录…", " --extract-to");
            }

            LogHelper.Info("已注册资源管理器右键菜单");
        }

        /// <summary>移除资源管理器右键菜单</summary>
        public void UnregisterContextMenu()
        {
            DeleteKeyTree(StarCompressKey);
            DeleteKeyTree(DirectoryCompressKey);

            foreach (string ext in ArchiveExtensions)
            {
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip.ExtractHere");
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip.ExtractTo");
            }

            LogHelper.Info("已移除资源管理器右键菜单");
        }

        /// <summary>通知资源管理器刷新关联与图标缓存</summary>
        public void NotifyShellChanged()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogHelper.Error("通知资源管理器刷新失败", ex);
            }
        }

        /// <summary>打开 Windows 默认应用设置页</summary>
        public void OpenDefaultAppsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.Error("打开默认应用设置失败", ex);
                throw;
            }
        }

        private void WriteCompressVerb(string keyPath)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath)!;
            key.SetValue(null, "使用 MZip 压缩");
            key.SetValue("Icon", QuotedExeWithIcon);
            key.SetValue("MultiSelectModel", "Player");
            using RegistryKey commandKey = key.CreateSubKey("command")!;
            commandKey.SetValue(null, QuotedExe + " --compress \"%1\"");
        }

        private void WriteExtractVerb(string keyPath, string displayName, string argument)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath)!;
            key.SetValue(null, displayName);
            key.SetValue("Icon", QuotedExeWithIcon);
            key.SetValue("MultiSelectModel", "Single");
            using RegistryKey commandKey = key.CreateSubKey("command")!;
            commandKey.SetValue(null, QuotedExe + argument + " \"%1\"");
        }

        /// <summary>写入扩展名文件图标，并备份原有 DefaultIcon 值</summary>
        private static void WriteExtensionIcon(RegistryKey extKey, string ext)
        {
            try
            {
                using RegistryKey backupRoot = Registry.CurrentUser.CreateSubKey(IconBackupKey)!;
                if (backupRoot.OpenSubKey(ext) == null)
                {
                    using RegistryKey? iconKey = extKey.OpenSubKey("DefaultIcon");
                    string original = iconKey?.GetValue(null)?.ToString() ?? "-";
                    using RegistryKey backupKey = backupRoot.CreateSubKey(ext)!;
                    backupKey.SetValue(null, original);
                    LogHelper.Info($"已备份 {ext} 原文件图标：{original}");
                }

                using RegistryKey defaultIconKey = extKey.CreateSubKey("DefaultIcon")!;
                defaultIconKey.SetValue(null, QuotedExeWithIcon);
                LogHelper.Info($"已设置 {ext} 文件图标：{QuotedExeWithIcon}");
            }
            catch (Exception ex)
            {
                // 图标写入失败不阻断文件关联注册
                LogHelper.Error("写入文件图标失败：" + ext, ex);
            }
        }

        /// <summary>按备份恢复扩展名文件图标；无备份时仅清除本程序写入的值</summary>
        private static void RestoreExtensionIcon(RegistryKey extKey, string ext)
        {
            try
            {
                string? original = null;
                bool hasBackup = false;
                using (RegistryKey? backupRoot = Registry.CurrentUser.OpenSubKey(IconBackupKey))
                using (RegistryKey? backupKey = backupRoot?.OpenSubKey(ext))
                {
                    if (backupKey != null)
                    {
                        hasBackup = true;
                        original = backupKey.GetValue(null)?.ToString();
                    }
                }

                if (hasBackup)
                {
                    if (string.IsNullOrEmpty(original) || original == "-")
                    {
                        // 原本没有文件图标：整体删除本程序创建的子键
                        extKey.DeleteSubKey("DefaultIcon", throwOnMissingSubKey: false);
                        LogHelper.Info($"已清除 {ext} 文件图标");
                    }
                    else
                    {
                        using RegistryKey? iconKey = extKey.CreateSubKey("DefaultIcon");
                        iconKey?.SetValue(null, original);
                        LogHelper.Info($"已恢复 {ext} 文件图标：{original}");
                    }
                }
                else
                {
                    using RegistryKey? iconKey = extKey.OpenSubKey("DefaultIcon", writable: true);
                    bool isOurs = iconKey != null && string.Equals(iconKey.GetValue(null)?.ToString(), QuotedExeWithIcon, StringComparison.OrdinalIgnoreCase);
                    iconKey?.Dispose();
                    if (isOurs)
                    {
                        extKey.DeleteSubKey("DefaultIcon", throwOnMissingSubKey: false);
                        LogHelper.Info($"已清除 {ext} 文件图标");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("恢复文件图标失败：" + ext, ex);
            }
        }

        private static void DeleteKeyTree(string keyPath)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                LogHelper.Error("删除注册表项失败：" + keyPath, ex);
                throw;
            }
        }

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }
}
