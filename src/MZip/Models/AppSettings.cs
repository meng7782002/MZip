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
        /// <summary>文件名编码代码页，0 = 自动</summary>
        public int DefaultCodePage { get; set; }
        /// <summary>解压成功后删除源压缩包</summary>
        public bool ExtractDeleteSource { get; set; }
        /// <summary>右键菜单级联收纳（仅经典菜单生效），默认 false = 平铺</summary>
        public bool CascadedMenu { get; set; }
        /// <summary>上次新建压缩使用的参数（密码不记忆）</summary>
        public LastCompressOptions LastCompress { get; set; } = new LastCompressOptions();
    }

    /// <summary>
    /// 记忆的上次压缩参数
    /// </summary>
    public class LastCompressOptions
    {
        public string Format { get; set; } = "7z";
        public int Level { get; set; } = 5;
        public int VolumeMB { get; set; }
        public bool Solid { get; set; }
        public bool DeleteSources { get; set; }
        public bool EncryptNames { get; set; }
    }
}
