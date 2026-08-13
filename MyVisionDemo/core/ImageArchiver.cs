using HalconDotNet;
using System;
using System.IO;

namespace MyVisionDemo.core
{
    /// <summary>
    /// 图像自动存档：按 日期/OK或NG/时间戳 自动保存检测图片
    /// 参考 OpenIVS 的图像存档设计
    /// </summary>
    public class ImageArchiver
    {
        private readonly string _archiveRoot;
        private readonly bool _saveOKImages;
        private readonly bool _saveNGImages;
        private readonly int _jpgQuality;

        public ImageArchiver(string archiveRoot, bool saveOKImages, bool saveNGImages, int jpgQuality)
        {
            _archiveRoot = archiveRoot;
            _saveOKImages = saveOKImages;
            _saveNGImages = saveNGImages;
            _jpgQuality = jpgQuality;
        }

        /// <summary>
        /// 保存检测图片
        /// </summary>
        /// <param name="imagePath">原始图片路径</param>
        /// <param name="isOK">检测结果是否 OK</param>
        /// <param name="barcode">条码（用于文件名）</param>
        /// <returns>保存路径，未保存返回空</returns>
        public string ArchiveImage(string imagePath, bool isOK, string barcode)
        {
            try
            {
                // 根据配置决定是否保存
                if (isOK && !_saveOKImages) return "";
                if (!isOK && !_saveNGImages) return "";

                // 构建存档路径: ArchiveRoot/2026-08-13/OK/143025_123_barcode.jpg
                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string resultStr = isOK ? "OK" : "NG";
                string dir = Path.Combine(_archiveRoot, dateStr, resultStr);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 文件名: 时间_条码
                string timeStr = DateTime.Now.ToString("HHmmss_fff");
                string safeBarcode = string.IsNullOrEmpty(barcode) ? "nobarcode" :
                    barcode.Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "")
                           .Replace("?", "").Replace("\"", "").Replace("<", "").Replace(">", "").Replace("|", "");
                if (safeBarcode.Length > 30) safeBarcode = safeBarcode.Substring(0, 30);
                string fileName = $"{timeStr}_{safeBarcode}.jpg";
                string destPath = Path.Combine(dir, fileName);

                // 复制文件（如果源文件存在）
                if (File.Exists(imagePath))
                {
                    File.Copy(imagePath, destPath, true);
                }

                return destPath;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 保存 Halcon 图像窗口截图
        /// </summary>
        public string ArchiveHalconImage(HImage image, bool isOK, string barcode)
        {
            try
            {
                if (isOK && !_saveOKImages) return "";
                if (!isOK && !_saveNGImages) return "";

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string resultStr = isOK ? "OK" : "NG";
                string dir = Path.Combine(_archiveRoot, dateStr, resultStr);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string timeStr = DateTime.Now.ToString("HHmmss_fff");
                string safeBarcode = string.IsNullOrEmpty(barcode) ? "nobarcode" : barcode.Substring(0, Math.Min(20, barcode.Length));
                string fileName = $"{timeStr}_{safeBarcode}.jpg";
                string destPath = Path.Combine(dir, fileName);

                image.WriteImage("jpg", _jpgQuality, destPath);
                return destPath;
            }
            catch
            {
                return "";
            }
        }
    }
}
