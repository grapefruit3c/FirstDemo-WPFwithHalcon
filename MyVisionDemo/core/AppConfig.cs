using System;
using System.IO;
using Newtonsoft.Json;

namespace _001Halconfirst
{
    /// <summary>
    /// 应用配置管理：从 appsettings.json 加载，支持自动创建默认配置
    /// 替换原 core.cs 中硬编码的 PathConfig
    /// </summary>
    public static class AppConfig
    {
        private static ConfigModel _config;
        private static readonly string _configPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        /// <summary>
        /// 获取配置实例（首次访问时自动加载）
        /// </summary>
        public static ConfigModel Current
        {
            get
            {
                if (_config == null)
                    Load();
                return _config;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载配置，文件不存在则创建默认配置
        /// </summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<ConfigModel>(json) ?? CreateDefault();
                }
                else
                {
                    _config = CreateDefault();
                    Save();
                }
            }
            catch (Exception)
            {
                _config = CreateDefault();
            }
        }

        /// <summary>
        /// 保存当前配置到 JSON 文件
        /// </summary>
        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        /// <summary>
        /// 获取模板路径（自动解析相对路径为绝对路径）
        /// </summary>
        public static string GetTemplatePath()
        {
            string path = Current.Paths.TemplatePath;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return path;
        }

        private static ConfigModel CreateDefault()
        {
            return new ConfigModel
            {
                Paths = new PathsConfig
                {
                    TemplatePath = "ModelID/barcode_template.shm",
                    RootImagePath = "",
                    BarcodePath = "",
                    CodeimagePath = "",
                    Industry25Path = ""
                },
                PLC = new PlcConfig
                {
                    Type = "S1200",
                    IpAddress = "127.0.0.1",
                    Rack = 0,
                    Slot = 1,
                    TriggerAddress = "M100",
                    TriggerValue = 256,
                    ResultAddress = "M200",
                    HeartbeatAddress = "DB1.0",
                    HeartbeatIntervalMs = 1000,
                    AutoReconnect = true,
                    ReconnectIntervalMs = 5000
                },
                Detection = new DetectionConfig
                {
                    MinScore = 0.65,
                    NumMatches = 1,
                    RoiOffset = 300,
                    BarcodeType = "Code 128",
                    BarcodeElementSizeMin = 1,
                    BarcodeElementSizeMax = 30,
                    BarcodeOrientation = 45,
                    Cam2SteelRingAreaThreshold = 20000,
                    Cam2FilterAreaThreshold = 20000,
                    Cam2ThresholdMin = 70,
                    Cam2ThresholdMax = 130
                },
                Camera = new CameraConfig
                {
                    LoopIntervalMs = 500,
                    MaxLogCount = 300
                }
            };
        }
    }

    public class ConfigModel
    {
        public PathsConfig Paths { get; set; }
        public PlcConfig PLC { get; set; }
        public DetectionConfig Detection { get; set; }
        public CameraConfig Camera { get; set; }
    }

    public class PathsConfig
    {
        public string TemplatePath { get; set; }
        public string RootImagePath { get; set; }
        public string BarcodePath { get; set; }
        public string CodeimagePath { get; set; }
        public string Industry25Path { get; set; }
    }

    public class PlcConfig
    {
        public string Type { get; set; }
        public string IpAddress { get; set; }
        public int Rack { get; set; }
        public int Slot { get; set; }
        public string TriggerAddress { get; set; }
        public int TriggerValue { get; set; }
        public string ResultAddress { get; set; }
        public string HeartbeatAddress { get; set; }
        public int HeartbeatIntervalMs { get; set; }
        public bool AutoReconnect { get; set; }
        public int ReconnectIntervalMs { get; set; }
    }

    public class DetectionConfig
    {
        public double MinScore { get; set; }
        public int NumMatches { get; set; }
        public int RoiOffset { get; set; }
        public string BarcodeType { get; set; }
        public int BarcodeElementSizeMin { get; set; }
        public int BarcodeElementSizeMax { get; set; }
        public double BarcodeOrientation { get; set; }
        public double Cam2SteelRingAreaThreshold { get; set; }
        public double Cam2FilterAreaThreshold { get; set; }
        public int Cam2ThresholdMin { get; set; }
        public int Cam2ThresholdMax { get; set; }
    }

    public class CameraConfig
    {
        public int LoopIntervalMs { get; set; }
        public int MaxLogCount { get; set; }
    }
}
