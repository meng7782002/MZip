using System.Collections.Generic;

namespace MZip.Models
{
    /// <summary>
    /// 程序设置，明文保存于 config/settings.json
    /// </summary>
    public class AppSettings
    {
        public string LastOutputDirectory { get; set; } = string.Empty;
        public string LastCompressFormat { get; set; } = "7z";
        public int CompressionLevel { get; set; } = 5;
        /// <summary>Ask / Overwrite / Skip / Rename</summary>
        public string OverwriteMode { get; set; } = "Ask";
        public List<string> RecentArchives { get; set; } = new List<string>();
    }
}
