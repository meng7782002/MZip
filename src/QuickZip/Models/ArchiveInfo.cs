using System.Collections.Generic;

namespace QuickZip.Models
{
    /// <summary>
    /// 压缩包整体信息
    /// </summary>
    public class ArchiveInfo
    {
        public string ArchivePath { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public long ArchiveSize { get; set; }
        public long TotalSize { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public List<ArchiveItem> Entries { get; set; } = new List<ArchiveItem>();

        public double Ratio => TotalSize <= 0 ? 0 : (double)ArchiveSize / TotalSize;
        public string RatioText => TotalSize <= 0 ? "-" : $"{Ratio * 100:0.#}%";
    }
}
