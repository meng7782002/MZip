using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MZip.Services;

/// <summary>
/// 哈希计算结果（MD5/SHA1/SHA256/CRC32，十六进制大写字符串）。
/// </summary>
public class HashResult
{
    public string Md5 { get; set; } = string.Empty;
    public string Sha1 { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Crc32 { get; set; } = string.Empty;
}

/// <summary>
/// 文件/流哈希计算服务（FR-107/108）。
/// MD5/SHA1/SHA256 使用 .NET 内置 IncrementalHash 流式计算；CRC32 为自实现查表法。
/// </summary>
public class HashService
{
    /// <summary>CRC32（IEEE 802.3，多项式 0xEDB88320）查表实现。</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        public static uint Update(uint crc, byte[] data, int count)
        {
            for (int i = 0; i < count; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }
    }

    /// <summary>
    /// 流式计算给定流的四种哈希值。progress 报告 0-100 百分比。
    /// </summary>
    public async Task<HashResult> ComputeAsync(Stream stream, long? totalLength = null,
        IProgress<int>? progress = null, CancellationToken token = default)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        uint crc = 0xFFFFFFFFu;
        var buffer = new byte[1024 * 512];
        long readTotal = 0;
        long length = totalLength ?? (stream.CanSeek ? stream.Length - stream.Position : 0);
        int lastPercent = -1;

        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            md5.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
            crc = Crc32.Update(crc, buffer, read);

            readTotal += read;
            if (progress != null && length > 0)
            {
                int percent = (int)Math.Min(100, readTotal * 100 / length);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent);
                }
            }
        }

        crc ^= 0xFFFFFFFFu;
        progress?.Report(100);

        return new HashResult
        {
            Md5 = ToHex(md5.GetHashAndReset()),
            Sha1 = ToHex(sha1.GetHashAndReset()),
            Sha256 = ToHex(sha256.GetHashAndReset()),
            Crc32 = crc.ToString("X8")
        };
    }

    /// <summary>计算本地文件的四种哈希值。</summary>
    public Task<HashResult> ComputeFileAsync(string path, IProgress<int>? progress = null,
        CancellationToken token = default)
    {
        var fileInfo = new FileInfo(path);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 512, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ComputeOwnedAsync(stream, fileInfo.Length, progress, token);
    }

    private async Task<HashResult> ComputeOwnedAsync(Stream owned, long length,
        IProgress<int>? progress, CancellationToken token)
    {
        await using (owned)
        {
            return await ComputeAsync(owned, length, progress, token);
        }
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes);
    }
}
