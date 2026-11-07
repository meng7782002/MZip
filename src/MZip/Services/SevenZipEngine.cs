using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MZip.Models;

namespace MZip.Services
{
    /// <summary>
    /// 压缩引擎异常
    /// </summary>
    public class SevenZipException : Exception
    {
        public bool IsPasswordProblem { get; }

        public SevenZipException(string message, bool isPasswordProblem = false, Exception? inner = null)
            : base(message, inner)
        {
            IsPasswordProblem = isPasswordProblem;
        }
    }

    public class ArchiveTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 7-Zip 命令行封装：列表、解压、压缩、追加、删除、测试
    /// </summary>
    public class SevenZipEngine
    {
        private static readonly Regex PercentRegex = new Regex(@"(\d{1,3})\s*%", RegexOptions.Compiled);

        public string SevenZipPath { get; }

        public SevenZipEngine(string? enginePath = null)
        {
            SevenZipPath = !string.IsNullOrWhiteSpace(enginePath) && File.Exists(enginePath)
                ? enginePath!
                : ResolveEnginePath();
        }

        public bool IsAvailable => File.Exists(SevenZipPath);

        public string EngineVersion
        {
            get
            {
                try
                {
                    return FileVersionInfo.GetVersionInfo(SevenZipPath).FileVersion ?? "未知";
                }
                catch
                {
                    return "未知";
                }
            }
        }

        private static string ResolveEnginePath()
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "7z.exe");
            if (File.Exists(bundled)) return bundled;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string installed = Path.Combine(programFiles, "7-Zip", "7z.exe");
            if (File.Exists(installed)) return installed;

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string installedX86 = Path.Combine(programFilesX86, "7-Zip", "7z.exe");
            if (File.Exists(installedX86)) return installedX86;

            return bundled;
        }

        /// <summary>
        /// 读取压缩包列表；codePage &gt; 0 时按指定代码页解析文件名（乱码修复）
        /// </summary>
        public async Task<ArchiveInfo> ListAsync(string archivePath, string? password, CancellationToken token, int codePage = 0)
        {
            EnsureEngine();
            LogHelper.Info($"读取压缩包列表：{archivePath}，代码页：{(codePage > 0 ? codePage.ToString() : "自动")}");
            if (!File.Exists(archivePath))
            {
                throw new SevenZipException("压缩包不存在：" + archivePath);
            }

            var args = new List<string> { "l", "-slt", "-sccUTF-8", PasswordArg(password) };
            if (codePage > 0)
            {
                args.Add("-mcp=" + codePage);
            }
            args.Add(archivePath);

            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, null, token).ConfigureAwait(false);
            if (result.Canceled) throw new OperationCanceledException();

            if (result.ExitCode >= 2)
            {
                bool pwdProblem = IsPasswordError(result.AllOutput);
                LogHelper.Warn("读取列表失败：" + result.AllOutput.Trim());
                throw new SevenZipException(BuildFriendlyMessage(result), pwdProblem);
            }

            ArchiveInfo info = ParseList(archivePath, result.StdOut);
            LogHelper.Info($"列表读取完成：{info.FileCount} 个文件，{info.FolderCount} 个目录，原始大小 {info.TotalSize} 字节");
            return info;
        }

        /// <summary>
        /// 解压压缩包；entries 为空表示全部解压
        /// </summary>
        public async Task ExtractAsync(
            string archivePath,
            string outputDirectory,
            string? password,
            IReadOnlyList<string>? entries,
            string overwriteMode,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            EnsureEngine();
            Directory.CreateDirectory(outputDirectory);

            LogHelper.Info($"开始解压：{archivePath} -> {outputDirectory}，覆盖模式：{overwriteMode}，条目数：{(entries == null ? "全部" : entries.Count.ToString())}");

            var args = new List<string>
            {
                "x",
                "-o" + outputDirectory,
                "-bsp1",
                "-bb1",
                "-sccUTF-8"
            };

            switch (overwriteMode)
            {
                case "Skip":
                    args.Add("-aos");
                    args.Add("-y");
                    break;
                case "Rename":
                    args.Add("-aou");
                    args.Add("-y");
                    break;
                default:
                    args.Add("-y");
                    break;
            }

            args.Add(PasswordArg(password));
            args.Add(archivePath);
            if (entries != null && entries.Count > 0)
            {
                args.AddRange(entries);
            }

            var parser = new ProgressParser(progress, "正在解压");
            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, parser.Parse, token).ConfigureAwait(false);

            if (result.Canceled) throw new OperationCanceledException();
            if (result.ExitCode >= 2 || IsPasswordError(result.AllOutput))
            {
                bool pwdProblem = IsPasswordError(result.AllOutput);
                LogHelper.Error("解压失败：" + result.AllOutput.Trim());
                throw new SevenZipException(BuildFriendlyMessage(result), pwdProblem);
            }

            if (result.ExitCode == 1)
            {
                LogHelper.Warn("解压完成但存在警告：" + result.AllOutput.Trim());
            }
            LogHelper.Info("解压完成：" + archivePath);
        }

        /// <summary>是否二层压缩包（.tar.gz / .tgz / .tar.bz2 / .tbz2 / .tar.xz / .txz）</summary>
        public static bool IsTwoStageArchive(string archivePath)
        {
            string lower = archivePath.ToLowerInvariant();
            return lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")
                || lower.EndsWith(".tar.bz2") || lower.EndsWith(".tbz2")
                || lower.EndsWith(".tar.xz") || lower.EndsWith(".txz");
        }

        /// <summary>
        /// 二层压缩包一次解完（FR-98）：先解出 tar 到临时目录，再把 tar 解到最终目录，清理临时目录
        /// </summary>
        public async Task ExtractTwoStageAsync(
            string archivePath,
            string outputDirectory,
            string? password,
            string overwriteMode,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            Directory.CreateDirectory(outputDirectory);
            string tempDir = Path.Combine(Path.GetTempPath(), "MZip_tgz_" + Guid.NewGuid().ToString("N"));
            LogHelper.Info($"二层解压开始：{archivePath} → {outputDirectory}（临时目录 {tempDir}）");

            var stageOne = new Progress<OperationProgress>(p =>
                progress?.Report(new OperationProgress { Percent = p.Percent * 0.5, CurrentItem = p.CurrentItem, Stage = "解出数据包" }));
            var stageTwo = new Progress<OperationProgress>(p =>
                progress?.Report(new OperationProgress { Percent = 50 + p.Percent * 0.5, CurrentItem = p.CurrentItem, Stage = "解出最终文件" }));

            try
            {
                await ExtractAsync(archivePath, tempDir, password, null, "Overwrite", stageOne, token).ConfigureAwait(false);

                string? tar = Directory.EnumerateFiles(tempDir, "*.tar", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (tar == null)
                {
                    // 未得到 tar（内容并非 tar），直接把中间产物移动到输出目录
                    LogHelper.Info("二层解压：内层不是 tar，直接转移产物");
                    foreach (string file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(tempDir, file);
                        string dest = Path.Combine(outputDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Move(file, dest, true);
                    }
                    return;
                }

                await ExtractAsync(tar, outputDirectory, null, null, overwriteMode, stageTwo, token).ConfigureAwait(false);
                LogHelper.Info("二层解压完成：" + archivePath);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    LogHelper.Error("清理二层解压临时目录失败：" + tempDir, ex);
                }
            }
        }

        /// <summary>
        /// 新建压缩包（目标存在时会先删除）
        /// </summary>
        public async Task CreateAsync(
            CreateArchiveOptions options,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            EnsureEngine();
            if (options.Sources.Count == 0) throw new SevenZipException("请至少选择一个文件或文件夹。");
            if (string.IsNullOrWhiteSpace(options.TargetPath)) throw new SevenZipException("请选择压缩包保存位置。");
            ValidateCommandLength(options.Sources);

            string? targetDir = Path.GetDirectoryName(options.TargetPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            if (File.Exists(options.TargetPath))
            {
                File.Delete(options.TargetPath);
            }
            if (options.VolumeMB > 0)
            {
                DeleteVolumeSeries(options.TargetPath);
            }

            string format = options.Format == "zip" ? "zip" : "7z";
            LogHelper.Info($"开始创建压缩包：{options.TargetPath}，格式 {format}，级别 {options.Level}，来源 {options.Sources.Count} 项，分卷 {options.VolumeMB}MB，固实 {options.Solid}");

            var args = new List<string>
            {
                "a",
                "-t" + format,
                "-mx=" + options.Level,
                "-bsp1",
                "-bb1",
                "-sccUTF-8"
            };
            if (options.VolumeMB > 0)
            {
                args.Add("-v" + options.VolumeMB + "m");
            }
            if (format == "7z" && options.Solid)
            {
                args.Add("-ms=on");
            }
            if (!string.IsNullOrEmpty(options.Password))
            {
                args.Add("-p" + options.Password);
                if (format == "7z" && options.EncryptNames)
                {
                    args.Add("-mhe=on");
                }
                if (format == "zip")
                {
                    args.Add("-mem=AES256");
                }
            }
            args.Add(options.TargetPath);
            args.AddRange(options.Sources);

            var parser = new ProgressParser(progress, "正在压缩");
            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, parser.Parse, token).ConfigureAwait(false);

            if (result.Canceled)
            {
                TryDelete(options.TargetPath);
                if (options.VolumeMB > 0) DeleteVolumeSeries(options.TargetPath);
                throw new OperationCanceledException();
            }
            if (result.ExitCode >= 2)
            {
                TryDelete(options.TargetPath);
                if (options.VolumeMB > 0) DeleteVolumeSeries(options.TargetPath);
                LogHelper.Error("创建压缩包失败：" + result.AllOutput.Trim());
                throw new SevenZipException(BuildFriendlyMessage(result));
            }
            if (result.ExitCode == 1)
            {
                LogHelper.Warn("创建压缩包完成但存在警告：" + result.AllOutput.Trim());
            }

            // ZIP 注释：7z.exe 不支持，后处理写入 EOCD 注释
            if (format == "zip" && !string.IsNullOrEmpty(options.Comment))
            {
                try
                {
                    string commentTarget = options.VolumeMB > 0
                        ? FindLastVolume(options.TargetPath) ?? options.TargetPath
                        : options.TargetPath;
                    ZipCommentWriter.SetComment(commentTarget, options.Comment);
                    LogHelper.Info("ZIP 注释写入完成：" + commentTarget);
                }
                catch (Exception ex)
                {
                    LogHelper.Error("ZIP 注释写入失败", ex);
                }
            }
            LogHelper.Info("创建压缩包完成：" + options.TargetPath);
        }

        /// <summary>
        /// 把压缩包中单个条目解压到内存流（x -so），用于图片预览/包内 MD5
        /// </summary>
        public async Task<MemoryStream> ExtractEntryToStreamAsync(
            string archivePath,
            string entryPath,
            string? password,
            CancellationToken token)
        {
            EnsureEngine();
            LogHelper.Info($"流式解出条目：{archivePath} -> {entryPath}");

            var psi = new ProcessStartInfo(SevenZipPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            psi.ArgumentList.Add("x");
            psi.ArgumentList.Add("-so");
            psi.ArgumentList.Add(PasswordArg(password));
            psi.ArgumentList.Add(archivePath);
            psi.ArgumentList.Add(entryPath);

            using var process = new Process { StartInfo = psi };
            var stderr = new System.Text.StringBuilder();
            process.Start();
            try { process.StandardInput.Close(); } catch { /* ignored */ }

            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { /* ignored */ }
            });

            Task errTask = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        stderr.AppendLine(line);
                    }
                }
                catch { /* ignored */ }
            }, CancellationToken.None);

            var memory = new MemoryStream();
            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(memory, 81920, token).ConfigureAwait(false);
                await errTask.ConfigureAwait(false);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                memory.Dispose();
                throw new OperationCanceledException();
            }

            if (token.IsCancellationRequested)
            {
                memory.Dispose();
                throw new OperationCanceledException();
            }

            string errText = stderr.ToString();
            if (process.ExitCode >= 2 || IsPasswordError(errText))
            {
                memory.Dispose();
                LogHelper.Error("流式解出失败：" + errText.Trim());
                throw new SevenZipException(
                    IsPasswordError(errText) ? "压缩包已加密，密码错误或未提供密码。" : "解出条目失败，详情请查看日志。",
                    IsPasswordError(errText));
            }

            memory.Position = 0;
            LogHelper.Info($"流式解出完成：{entryPath}，{memory.Length} 字节");
            return memory;
        }

        /// <summary>
        /// 向已有压缩包追加文件/文件夹
        /// </summary>
        public async Task AddAsync(
            string archivePath,
            string archiveFormat,
            IReadOnlyList<string> sources,
            string? password,
            int level,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            EnsureEngine();
            if (sources.Count == 0) throw new SevenZipException("请选择要添加的文件或文件夹。");
            ValidateCommandLength(sources);
            LogHelper.Info($"追加内容：{archivePath}，数量 {sources.Count}");

            var args = new List<string>
            {
                "a",
                "-mx=" + level,
                "-bsp1",
                "-bb1",
                "-sccUTF-8",
                PasswordArg(password)
            };
            if (password != null && password.Length > 0 && archiveFormat.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-mem=AES256");
            }
            args.Add(archivePath);
            args.AddRange(sources);

            var parser = new ProgressParser(progress, "正在添加");
            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, parser.Parse, token).ConfigureAwait(false);

            if (result.Canceled) throw new OperationCanceledException();
            if (result.ExitCode >= 2)
            {
                LogHelper.Error("追加内容失败：" + result.AllOutput.Trim());
                throw new SevenZipException(BuildFriendlyMessage(result), IsPasswordError(result.AllOutput));
            }
            LogHelper.Info("追加内容完成：" + archivePath);
        }

        /// <summary>
        /// 从压缩包删除条目（仅 ZIP / 7Z）
        /// </summary>
        public async Task DeleteAsync(
            string archivePath,
            string? password,
            IReadOnlyList<string> entries,
            CancellationToken token)
        {
            EnsureEngine();
            if (entries.Count == 0) throw new SevenZipException("请选择要删除的条目。");
            LogHelper.Info($"删除条目：{archivePath}，数量 {entries.Count}");

            var args = new List<string>
            {
                "d",
                "-y",
                "-sccUTF-8",
                PasswordArg(password),
                archivePath
            };
            args.AddRange(entries);

            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, null, token).ConfigureAwait(false);
            if (result.Canceled) throw new OperationCanceledException();
            if (result.ExitCode >= 2)
            {
                LogHelper.Error("删除条目失败：" + result.AllOutput.Trim());
                throw new SevenZipException(BuildFriendlyMessage(result), IsPasswordError(result.AllOutput));
            }
            LogHelper.Info("删除条目完成：" + archivePath);
        }

        /// <summary>
        /// 测试压缩包完整性
        /// </summary>
        public async Task<ArchiveTestResult> TestAsync(
            string archivePath,
            string? password,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            EnsureEngine();
            LogHelper.Info("开始测试压缩包：" + archivePath);

            var args = new List<string>
            {
                "t",
                "-bsp1",
                "-sccUTF-8",
                PasswordArg(password),
                archivePath
            };

            var parser = new ProgressParser(progress, "正在校验");
            var result = await ProcessRunner.RunAsync(SevenZipPath, args, null, parser.Parse, token).ConfigureAwait(false);

            if (result.Canceled) throw new OperationCanceledException();
            bool passwordProblem = IsPasswordError(result.AllOutput);
            if (result.ExitCode >= 2)
            {
                LogHelper.Warn("测试压缩包失败：" + result.AllOutput.Trim());
                return new ArchiveTestResult
                {
                    Success = false,
                    Message = passwordProblem ? "压缩包已加密，密码错误或未提供密码。" : BuildFriendlyMessage(result)
                };
            }

            LogHelper.Info("测试压缩包完成：" + archivePath);
            return new ArchiveTestResult
            {
                Success = true,
                Message = result.ExitCode == 1
                    ? "校验完成，但部分文件存在警告，请查看日志。"
                    : "校验通过，压缩包完整。"
            };
        }

        private void EnsureEngine()
        {
            if (!IsAvailable)
            {
                LogHelper.Error("压缩引擎缺失：" + SevenZipPath);
                throw new SevenZipException("压缩引擎 7z.exe 缺失，请确认程序 tools 目录完整。");
            }
        }

        private static string PasswordArg(string? password)
        {
            // 始终传入 -p：空密码时避免 7-Zip 在控制台等待交互输入
            return "-p" + (password ?? string.Empty);
        }

        private static void ValidateCommandLength(IReadOnlyList<string> sources)
        {
            int total = sources.Sum(s => s.Length + 3);
            if (total > 30000)
            {
                throw new SevenZipException("选择的文件过多，命令行长度超出限制，请分批压缩或减少选择项。");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                LogHelper.Error("清理临时文件失败：" + path, ex);
            }
        }

        /// <summary>
        /// 删除分卷序列 xxx.001 ~ xxx.999
        /// </summary>
        private static void DeleteVolumeSeries(string targetPath)
        {
            for (int i = 1; i <= 999; i++)
            {
                string vol = targetPath + "." + i.ToString("000");
                if (File.Exists(vol)) TryDelete(vol);
            }
        }

        /// <summary>
        /// 查找分卷序列中最后一个分卷文件
        /// </summary>
        private static string? FindLastVolume(string targetPath)
        {
            string? last = null;
            for (int i = 1; i <= 999; i++)
            {
                string vol = targetPath + "." + i.ToString("000");
                if (File.Exists(vol)) last = vol;
                else break;
            }
            return last;
        }

        private static bool IsPasswordError(string output)
        {
            if (string.IsNullOrEmpty(output)) return false;
            return output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
                || output.Contains("open encrypted archive", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Enter password", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Can not open encrypted", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Cannot open encrypted", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFriendlyMessage(ProcessResult result)
        {
            string all = result.AllOutput;
            if (IsPasswordError(all))
            {
                return "压缩包已加密，密码错误或未提供密码。";
            }
            if (all.Contains("Can't open as archive") || all.Contains("Cannot open the file as archive"))
            {
                return "无法打开压缩包，文件可能已损坏或格式不受支持。";
            }
            if (all.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return "访问被拒绝，请检查文件权限或文件是否被其他程序占用。";
            }
            if (all.Contains("There is not enough space", StringComparison.OrdinalIgnoreCase)
                || all.Contains("No space left", StringComparison.OrdinalIgnoreCase))
            {
                return "磁盘空间不足，请清理空间后重试。";
            }
            if (all.Contains("The system cannot find the file", StringComparison.OrdinalIgnoreCase)
                || all.Contains("The system cannot find the path", StringComparison.OrdinalIgnoreCase))
            {
                return "文件或路径不存在，请检查后重试。";
            }

            string? errorLine = all
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(errorLine))
            {
                return "操作失败：" + errorLine;
            }
            return $"7-Zip 执行失败（返回码 {result.ExitCode}），详情请查看日志。";
        }

        /// <summary>
        /// 解析 7z l -slt 输出
        /// </summary>
        private static ArchiveInfo ParseList(string archivePath, string output)
        {
            var info = new ArchiveInfo { ArchivePath = archivePath };
            try { info.ArchiveSize = new FileInfo(archivePath).Length; } catch { /* ignored */ }

            var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool entriesStarted = false;
            bool headerParsed = false;

            void FlushBlock()
            {
                if (block.Count == 0) return;

                if (!entriesStarted && !headerParsed && block.ContainsKey("Type"))
                {
                    info.Format = block["Type"];
                    headerParsed = true;
                    block.Clear();
                    return;
                }

                if (entriesStarted && block.TryGetValue("Path", out string? entryPath) && !string.IsNullOrEmpty(entryPath))
                {
                    var item = BuildItem(entryPath, block);
                    info.Entries.Add(item);
                    if (item.IsFolder) info.FolderCount++;
                    else
                    {
                        info.FileCount++;
                        info.TotalSize += item.Size;
                    }
                }
                block.Clear();
            }

            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    FlushBlock();
                    continue;
                }
                if (trimmed.StartsWith("----------", StringComparison.Ordinal))
                {
                    FlushBlock();
                    entriesStarted = true;
                    continue;
                }

                int index = line.IndexOf(" = ", StringComparison.Ordinal);
                if (index > 0)
                {
                    string key = line.Substring(0, index).Trim();
                    string value = line.Substring(index + 3).Trim();
                    block[key] = value;
                }
            }
            FlushBlock();

            info.Entries = info.Entries
                .OrderBy(e => e.IsFolder ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return info;
        }

        private static ArchiveItem BuildItem(string fullPath, Dictionary<string, string> block)
        {
            string cleanPath = fullPath.TrimEnd('\\', '/');
            int sep = Math.Max(cleanPath.LastIndexOf('\\'), cleanPath.LastIndexOf('/'));
            string name = sep >= 0 ? cleanPath.Substring(sep + 1) : cleanPath;

            bool isFolder = block.TryGetValue("Folder", out string? folder) && folder == "+"
                || (block.TryGetValue("Attributes", out string? attr) && attr.Contains('D', StringComparison.OrdinalIgnoreCase));

            var item = new ArchiveItem
            {
                FullPath = cleanPath,
                Name = string.IsNullOrEmpty(name) ? cleanPath : name,
                IsFolder = isFolder,
                Attributes = block.TryGetValue("Attributes", out string? attributes) ? attributes : string.Empty,
                Crc = block.TryGetValue("CRC", out string? crc) ? crc : string.Empty,
                Method = block.TryGetValue("Method", out string? method) ? method : string.Empty,
                IsEncrypted = block.TryGetValue("Encrypted", out string? encrypted) && encrypted == "+"
            };

            if (block.TryGetValue("Size", out string? sizeText))
            {
                item.Size = ParseLong(sizeText);
            }
            if (block.TryGetValue("Packed Size", out string? packedText))
            {
                item.PackedSize = ParseLong(packedText);
            }
            if (block.TryGetValue("Modified", out string? modifiedText))
            {
                item.Modified = ParseDate(modifiedText);
            }
            return item;
        }

        private static long ParseLong(string text)
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
        }

        private static DateTime? ParseDate(string text)
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// 解析 7-Zip 进度输出
        /// </summary>
        private class ProgressParser
        {
            private readonly IProgress<OperationProgress>? _progress;
            private readonly string _stage;
            private readonly object _gate = new object();
            private double _lastPercent = -1;
            private string _lastItem = string.Empty;

            public ProgressParser(IProgress<OperationProgress>? progress, string stage)
            {
                _progress = progress;
                _stage = stage;
            }

            public void Parse(string segment)
            {
                if (_progress == null) return;
                string text = segment.Trim();
                if (text.Length == 0) return;

                if ((text[0] == '-' || text[0] == '+') && text.Length > 2)
                {
                    string item = text.Substring(2).Trim().TrimEnd('\\', '/');
                    if (item.Length > 0 && item != _lastItem)
                    {
                        double percent;
                        lock (_gate)
                        {
                            _lastItem = item;
                            percent = _lastPercent < 0 ? 0 : _lastPercent;
                        }
                        _progress.Report(new OperationProgress
                        {
                            Percent = percent,
                            CurrentItem = item,
                            Stage = _stage
                        });
                    }
                    return;
                }

                foreach (Match match in PercentRegex.Matches(text))
                {
                    double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (value < 0 || value > 100) continue;

                    double reportValue;
                    string item;
                    lock (_gate)
                    {
                        if (value == _lastPercent) continue;
                        _lastPercent = value;
                        item = _lastItem;
                    }
                    reportValue = value;
                    _progress.Report(new OperationProgress
                    {
                        Percent = reportValue,
                        CurrentItem = item.Length > 0 ? item : _stage,
                        Stage = _stage
                    });
                }
            }
        }
    }
}
