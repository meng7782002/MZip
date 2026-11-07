using System;
using System.IO;
using System.Text;

namespace MZip.Services
{
    /// <summary>
    /// ZIP 归档注释写入：重写 EOCD（End of Central Directory）记录的注释字段
    /// </summary>
    public static class ZipCommentWriter
    {
        private static readonly byte[] EocdSignature = { 0x50, 0x4B, 0x05, 0x06 };
        private const int EocdMinSize = 22;
        private const int MaxCommentSearch = 65535 + EocdMinSize;

        /// <summary>
        /// 设置 ZIP 文件注释（UTF-8），覆盖原有注释
        /// </summary>
        public static void SetComment(string zipPath, string comment)
        {
            byte[] commentBytes = Encoding.UTF8.GetBytes(comment ?? string.Empty);
            if (commentBytes.Length > 65535)
            {
                throw new InvalidDataException("注释过长，最多 65535 字节。");
            }

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            long eocdOffset = FindEocd(fs);
            if (eocdOffset < 0)
            {
                throw new InvalidDataException("未找到 ZIP 结尾记录，文件可能不是有效的 ZIP。");
            }

            fs.Position = eocdOffset;
            using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            byte[] eocd = reader.ReadBytes(EocdMinSize);
            if (eocd.Length < EocdMinSize)
            {
                throw new InvalidDataException("ZIP 结尾记录不完整。");
            }

            // 注释长度字段在 EOCD 偏移 20 处（UInt16 小端）
            eocd[20] = (byte)(commentBytes.Length & 0xFF);
            eocd[21] = (byte)((commentBytes.Length >> 8) & 0xFF);

            fs.Position = eocdOffset;
            using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);
            writer.Write(eocd);
            writer.Write(commentBytes);
            fs.SetLength(fs.Position);
        }

        /// <summary>
        /// 从文件尾部向前搜索 EOCD 签名
        /// </summary>
        private static long FindEocd(FileStream fs)
        {
            long searchLength = Math.Min(fs.Length, MaxCommentSearch);
            if (searchLength < EocdMinSize) return -1;

            fs.Position = fs.Length - searchLength;
            var buffer = new byte[searchLength];
            int read = fs.Read(buffer, 0, buffer.Length);

            for (int i = read - EocdMinSize; i >= 0; i--)
            {
                if (buffer[i] == EocdSignature[0]
                    && buffer[i + 1] == EocdSignature[1]
                    && buffer[i + 2] == EocdSignature[2]
                    && buffer[i + 3] == EocdSignature[3])
                {
                    return fs.Length - searchLength + i;
                }
            }
            return -1;
        }
    }
}
