using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MZip.Models;

namespace MZip.Services
{
    /// <summary>
    /// 自解压 EXE 生成：7z.sfx + config.txt + 7z 数据 二进制拼接
    /// </summary>
    public class SfxService
    {
        private readonly SevenZipEngine _engine;

        public SfxService(SevenZipEngine engine)
        {
            _engine = engine;
        }

        public string SfxModulePath => Path.Combine(AppContext.BaseDirectory, "tools", "7z.sfx");

        public bool IsAvailable => File.Exists(SfxModulePath);

        /// <summary>
        /// 生成自解压包：先用 7Z 压缩到临时文件，再与 sfx 模块拼接
        /// </summary>
        public async Task CreateSfxAsync(
            CreateArchiveOptions options,
            IProgress<OperationProgress>? progress,
            CancellationToken token)
        {
            if (!IsAvailable)
            {
                LogHelper.Error("7z.sfx 模块缺失：" + SfxModulePath);
                throw new SevenZipException("自解压模块 7z.sfx 缺失，请确认程序 tools 目录完整。");
            }

            string tempArchive = Path.Combine(Path.GetTempPath(), "mzip_sfx_" + Guid.NewGuid().ToString("N") + ".7z");
            LogHelper.Info($"开始生成自解压包：{options.TargetPath}，临时归档 {tempArchive}");
            try
            {
                // 阶段 1：压缩为临时 7Z（进度映射 0-90%）
                var innerOptions = new CreateArchiveOptions
                {
                    Format = "7z",
                    Level = options.Level,
                    Password = options.Password,
                    EncryptNames = options.EncryptNames,
                    Solid = options.Solid,
                    TargetPath = tempArchive,
                    Sources = options.Sources
                };

                var innerProgress = progress == null ? null : new Progress<OperationProgress>(p =>
                {
                    progress.Report(new OperationProgress
                    {
                        Percent = p.Percent * 0.9,
                        CurrentItem = p.CurrentItem,
                        Stage = p.Stage
                    });
                });

                await _engine.CreateAsync(innerOptions, innerProgress, token).ConfigureAwait(false);

                // 阶段 2：拼接 sfx + config + 7z 数据
                progress?.Report(new OperationProgress { Percent = 92, CurrentItem = "正在生成自解压程序", Stage = "正在压缩" });
                await Task.Run(() => WriteSfx(options.TargetPath, tempArchive), token).ConfigureAwait(false);

                progress?.Report(new OperationProgress { Percent = 100, CurrentItem = options.TargetPath, Stage = "正在压缩" });
                LogHelper.Info("自解压包生成完成：" + options.TargetPath);
            }
            catch
            {
                try { if (File.Exists(options.TargetPath)) File.Delete(options.TargetPath); } catch { /* ignored */ }
                throw;
            }
            finally
            {
                try { if (File.Exists(tempArchive)) File.Delete(tempArchive); } catch { /* ignored */ }
            }
        }

        private void WriteSfx(string targetPath, string archivePath)
        {
            string config = BuildConfig();
            byte[] configBytes = new UTF8Encoding(false).GetBytes(config);

            using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sfx = File.OpenRead(SfxModulePath))
            using (var archive = File.OpenRead(archivePath))
            {
                sfx.CopyTo(output);
                output.Write(configBytes, 0, configBytes.Length);
                archive.CopyTo(output);
            }
        }

        /// <summary>
        /// 7z.sfx 标准配置块，默认解压到 exe 所在目录
        /// </summary>
        private static string BuildConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine(";!@Install@!UTF-8!");
            sb.AppendLine("Title=\"MZip 自解压包\"");
            sb.AppendLine("BeginPrompt=\"即将解压文件到当前目录，是否继续？\"");
            sb.AppendLine("ExtractTitle=\"正在解压\"");
            sb.AppendLine("ExtractDialogText=\"正在解压文件，请稍候…\"");
            sb.AppendLine("ExtractCancelText=\"取消\"");
            sb.AppendLine(";!@InstallEnd@!");
            return sb.ToString();
        }
    }
}
