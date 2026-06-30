using RobotControlSystem.Interfaces;
using System.Windows;
using System;
using System.IO;
using System.Linq;

namespace RobotControlSystem
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 调试输出：应用程序基目录，并检查 Plugins 目录及其内容
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"[Startup] AppDomain.CurrentDomain.BaseDirectory: {baseDir}");

                string pluginsPath = Path.Combine(baseDir, "Plugins");
                if (Directory.Exists(pluginsPath))
                {
                    var files = Directory.GetFiles(pluginsPath);
                    System.Diagnostics.Debug.WriteLine($"[Startup] Plugins folder exists: {pluginsPath} (files: {files.Length})");
                    foreach (var f in files)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Startup] Plugin file: {f}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Startup] Plugins folder NOT found at: {pluginsPath}");

                    // 尝试在基目录下及其子目录中查找 Plugin.UserDevice DLL
                    try
                    {
                        var found = Directory.GetFiles(baseDir, "Plugin.UserDevice*.dll", SearchOption.AllDirectories);
                        if (found != null && found.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Startup] Found Plugin.UserDevice dll(s) under base directory:");
                            foreach (var f in found)
                                System.Diagnostics.Debug.WriteLine($"[Startup]  {f}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[Startup] Plugin.UserDevice.dll not found under AppDomain.BaseDirectory.");

                            // 进一步检查常见的输出位置（例如项目中的 RCS\bin\Debug\net8.0-windows\Plugins）
                            string checkCandidate = Path.GetFullPath(Path.Combine(baseDir, "..", "RCS", "bin", "Debug", "net8.0-windows", "Plugins"));
                            if (Directory.Exists(checkCandidate))
                            {
                                System.Diagnostics.Debug.WriteLine($"[Startup] Found Plugins at candidate path: {checkCandidate}");
                                var candFiles = Directory.GetFiles(checkCandidate);
                                foreach (var f in candFiles)
                                    System.Diagnostics.Debug.WriteLine($"[Startup] Candidate plugin file: {f}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[Startup] Candidate path does not exist: {checkCandidate}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Startup] Error searching for Plugin.UserDevice.dll: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Startup] Error while inspecting Plugins folder: {ex.Message}");
            }

            // 1. 加载所有插件，初始化单例
            DeviceSingleton.Instance.Initialize();

            // 全局服务存储插件
            GlobalServices.UserDevice = PluginManager.LoadFirstPlugin<IUserDevice>();

            // 2. 订阅用户传输装置事件（如果有插件）
            var userDevice = GlobalServices.UserDevice;
            if (userDevice != null)
            {
                userDevice.StatusChanged += OnUserDeviceStatusChanged;

                // 从数据库读取配置（示例：监听端口 7799，设备 ID 为 GST200）
                string config = "{\"listenPort\":7799,\"deviceId\":\"GST200\"}";
                userDevice.SetParameters(config);

                // 启动连接和监控
                _ = userDevice.ConnectAsync();
                userDevice.StartMonitoring();

                System.Diagnostics.Debug.WriteLine("✅ 用户传输装置插件已加载并启动");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 未找到用户传输装置插件");
            }

            // 3. 订阅 AGV 事件（可选）
            var agv = DeviceSingleton.Instance.Agv;
            if (agv != null)
            {
                agv.StatusChanged += OnAgvStatusChanged;
                System.Diagnostics.Debug.WriteLine("✅ AGV 插件已加载");
            }

            // 4. 订阅机器人事件
            var robot = DeviceSingleton.Instance.Robot;
            if (robot != null)
            {
                robot.StatusChanged += OnRobotStatusChanged;
                System.Diagnostics.Debug.WriteLine("✅ 机器人插件已加载");
            }
        }

        private void OnUserDeviceStatusChanged(object sender, DeviceEventArgs e)
        {
            // 主程序处理用户传输装置的状态变化（如火警）
            System.Diagnostics.Debug.WriteLine($"[UserDevice] {e.Status}: {e.Message}");

            // 现在由 EventProcessor 统一处理事件匹配，不再手动判断火警
            // EventProcessor 会在后台线程匹配规则并触发配方执行
        }

        private void OnAgvStatusChanged(object sender, DeviceEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[AGV] {e.Status}: {e.Message}");
            // 可更新 UI 状态，通过主窗口的 Dispatcher
        }

        private void OnRobotStatusChanged(object sender, DeviceEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Robot] {e.Status}: {e.Message}");
        }
    }
}