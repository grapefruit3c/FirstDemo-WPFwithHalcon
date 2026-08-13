using HalconDotNet;
using MvCamCtrl.NET;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MyVisionDemo
{
    /// <summary>
    /// 海康相机管理器
    /// 优化点：
    /// 1. GenImage1 替代 GenImage1Extern（拷贝数据，防止 use-after-free）
    /// 2. CancellationToken 替代 Thread.Abort（安全取消）
    /// 3. 移除 GC.Collect（改用规范 Dispose）
    /// 4. 异常处理改进（不再吞掉异常）
    /// </summary>
    public class HikCameraManager
    {
        private MyCamera m_pMyCamera = new MyCamera();
        private bool m_bGrabbing = false;
        private Thread m_CaptureThread;
        private CancellationTokenSource m_cts;

        /// <summary>拍到图了，通知 WPF 界面进行检测</summary>
        public event Action<HImage> OnImageCaptured;

        /// <summary>采集异常通知</summary>
        public event Action<string> OnError;

        // 1. 获取相机列表
        public MyCamera.MV_CC_DEVICE_INFO_LIST GetDeviceList()
        {
            var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int nRet = MyCamera.MV_CC_EnumDevices_NET(
                MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref deviceList);
            if (nRet != MyCamera.MV_OK)
                return new MyCamera.MV_CC_DEVICE_INFO_LIST();
            return deviceList;
        }

        // 2. 连接相机
        public bool ConnectCamera(MyCamera.MV_CC_DEVICE_INFO deviceInfo)
        {
            int nRet = m_pMyCamera.MV_CC_CreateDevice_NET(ref deviceInfo);
            if (nRet != MyCamera.MV_OK) return false;

            nRet = m_pMyCamera.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK) return false;

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
            m_cts = new CancellationTokenSource();
            m_CaptureThread = new Thread(() => CaptureLoop(m_pMyCamera, m_cts.Token))
            {
                IsBackground = true
            };
            m_CaptureThread.Start();
        }

        // 4. 后台采图线程
        private void CaptureLoop(MyCamera device, CancellationToken token)
        {
            MyCamera.MV_FRAME_OUT stFrameOut = new MyCamera.MV_FRAME_OUT();

            while (m_bGrabbing && !token.IsCancellationRequested)
            {
                int nRet = device.MV_CC_GetImageBuffer_NET(ref stFrameOut, 100);
                if (nRet == MyCamera.MV_OK)
                {
                    try
                    {
                        int width = stFrameOut.stFrameInfo.nWidth;
                        int height = stFrameOut.stFrameInfo.nHeight;
                        int bufSize = width * height;

                        // 【关键修复】拷贝数据到托管内存，再创建 HImage
                        // 原 GenImage1Extern 只是引用指针，FreeImageBuffer 后会导致 use-after-free
                        byte[] imageBuffer = new byte[bufSize];
                        Marshal.Copy(stFrameOut.pBufAddr, imageBuffer, 0, bufSize);

                        // 使用 GenImage1 创建独立的 HImage（拥有自己的数据副本）
                        HImage image;
                        GCHandle handle = GCHandle.Alloc(imageBuffer, GCHandleType.Pinned);
                        try
                        {
                            HOperatorSet.GenImage1(out HObject hobj, "byte", width, height, handle.AddrOfPinnedObject());
                            image = new HImage(hobj);
                            hobj.Dispose();
                        }
                        finally
                        {
                            handle.Free();
                        }

                        // 通知 UI 线程（HImage 有独立数据副本，安全传递）
                        OnImageCaptured?.Invoke(image);
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke($"图像转换异常: {ex.Message}");
                    }
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
                m_bGrabbing = false;

                // 通过 CancellationToken 通知线程退出
                if (m_cts != null)
                {
                    m_cts.Cancel();
                    m_cts.Dispose();
                    m_cts = null;
                }

                if (m_pMyCamera != null)
                {
                    m_pMyCamera.MV_CC_StopGrabbing_NET();
                    m_pMyCamera.MV_CC_CloseDevice_NET();
                }

                // 等待线程退出（不再使用 Thread.Abort）
                if (m_CaptureThread != null && m_CaptureThread.IsAlive)
                {
                    m_CaptureThread.Join(2000);
                }
                m_CaptureThread = null;

                // 不再手动 GC.Collect，依靠规范的 Dispose 管理内存
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"停止采集异常: {ex.Message}");
            }
        }
    }
}
