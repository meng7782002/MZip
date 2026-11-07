using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MZip.Services;

/// <summary>
/// ZIP 修复结果。
/// </summary>
public class ZipRepairResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int RecoveredCount { get; set; }
    public int SkippedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// ZIP 压缩包修复服务（FR-109）：仅支持 ZIP。
/// 对中央目录损坏的 ZIP 顺序扫描本地文件头（PK\x03\x04），重建中央目录与 EOCD，
/// 输出 <原名>_repaired.zip 到同目录，不覆盖原文件。修复为尽力而为。
/// </summary>
public class ZipRepairService
{
    static ZipRepairService()
    {
        // 文件名按 bit11 判定 UTF-8，否则按 GBK 解码；需确保代码页提供程序已注册，
        // 避免在非 WPF 启动路径（如命令行/测试）调用时 GetEncoding(936) 抛异常。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const uint LocalHeaderSig = 0x04034b50;
    private const uint CentralHeaderSig = 0x02014b50;
    private const uint EocdSig = 0x06054b50;
    private const uint DataDescriptorSig = 0x08074b50;

    private class RecoveredEntry
    {
        public string Name = string.Empty;
        public ushort Method;
        public ushort Flags;
        public ushort ModTime;
        public ushort ModDate;
        public uint Crc;
        public uint CompressedSize;
        public uint UncompressedSize;
        public uint LocalHeaderOffset;
        public byte[] NameBytes = Array.Empty<byte>();
    }

    /// <summary>
    /// 尝试修复 ZIP 文件。返回结果包含恢复条目数与输出路径。
    /// </summary>
    public ZipRepairResult Repair(string zipPath)
    {
        LogHelper.Info("开始修复压缩包：" + zipPath);
        string outputPath = Path.Combine(
            Path.GetDirectoryName(zipPath)!,
            Path.GetFileNameWithoutExtension(zipPath) + "_repaired.zip");

        if (File.Exists(outputPath))
        {
            outputPath = Path.Combine(
                Path.GetDirectoryName(zipPath)!,
                Path.GetFileNameWithoutExtension(zipPath) + "_repaired_"
                    + DateTime.Now.ToString("HHmmss") + ".zip");
        }

        var entries = new List<RecoveredEntry>();
        int skipped = 0;
        long dataEnd = 0; // 最后一个条目数据结束位置（原文件保留前缀长度）

        try
        {
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                while (fs.Position < fs.Length - 4)
                {
                    uint sig = reader.ReadUInt32();
                    if (sig == CentralHeaderSig || sig == EocdSig)
                    {
                        break; // 到达（可能损坏的）中央目录，停止扫描
                    }
                    if (sig != LocalHeaderSig)
                    {
                        fs.Position -= 3; // 逐字节回退继续找签名
                        continue;
                    }

                    long headerOffset = fs.Position - 4;
                    if (fs.Length - headerOffset < 30)
                    {
                        skipped++;
                        break;
                    }

                    try
                    {
                        var entry = ParseLocalHeader(reader, fs, headerOffset);
                        if (entry == null)
                        {
                            skipped++;
                            fs.Position = headerOffset + 4;
                            continue;
                        }
                        entries.Add(entry);
                        dataEnd = fs.Position;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"修复：解析本地文件头失败（偏移 {headerOffset}）", ex);
                        skipped++;
                        fs.Position = headerOffset + 4;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("修复：读取文件失败 " + zipPath, ex);
            return new ZipRepairResult { Success = false, Message = "读取文件失败：" + ex.Message };
        }

        if (entries.Count == 0)
        {
            LogHelper.Info("修复：未扫描到任何有效条目");
            return new ZipRepairResult
            {
                Success = false,
                SkippedCount = skipped,
                Message = "未找到可恢复的条目，文件可能不是 ZIP 或损坏严重。"
            };
        }

        try
        {
            RebuildArchive(zipPath, outputPath, dataEnd, entries);
        }
        catch (Exception ex)
        {
            LogHelper.Error("修复：写入修复文件失败 " + outputPath, ex);
            TryDelete(outputPath);
            return new ZipRepairResult { Success = false, Message = "写入修复文件失败：" + ex.Message };
        }

        LogHelper.Info($"修复完成：恢复 {entries.Count} 个条目，跳过 {skipped} 个，输出 {outputPath}");
        return new ZipRepairResult
        {
            Success = true,
            OutputPath = outputPath,
            RecoveredCount = entries.Count,
            SkippedCount = skipped,
            Message = $"成功恢复 {entries.Count} 个条目" + (skipped > 0 ? $"，跳过 {skipped} 个损坏条目" : "")
        };
    }

    /// <summary>
    /// 解析一个本地文件头；成功后流位置停在条目数据末尾。解析失败返回 null。
    /// </summary>
    private RecoveredEntry? ParseLocalHeader(BinaryReader reader, FileStream fs, long headerOffset)
    {
        _ = reader.ReadUInt16();           // version needed
        ushort flags = reader.ReadUInt16();
        ushort method = reader.ReadUInt16();
        ushort modTime = reader.ReadUInt16();
        ushort modDate = reader.ReadUInt16();
        uint crc = reader.ReadUInt32();
        uint compressedSize = reader.ReadUInt32();
        uint uncompressedSize = reader.ReadUInt32();
        ushort nameLen = reader.ReadUInt16();
        ushort extraLen = reader.ReadUInt16();

        // 合理性校验
        if (nameLen == 0 || nameLen > 4096 || method > 99) return null;
        if (headerOffset + 30L + nameLen + extraLen > fs.Length) return null;

        byte[] nameBytes = reader.ReadBytes(nameLen);
        if (nameBytes.Length != nameLen) return null;
        fs.Position += extraLen;

        // 文件名解码：bit11 为 UTF-8，否则按 GBK（早期中文压缩包常见）
        Encoding nameEncoding = (flags & 0x0800) != 0
            ? Encoding.UTF8
            : Encoding.GetEncoding(936);
        string name = nameEncoding.GetString(nameBytes);
        if (string.IsNullOrWhiteSpace(name)) return null;

        long dataStart = fs.Position;
        bool hasDataDescriptor = (flags & 0x08) != 0;

        if (!hasDataDescriptor || compressedSize > 0 || method == 0)
        {
            // 尺寸在本地头中已知，直接跳过数据区
            if (compressedSize == 0 && method != 0 && !name.EndsWith("/"))
            {
                // 尺寸未知，只能扫描定位
                if (!ScanToDataEnd(fs, out crc, out compressedSize, out uncompressedSize))
                {
                    return null;
                }
            }
            else if (dataStart + compressedSize > fs.Length)
            {
                return null;
            }
            else
            {
                fs.Position = dataStart + compressedSize;
                if (hasDataDescriptor)
                {
                    SkipDataDescriptor(fs, reader);
                }
            }
        }
        else
        {
            // bit3 置位且尺寸为 0：扫描下一个 PK 签名定位数据末端
            if (!ScanToDataEnd(fs, out crc, out compressedSize, out uncompressedSize))
            {
                return null;
            }
        }

        return new RecoveredEntry
        {
            Name = name,
            NameBytes = nameBytes,
            Method = method,
            Flags = flags,
            ModTime = modTime,
            ModDate = modDate,
            Crc = crc,
            CompressedSize = compressedSize,
            UncompressedSize = uncompressedSize,
            LocalHeaderOffset = (uint)headerOffset
        };
    }

    /// <summary>
    /// 从当前位置扫描下一个本地头/中央目录签名定位数据末端；
    /// 若紧邻数据末端存在数据描述符则读取其中的 CRC 与尺寸。
    /// </summary>
    private bool ScanToDataEnd(FileStream fs, out uint crc, out uint compressedSize, out uint uncompressedSize)
    {
        crc = 0;
        compressedSize = 0;
        uncompressedSize = 0;

        long dataStart = fs.Position;
        long nextSig = FindNextSignature(fs, dataStart);
        if (nextSig < 0) return false;

        long dataEnd = nextSig;
        // 检查数据末端前是否有数据描述符（带签名 16 字节 / 无签名 12 字节）
        if (dataEnd - 16 >= dataStart)
        {
            fs.Position = dataEnd - 16;
            var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            uint maybeSig = br.ReadUInt32();
            if (maybeSig == DataDescriptorSig)
            {
                crc = br.ReadUInt32();
                compressedSize = br.ReadUInt32();
                uncompressedSize = br.ReadUInt32();
                fs.Position = nextSig;
                return true;
            }
        }
        if (dataEnd - 12 >= dataStart)
        {
            fs.Position = dataEnd - 12;
            var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            uint c = br.ReadUInt32();
            uint cs = br.ReadUInt32();
            uint us = br.ReadUInt32();
            // 无签名描述符：尺寸应恰好等于数据区长度减 12
            if (cs == (uint)(dataEnd - 12 - dataStart))
            {
                crc = c;
                compressedSize = cs;
                uncompressedSize = us;
                fs.Position = nextSig;
                return true;
            }
        }

        // 没有可用描述符：按扫描出的数据长度填充（CRC 未知，置 0）
        compressedSize = (uint)(dataEnd - dataStart);
        uncompressedSize = 0;
        fs.Position = nextSig;
        return true;
    }

    /// <summary>从指定位置滚动扫描下一个 PK 签名（本地头/中央目录/EOCD），找不到返回 -1。</summary>
    private long FindNextSignature(FileStream fs, long from)
    {
        const int WindowSize = 128 * 1024;
        var window = new byte[WindowSize + 4];
        fs.Position = from;

        long windowStart = from;
        while (windowStart < fs.Length)
        {
            int read = fs.Read(window, 0, (int)Math.Min(window.Length, fs.Length - windowStart));
            if (read < 4) break;
            for (int i = 0; i <= read - 4; i++)
            {
                if (window[i] == 0x50 && window[i + 1] == 0x4B)
                {
                    uint sig = (uint)(window[i] | (window[i + 1] << 8) | (window[i + 2] << 16) | (window[i + 3] << 24));
                    if (sig == LocalHeaderSig || sig == CentralHeaderSig || sig == EocdSig)
                    {
                        return windowStart + i;
                    }
                }
            }
            // 保留末尾 3 字节与下一窗口衔接
            windowStart += read - 3;
            fs.Position = windowStart;
        }
        return -1;
    }

    private void SkipDataDescriptor(FileStream fs, BinaryReader reader)
    {
        if (fs.Position + 4 > fs.Length) return;
        long pos = fs.Position;
        uint maybeSig = reader.ReadUInt32();
        if (maybeSig == DataDescriptorSig)
        {
            fs.Position += 12; // 带签名描述符共 16 字节
        }
        else
        {
            fs.Position = pos + 12; // 无签名描述符 12 字节
        }
    }

    /// <summary>复制原文件前缀（全部本地条目）并追加重建的中央目录与 EOCD。</summary>
    private void RebuildArchive(string sourcePath, string outputPath, long dataEnd, List<RecoveredEntry> entries)
    {
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            // 1. 原样复制本地条目区（偏移不变）
            var buffer = new byte[1024 * 512];
            long remaining = dataEnd;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0) break;
                output.Write(buffer, 0, read);
                remaining -= read;
            }

            // 2. 写入中央目录
            long cdOffset = output.Position;
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                foreach (RecoveredEntry entry in entries)
                {
                    writer.Write(CentralHeaderSig);
                    writer.Write((ushort)20);          // version made by
                    writer.Write((ushort)20);          // version needed
                    writer.Write(entry.Flags);
                    writer.Write(entry.Method);
                    writer.Write(entry.ModTime);
                    writer.Write(entry.ModDate);
                    writer.Write(entry.Crc);
                    writer.Write(entry.CompressedSize);
                    writer.Write(entry.UncompressedSize);
                    writer.Write((ushort)entry.NameBytes.Length);
                    writer.Write((ushort)0);           // extra len
                    writer.Write((ushort)0);           // comment len
                    writer.Write((ushort)0);           // disk start
                    writer.Write((ushort)0);           // internal attr
                    writer.Write((uint)0);             // external attr
                    writer.Write(entry.LocalHeaderOffset);
                    writer.Write(entry.NameBytes);
                }

                // 3. 写入 EOCD
                long cdSize = output.Position - cdOffset;
                writer.Write(EocdSig);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)entries.Count);
                writer.Write((ushort)entries.Count);
                writer.Write((uint)cdSize);
                writer.Write((uint)cdOffset);
                writer.Write((ushort)0);
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            LogHelper.Error("修复：清理半成品文件失败 " + path, ex);
        }
    }
}
