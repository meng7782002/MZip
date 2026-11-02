using System.Collections.Generic;

namespace JsjmZip.Models
{
    /// <summary>
    /// 新建压缩包的参数
    /// </summary>
    public class CreateArchiveOptions
    {
        /// <summary>7z 或 zip</summary>
        public string Format { get; set; } = "7z";
        /// <summary>压缩级别 0-9</summary>
        public int Level { get; set; } = 5;
        public string Password { get; set; } = string.Empty;
        /// <summary>仅 7Z 支持：加密文件名</summary>
        public bool EncryptNames { get; set; }
        public string TargetPath { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>
    /// 一次压缩/解压操作的进度
    /// </summary>
    public class OperationProgress
    {
        public double Percent { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
    }
}
