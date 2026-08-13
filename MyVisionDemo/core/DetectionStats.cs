using System;

namespace MyVisionDemo.core
{
    /// <summary>
    /// 检测统计：OK/NG 计数、良率计算
    /// 参考 OpenIVS 的生产统计设计
    /// </summary>
    public class DetectionStats
    {
        public int TotalCount { get; private set; }
        public int OKCount { get; private set; }
        public int NGCount { get; private set; }

        /// <summary>
        /// 良率百分比（0-100）
        /// </summary>
        public double YieldRate
        {
            get
            {
                if (TotalCount == 0) return 0;
                return (double)OKCount / TotalCount * 100;
            }
        }

        /// <summary>
        /// 记录一次检测结果
        /// </summary>
        public void Record(bool isOK)
        {
            TotalCount++;
            if (isOK)
                OKCount++;
            else
                NGCount++;
        }

        /// <summary>
        /// 重置统计
        /// </summary>
        public void Reset()
        {
            TotalCount = 0;
            OKCount = 0;
            NGCount = 0;
        }

        /// <summary>
        /// 获取格式化的良率字符串
        /// </summary>
        public string GetYieldString()
        {
            return $"{YieldRate:F1}%";
        }
    }

    /// <summary>
    /// 检测计时器：记录各步骤耗时
    /// 参考 OpenIVS 的三层计时设计
    /// </summary>
    public class DetectionTimer : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch _totalStopwatch;
        private readonly System.Diagnostics.Stopwatch _stepStopwatch;

        public double TotalMs { get; private set; }
        public double LastStepMs { get; private set; }

        public DetectionTimer()
        {
            _totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
        }

        /// <summary>
        /// 标记一个步骤完成，记录耗时
        /// </summary>
        public double MarkStep()
        {
            LastStepMs = _stepStopwatch.Elapsed.TotalMilliseconds;
            _stepStopwatch.Restart();
            return LastStepMs;
        }

        /// <summary>
        /// 结束计时，返回总耗时
        /// </summary>
        public double Stop()
        {
            _totalStopwatch.Stop();
            _stepStopwatch.Stop();
            TotalMs = _totalStopwatch.Elapsed.TotalMilliseconds;
            return TotalMs;
        }

        public void Dispose()
        {
            _totalStopwatch?.Stop();
            _stepStopwatch?.Stop();
        }
    }
}
