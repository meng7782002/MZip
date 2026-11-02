using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickZip.Services
{
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public bool Canceled { get; set; }

        public string AllOutput => StdOut + Environment.NewLine + StdErr;
    }

    /// <summary>
    /// 外部进程执行器：异步读取 stdout/stderr，按 \r、\n 切分片段回调，支持取消
    /// </summary>
    public static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(
            string exePath,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            Action<string>? onSegment,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("找不到压缩引擎：" + exePath);
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.Start();

            // 关闭标准输入，避免 7-Zip 在缺少密码时等待交互输入
            try { process.StandardInput.Close(); } catch { /* ignored */ }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { /* ignored */ }
            });

            Task pumpOut = PumpAsync(process.StandardOutput, stdout, onSegment, cancellationToken);
            Task pumpErr = PumpAsync(process.StandardError, stderr, onSegment, cancellationToken);

            try
            {
                await Task.WhenAll(pumpOut, pumpErr).ConfigureAwait(false);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消，忽略进程被终止产生的异常
            }

            bool canceled = cancellationToken.IsCancellationRequested;
            int exitCode = canceled ? -1 : process.ExitCode;
            return new ProcessResult
            {
                ExitCode = exitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
                Canceled = canceled
            };
        }

        private static async Task PumpAsync(StreamReader reader, StringBuilder full, Action<string>? onSegment, CancellationToken token)
        {
            var buffer = new char[4096];
            var segment = new StringBuilder(256);
            try
            {
                while (true)
                {
                    int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (read <= 0) break;

                    full.Append(buffer, 0, read);
                    for (int i = 0; i < read; i++)
                    {
                        char c = buffer[i];
                        if (c == '\r' || c == '\n')
                        {
                            FlushSegment(segment, onSegment);
                        }
                        else
                        {
                            segment.Append(c);
                        }
                    }
                }
                FlushSegment(segment, onSegment);
            }
            catch (OperationCanceledException) { /* ignored */ }
            catch (ObjectDisposedException) { /* ignored */ }
            catch (IOException) { /* ignored */ }
        }

        private static void FlushSegment(StringBuilder segment, Action<string>? onSegment)
        {
            if (segment.Length == 0) return;
            string text = segment.ToString();
            segment.Clear();
            if (onSegment != null && text.Length > 0)
            {
                try { onSegment(text); } catch { /* ignored */ }
            }
        }
    }
}
