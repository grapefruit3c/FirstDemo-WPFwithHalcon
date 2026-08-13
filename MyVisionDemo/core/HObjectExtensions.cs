using HalconDotNet;
using System;

namespace MyVisionDemo.core
{
    /// <summary>
    /// HObject 扩展方法：简化 HOperatorSet 调用，支持链式编程
    /// 参考 MachineVision 项目的 HObjectExtensions 设计
    /// </summary>
    public static class HObjectExtensions
    {
        /// <summary>裁剪定义域</summary>
        public static HObject ReduceDomain(this HObject image, HObject region)
        {
            HObject result;
            HOperatorSet.ReduceDomain(image, region, out result);
            return result;
        }

        /// <summary>裁剪到最小包围矩形</summary>
        public static HObject CropDomain(this HObject image)
        {
            HObject result;
            HOperatorSet.CropDomain(image, out result);
            return result;
        }

        /// <summary>生成矩形区域</summary>
        public static HObject GenRectangle1(double row1, double col1, double row2, double col2)
        {
            HObject result;
            HOperatorSet.GenRectangle1(out result, row1, col1, row2, col2);
            return result;
        }

        /// <summary>生成圆形区域</summary>
        public static HObject GenCircle(double row, double col, double radius)
        {
            HObject result;
            HOperatorSet.GenCircle(out result, row, col, radius);
            return result;
        }

        /// <summary>生成带角度的矩形</summary>
        public static HObject GenRectangle2(double row, double col, double angle, double length1, double length2)
        {
            HObject result;
            HOperatorSet.GenRectangle2(out result, row, col, angle, length1, length2);
            return result;
        }

        /// <summary>阈值分割</summary>
        public static HObject Threshold(this HObject image, double minGray, double maxGray)
        {
            HObject result;
            HOperatorSet.Threshold(image, out result, minGray, maxGray);
            return result;
        }

        /// <summary>连通区域分割</summary>
        public static HObject Connection(this HObject region)
        {
            HObject result;
            HOperatorSet.Connection(region, out result);
            return result;
        }

        /// <summary>形状筛选</summary>
        public static HObject SelectShape(this HObject regions, string feature, string operation, double min, double max)
        {
            HObject result;
            HOperatorSet.SelectShape(regions, out result, feature, operation, min, max);
            return result;
        }

        /// <summary>区域合并</summary>
        public static HObject ConcatObj(this HObject obj1, HObject obj2)
        {
            HObject result;
            HOperatorSet.ConcatObj(obj1, obj2, out result);
            return result;
        }

        /// <summary>区域交集</summary>
        public static HObject Intersection(this HObject obj1, HObject obj2)
        {
            HObject result;
            HOperatorSet.Intersection(obj1, obj2, out result);
            return result;
        }

        /// <summary>获取区域面积和中心</summary>
        public static void AreaCenter(this HObject regions, out HTuple area, out HTuple row, out HTuple col)
        {
            HOperatorSet.AreaCenter(regions, out area, out row, out col);
        }

        /// <summary>安全释放 HObject（如果不为 null）</summary>
        public static void SafeDispose(this HObject obj)
        {
            if (obj != null)
            {
                try { obj.Dispose(); } catch { }
            }
        }

        /// <summary>安全释放多个 HObject</summary>
        public static void SafeDisposeAll(params HObject[] objects)
        {
            if (objects == null) return;
            foreach (var obj in objects)
            {
                obj.SafeDispose();
            }
        }
    }
}
