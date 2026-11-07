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
        private const string StarZipQuickKey = @"Software\Classes\*\shell\MZip.ZipQuick";
        private const string Star7zSolidKey = @"Software\Classes\*\shell\MZip.7zSolid";
        private const string StarAddToKey = @"Software\Classes\*\shell\MZip.AddTo";
        private const string DirZipQuickKey = @"Software\Classes\Directory\shell\MZip.ZipQuick";
        private const string Dir7zSolidKey = @"Software\Classes\Directory\shell\MZip.7zSolid";
        private const string DirAddToKey = @"Software\Classes\Directory\shell\MZip.AddTo";
        private const string StarCascadeKey = @"Software\Classes\*\shell\MZip";
        private const string DirCascadeKey = @"Software\Classes\Directory\shell\MZip";
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

        /// <summary>右键菜单是否已注册（平铺或级联任一模式）</summary>
        public bool IsContextMenuRegistered()
        {
            using RegistryKey? flatKey = Registry.CurrentUser.OpenSubKey(StarCompressKey);
            if (flatKey != null) return true;
            using RegistryKey? cascadeKey = Registry.CurrentUser.OpenSubKey(StarCascadeKey);
            return cascadeKey != null;
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

        /// <summary>注册资源管理器右键菜单（压缩 + 解压 + 修复），cascaded=true 时收纳到 MZip 级联菜单</summary>
        public void RegisterContextMenu(bool cascaded)
        {
            // 先清理另一种模式的残留，保证同一时刻只有一种模式生效
            UnregisterContextMenu();

            if (cascaded)
            {
                WriteCascadeMenu(StarCascadeKey, includeCompress: true);
                WriteCascadeMenu(DirCascadeKey, includeCompress: true);
                foreach (string ext in ArchiveExtensions)
                {
                    string basePath = @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip";
                    bool isZip = string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase);
                    WriteCascadeMenu(basePath, includeCompress: false, includeRepair: isZip);
                }
                LogHelper.Info("已注册资源管理器右键菜单（级联模式，Win11 需经典菜单生效）");
            }
            else
            {
                WriteMenuVerb(StarCompressKey, "使用 MZip 压缩", "--compress", "Player");
                WriteMenuVerb(DirectoryCompressKey, "使用 MZip 压缩", "--compress", "Player");
                WriteMenuVerb(StarZipQuickKey, "压缩为同名 ZIP 文件", "/zip", "Player");
                WriteMenuVerb(DirZipQuickKey, "压缩为同名 ZIP 文件", "/zip", "Player");
                WriteMenuVerb(Star7zSolidKey, "压缩为高压缩比 7Z 文件", "/7zsolid", "Player");
                WriteMenuVerb(Dir7zSolidKey, "压缩为高压缩比 7Z 文件", "/7zsolid", "Player");
                WriteMenuVerb(StarAddToKey, "添加到压缩文件…", "/add", "Player");
                WriteMenuVerb(DirAddToKey, "添加到压缩文件…", "/add", "Player");

                foreach (string ext in ArchiveExtensions)
                {
                    string basePath = @"Software\Classes\SystemFileAssociations\" + ext + @"\shell";
                    WriteMenuVerb(basePath + @"\MZip.ExtractHere", "解压到本目录", "--extract-here", "Single");
                    WriteMenuVerb(basePath + @"\MZip.ExtractTo", "解压到指定目录…", "--extract-to", "Single");
                    if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // FR-112：修复仅 ZIP 可用，其余格式不注册该项
                        WriteMenuVerb(basePath + @"\MZip.Repair", "使用 MZip 修复", "/repair", "Single");
                    }
                }
                LogHelper.Info("已注册资源管理器右键菜单（平铺模式）");
            }
        }

        /// <summary>移除资源管理器右键菜单（平铺与级联全部清理）</summary>
        public void UnregisterContextMenu()
        {
            DeleteKeyTree(StarCompressKey);
            DeleteKeyTree(DirectoryCompressKey);
            DeleteKeyTree(StarZipQuickKey);
            DeleteKeyTree(DirZipQuickKey);
            DeleteKeyTree(Star7zSolidKey);
            DeleteKeyTree(Dir7zSolidKey);
            DeleteKeyTree(StarAddToKey);
            DeleteKeyTree(DirAddToKey);
            DeleteKeyTree(StarCascadeKey);
            DeleteKeyTree(DirCascadeKey);

            foreach (string ext in ArchiveExtensions)
            {
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip.ExtractHere");
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip.ExtractTo");
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip.Repair");
                DeleteKeyTree(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\MZip");
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

        /// <summary>写入单个右键菜单项（argument 不含引号路径，如 /zip、--compress）</summary>
        private void WriteMenuVerb(string keyPath, string displayName, string argument, string multiSelectModel)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath)!;
            key.SetValue(null, displayName);
            key.SetValue("Icon", QuotedExeWithIcon);
            key.SetValue("MultiSelectModel", multiSelectModel);
            using RegistryKey commandKey = key.CreateSubKey("command")!;
            commandKey.SetValue(null, QuotedExe + " " + argument + " \"%1\"");
        }

        /// <summary>
        /// 写入级联菜单容器及其子项（SubCommands + shell 子键，纯注册表实现，仅经典菜单生效）。
        /// includeCompress=true 用于文件/文件夹（压缩类子项）；否则为压缩包（解压类子项，ZIP 附修复）。
        /// </summary>
        private void WriteCascadeMenu(string containerKeyPath, bool includeCompress, bool includeRepair = false)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(containerKeyPath)!)
            {
                key.SetValue("MUIVerb", "MZip");
                key.SetValue("Icon", QuotedExeWithIcon);
                key.SetValue("SubCommands", string.Empty);
                key.SetValue("MultiSelectModel", includeCompress ? "Player" : "Single");
            }

            string shellPath = containerKeyPath + @"\shell";
            if (includeCompress)
            {
                WriteMenuVerb(shellPath + @"\01Compress", "使用 MZip 压缩", "--compress", "Player");
                WriteMenuVerb(shellPath + @"\02ZipQuick", "压缩为同名 ZIP 文件", "/zip", "Player");
                WriteMenuVerb(shellPath + @"\037zSolid", "压缩为高压缩比 7Z 文件", "/7zsolid", "Player");
                WriteMenuVerb(shellPath + @"\04AddTo", "添加到压缩文件…", "/add", "Player");
            }
            else
            {
                WriteMenuVerb(shellPath + @"\01ExtractHere", "解压到本目录", "--extract-here", "Single");
                WriteMenuVerb(shellPath + @"\02ExtractTo", "解压到指定目录…", "--extract-to", "Single");
                if (includeRepair)
                {
                    WriteMenuVerb(shellPath + @"\03Repair", "使用 MZip 修复", "/repair", "Single");
                }
            }
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
