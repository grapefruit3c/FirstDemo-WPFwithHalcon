using _001Halconfirst;
using HalconDotNet;
using HslCommunication.Profinet.Siemens;
using MvCamCtrl.NET;
using MyVisionDemo.core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyVisionDemo
{
    /// <summary>
    /// MainWindow 交互逻辑
    /// v2.1 新增功能：
    /// 1. OK/NG 计数器 + 良率显示
    /// 2. 图像自动存档（按日期/OK/NG 分目录）
    /// 3. 生产日志 CSV（每周期记录）
    /// 4. 检测计时统计（匹配/条码/总耗时）
    /// 5. UI 优化（深色主题 + 统计看板 + 计时面板）
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _currentImagePath = "";
        private readonly HalconProcessor halconProcessor = new HalconProcessor();
        private readonly HalconProcessor_Cam2 halconProcessor_Cam2 = new HalconProcessor_Cam2();
        private readonly HikCameraManager _cameraManager = new HikCameraManager();

        // 新功能组件
        private DetectionStats _stats = new DetectionStats();
        private ProductionLogger _productionLogger;
        private ImageArchiver _imageArchiver;

        private SiemensS7Net plc;
        private DispatcherTimer _heartbeatTimer;
        private DispatcherTimer _reconnectTimer;
        private DispatcherTimer _clockTimer;
        private bool _isRunningDetection = false;

        private string[] _loopImageFiles_Cam1;
        private int _loopIndex_Cam1 = 0;
        private string[] _loopImageFiles_Cam2;
        private int _loopIndex_Cam2 = 0;

        private DispatcherTimer _loopTimer;

        // 最近一次检测结果（用于日志和存档）
        private string _lastBarcode = "";
        private string _lastStatus = "";
        private double _lastMatchMs = 0;
        private double _lastTotalMs = 0;

        public MainWindow()
        {
            InitializeComponent();

            AppConfig.Load();

            try
            {
                halconProcessor.InitModel(AppConfig.GetTemplatePath());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("模板加载失败: " + ex.Message);
            }

            // 初始化生产日志和图像存档
            if (AppConfig.Current.Logging.Enabled)
                _productionLogger = new ProductionLogger(AppConfig.GetLogDirectory());
            if (AppConfig.Current.Archive.Enabled)
                _imageArchiver = new ImageArchiver(
                    AppConfig.GetArchiveRoot(),
                    AppConfig.Current.Archive.SaveOKImages,
                    AppConfig.Current.Archive.SaveNGImages,
                    AppConfig.Current.Archive.JpgQuality);

            _cameraManager.OnError += (msg) => AddLog($"[相机] {msg}");

            // 启动时钟
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => TxtClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var plcCfg = AppConfig.Current.PLC;

            plc = new SiemensS7Net(SiemensPLCS.S1200, plcCfg.IpAddress)
            {
                Rack = (byte)plcCfg.Rack,
                Slot = (byte)plcCfg.Slot
            };

            ConnectPlc();

            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(plcCfg.HeartbeatIntervalMs) };
            _heartbeatTimer.Tick += HeartbeatTimer_Tick;

            if (plcCfg.AutoReconnect)
            {
                _reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(plcCfg.ReconnectIntervalMs) };
                _reconnectTimer.Tick += ReconnectTimer_Tick;
            }

            if (IsPlcConnected())
            {
                _heartbeatTimer.Start();
                PlcHeartbeatText.Text = "正常";
                PlcHeartbeatText.Tag = "Alive";
            }

            AddLog("系统初始化完成");
        }

        private void ConnectPlc()
        {
            try
            {
                var result = plc.ConnectServer();
                if (result.IsSuccess)
                {
                    AddLog("PLC 连接成功");
                    PlcStatusLight.Fill = Brushes.LimeGreen;
                }
                else
                {
                    AddLog($"PLC 连接失败: {result.Message}");
                    PlcStatusLight.Fill = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                AddLog($"PLC 连接异常: {ex.Message}");
                PlcStatusLight.Fill = Brushes.Red;
            }
        }

        private bool IsPlcConnected()
        {
            try { return plc != null && plc.ReadInt16(AppConfig.Current.PLC.HeartbeatAddress).IsSuccess; }
            catch { return false; }
        }

        private void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            AddLog("尝试重新连接 PLC...");
            ConnectPlc();
            if (IsPlcConnected())
            {
                _reconnectTimer.Stop();
                _heartbeatTimer.Start();
                PlcHeartbeatText.Tag = "Alive";
                PlcHeartbeatText.Text = "正常";
                PlcHeartbeatText.Foreground = Brushes.White;
                AddLog("PLC 重新连接成功");
            }
        }

        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            if (plc == null) return;
            var plcCfg = AppConfig.Current.PLC;

            var heartbeatResult = plc.ReadInt16(plcCfg.HeartbeatAddress);
            if (!heartbeatResult.IsSuccess)
            {
                PlcHeartbeatText.Tag = "Dead";
                PlcHeartbeatText.Text = "断开";
                PlcHeartbeatText.Foreground = Brushes.Gray;
                _heartbeatTimer.Stop();
                AddLog("PLC 心跳丢失");
                if (plcCfg.AutoReconnect && _reconnectTimer != null && !_reconnectTimer.IsEnabled)
                {
                    _reconnectTimer.Start();
                    AddLog($"将在 {plcCfg.ReconnectIntervalMs / 1000} 秒后重连...");
                }
                return;
            }

            // 触发信号检测
            var triggerResult = plc.ReadInt16(plcCfg.TriggerAddress);
            if (triggerResult.IsSuccess && triggerResult.Content == plcCfg.TriggerValue)
            {
                if (!_isRunningDetection)
                {
                    _isRunningDetection = true;
                    AddLog($"收到 PLC 触发，开始检测...");
                    StepToNextImageAndDetect("PLC触发");
                    plc.Write(plcCfg.TriggerAddress, (short)0);
                    _isRunningDetection = false;
                }
            }
        }

        /// <summary>
        /// 执行一次检测（含计时 + 统计 + 存档 + 日志）
        /// </summary>
        private void RunOneDetection()
        {
            if (string.IsNullOrEmpty(_currentImagePath)) return;

            var timer = new DetectionTimer();
            double matchMs = 0, barcodeMs = 0;

            try
            {
                string barcodeText;
                string statusStr;
                double matchRow, matchCol;
                BitmapSource tempImg;

                // 执行检测
                halconProcessor.RunOneImage(_currentImagePath, out tempImg,
                    out barcodeText, out statusStr, out matchRow, out matchCol);

                matchMs = timer.MarkStep();
                barcodeMs = matchMs; // RunOneImage 内部已包含两步，记录为总匹配耗时

                // 写入 PLC 结果
                var plcCfg = AppConfig.Current.PLC;
                if (plc != null && _heartbeatTimer != null && _heartbeatTimer.IsEnabled)
                {
                    bool isOK = (statusStr == "OK");
                    plc.Write(plcCfg.ResultAddress, isOK);
                }

                // 更新界面
                TxtResult.Text = $"{statusStr} : {barcodeText}";
                TxtResult.Foreground = statusStr == "OK" ? Brushes.LimeGreen : Brushes.OrangeRed;

                // 绘制 Halcon 结果
                DrawDetectionResult(_currentImagePath, statusStr, barcodeText, matchRow, matchCol);

                // 计时显示
                double totalMs = timer.Stop();
                _lastMatchMs = matchMs;
                _lastTotalMs = totalMs;
                TxtMatchMs.Text = $"{matchMs:F0} ms";
                TxtBarcodeMs.Text = $"{barcodeMs:F0} ms";
                TxtTotalMs.Text = $"{totalMs:F0} ms";

                // 统计计数
                bool resultOK = (statusStr == "OK");
                _stats.Record(resultOK);
                UpdateStatsDisplay();

                // 图像存档
                if (_imageArchiver != null)
                {
                    string archivedPath = _imageArchiver.ArchiveImage(_currentImagePath, resultOK, barcodeText);
                    if (!string.IsNullOrEmpty(archivedPath) && !resultOK)
                        AddLog($"NG 图像已存档: {Path.GetFileName(archivedPath)}");
                }

                // 生产日志 CSV
                _lastBarcode = barcodeText;
                _lastStatus = statusStr;
                _productionLogger?.LogCycle(barcodeText, statusStr, "-", "-", matchMs, totalMs, _currentImagePath);

                AddLog($"检测完成: {statusStr} (条码: {barcodeText}) [{totalMs:F0}ms]");
            }
            catch (Exception ex)
            {
                timer.Stop();
                AddLog($"检测异常: {ex.Message}");

                // 异常也计入 NG
                _stats.Record(false);
                UpdateStatsDisplay();
                _productionLogger?.LogCycle("", "ERROR", "-", "-", matchMs, timer.TotalMs, _currentImagePath);
            }
        }

        /// <summary>
        /// 在 Halcon 窗口绘制检测结果
        /// </summary>
        private void DrawDetectionResult(string imagePath, string statusStr, string barcodeText, double matchRow, double matchCol)
        {
            Hwin.HalconWindow.ClearWindow();
            Hwin.HalconWindow.SetPart(0, 0, -2, -2);

            using (HImage displayImage = new HImage(imagePath))
            {
                Hwin.HalconWindow.DispImage(displayImage);
            }

            if (!string.IsNullOrEmpty(barcodeText))
            {
                // 绿色匹配框
                Hwin.HalconWindow.SetColor("green");
                Hwin.HalconWindow.SetLineWidth(2);
                Hwin.HalconWindow.SetDraw("margin");
                Hwin.HalconWindow.DispRectangle2(
                    new HTuple(matchRow), new HTuple(matchCol),
                    new HTuple(0), new HTuple(120), new HTuple(220));

                // 十字标记
                Hwin.HalconWindow.SetColor("green");
                Hwin.HalconWindow.SetDraw("fill");
                Hwin.HalconWindow.DispCircle(matchRow, matchCol, 5);

                // 结果文字
                Hwin.HalconWindow.SetColor("green");
                Hwin.HalconWindow.SetTposition(40, 40);
                Hwin.HalconWindow.WriteString($"条码: {barcodeText}");
                Hwin.HalconWindow.SetTposition(70, 40);
                Hwin.HalconWindow.WriteString($"结果: {statusStr}");
                Hwin.HalconWindow.SetTposition(100, 40);
                Hwin.HalconWindow.WriteString($"耗时: {_lastTotalMs:F0} ms");
            }
            else
            {
                Hwin.HalconWindow.SetColor("red");
                Hwin.HalconWindow.SetTposition(40, 40);
                Hwin.HalconWindow.WriteString($"检测结果: {statusStr}");
            }
        }

        /// <summary>
        /// 更新统计看板显示
        /// </summary>
        private void UpdateStatsDisplay()
        {
            TxtOKCount.Text = _stats.OKCount.ToString();
            TxtNGCount.Text = _stats.NGCount.ToString();
            TxtTotalCount.Text = _stats.TotalCount.ToString();
            TxtYield.Text = _stats.GetYieldString();

            // 良率颜色：>95% 绿色, 90-95% 黄色, <90% 红色
            if (_stats.YieldRate >= 95)
                TxtYield.Foreground = Brushes.LimeGreen;
            else if (_stats.YieldRate >= 90)
                TxtYield.Foreground = Brushes.Yellow;
            else
                TxtYield.Foreground = Brushes.OrangeRed;
        }

        private void StepToNextImageAndDetect(string triggerSource)
        {
            // 相机1检测
            if (_loopImageFiles_Cam1 != null && _loopImageFiles_Cam1.Length > 0)
            {
                string file1 = _loopImageFiles_Cam1[_loopIndex_Cam1];
                _currentImagePath = file1;
                RunOneDetection();
                _loopIndex_Cam1++;
                if (_loopIndex_Cam1 >= _loopImageFiles_Cam1.Length) _loopIndex_Cam1 = 0;
            }

            // 相机2检测
            if (_loopImageFiles_Cam2 != null && _loopImageFiles_Cam2.Length > 0)
            {
                string file2 = _loopImageFiles_Cam2[_loopIndex_Cam2];

                using (HImage img2 = new HImage(file2))
                {
                    Hwin2.HalconWindow.ClearWindow();
                    Hwin2.HalconWindow.SetPart(0, 0, -2, -2);
                    Hwin2.HalconWindow.DispImage(img2);
                }

                string resultRing, resultFilter;
                HTuple area1, area2;
                halconProcessor_Cam2.RunOneImage(file2, out resultRing, out resultFilter, out area1, out area2);

                AddLog($"相机2: 钢圈=[{resultRing}], 滤芯=[{resultFilter}]");

                Hwin2.HalconWindow.SetTposition(40, 40);
                Hwin2.HalconWindow.SetColor(resultRing == "OK" ? "green" : "red");
                Hwin2.HalconWindow.WriteString($"钢圈有无: {resultRing}");
                Hwin2.HalconWindow.SetTposition(70, 40);
                Hwin2.HalconWindow.SetColor(resultFilter == "OK" ? "green" : "red");
                Hwin2.HalconWindow.WriteString($"滤芯有无: {resultFilter}");

                _loopIndex_Cam2++;
                if (_loopIndex_Cam2 >= _loopImageFiles_Cam2.Length) _loopIndex_Cam2 = 0;
            }

            if (triggerSource != "")
                AddLog($"[{triggerSource}] 触发翻页");
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "请选择包含 /1 和 /2 子文件夹的父文件夹";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string basePath = dialog.SelectedPath;
                string pathCam1 = Path.Combine(basePath, "1");
                string pathCam2 = Path.Combine(basePath, "2");

                if (Directory.Exists(pathCam1))
                {
                    _loopImageFiles_Cam1 = Directory.GetFiles(pathCam1, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam1);
                    _loopIndex_Cam1 = 0;
                    AddLog($"相机1: {pathCam1}，{_loopImageFiles_Cam1.Length} 张图片");

                    if (_loopImageFiles_Cam1.Length > 0)
                    {
                        using (HImage img1 = new HImage(_loopImageFiles_Cam1[0]))
                        {
                            Hwin.HalconWindow.ClearWindow();
                            Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                            Hwin.HalconWindow.DispImage(img1);
                        }
                    }
                }
                else AddLog($"未找到相机1路径: {pathCam1}");

                if (Directory.Exists(pathCam2))
                {
                    _loopImageFiles_Cam2 = Directory.GetFiles(pathCam2, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam2);
                    _loopIndex_Cam2 = 0;
                    AddLog($"相机2: {pathCam2}，{_loopImageFiles_Cam2.Length} 张图片");

                    if (_loopImageFiles_Cam2.Length > 0)
                    {
                        using (HImage img2 = new HImage(_loopImageFiles_Cam2[0]))
                        {
                            Hwin2.HalconWindow.ClearWindow();
                            Hwin2.HalconWindow.SetPart(0, 0, -2, -2);
                            Hwin2.HalconWindow.DispImage(img2);
                        }
                    }
                }
                else AddLog($"未找到相机2路径: {pathCam2}");
            }
        }

        private void LoopTimer_Tick(object sender, EventArgs e)
        {
            StepToNextImageAndDetect("自动循环");
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            bool hasCam1 = (_loopImageFiles_Cam1 != null && _loopImageFiles_Cam1.Length > 0);
            bool hasCam2 = (_loopImageFiles_Cam2 != null && _loopImageFiles_Cam2.Length > 0);

            if (!hasCam1 && !hasCam2)
            {
                System.Windows.MessageBox.Show("请先选择包含 /1 或 /2 子文件夹的图片目录！");
                return;
            }

            if (_loopTimer == null)
            {
                _loopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConfig.Current.Camera.LoopIntervalMs) };
                _loopTimer.Tick += LoopTimer_Tick;
            }

            if (!_loopTimer.IsEnabled)
            {
                _loopTimer.Start();
                AddLog($"开始循环检测，间隔 {AppConfig.Current.Camera.LoopIntervalMs}ms");
                StepToNextImageAndDetect("自动循环");
            }
        }

        private void BtnSearchHistory_Click(object sender, RoutedEventArgs e)
        {
            // 在存档目录中搜索图片
            string keyword = TxtSearchHistory.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                System.Windows.MessageBox.Show("请输入搜索关键字（条码或日期）");
                return;
            }

            HistoryList.Items.Clear();
            string archiveRoot = AppConfig.GetArchiveRoot();
            if (!Directory.Exists(archiveRoot))
            {
                AddLog("存档目录不存在");
                return;
            }

            try
            {
                var files = Directory.GetFiles(archiveRoot, "*" + keyword + "*", SearchOption.AllDirectories);
                int count = 0;
                foreach (var file in files)
                {
                    if (count >= 50) break;
                    HistoryList.Items.Add(Path.GetFileName(file));
                    count++;
                }
                AddLog($"搜索到 {count} 张匹配图片");
            }
            catch (Exception ex)
            {
                AddLog($"搜索异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置统计
        /// </summary>
        private void BtnResetStats_Click(object sender, RoutedEventArgs e)
        {
            _stats.Reset();
            UpdateStatsDisplay();
            AddLog("统计数据已重置");
        }

        private void AddLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                var newLine = new TextBlock
                {
                    Text = $"[{time}] {message}",
                    Foreground = Brushes.LightGray,
                    FontSize = 11,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                LogPanel.Children.Insert(0, newLine);

                int maxCount = AppConfig.Current.Camera.MaxLogCount;
                while (LogPanel.Children.Count > maxCount)
                    LogPanel.Children.RemoveAt(LogPanel.Children.Count - 1);
            }));
        }

        private void BtnConnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var deviceList = _cameraManager.GetDeviceList();
                if (deviceList.nDeviceNum == 0)
                {
                    System.Windows.MessageBox.Show("未发现任何相机设备！");
                    return;
                }

                var deviceInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                    deviceList.pDeviceInfo[0], typeof(MyCamera.MV_CC_DEVICE_INFO));

                if (_cameraManager.ConnectCamera(deviceInfo))
                {
                    _cameraManager.OnImageCaptured += OnCameraImageArrived;
                    _cameraManager.StartGrabbing();
                    CamStatusLight.Fill = Brushes.LimeGreen;
                    TxtResult.Text = "相机已连接";
                    AddLog("相机连接成功");
                }
                else
                {
                    System.Windows.MessageBox.Show("相机连接失败");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("连接异常: " + ex.Message);
            }
        }

        private void OnCameraImageArrived(HImage image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Hwin.HalconWindow.ClearWindow();
                    Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                    Hwin.HalconWindow.DispImage(image);

                    string tempPath = Path.GetTempPath() + Guid.NewGuid().ToString() + ".jpg";
                    image.WriteImage("jpg", 0, tempPath);
                    _currentImagePath = tempPath;

                    RunOneDetection();
                }
                catch (Exception ex)
                {
                    AddLog($"图像处理异常: {ex.Message}");
                }
                finally
                {
                    image?.Dispose();
                }
            }));
        }

        private void BtnDisconnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cameraManager.OnImageCaptured -= OnCameraImageArrived;
                _cameraManager.StopGrabbing();
                CamStatusLight.Fill = Brushes.Gray;
                TxtResult.Text = "相机已断开";
                AddLog("相机已断开");
            }
            catch (Exception ex)
            {
                AddLog($"断开相机异常: {ex.Message}");
            }
        }

        private void BtnSingle(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null && _loopTimer.IsEnabled)
            {
                _loopTimer.Stop();
                AddLog("循环已暂停");
            }
            StepToNextImageAndDetect("单次触发");
        }

        private void BtnStopLoop(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null && _loopTimer.IsEnabled)
            {
                _loopTimer.Stop();
                AddLog("已停止循环检测");
            }
        }
    }
}
