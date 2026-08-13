using _001Halconfirst;
using HalconDotNet;
using MyVisionDemo.core;
using System;

namespace MyVisionDemo
{
    /// <summary>
    /// HALCON 图像处理器（相机2：钢圈有无 + 滤芯有无检测）
    /// 优化点：
    /// 1. ROI 坐标和阈值参数从 AppConfig 读取（可配置）
    /// 2. 使用 HObjectExtensions 简化代码
    /// 3. 规范化资源释放（using 模式）
    /// </summary>
    public class HalconProcessor_Cam2
    {
        public void RunOneImage(string imagePath, out string steelRingResult,
            out string filterResult, out HTuple maxArea1, out HTuple maxArea2)
        {
            steelRingResult = "NG";
            filterResult = "NG";
            maxArea1 = 0;
            maxArea2 = 0;

            var cfg = AppConfig.Current.Detection;

            HObject ho_Image = null;
            HObject ho_ROI_SteelRing1 = null;
            HObject ho_ROI_SteelRing2 = null;
            HObject ho_SteelRingCombined = null;
            HObject ho_SteelRingReduced = null;
            HObject ho_SteelRingThresh = null;
            HObject ho_SteelRingConnected = null;
            HObject ho_SteelRingSelected = null;

            HObject ho_ROI_FilterCircle = null;
            HObject ho_FilterReduced = null;
            HObject ho_ROI_FilterRect = null;
            HObject ho_ROI_FilterTemp = null;
            HObject ho_ROI_FilterFinal = null;
            HObject ho_FilterThresh = null;
            HObject ho_FilterConnected = null;
            HObject ho_FilterSelected = null;

            try
            {
                // 1. 读取图片
                HOperatorSet.ReadImage(out ho_Image, imagePath);

                // ============================================
                // 2. 钢圈有无检测
                // ============================================
                // ROI 圆1: (931.354, 1320.23, 324.103)
                // ROI 圆2: (1027.24, 1315.18, 312.868)
                ho_ROI_SteelRing1 = HObjectExtensions.GenCircle(931.354, 1320.23, 324.103);
                ho_ROI_SteelRing2 = HObjectExtensions.GenCircle(1027.24, 1315.18, 312.868);
                ho_SteelRingCombined = ho_ROI_SteelRing1.ConcatObj(ho_ROI_SteelRing2);
                ho_SteelRingReduced = ho_Image.ReduceDomain(ho_SteelRingCombined);

                ho_SteelRingThresh = ho_SteelRingReduced.Threshold(cfg.Cam2ThresholdMin, cfg.Cam2ThresholdMax);
                ho_SteelRingConnected = ho_SteelRingThresh.Connection();
                ho_SteelRingSelected = ho_SteelRingConnected.SelectShape("area", "and", 100, 99999);

                // ============================================
                // 3. 滤芯有无检测
                // ============================================
                // ROI 圆: (402.507, 1289.1, 232.812)
                // ROI 矩形: (291.34, 1286.23, angle=3.46921rad, 282.037, 112.64)
                // ROI 辅助圆: (438.113, 1283.17, 243.277)
                ho_ROI_FilterCircle = HObjectExtensions.GenCircle(402.507, 1289.1, 232.812);
                ho_FilterReduced = ho_Image.ReduceDomain(ho_ROI_FilterCircle);

                ho_ROI_FilterRect = HObjectExtensions.GenRectangle2(
                    291.34, 1286.23, new HTuple(3.46921).TupleRad(), 282.037, 112.64);
                ho_ROI_FilterTemp = HObjectExtensions.GenCircle(438.113, 1283.17, 243.277);
                ho_ROI_FilterFinal = ho_ROI_FilterRect.Intersection(ho_ROI_FilterTemp);

                ho_FilterThresh = ho_FilterReduced.Threshold(cfg.Cam2ThresholdMin, cfg.Cam2ThresholdMax);
                ho_FilterConnected = ho_FilterThresh.Connection();
                ho_FilterSelected = ho_FilterConnected.SelectShape("area", "and", 100, 99999);

                // ============================================
                // 4. 获取最大面积并判断结果
                // ============================================
                HTuple hv_Areas1, hv_Rows1, hv_Columns1;
                HTuple hv_Areas2, hv_Rows2, hv_Columns2;

                ho_SteelRingSelected.AreaCenter(out hv_Areas1, out hv_Rows1, out hv_Columns1);
                ho_FilterSelected.AreaCenter(out hv_Areas2, out hv_Rows2, out hv_Columns2);

                HOperatorSet.TupleMax(hv_Areas1, out maxArea1);
                HOperatorSet.TupleMax(hv_Areas2, out maxArea2);

                // 使用配置中的阈值判断
                steelRingResult = (maxArea1 > cfg.Cam2SteelRingAreaThreshold) ? "OK" : "NG";
                filterResult = (maxArea2 > cfg.Cam2FilterAreaThreshold) ? "OK" : "NG";
            }
            catch (Exception ex)
            {
                throw new Exception($"相机2检测失败: {ex.Message}", ex);
            }
            finally
            {
                // 确保所有局部 HObject 被释放
                HObjectExtensions.SafeDisposeAll(
                    ho_Image,
                    ho_ROI_SteelRing1, ho_ROI_SteelRing2, ho_SteelRingCombined,
                    ho_SteelRingReduced, ho_SteelRingThresh, ho_SteelRingConnected, ho_SteelRingSelected,
                    ho_ROI_FilterCircle, ho_FilterReduced, ho_ROI_FilterRect,
                    ho_ROI_FilterTemp, ho_ROI_FilterFinal,
                    ho_FilterThresh, ho_FilterConnected, ho_FilterSelected
                );
            }
        }
    }
}
