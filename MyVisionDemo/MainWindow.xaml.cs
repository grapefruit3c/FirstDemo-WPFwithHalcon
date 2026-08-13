using _001Halconfirst;
using HalconDotNet;
using HslCommunication;
using HslCommunication.Profinet.Siemens;
using Microsoft.Win32;
using MvCamCtrl.NET;
using MyVisionDemo.core;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyVisionDemo
{
    /// <summary>
    /// MainWindow 交互逻辑
    /// 优化点：
    /// 1. 修复 PLC 双 ConnectServer bug
    /// 2. 添加 PLC 断线自动重连
    /// 3. 所有参数从 AppConfig 读取
    /// 4. 相机回调改用 BeginInvoke（异步，不阻塞采图线程）
    /// 5. 移除 GC.Collect
    /// 6. HImage 使用后及时释放
    /// 7. 改进异常处理（不再吞异常）
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _currentImagePath = "";
        private readonly HalconProcessor halconProcessor = new HalconProcessor();
        private readonly HalconProcessor_Cam2 halconProcessor_Cam2 = new HalconProcessor_Cam2();
        private readonly HikCameraManager _cameraManager = new HikCameraManager();

        private SiemensS7Net plc;
        private DispatcherTimer _heartbeatTimer;
        private DispatcherTimer _reconnectTimer;
        private bool _isRunningDetection = false;

        private string[] _loopImageFiles_Cam1;
        private int _loopIndex_Cam1 = 0;
        private string[] _loopImageFiles_Cam2;
        private int _loopIndex_Cam2 = 0;

        private DispatcherTimer _loopTimer;

        public MainWindow()
        {
            InitializeComponent();

            // 加载配置
            AppConfig.Load();

            try
            {
                halconProcessor.InitModel(AppConfig.GetTemplatePath());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("模板加载失败: " + ex.Message);
            }

            // 订阅相机错误事件
            _cameraManager.OnError += (msg) => AddLog($"[相机] {msg}");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var plcCfg = AppConfig.Current.PLC;

            // 初始化 PLC（只连接一次，不再重复 ConnectServer）
            plc = new SiemensS7Net(SiemensPLCS.S1200, plcCfg.IpAddress)
            {
                Rack = (byte)plcCfg.Rack,
                Slot = (byte)plcCfg.Slot
            };

            ConnectPlc();

            // 心跳定时器
            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(plcCfg.HeartbeatIntervalMs)
            };
            _heartbeatTimer.Tick += HeartbeatTimer_Tick;

            if (plcCfg.AutoReconnect)
            {
                // 断线重连定时器
                _reconnectTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(plcCfg.ReconnectIntervalMs)
                };
                _reconnectTimer.Tick += ReconnectTimer_Tick;
            }

            // 如果 PLC 已连接，启动心跳
            if (IsPlcConnected())
            {
                _heartbeatTimer.Start();
                PlcHeartbeatText.Text = "正常";
                PlcHeartbeatText.Tag = "Alive";
            }
        }

        /// <summary>
        /// 连接 PLC
        /// </summary>
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

        /// <summary>
        /// 检查 PLC 是否已连接
        /// </summary>
        private bool IsPlcConnected()
        {
            try
            {
                return plc != null &&
                       plc.ReadInt16(AppConfig.Current.PLC.HeartbeatAddress).IsSuccess;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 断线自动重连
        /// </summary>
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
                AddLog("PLC 重新连接成功，恢复心跳监测");
            }
        }

        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            if (plc == null) return;

            var plcCfg = AppConfig.Current.PLC;

            // 1. 心跳检测
            var heartbeatResult = plc.ReadInt16(plcCfg.HeartbeatAddress);
            if (!heartbeatResult.IsSuccess)
            {
                PlcHeartbeatText.Tag = "Dead";
                PlcHeartbeatText.Text = "断开";
                PlcHeartbeatText.Foreground = Brushes.Gray;
                _heartbeatTimer.Stop();
                AddLog("PLC 心跳丢失，连接已断开");

                // 启动自动重连
                if (plcCfg.AutoReconnect && _reconnectTimer != null && !_reconnectTimer.IsEnabled)
                {
                    _reconnectTimer.Start();
                    AddLog($"将在 {plcCfg.ReconnectIntervalMs / 1000} 秒后尝试重连...");
                }
                return;
            }

            // 2. 读取触发信号
            // 注意：TriggerValue 默认为 256（0x0100），这是西门子 M 区的字节序差异
            // 实际值 1 在 ReadInt16 读取后表现为 256，属正常现象
            var triggerResult = plc.ReadInt16(plcCfg.TriggerAddress);
            if (triggerResult.IsSuccess && triggerResult.Content == plcCfg.TriggerValue)
            {
                if (!_isRunningDetection)
                {
                    _isRunningDetection = true;

                    AddLog($"收到 PLC 触发指令 ({plcCfg.TriggerAddress})，开始检测...");

                    StepToNextImageAndDetect("PLC触发");

                    // 检测完成后清除触发信号
                    plc.Write(plcCfg.TriggerAddress, (short)0);
                    AddLog($"检测完成，已清除 PLC 触发指令");

                    _isRunningDetection = false;
                }
            }
        }

        private void ShowImage(HImage image)
        {
            Hwin.HalconWindow.ClearWindow();
            Hwin.HalconWindow.DispImage(image);
            Hwin.HalconWindow.SetPart(0, 0, -2, -2);
        }

        private void RunOneDetection()
        {
            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            try
            {
                string barcodeText;
                string statusStr;
                double matchRow, matchCol;
                BitmapSource tempImg;

                halconProcessor.RunOneImage(_currentImagePath, out tempImg,
                    out barcodeText, out statusStr, out matchRow, out matchCol);

                // 写入 PLC 结果
                var plcCfg = AppConfig.Current.PLC;
                if (plc != null && _heartbeatTimer != null && _heartbeatTimer.IsEnabled)
                {
                    bool isOK = (statusStr == "OK");
                    var writeResult = plc.Write(plcCfg.ResultAddress, isOK);

                    if (writeResult.IsSuccess)
                    {
                        AddLog($"已向 PLC 输出结果: {plcCfg.ResultAddress} = {(isOK ? "True(OK)" : "False(NG)")}");
                    }
                    else
                    {
                        AddLog($"向 PLC 输出结果失败: {writeResult.Message}");
                    }
                }

                // 更新界面显示
                TxtResult.Text = $"{statusStr} : {barcodeText}";
                TxtResult.Foreground = statusStr == "OK" ? Brushes.Green : Brushes.Red;

                // 在 Halcon 窗口绘制结果
                Hwin.HalconWindow.ClearWindow();
                Hwin.HalconWindow.SetPart(0, 0, -2, -2);

                using (HImage displayImage = new HImage(_currentImagePath))
                {
                    Hwin.HalconWindow.DispImage(displayImage);
                }

                if (!string.IsNullOrEmpty(barcodeText))
                {
                    Hwin.HalconWindow.SetColor("green");
                    Hwin.HalconWindow.SetLineWidth(3);
                    Hwin.HalconWindow.SetDraw("margin");
                    Hwin.HalconWindow.DispRectangle2(
                        new HTuple(matchRow), new HTuple(matchCol),
                        new HTuple(0), new HTuple(100), new HTuple(200));

                    Hwin.HalconWindow.SetColor("green");
                    Hwin.HalconWindow.SetTposition(50, 50);
                    Hwin.HalconWindow.WriteString($"条码信息: {barcodeText}");
                    Hwin.HalconWindow.NewLine();
                    Hwin.HalconWindow.SetTposition(100, 50);
                    Hwin.HalconWindow.WriteString($"检测结果: {statusStr}");
                }
                else
                {
                    Hwin.HalconWindow.SetColor("red");
                    Hwin.HalconWindow.SetTposition(50, 50);
                    Hwin.HalconWindow.WriteString($"检测结果: {statusStr}");
                }

                AddLog($"检测完成: {statusStr} (条码: {barcodeText})");
            }
            catch (Exception ex)
            {
                AddLog($"检测异常: {ex.Message}");
            }
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

                AddLog($"相机2检测: 钢圈=[{resultRing}], 滤芯=[{resultFilter}]");

                // 绘制结果文字
                Hwin2.HalconWindow.SetTposition(50, 50);
                Hwin2.HalconWindow.SetColor(resultRing == "OK" ? "green" : "red");
                Hwin2.HalconWindow.WriteString($"钢圈有无检测：{resultRing}");

                Hwin2.HalconWindow.SetTposition(100, 50);
                Hwin2.HalconWindow.SetColor(resultFilter == "OK" ? "green" : "red");
                Hwin2.HalconWindow.WriteString($"滤芯有无检测：{resultFilter}");

                _loopIndex_Cam2++;
                if (_loopIndex_Cam2 >= _loopImageFiles_Cam2.Length) _loopIndex_Cam2 = 0;
            }

            if (triggerSource != "")
            {
                AddLog($"[{triggerSource}] 触发翻页");
            }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "请选择包含 /1 和 /2 子文件夹的父文件夹";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string basePath = dialog.SelectedPath;
                string pathCam1 = System.IO.Path.Combine(basePath, "1");
                string pathCam2 = System.IO.Path.Combine(basePath, "2");

                // 相机1
                if (System.IO.Directory.Exists(pathCam1))
                {
                    _loopImageFiles_Cam1 = System.IO.Directory.GetFiles(pathCam1, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam1);
                    _loopIndex_Cam1 = 0;
                    AddLog($"相机1路径: {pathCam1}，找到 {_loopImageFiles_Cam1.Length} 张图片");

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
                else
                {
                    AddLog($"未找到相机1路径: {pathCam1}");
                }

                // 相机2
                if (System.IO.Directory.Exists(pathCam2))
                {
                    _loopImageFiles_Cam2 = System.IO.Directory.GetFiles(pathCam2, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam2);
                    _loopIndex_Cam2 = 0;
                    AddLog($"相机2路径: {pathCam2}，找到 {_loopImageFiles_Cam2.Length} 张图片");

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
                else
                {
                    AddLog($"未找到相机2路径: {pathCam2}");
                }
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
                _loopTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(AppConfig.Current.Camera.LoopIntervalMs)
                };
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
        }

        private void AddLog(string message)
        {
            // 使用 BeginInvoke 异步更新 UI（不阻塞调用线程）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");

                var newLine = new TextBlock
                {
                    Text = $"[{time}] {message}",
                    Foreground = Brushes.LightGray,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                LogPanel.Children.Insert(0, newLine);

                int maxCount = AppConfig.Current.Camera.MaxLogCount;
                while (LogPanel.Children.Count > maxCount)
                {
                    LogPanel.Children.RemoveAt(LogPanel.Children.Count - 1);
                }
            }));
        }

        private void BtnConnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var deviceList = _cameraManager.GetDeviceList();

                if (deviceList.nDeviceNum == 0)
                {
                    System.Windows.MessageBox.Show("未发现任何相机设备！\n请确认海康 MVS 的虚拟相机已开启。");
                    return;
                }

                var deviceInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                    deviceList.pDeviceInfo[0], typeof(MyCamera.MV_CC_DEVICE_INFO));

                if (_cameraManager.ConnectCamera(deviceInfo))
                {
                    _cameraManager.OnImageCaptured += OnCameraImageArrived;
                    _cameraManager.StartGrabbing();

                    CamStatusLight.Fill = Brushes.LimeGreen;
                    TxtResult.Text = "相机已连接，流水线开启...";
                    AddLog("相机连接成功，开始采图。");
                }
                else
                {
                    System.Windows.MessageBox.Show("相机设备连接失败，请检查驱动或重启 MVS。");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("连接异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 相机图像到达回调
        /// 优化：使用 BeginInvoke（异步）替代 Invoke（同步），不阻塞采图线程
        /// </summary>
        private void OnCameraImageArrived(HImage image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Hwin.HalconWindow.ClearWindow();
                    Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                    Hwin.HalconWindow.DispImage(image);

                    // 保存临时文件供检测使用
                    string tempPath = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".jpg";
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
                    // HImage 使用完毕后释放
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

                // 不再手动 GC.Collect，依靠规范的 Dispose 管理
                CamStatusLight.Fill = Brushes.Gray;
                TxtResult.Text = "相机已断开。";
                AddLog("相机已安全断开连接，资源已释放。");
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
                AddLog("自动循环已暂停，执行单次触发。");
            }

            StepToNextImageAndDetect("单次触发");
        }

        private void BtnStopLoop(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null && _loopTimer.IsEnabled)
            {
                _loopTimer.Stop();
                AddLog("已停止循环检测。");
            }
            else
            {
                AddLog("当前没有在运行循环检测。");
            }
        }
    }
}
