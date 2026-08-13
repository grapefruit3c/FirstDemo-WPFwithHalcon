using System;
using System.IO;
using System.Text;

namespace MyVisionDemo.core
{
    /// <summary>
    /// 生产日志记录器：CSV 格式持久化每周期检测结果
    /// 参考 OpenIVS 的 ProductionLogService 设计
    /// </summary>
    public class ProductionLogger
    {
        private readonly string _logDirectory;

        public ProductionLogger(string logDirectory)
        {
            _logDirectory = logDirectory;
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>
        /// 记录一条检测周期日志
        /// </summary>
        public void LogCycle(string barcode, string status, string cam2Ring,
            string cam2Filter, double matchMs, double totalMs, string imagePath)
        {
            try
            {
                string fileName = $"production_{DateTime.Now:yyyyMMdd}.csv";
                string filePath = Path.Combine(_logDirectory, fileName);
                bool fileExists = File.Exists(filePath);

                using (var writer = new StreamWriter(filePath, true, Encoding.UTF8))
                {
                    // 首次创建时写入表头
                    if (!fileExists)
                    {
                        writer.WriteLine("时间,条码,检测结果,钢圈检测,滤芯检测,匹配耗时(ms),总耗时(ms),图片路径");
                    }

                    string time = DateTime.Now.ToString("HH:mm:ss.fff");
                    writer.WriteLine($"{time},{barcode},{status},{cam2Ring},{cam2Filter},{matchMs:F1},{totalMs:F1},{imagePath}");
                }
            }
            catch { }
        }
    }
}
