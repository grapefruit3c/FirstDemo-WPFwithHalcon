# 更新记录

## [v2.0.0] - 2026-08-13 代码全面优化

### 新增

- **appsettings.json**：配置文件，所有路径、PLC 参数、检测阈值、相机参数集中管理
- **AppConfig.cs**：配置管理类，自动加载/创建/保存配置，支持相对路径解析（替代原 core.cs 的硬编码 PathConfig）
- **HObjectExtensions.cs**：HALCON HObject 扩展方法（ReduceDomain / Threshold / Connection / SelectShape / GenCircle / GenRectangle1 / GenRectangle2 / SafeDispose / SafeDisposeAll），简化 HOperatorSet 调用
- **CHANGELOG.md**：本文件

### 修改

#### 配置外部化
- 所有硬编码路径（`E:\Desktop\测试文件夹\...`）提取到 `appsettings.json`
- PLC 地址（IP、触发地址 M100、结果地址 M200、心跳地址 DB1.0）可配置
- 检测参数（匹配分数 0.65、ROI 偏移量 300、面积阈值 20000、灰度阈值 70-130）可配置
- 相机参数（循环间隔 500ms、最大日志数 300）可配置
- `.csproj` 添加 Newtonsoft.Json 包引用和 appsettings.json / 模型文件自动复制到输出目录

#### HALCON 算法层
- **HalconProcessor.cs**：
  - 移除 15+ 个实例字段全局变量，改用方法局部变量（线程安全）
  - 条码模型从每次检测创建/销毁改为初始化时创建一次、运行时复用
  - 实现 `IDisposable` 接口，规范化模型资源释放
  - 算法参数从 AppConfig 读取
  - 移除注释掉的废弃代码
- **HalconProcessor_Cam2.cs**：
  - 检测阈值参数从 AppConfig 读取
  - 使用 HObjectExtensions 扩展方法简化代码
  - 资源释放使用 SafeDisposeAll 统一管理

#### 相机管理 (HikCameraManager.cs)
- **修复 use-after-free**：`GenImage1Extern`（引用指针）改为 `GenImage1`（拷贝数据），防止海康缓冲区释放后 HImage 访问已释放内存
- **CancellationToken 替代 Thread.Abort**：使用协作式取消替代已过时的 `Thread.Abort()`
- 移除 `GC.Collect()` 反模式
- 新增 `OnError` 事件通知异常，不再吞掉错误

#### PLC 通信 (MainWindow.xaml.cs)
- **修复双 ConnectServer bug**：原代码在 Window_Loaded 中调用了两次 `plc.ConnectServer()`，第二次创建多余连接
- **新增断线自动重连**：心跳丢失后启动重连定时器（默认 5 秒间隔），PLC 恢复后自动恢复心跳监测
- 触发值 256 字节序问题添加注释说明（西门子 M 区字节序差异）
- PLC 地址从配置文件读取

#### UI 与日志
- 相机回调从 `Dispatcher.Invoke`（同步阻塞）改为 `Dispatcher.BeginInvoke`（异步），不阻塞采图线程
- HImage 使用后及时 `Dispose`（`using` 模式）
- 改进异常处理，不再用空 `catch {}` 吞掉错误
- 日志上限从配置文件读取

### 删除
- **core.cs**：被 AppConfig.cs 替代（原文件中所有路径为硬编码个人桌面路径）

### 修复
- 模板文件 `barcode_template.shm` 配置为自动复制到输出目录（解决运行时找不到模板文件的问题）
- PLC Rack/Slot 属性 byte 类型转换

---

## [v1.0.0] - 2026-08-12 初始版本

- 完成空滤器视觉检测系统原型
- 实现单工位双曝光检测（条码识别 + 钢圈/滤芯有无检测）
- 集成海康 MVS 相机 SDK
- 集成 HslCommunication PLC 通讯（西门子 S7 协议）
- 三种运行模式：单张调试、批量循环、PLC 自动触发
- 实时检测结果显示与日志记录
