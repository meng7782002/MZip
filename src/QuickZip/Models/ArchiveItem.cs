using System;

namespace QuickZip.Models
{
    /// <summary>
    /// 压缩包内的一个条目（文件或文件夹）
    /// </summary>
    public class ArchiveItem
    {
        public string FullPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public long PackedSize { get; set; }
        public DateTime? Modified { get; set; }
        public string ModifiedText => Modified?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
        public string Attributes { get; set; } = string.Empty;
        public string Crc { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public string Method { get; set; } = string.Empty;

        /// <summary>文件夹图标 / 文件图标（Segoe MDL2 字形）</summary>
        public string Glyph => IsFolder ? "\uE8B7" : "\uE7C3";

        /// <summary>压缩率（压缩后 / 原始大小）</summary>
        public double Ratio
        {
            get
            {
                if (IsFolder || Size <= 0) return 0;
                return (double)PackedSize / Size;
            }
        }

        public string RatioText => IsFolder || Size <= 0 ? string.Empty : $"{Ratio * 100:0.#}%";
    }
}
