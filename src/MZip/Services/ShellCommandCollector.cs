using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace MZip.Services
{
    /// <summary>
    /// 资源管理器多选压缩时，每个条目会启动一个进程；
    /// 通过命名互斥量 + 命名管道把路径汇总到首个实例，保证只生成一个压缩包。
    /// </summary>
    public static class ShellCommandCollector
    {
        private const string MutexName = @"Local\MZip.Compress.Collect";
        private const string PipeName = "MZip.Compress";
        private const int SilenceTimeoutMs = 600;
        private const int TotalTimeoutMs = 5000;

        /// <summary>
        /// 汇总多选路径。返回 null 表示当前进程是后续实例（已把路径转发给首个实例，应直接退出）。
        /// </summary>
        public static List<string>? Collect(IEnumerable<string> initialPaths)
        {
            var paths = new List<string>(initialPaths);
            bool isOwner;
            var mutex = new Mutex(true, MutexName, out isOwner);

            if (!isOwner)
            {
                bool sent = SendToOwner(paths);
                if (!sent)
                {
                    LogHelper.Warn("多选压缩：连接首个实例失败，忽略 " + paths.Count + " 个路径");
                }
                return null;
            }

            try
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(TotalTimeoutMs);
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                while (DateTime.UtcNow < deadline)
                {
                    using var timeoutCts = new CancellationTokenSource(SilenceTimeoutMs);
                    try
                    {
                        server.WaitForConnectionAsync(timeoutCts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    using (var reader = new StreamReader(server, System.Text.Encoding.UTF8, false, 1024, leaveOpen: true))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                paths.Add(line);
                            }
                        }
                    }

                    server.Disconnect();
                }

                LogHelper.Info("多选压缩：共收集 " + paths.Count + " 个路径");
                return paths;
            }
            catch (Exception ex)
            {
                LogHelper.Error("多选压缩：路径收集失败", ex);
                return paths;
            }
            finally
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                    // 忽略重复释放异常
                }
                mutex.Dispose();
            }
        }

        private static bool SendToOwner(IReadOnlyList<string> paths)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(200);
                    using var writer = new StreamWriter(client, System.Text.Encoding.UTF8, 1024, leaveOpen: true);
                    foreach (string path in paths)
                    {
                        writer.WriteLine(path);
                    }
                    writer.Flush();
                    return true;
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }
            return false;
        }
    }
}
