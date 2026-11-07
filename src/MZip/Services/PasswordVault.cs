using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MZip.Services
{
    /// <summary>
    /// 密码库（FR-100）：config/passwords.json 明文保存成功使用过的密码，MRU 排序，上限 50 条
    /// </summary>
    public class PasswordVault
    {
        private const int MaxCount = 50;

        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private List<string> _passwords = new List<string>();

        public PasswordVault()
        {
            _path = Path.Combine(AppContext.BaseDirectory, "config", "passwords.json");
            Load();
        }

        /// <summary>按最近使用排序的密码列表（明文）</summary>
        public IReadOnlyList<string> Passwords => _passwords;

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string json = File.ReadAllText(_path, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
                if (list != null)
                {
                    _passwords = list.Where(p => !string.IsNullOrEmpty(p)).ToList();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("读取密码库失败，使用空库", ex);
                _passwords = new List<string>();
            }
        }

        private void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(_passwords, JsonOptions);
                File.WriteAllText(_path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogHelper.Error("保存密码库失败", ex);
            }
        }

        /// <summary>记录成功使用的密码：去重置顶，超出上限淘汰最旧</summary>
        public void Add(string password)
        {
            if (string.IsNullOrEmpty(password)) return;
            _passwords.RemoveAll(p => p == password);
            _passwords.Insert(0, password);
            while (_passwords.Count > MaxCount)
            {
                _passwords.RemoveAt(_passwords.Count - 1);
            }
            Save();
            LogHelper.Info("密码库：已记录新密码（当前共 " + _passwords.Count + " 条）");
        }

        public void Remove(string password)
        {
            if (_passwords.RemoveAll(p => p == password) > 0)
            {
                Save();
                LogHelper.Info("密码库：删除单条密码（当前共 " + _passwords.Count + " 条）");
            }
        }

        public void Clear()
        {
            _passwords.Clear();
            Save();
            LogHelper.Info("密码库：已清空全部密码");
        }
    }
}
