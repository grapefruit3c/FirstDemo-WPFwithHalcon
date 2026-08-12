//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

using HalconDotNet;
using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVisionDemo
{
    public class HalconProcessor_Cam2
    {
        // 不需要全局变量，因为是单图调用
        public void RunOneImage(string imagePath, out string steelRingResult, out string filterResult, out HTuple maxArea1, out HTuple maxArea2)
        {
            steelRingResult = "NG";
            filterResult = "NG";
            maxArea1 = 0;
            maxArea2 = 0;

            HObject ho_Image = null;
            HObject ho_ROI_0 = null, ho_ROI_1 = null, ho_ROI1 = null;
            HObject ho_ImageReduced1 = null, ho_ROI_2 = null, ho_ImageReduced2 = null;
            HObject ho_ROI_3 = null, ho_TMP_Region = null, ho_ROI_4 = null;
            HObject ho_ConnectedRegions1 = null, ho_ConnectedRegions2 = null;
            HObject ho_SelectedRegions1 = null, ho_SelectedRegions2 = null;

            try
            {
                // 1. 从路径读取图片
                HOperatorSet.GenEmptyObj(out ho_Image);
                HOperatorSet.ReadImage(out ho_Image, imagePath);

                // 2. 钢圈有无检测 (ROI 0 和 1)
                HOperatorSet.GenCircle(out ho_ROI_0, 931.354, 1320.23, 324.103);
                HOperatorSet.GenCircle(out ho_ROI_1, 1027.24, 1315.18, 312.868);
                HOperatorSet.ConcatObj(ho_ROI_0, ho_ROI_1, out ho_ROI1);
                HOperatorSet.ReduceDomain(ho_Image, ho_ROI1, out ho_ImageReduced1);
                HOperatorSet.Threshold(ho_ImageReduced1, out ho_ROI1, 70, 130);
                HOperatorSet.Connection(ho_ROI1, out ho_ConnectedRegions1);
                HOperatorSet.SelectShape(ho_ConnectedRegions1, out ho_SelectedRegions1, "area", "and", 100, 99999);

                // 3. 滤芯有无检测 (ROI 2, 3, 4)
                HOperatorSet.GenCircle(out ho_ROI_2, 402.507, 1289.1, 232.812);
                HOperatorSet.ReduceDomain(ho_Image, ho_ROI_2, out ho_ImageReduced2);
                HOperatorSet.GenRectangle2(out ho_ROI_3, 291.34, 1286.23, (new HTuple(3.46921)).TupleRad(), 282.037, 112.64);
                HOperatorSet.GenCircle(out ho_TMP_Region, 438.113, 1283.17, 243.277);
                HOperatorSet.Intersection(ho_ROI_3, ho_TMP_Region, out ho_ROI_4);
                HOperatorSet.Threshold(ho_ImageReduced2, out ho_ROI_4, 70, 130);
                HOperatorSet.Connection(ho_ROI_4, out ho_ConnectedRegions2);
                HOperatorSet.SelectShape(ho_ConnectedRegions2, out ho_SelectedRegions2, "area", "and", 100, 99999);

                // 4. 获取最大面积数值
                HTuple hv_Areas1, hv_Rows1, hv_Columns1, hv_Areas2, hv_Rows2, hv_Columns2;
                HOperatorSet.AreaCenter(ho_SelectedRegions1, out hv_Areas1, out hv_Rows1, out hv_Columns1);
                HOperatorSet.AreaCenter(ho_SelectedRegions2, out hv_Areas2, out hv_Rows2, out hv_Columns2);

                HOperatorSet.TupleMax(hv_Areas1, out maxArea1);
                HOperatorSet.TupleMax(hv_Areas2, out maxArea2);

                // 5. 判断结果
                steelRingResult = (maxArea1 > 20000) ? "OK" : "NG";
                filterResult = (maxArea2 > 20000) ? "OK" : "NG";
            }
            catch (Exception ex)
            {
                throw new Exception("第二个算法检测失败: " + ex.Message);
            }
            finally
            {
                // 释放局部内存（防止内存泄漏）
                ho_Image?.Dispose(); ho_ROI_0?.Dispose(); ho_ROI_1?.Dispose();
                ho_ROI1?.Dispose(); ho_ImageReduced1?.Dispose(); ho_ROI_2?.Dispose();
                ho_ImageReduced2?.Dispose(); ho_ROI_3?.Dispose(); ho_TMP_Region?.Dispose();
                ho_ROI_4?.Dispose(); ho_ConnectedRegions1?.Dispose(); ho_ConnectedRegions2?.Dispose();
                ho_SelectedRegions1?.Dispose(); ho_SelectedRegions2?.Dispose();
            }
        }
    }
}