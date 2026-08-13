# 更新记录

## [v2.1.0] - 2026-08-13 生产功能 + UI 优化

### 新增

- **ProductionLogger.cs**：CSV 生产日志记录器，每周期记录时间/条码/结果/钢圈/滤芯/匹配耗时/总耗时/图片路径，按日期分文件
- **ImageArchiver.cs**：图像自动存档，按 `Archive/日期/OK或NG/时间戳_条码.jpg` 分目录保存，可配置是否存 OK 图、NG 图、JPG 质量
- **DetectionStats.cs**：检测统计（OK/NG 计数、良率计算）+ 检测计时器（Stopwatch 分步计时）
- **统计看板 UI**：良率大字显示（>95% 绿/90-95% 黄/<90% 红）、OK/NG/总数三色计数器、重置按钮
- **检测计时面板**：左栏显示匹配耗时、条码识别耗时、总耗时
- **顶栏时钟**：实时显示当前日期时间
- **历史图片搜索**：按条码或日期关键字搜索存档目录中的图片
- **配置项**：appsettings.json 新增 Archive（存档）和 Logging（日志）配置段

### 修改

#### UI 优化 (MainWindow.xaml)
- 全新深色主题配色（`#1E1E2E` / `#252535` / `#0D0D15`），更现代的工业风
- 左栏重新分区：设备状态（圆角卡片）→ 检测计时 → 操作面板
- 右栏重新分区：检测状态 → 生产统计（良率 + OK/NG/总数）→ 操作日志 → 历史搜索
- 中栏图像窗口边框改为圆角 + 细边
- 所有字体/间距/圆角统一规范
- 窗口尺寸从 1200x700 调整为 1280x750

#### 功能集成 (MainWindow.xaml.cs)
- 检测流程集成计时：每次检测记录匹配耗时和总耗时
- 检测结果自动统计：OK/NG 实时计数 + 良率自动计算
- 检测图片自动存档：NG 图默认保存（OK 图可选）
- 每周期写入 CSV 生产日志
- Halcon 窗口绘制增强：匹配框 + 十字标记 + 条码文本 + 耗时显示
- 历史搜索功能实现：扫描 Archive 目录按关键字匹配

#### 配置扩展 (AppConfig.cs)
- 新增 `ArchiveConfig`：存档开关、根目录、OK/NG 保存控制、JPG 质量
- 新增 `LoggingConfig`：日志开关、日志目录
- 新增 `GetArchiveRoot()` 和 `GetLogDirectory()` 相对路径解析方法

### 配置新增项

```json
"Archive": {
  "Enabled": true,
  "ArchiveRoot": "Archive",
  "SaveOKImages": false,
  "SaveNGImages": true,
  "JpgQuality": 80
},
"Logging": {
  "Enabled": true,
  "LogDirectory": "Logs"
}
```

---

## [v2.0.0] - 2026-08-13 代码全面优化

### 新增

- **appsettings.json**：配置文件，所有路径、PLC 参数、检测阈值、相机参数集中管理
- **AppConfig.cs**：配置管理类，自动加载/创建/保存配置，支持相对路径解析（替代原 core.cs 的硬编码 PathConfig）
- **HObjectExtensions.cs**：HALCON HObject 扩展方法（ReduceDomain / Threshold / Connection / SelectShape / GenCircle / GenRectangle1 / GenRectangle2 / SafeDispose / SafeDisposeAll），简化 HOperatorSet 调用
- **CHANGELOG.md**：本文件

### 修改

#### 配置外部化
- 所有硬编码路径提取到 `appsettings.json`
- PLC 地址、检测参数、相机参数全部可配置
- `.csproj` 添加 Newtonsoft.Json 包引用和 appsettings.json / 模型文件自动复制到输出目录

#### HALCON 算法层
- **HalconProcessor.cs**：移除实例字段全局变量（线程安全）、条码模型复用、IDisposable、参数从 AppConfig 读取
- **HalconProcessor_Cam2.cs**：阈值参数配置化、扩展方法简化、SafeDisposeAll 统一释放

#### 相机管理 (HikCameraManager.cs)
- 修复 use-after-free（GenImage1Extern → GenImage1 拷贝数据）
- CancellationToken 替代 Thread.Abort
- 移除 GC.Collect，新增 OnError 事件

#### PLC 通信 (MainWindow.xaml.cs)
- 修复双 ConnectServer bug
- 新增断线自动重连（5 秒间隔）
- 触发值 256 字节序注释说明

#### UI 与日志
- 相机回调改用 BeginInvoke（异步）
- HImage 规范化 Dispose
- 改进异常处理

### 删除
- **core.cs**：被 AppConfig.cs 替代

### 修复
- 模板文件自动复制到输出目录
- PLC Rack/Slot byte 类型转换

---

## [v1.0.0] - 2026-08-12 初始版本

- 完成空滤器视觉检测系统原型
- 实现单工位双曝光检测（条码识别 + 钢圈/滤芯有无检测）
- 集成海康 MVS 相机 SDK
- 集成 HslCommunication PLC 通讯（西门子 S7 协议）
- 三种运行模式：单张调试、批量循环、PLC 自动触发
- 实时检测结果显示与日志记录
