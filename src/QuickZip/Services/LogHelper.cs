using System;
using System.IO;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace QuickZip.Services
{
    /// <summary>
    /// log4net 日志封装：日志文件输出到程序目录 logs 下，按天滚动
    /// </summary>
    public static class LogHelper
    {
        private static bool _initialized;
        private static readonly ILog Logger = LogManager.GetLogger("QuickZip");

        public static void Initialize()
        {
            if (_initialized) return;
            try
            {
                string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);

                var appender = new RollingFileAppender
                {
                    File = Path.Combine(logDir, "quickzip"),
                    AppendToFile = true,
                    RollingStyle = RollingFileAppender.RollingMode.Date,
                    DatePattern = "yyyy-MM-dd'.log'",
                    StaticLogFileName = false,
                    MaxSizeRollBackups = 30,
                    Layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger - %message%newline")
                };
                appender.ActivateOptions();

                var hierarchy = (Hierarchy)LogManager.GetRepository(Assembly.GetExecutingAssembly());
                hierarchy.Root.AddAppender(appender);
                hierarchy.Root.Level = Level.Debug;
                hierarchy.Configured = true;

                _initialized = true;
                Logger.Info("日志系统初始化完成，目录：" + logDir);
            }
            catch
            {
                // 日志初始化失败时不阻断程序运行
            }
        }

        public static void Info(string message) => Logger.Info(message);

        public static void Debug(string message) => Logger.Debug(message);

        public static void Warn(string message) => Logger.Warn(message);

        public static void Error(string message) => Logger.Error(message);

        public static void Error(string message, Exception ex) => Logger.Error(message, ex);
    }
}
