using System;
using System.Collections.Generic;

namespace MZip.Models
{
    /// <summary>
    /// 新建压缩包的参数
    /// </summary>
    public class CreateArchiveOptions
    {
        /// <summary>7z / zip / exe（自解压包）</summary>
        public string Format { get; set; } = "7z";
        /// <summary>压缩级别 0-9</summary>
        public int Level { get; set; } = 5;
        public string Password { get; set; } = string.Empty;
        /// <summary>仅 7Z 支持：加密文件名</summary>
        public bool EncryptNames { get; set; }
        /// <summary>分卷大小（MB），0 表示不分卷；EXE 不支持分卷</summary>
        public int VolumeMB { get; set; }
        /// <summary>仅 7Z 支持：固实压缩</summary>
        public bool Solid { get; set; }
        /// <summary>仅 ZIP 支持：压缩包注释</summary>
        public string Comment { get; set; } = string.Empty;
        /// <summary>压缩成功并校验通过后删除源文件</summary>
        public bool DeleteSources { get; set; }
        public string TargetPath { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new List<string>();

        public bool SelfExtracting => string.Equals(Format, "exe", StringComparison.OrdinalIgnoreCase);
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
