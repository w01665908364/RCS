using System;
using System.IO;

namespace RobotControlSystem.Utils
{
    public static class LogHelper
    {
        private static readonly object _lock = new object();
        private static string _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RobotControlSystem",
            "Logs");

        /// <summary>
        /// 记录日志到文件
        /// </summary>
        public static void LogToFile(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                try
                {
                    // 确保日志目录存在
                    if (!Directory.Exists(_logDirectory))
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }

                    // 日志文件名（按天）
                    string fileName = $"RobotLog_{DateTime.Now:yyyyMMdd}.log";
                    string filePath = Path.Combine(_logDirectory, fileName);

                    // 日志格式
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

                    // 写入文件
                    File.AppendAllText(filePath, logEntry + Environment.NewLine);

                    // 同时输出到控制台
                    Console.WriteLine(logEntry);
                }
                catch
                {
                    // 日志记录失败时不抛出异常
                }
            }
        }

        /// <summary>
        /// 日志级别
        /// </summary>
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }
    }
}