using HalconDotNet;
using MvCamCtrl.NET;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Media3D;

namespace MyVisionDemo
{
    public class HikCameraManager
    {
        private MyCamera m_pMyCamera = new MyCamera();
        private bool m_bGrabbing = false;
        private Thread m_CaptureThread;

        // 用于通知 WPF 界面：拍到图了，请进行检测！
        public event Action<HImage> OnImageCaptured;

        // 1. 获取相机列表（用于UI下拉框填充）
        public MyCamera.MV_CC_DEVICE_INFO_LIST GetDeviceList()
        {
            var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref deviceList);
            if (nRet != MyCamera.MV_OK) return new MyCamera.MV_CC_DEVICE_INFO_LIST();
            return deviceList;
        }

        // 2. 连接相机
        public bool ConnectCamera(MyCamera.MV_CC_DEVICE_INFO deviceInfo)
        {
            int nRet = m_pMyCamera.MV_CC_CreateDevice_NET(ref deviceInfo);
            if (nRet != MyCamera.MV_OK) return false;

            nRet = m_pMyCamera.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK) return false;

            // 设为连续采集模式
            m_pMyCamera.MV_CC_SetEnumValue_NET("AcquisitionMode", 2);
            m_pMyCamera.MV_CC_SetEnumValue_NET("TriggerMode", 0);
            return true;
        }

        // 3. 开始采集
        public void StartGrabbing()
        {
            if (m_bGrabbing) return;
            int nRet = m_pMyCamera.MV_CC_StartGrabbing_NET();
            if (nRet != MyCamera.MV_OK) return;

            m_bGrabbing = true;
            m_CaptureThread = new Thread(CaptureLoop);
            m_CaptureThread.Start(m_pMyCamera);
        }

        // 4. 后台采图线程（核心转换部分：从内存指针 -> Halcon HImage）
        private void CaptureLoop(object obj)
        {
            MyCamera device = obj as MyCamera;
            MyCamera.MV_FRAME_OUT stFrameOut = new MyCamera.MV_FRAME_OUT();

            while (m_bGrabbing)
            {
                int nRet = device.MV_CC_GetImageBuffer_NET(ref stFrameOut, 100);
                if (nRet == MyCamera.MV_OK)
                {
                    try
                    {
                        HObject hobj;
                        // 我们直接简化处理，假设您是用黑白（Mono8）相机
                        HOperatorSet.GenImage1Extern(out hobj, "byte", stFrameOut.stFrameInfo.nWidth,
                                                    stFrameOut.stFrameInfo.nHeight, stFrameOut.pBufAddr, IntPtr.Zero);

                        HImage image = new HImage(hobj);

                        // 触发事件，把HImage发给WPF主界面
                        OnImageCaptured?.Invoke(image);

                        hobj.Dispose();
                    }
                    catch { }
                    finally
                    {
                        device.MV_CC_FreeImageBuffer_NET(ref stFrameOut);
                    }
                }
            }
        }

        // 5. 停止采集
        public void StopGrabbing()
        {
            try
            {
                // 1. 标记停止，让采图循环退出
                m_bGrabbing = false;

                // 2. 安全停止抓图（如果相机还活着）
                if (m_pMyCamera != null)
                {
                    // 官方代码：停止抓图
                    m_pMyCamera.MV_CC_StopGrabbing_NET();

                    // 官方代码：关闭设备
                    m_pMyCamera.MV_CC_CloseDevice_NET();
                }

                // 3. 强制中断后台采图线程（防止线程在 SDK 底层死锁）
                if (m_CaptureThread != null && m_CaptureThread.IsAlive)
                {
                    // 给它 1 秒钟优雅退出的时间
                    if (!m_CaptureThread.Join(1000))
                    {
                        // 如果 1 秒后它还不死，说明卡在 SDK 里了，直接暴力中止！
                        m_CaptureThread.Abort();
                    }
                    m_CaptureThread = null;
                }

                // 4. 强制通知 .NET 释放非托管内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception)
            {
                // 如果官方代码报错，直接忽略，保证程序不死
            }
        }
    }
}