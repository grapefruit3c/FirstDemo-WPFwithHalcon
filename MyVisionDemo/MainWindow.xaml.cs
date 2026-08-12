using _001Halconfirst;
using HalconDotNet;
using HslCommunication;
using HslCommunication.Core.Address;
using HslCommunication.Profinet.Siemens;
using Microsoft.Win32;
using MvCamCtrl.NET;
using MyVisionDemo.core;
using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;


namespace MyVisionDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //全局变量，存储当前选中的图像路径
        private string _currentImagePath = "";
        HalconProcessor halconProcessor = new HalconProcessor();
        private HalconProcessor_Cam2 halconProcessor_Cam2 = new HalconProcessor_Cam2();
        double matchRow = 0; 
        double matchCol = 0;
        private HikCameraManager _cameraManager = new HikCameraManager();//声明 HikCameraManager 实例
        private SiemensS7Net plc;
        private System.Windows.Threading.DispatcherTimer _heartbeatTimer;
        private bool _isRunningDetection = false; // 检测锁，防止重复触发
        // ==== 改为双工位变量 ====
       // 专门给 窗口1 (路径 /2) 用的
        private string[] _loopImageFiles_Cam1;
        private int _loopIndex_Cam1 = 0;

        // 专门给 窗口2 (路径 /1) 用的
        private string[] _loopImageFiles_Cam2;
        private int _loopIndex_Cam2 = 0;
        private string _loopFolderPath = ""; // 当前循环模式的文件夹路径

        private System.Windows.Threading.DispatcherTimer _loopTimer;

        public MainWindow()
        {
            InitializeComponent();


            try
            {
                // 2. 调用读取模板函数
                halconProcessor.InitModel(PathConfig.templatePath);
                // 如果执行到这里没有弹窗报错，就说明模板加载成功了！
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("模板加载失败: " + ex.Message);
            }
          
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 【关键】连接到本机 (127.0.0.1) 的 S1200 虚拟服务器
            plc = new HslCommunication.Profinet.Siemens.SiemensS7Net(HslCommunication.Profinet.Siemens.SiemensPLCS.S1200, "127.0.0.1");
            plc.Rack = 0;
            plc.Slot = 1;
           // 尝试连接
           var connectResult = plc.ConnectServer();

            if (connectResult.IsSuccess)
            {
                AddLog("✅ PLC 虚拟服务器连接成功！");
                PlcStatusLight.Fill = System.Windows.Media.Brushes.LimeGreen;
            }
            else
            {
                AddLog($"❌ 连接失败: {connectResult.Message}");
                PlcStatusLight.Fill = System.Windows.Media.Brushes.Red;
            }

            // 2. 初始化心跳定时器 (间隔 1 秒)
            _heartbeatTimer = new System.Windows.Threading.DispatcherTimer();
            _heartbeatTimer.Interval = TimeSpan.FromSeconds(1);
            _heartbeatTimer.Tick += HeartbeatTimer_Tick;

            // 如果 PLC 连接成功，立刻开启心跳
            if (plc != null && plc.ConnectServer().IsSuccess)
            {
                _heartbeatTimer.Start();
                PlcHeartbeatText.Text = "运行中"; // 或者保持空文本
                PlcHeartbeatText.Tag = "Alive";  // 【关键】触发 XAML 里写的绿色闪烁动画！
            }
        }
        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            if (plc == null) return;

            // ==========================================
            // 1. 维持心跳 (监测 PLC 是否还活着)
            // ==========================================
            var heartbeatResult = plc.ReadInt16("DB1.0");
            if (heartbeatResult.IsSuccess)
            {
                if ((string)PlcHeartbeatText.Tag != "Alive")
                {
                    PlcHeartbeatText.Tag = "Alive";
                    PlcHeartbeatText.Text = "正常";
                }
            }
            else
            {
                PlcHeartbeatText.Tag = "Dead";
                PlcHeartbeatText.Text = "断开";
                PlcHeartbeatText.Foreground = System.Windows.Media.Brushes.Gray;
                _heartbeatTimer.Stop();
                AddLog("PLC 心跳丢失，连接已断开！");
                return; // 心跳断了就直接退出，不用再往下读了
            }

            // ==========================================
            // 2. 【新增】读取 PLC 的触发指令 (M100)
            // ==========================================
            // 注意：M100 是西门子 M 区地址，在 HSL 中通常写为 "M100"
            var triggerResult = plc.ReadInt16("M100");

            if (triggerResult.IsSuccess)
            {
                // 如果 M100 的值等于 1 (代表 PLC 要求开始检测)
                if (triggerResult.Content == 256)
                {
                    // 【防重复触发保护】如果上一次检测还没跑完，这次不重复跑
                    if (_isRunningDetection == false)
                    {
                        _isRunningDetection = true; // 锁住，防止连续触发多次

                        AddLog($"📟 收到 PLC 触发指令 (M100 = 1)，开始执行检测...");

                        // 执行检测逻辑
                        StepToNextImageAndDetect("PLC触发");

                        // 【重要】检测完成后，主动把 PLC 的 M100 清 0
                        // 这就好比做完事回个电话：告诉 PLC “收到并执行完毕”
                        plc.Write("M100", (short)0);
                        AddLog($"✅ 检测完成，已向 PLC 清除指令 (M100 = 0)");

                        _isRunningDetection = false; // 解开锁
                    }
                }
            }
        }
        private void ShowImage(HImage image) 
        {
            
            Hwin.HalconWindow.ClearWindow();
            Hwin.HalconWindow.DispImage(image);
            Hwin.HalconWindow.SetPart(0, 0, -2, -2);
        }
        // ==========================================
        // 核心检测逻辑（提取出来的公共方法）
        // ==========================================
        private void RunOneDetection()
        {
            // 安全检查：如果没有当前图片路径，直接退出
            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            try
            {
                // 准备接收算法返回的参数
                string barcodeText;
                string statusStr;
                double matchRow, matchCol;
                BitmapSource tempImg;

                // 调用您的 Halcon 检测逻辑
                // 注意：这里会自动执行：匹配模板 -> 裁剪 -> 找条码 -> 返回结果
                halconProcessor.RunOneImage(_currentImagePath, out tempImg, out barcodeText, out statusStr, out matchRow, out matchCol);


                if (plc != null && _heartbeatTimer != null && _heartbeatTimer.IsEnabled)
                {
                    bool isOK = (statusStr == "\"OK\"" );
                    var writeResult = plc.Write("M200", isOK);

                    if (writeResult.IsSuccess)
                    {
                        AddLog($"已向 PLC 输出结果: M200 = {(isOK ? "True(OK)" : "False(NG)")}");
                    }
                    else
                    {
                        AddLog($"⚠️ 向 PLC 输出结果失败: {writeResult.Message}");
                    }
                }
                // ====== 更新右侧界面的文本显示 ======
                TxtResult.Text = $"{statusStr} : {barcodeText}";
                if (statusStr == "\"OK\"")
                    TxtResult.Foreground = System.Windows.Media.Brushes.Green;
                else
                    TxtResult.Foreground = System.Windows.Media.Brushes.Red;

                // ====== 在 Halcon 窗口中绘制绿框和绿字 ======
                Hwin.HalconWindow.ClearWindow();
                Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                Hwin.HalconWindow.DispImage(new HImage(_currentImagePath));

                if (!string.IsNullOrEmpty(barcodeText))
                {
                    Hwin.HalconWindow.SetColor("green");
                    Hwin.HalconWindow.SetLineWidth(3);
                    Hwin.HalconWindow.SetDraw("margin");



                    // 画框（用检测到的真实坐标 matchRow, matchCol）
                    Hwin.HalconWindow.DispRectangle2(
                        new HTuple(matchRow),
                        new HTuple(matchCol),
                        new HTuple(0),
                        new HTuple(100),
                        new HTuple(200)
                    );

                    // 写绿字
                    Hwin.HalconWindow.SetColor("green");
                    Hwin.HalconWindow.SetTposition(50, 50);
                    Hwin.HalconWindow.WriteString($"条码信息: {barcodeText}");
                    Hwin.HalconWindow.NewLine();
                    Hwin.HalconWindow.SetTposition(100, 50);
                    Hwin.HalconWindow.WriteString($"检测结果: {statusStr}");
                }
                else
                {
                    // 如果是 NG，在图上标红字提示
                    Hwin.HalconWindow.SetColor("red");
                    Hwin.HalconWindow.SetTposition(50, 50);
                    Hwin.HalconWindow.WriteString($"检测结果: {statusStr}");
                }

                // 记录一条日志
                AddLog($"检测完成: {statusStr} (条码: {barcodeText})");
            }
            catch (Exception ex)
            {
                // 如果发生异常，弹窗提示，并记录错误日志
                AddLog($"检测异常: {ex.Message}");
                System.Windows.MessageBox.Show("检测异常: " + ex.Message);
            }


        }

        // ==========================================
        // 核心动作：从文件夹取下一张图并检测（无论谁来触发都能用）
        // ==========================================
        private void StepToNextImageAndDetect(string triggerSource)
        {
            // ==========================================
            // 1. 窗体1 (Hwin) -> 路径 /1
            // ==========================================
            if (_loopImageFiles_Cam1 != null && _loopImageFiles_Cam1.Length > 0)
            {
                // 获取当前需要检测的图片路径
                string file1 = _loopImageFiles_Cam1[_loopIndex_Cam1];
                _currentImagePath = file1; // 更新全局当前路径，以便 RunOneDetection 使用

                // 【恢复检测功能】
                // 直接在界面显示图片（不强制清空，由检测逻辑覆盖）
                // 调用您的核心检测方法（包含：找模板 -> 读条码 -> 显示绿框和结果）
                RunOneDetection();

                // 检测完成后，索引 + 1，为下一次触发做准备
                _loopIndex_Cam1++;
                if (_loopIndex_Cam1 >= _loopImageFiles_Cam1.Length) _loopIndex_Cam1 = 0;
            }

            // ==========================================
            // 2. 窗体2 (Hwin2) -> 路径 /2 (调用新算法并实时显示结果)
            // ==========================================
            if (_loopImageFiles_Cam2 != null && _loopImageFiles_Cam2.Length > 0)
            {
                string file2 = _loopImageFiles_Cam2[_loopIndex_Cam2];

                // 1. 读取图片
                HImage img2 = new HImage(file2);

                // 2. 清空窗口，铺底图（先清空，再显示干净的原图，避免留下上一帧的残影）
                Hwin2.HalconWindow.ClearWindow();
                Hwin2.HalconWindow.SetPart(0, 0, -2, -2);
                Hwin2.HalconWindow.DispImage(img2);

                // 3. 调用相机2的 Halcon 算法
                string resultRing, resultFilter;
                HTuple area1, area2;
                halconProcessor_Cam2.RunOneImage(file2, out resultRing, out resultFilter, out area1, out area2);

                // 4. 将结果打印到右侧日志
                AddLog($"📊 相机2检测: 钢圈状态=【{resultRing}】, 滤芯状态=【{resultFilter}】");

                // =======================================================
                // 【核心新增】在 Hwin2 窗口上直接画出绿色/红色结果文字
                // =======================================================

                // 【终极修复】使用微软雅黑字体，字号24。双引号必须严格照抄。
                //Hwin2.HalconWindow.SetFont("-Microsoft YaHei-24-*");

                // 第一行：钢圈有无检测结果
                Hwin2.HalconWindow.SetTposition(50, 50); // 坐标(行, 列) => 左上角
                if (resultRing == "OK")
                {
                    Hwin2.HalconWindow.SetColor("green");
                    Hwin2.HalconWindow.WriteString($"钢圈有无检测：{resultRing}");
                }
                else
                {
                    Hwin2.HalconWindow.SetColor("red");
                    Hwin2.HalconWindow.WriteString($"钢圈有无检测：{resultRing}");
                }

                // 第二行：滤芯有无检测结果 (往下偏移 50 像素)
                Hwin2.HalconWindow.SetTposition(100, 50);
                if (resultFilter == "OK")
                {
                    Hwin2.HalconWindow.SetColor("green");
                    Hwin2.HalconWindow.WriteString($"滤芯有无检测：{resultFilter}");
                }
                else
                {
                    Hwin2.HalconWindow.SetColor("red");
                    Hwin2.HalconWindow.WriteString($"滤芯有无检测：{resultFilter}");
                }

                // 2. 翻页索引推进
                _loopIndex_Cam2++;
                if (_loopIndex_Cam2 >= _loopImageFiles_Cam2.Length) _loopIndex_Cam2 = 0;
            }
            // 记录日志
            if (triggerSource != "")
                {
                    AddLog($"📸 [{triggerSource}] 触发翻页");
                }
            }
        
        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            // 1. 弹出文件夹选择框
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "请选择包含 /1 和 /2 子文件夹的【父文件夹】";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string basePath = dialog.SelectedPath;

                // 2. 自动拼接子路径：相机1对应 /1，相机2对应 /2
                string pathCam1 = System.IO.Path.Combine(basePath, "1");
                string pathCam2 = System.IO.Path.Combine(basePath, "2");

                // ==========================================
                // 3. 检查路径是否存在，如果存在则读取并显示
                // ==========================================

                // --- 处理相机1 (Hwin) ---
                if (System.IO.Directory.Exists(pathCam1))
                {
                    _loopImageFiles_Cam1 = System.IO.Directory.GetFiles(pathCam1, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam1);
                    _loopIndex_Cam1 = 0;
                    AddLog($"📁 相机1 [Hwin] 路径: {pathCam1}，找到 {_loopImageFiles_Cam1.Length} 张图片");

                    if (_loopImageFiles_Cam1.Length > 0)
                    {
                        // 显示第一张图
                        HImage img1 = new HImage(_loopImageFiles_Cam1[0]);
                        Hwin.HalconWindow.ClearWindow();
                        Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                        Hwin.HalconWindow.DispImage(img1);
                    }
                }
                else
                {
                    AddLog($"⚠️ 未找到相机1路径: {pathCam1}");
                }

                // --- 处理相机2 (Hwin2) ---
                if (System.IO.Directory.Exists(pathCam2))
                {
                    _loopImageFiles_Cam2 = System.IO.Directory.GetFiles(pathCam2, "*.jpg");
                    Array.Sort(_loopImageFiles_Cam2);
                    _loopIndex_Cam2 = 0;
                    AddLog($"📁 相机2 [Hwin2] 路径: {pathCam2}，找到 {_loopImageFiles_Cam2.Length} 张图片");

                    if (_loopImageFiles_Cam2.Length > 0)
                    {
                        // 显示第一张图
                        HImage img2 = new HImage(_loopImageFiles_Cam2[0]);
                        Hwin2.HalconWindow.ClearWindow();
                        Hwin2.HalconWindow.SetPart(0, 0, -2, -2);
                        Hwin2.HalconWindow.DispImage(img2);
                    }
                }
                else
                {
                    AddLog($"⚠️ 未找到相机2路径: {pathCam2}");
                }
            }
        }


        private void LoopTimer_Tick(object sender, EventArgs e)
        {
            // 这里不用写翻页逻辑，因为翻页逻辑已经在 StepToNextImageAndDetect 里了
            StepToNextImageAndDetect("自动循环");
        }
        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            // ==========================================
            // 1. 安全检测：确保 相机1(Hwin) 或 相机2(Hwin2) 至少有一个加载了图片
            // ==========================================
            bool hasCam1 = (_loopImageFiles_Cam1 != null && _loopImageFiles_Cam1.Length > 0);
            bool hasCam2 = (_loopImageFiles_Cam2 != null && _loopImageFiles_Cam2.Length > 0);

            // 如果两个文件夹都没有图片，提示并退出
            if (!hasCam1 && !hasCam2)
            {
                System.Windows.MessageBox.Show("请先点击【选择图片】按钮，选择路径以 /1 或 /2 结尾的图片文件夹！");
                return;
            }

            // ==========================================
            // 2. 开启/停止 循环定时器
            // ==========================================
            if (_loopTimer == null)
            {
                _loopTimer = new System.Windows.Threading.DispatcherTimer();
                _loopTimer.Interval = TimeSpan.FromMilliseconds(500);
                _loopTimer.Tick += LoopTimer_Tick;
            }

            if (!_loopTimer.IsEnabled)
            {
                _loopTimer.Start();
                AddLog("🔁 开始双工位循环检测，间隔 0.5 秒一张...");

                // 启动后立刻执行第一张
                StepToNextImageAndDetect("自动循环");
            }
        }

        private void BtnSearchHistory_Click(object sender, RoutedEventArgs e)
        {

        }
        // ==========================================
        // 添加操作日志的方法
        // ==========================================
        private void AddLog(string message)
        {
            // 1. 确保在 UI 线程执行 (防止后台线程报错)
            Dispatcher.Invoke(() =>
            {
                // 2. 获取当前时间
                string time = DateTime.Now.ToString("HH:mm:ss");

                // 3. 创建一行新日志
                TextBlock newLine = new TextBlock();
                newLine.Text = $"[{time}] {message}";
                newLine.Foreground = System.Windows.Media.Brushes.LightGray; // 默认灰色
                newLine.FontSize = 12;
                newLine.Margin = new Thickness(0, 2, 0, 2);

                // 4. 插到列表的最前面 (最新日志在最上面)
                LogPanel.Children.Insert(0, newLine);

                // 5. (可选) 防止日志太多撑爆内存：最多保留 300 条
                if (LogPanel.Children.Count > 300)
                {
                    LogPanel.Children.RemoveAt(LogPanel.Children.Count - 1);
                }
            });
        }
        private void BtnConnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 尝试获取设备列表
                var deviceList = _cameraManager.GetDeviceList();

                // 检查是否找到相机（如果是电脑没有连物理相机，您可以先运行海康 MVS 里的虚拟相机）
                if (deviceList.nDeviceNum == 0)
                {
                    System.Windows.MessageBox.Show("未发现任何相机设备！\n请确认海康 MVS 的虚拟相机已开启。");
                    return;
                }

                // 连接列表里的第一个相机
                var deviceInfo = (MyCamera.MV_CC_DEVICE_INFO)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    deviceList.pDeviceInfo[0], typeof(MyCamera.MV_CC_DEVICE_INFO));

                if (_cameraManager.ConnectCamera(deviceInfo))
                {
                    // 订阅相机采到图的回调事件
                    _cameraManager.OnImageCaptured += OnCameraImageArrived;

                    // 开始疯狂采图（一秒几十张）
                    _cameraManager.StartGrabbing();

                    // 更新 UI（左栏绿灯亮起）
                    CamStatusLight.Fill = System.Windows.Media.Brushes.LimeGreen;
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
        private void OnCameraImageArrived(HImage image)
        {
            // 注意！这个回调是在后台线程运行的，不能直接修改 WPF 界面！
            // 必须通过 Dispatcher 切回 UI 主线程来执行。
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. 把相机的图显示到左边的黑框里
                    Hwin.HalconWindow.ClearWindow();
                    Hwin.HalconWindow.SetPart(0, 0, -2, -2);
                    Hwin.HalconWindow.DispImage(image);

                    // 2. 把 HImage 存到硬盘临时路径，并赋值给全局检测变量
                    // 因为您的 HalconProcessor 使用的是读取本地路径的字符串
                    string tempPath = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".jpg";
                    image.WriteImage("jpg", 0, tempPath); // 保存成临时文件
                    _currentImagePath = tempPath;

                    // 3. 调用您的检测逻辑（之前写的多线程方法）
                    RunOneDetection();
                }
                catch { /* 忽略偶尔的图片转换小错误，防止程序崩溃 */ }
            });
        }
        private void BtnDisconnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 强制取消订阅事件，防止残留回调
                if (_cameraManager != null)
                {
                    _cameraManager.OnImageCaptured -= OnCameraImageArrived;
                }

                // 2. 执行彻底断开并释放内存
                if (_cameraManager != null)
                {
                    _cameraManager.StopGrabbing(); // 这里包含了停止采集、关闭设备、清理线程
                }

                // 3. 手动触发一次垃圾回收（强制释放海康SDK占用的非托管内存）
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 4. 更新界面
                CamStatusLight.Fill = System.Windows.Media.Brushes.Gray;
                TxtResult.Text = "相机已断开。";
                AddLog("相机已安全断开连接，资源已释放。");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("断开相机时发生异常: " + ex.Message);
            }
        }

        private void BtnSingle(object sender, RoutedEventArgs e)
        {
            // 1. 如果正在自动循环，先暂停它（防止冲突）
            if (_loopTimer != null && _loopTimer.IsEnabled)
            {
                _loopTimer.Stop();
                BtnRun.Content = "🔁 循环检测"; // 把按钮文字改回去
                AddLog("⏸️ 自动循环已暂停，执行单次触发。");
            }

            // 2. 直接调用统一的翻页检测方法（不管是单张还是循环文件夹，它都会处理）
            StepToNextImageAndDetect("单次触发");
        }

        private void BtnStopLoop(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null && _loopTimer.IsEnabled)
            {
                _loopTimer.Stop();
                AddLog("⏹️ 已停止循环检测。");
            }
            else
            {
                AddLog("当前没有在运行循环检测。");
            }
        }
    } }