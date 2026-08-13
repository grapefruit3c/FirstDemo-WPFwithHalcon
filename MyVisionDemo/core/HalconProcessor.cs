using _001Halconfirst;
using HalconDotNet;
using System;
using System.Windows.Media.Imaging;

namespace MyVisionDemo.core
{
    /// <summary>
    /// HALCON 图像处理器（相机1：形状匹配 + 条码识别）
    /// 优化点：
    /// 1. 移除实例字段全局变量，改用局部变量（线程安全）
    /// 2. 条码模型只创建一次，运行时复用（避免每帧创建/销毁）
    /// 3. 使用 HObjectExtensions 简化代码
    /// 4. 规范化资源释放（using 模式 + finally）
    /// 5. 算法参数从 AppConfig 读取（可配置）
    /// </summary>
    internal class HalconProcessor : IDisposable
    {
        // 仅保留必要的模型 ID（线程安全的持久状态）
        private HTuple _modelID = new HTuple();
        private HTuple _barCodeHandle = new HTuple();
        private bool _disposed = false;

        /// <summary>
        /// 初始化模型：加载形状模板 + 创建条码模型（只执行一次）
        /// </summary>
        public void InitModel(string templateImagePath)
        {
            // 加载形状匹配模板
            HOperatorSet.ReadShapeModel(templateImagePath, out _modelID);

            // 创建条码模型（只创建一次，运行时复用）
            HOperatorSet.CreateBarCodeModel(new HTuple(), new HTuple(), out _barCodeHandle);

            var cfg = AppConfig.Current.Detection;
            HOperatorSet.SetBarCodeParam(_barCodeHandle, "element_size_min", cfg.BarcodeElementSizeMin);
            HOperatorSet.SetBarCodeParam(_barCodeHandle, "element_size_max", cfg.BarcodeElementSizeMax);
            HOperatorSet.SetBarCodeParam(_barCodeHandle, "orientation", cfg.BarcodeOrientation);
            HOperatorSet.SetBarCodeParam(_barCodeHandle, "stop_after_result_num", 1);
        }

        /// <summary>
        /// 执行单张图像检测：形状匹配 → ROI 裁剪 → 条码识别
        /// </summary>
        public void RunOneImage(string imagePath, out BitmapSource wpfImage,
            out string barcodeStr, out string statusStr,
            out double centerRow, out double centerCol)
        {
            wpfImage = null;
            barcodeStr = "";
            statusStr = "NG";
            centerRow = 0;
            centerCol = 0;

            var cfg = AppConfig.Current.Detection;

            // 局部变量声明（不再使用实例字段，保证线程安全）
            HObject ho_Image = null;
            HObject ho_ROI = null;
            HObject ho_ReducedImage = null;
            HObject ho_SymbolRegions = null;

            try
            {
                // 1. 读取图像
                HOperatorSet.ReadImage(out ho_Image, imagePath);

                // 2. 设置匹配参数（从配置读取）
                HOperatorSet.SetGenericShapeModelParam(_modelID, "min_score", cfg.MinScore);
                HOperatorSet.SetGenericShapeModelParam(_modelID, "num_matches", cfg.NumMatches);
                HOperatorSet.SetGenericShapeModelParam(_modelID, "border_shape_models", "false");

                // 3. 执行形状匹配
                HTuple hv_MatchResultID, hv_NumMatchResult;
                HOperatorSet.FindGenericShapeModel(ho_Image, _modelID,
                    out hv_MatchResultID, out hv_NumMatchResult);

                // 默认坐标（匹配失败时使用）
                centerRow = 500;
                centerCol = 500;

                // 4. 处理匹配结果
                if ((int)(new HTuple(hv_NumMatchResult.TupleGreater(0))) != 0)
                {
                    // 获取匹配中心坐标
                    HTuple hv_Row, hv_Column, hv_Score;
                    HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "row", out hv_Row);
                    HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "column", out hv_Column);
                    HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "score", out hv_Score);

                    centerRow = hv_Row.ToDArr()[0];
                    centerCol = hv_Column.ToDArr()[0];

                    // 5. 生成 ROI 区域并裁剪
                    int offset = cfg.RoiOffset;
                    ho_ROI = HObjectExtensions.GenRectangle1(
                        centerRow - offset, centerCol - offset,
                        centerRow + offset, centerCol + offset);
                    ho_ReducedImage = ho_Image.ReduceDomain(ho_ROI);

                    // 6. 条码识别（复用已创建的模型）
                    HTuple hv_DecodedDataStrings;
                    HOperatorSet.FindBarCode(ho_ReducedImage, out ho_SymbolRegions,
                        _barCodeHandle, cfg.BarcodeType, out hv_DecodedDataStrings);

                    // 7. 判断结果
                    if (hv_DecodedDataStrings.Length > 0)
                    {
                        barcodeStr = hv_DecodedDataStrings.ToString();
                        statusStr = "OK";
                    }
                    else
                    {
                        barcodeStr = "";
                        statusStr = "NG";
                    }

                    // 释放匹配结果
                    hv_MatchResultID.Dispose();
                    hv_Score.Dispose();
                }
                else
                {
                    statusStr = "NG";
                    barcodeStr = "";
                }
            }
            catch (Exception ex)
            {
                statusStr = "NG";
                barcodeStr = "";
                throw new Exception($"形状匹配/条码检测失败: {ex.Message}", ex);
            }
            finally
            {
                // 确保所有局部 HObject 被释放
                HObjectExtensions.SafeDisposeAll(ho_Image, ho_ROI, ho_ReducedImage, ho_SymbolRegions);
            }
        }

        /// <summary>
        /// 释放模型资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_modelID != null) _modelID.Dispose();
                    if (_barCodeHandle != null)
                    {
                        HOperatorSet.ClearBarCodeModel(_barCodeHandle);
                        _barCodeHandle.Dispose();
                    }
                }
                catch { }
                _disposed = true;
            }
        }
    }
}
