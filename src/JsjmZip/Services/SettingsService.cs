using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JsjmZip.Models;

namespace JsjmZip.Services
{
    /// <summary>
    /// 设置读写：config/settings.json，明文保存
    /// </summary>
    public class SettingsService
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public AppSettings Current { get; private set; } = new AppSettings();

        public string SettingsPath => _path;

        public SettingsService()
        {
            _path = Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string json = File.ReadAllText(_path, Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    Current = settings;
                }
                LogHelper.Info("已加载设置：" + _path);
            }
            catch (Exception ex)
            {
                LogHelper.Error("读取设置失败，使用默认设置", ex);
            }
        }

        public void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(_path, json, Encoding.UTF8);
                LogHelper.Info("设置已保存：" + _path);
            }
            catch (Exception ex)
            {
                LogHelper.Error("保存设置失败", ex);
            }
        }

        public void AddRecentArchive(string path)
        {
            Current.RecentArchives.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Current.RecentArchives.Insert(0, path);
            while (Current.RecentArchives.Count > 10)
            {
                Current.RecentArchives.RemoveAt(Current.RecentArchives.Count - 1);
            }
            Save();
        }

        public void RemoveRecentArchive(string path)
        {
            Current.RecentArchives.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }
}
