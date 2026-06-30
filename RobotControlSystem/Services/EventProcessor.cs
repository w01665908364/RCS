using System;
using System.Threading.Tasks;
using System.Windows;
using RobotControlSystem.Models;
using RobotControlSystem.Interfaces;

namespace RobotControlSystem.Services
{
    /// <summary>
    /// 事件处理器
    /// 负责订阅设备状态变化事件，并在后台线程中处理规则匹配
    /// 解决界面卡死问题
    /// </summary>
    public class EventProcessor : IDisposable
    {
        private IUserDevice? _userDevice;
        private bool _disposed = false;

        /// <summary>
        /// 规则匹配事件，当匹配到规则时触发（在UI线程）
        /// </summary>
        public event Action<EventRule, DeviceEventArgs>? RuleMatched;

        /// <summary>
        /// 当前正在处理的设备状态
        /// </summary>
        public DeviceStatus? CurrentStatus { get; private set; }

        /// <summary>
        /// 最后处理的消息
        /// </summary>
        public string? LastMessage { get; private set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public EventProcessor() { }

        /// <summary>
        /// 带设备实例的构造函数（推荐）
        /// </summary>
        /// <param name="userDevice">用户设备接口实例</param>
        public EventProcessor(IUserDevice? userDevice)
        {
            _userDevice = userDevice;
            if (_userDevice != null)
            {
                _userDevice.StatusChanged += OnDeviceStatusChanged;
                System.Diagnostics.Debug.WriteLine("[EventProcessor] 已订阅设备状态变化事件");
            }
        }

        /// <summary>
        /// 初始化：使用全局设备实例订阅事件
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 优先使用构造函数传入的设备，其次使用全局服务
                if (_userDevice == null)
                {
                    _userDevice = GlobalServices.UserDevice;
                }

                if (_userDevice != null)
                {
                    _userDevice.StatusChanged += OnDeviceStatusChanged;
                    System.Diagnostics.Debug.WriteLine("[EventProcessor] 已订阅 IUserDevice.StatusChanged 事件");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[EventProcessor] 警告: 未找到用户设备实例");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventProcessor] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消事件订阅
        /// </summary>
        public void Shutdown()
        {
            try
            {
                if (_userDevice != null)
                {
                    _userDevice.StatusChanged -= OnDeviceStatusChanged;
                    System.Diagnostics.Debug.WriteLine("[EventProcessor] 已取消订阅事件");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventProcessor] 取消订阅失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理设备状态变化事件
        /// 使用 Task.Run 在后台线程处理，不阻塞UI线程
        /// </summary>
        private void OnDeviceStatusChanged(object? sender, DeviceEventArgs e)
        {
            // 更新当前状态（轻量操作，可在事件线程完成）
            CurrentStatus = e.Status;
            LastMessage = e.Message;

            System.Diagnostics.Debug.WriteLine($"[EventProcessor] 收到事件: Status={e.Status}, Message={e.Message ?? "(null)"}");

            // ✅ 核心：将处理逻辑放到后台线程，避免阻塞UI
            _ = Task.Run(() => ProcessEvent(e));
        }

        /// <summary>
        /// 处理事件的内部方法（在后台线程执行）
        /// </summary>
        private void ProcessEvent(DeviceEventArgs e)
        {
            try
            {
                // 确定事件类型提示
                string? eventTypeHint = GetEventTypeHint(e);

                // 调用匹配引擎查找匹配的规则
                var matchedRules = EventMatchEngine.Instance.MatchRules(e.Status, e.Message ?? "", eventTypeHint);

                foreach (var rule in matchedRules)
                {
                    System.Diagnostics.Debug.WriteLine($"[EventProcessor] 匹配到规则: {rule.EventName} -> 执行配方: {rule.RecipeName}");

                    // 通知外部订阅者（切换到UI线程）
                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.HasShutdownStarted)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                RuleMatched?.Invoke(rule, e);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[EventProcessor] 触发RuleMatched异常: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        // 没有Dispatcher时直接触发
                        RuleMatched?.Invoke(rule, e);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventProcessor] 处理事件异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据事件参数获取事件类型提示
        /// </summary>
        private string? GetEventTypeHint(DeviceEventArgs e)
        {
            // 如果消息以特定前缀开头，可能是远端命令
            if (!string.IsNullOrEmpty(e.Message))
            {
                // 常见的远端命令前缀
                string[] remotePrefixes = { "CMD_", "REMOTE_", "CONTROL_" };
                foreach (var prefix in remotePrefixes)
                {
                    if (e.Message.StartsWith(prefix))
                    {
                        return "远端";
                    }
                }
            }

            // 根据设备状态判断
            return e.Status switch
            {
                DeviceStatus.FireAlarm => "火警",
                DeviceStatus.Online => "状态",
                DeviceStatus.Offline => "状态",
                DeviceStatus.Error => "状态",
                DeviceStatus.Busy => "状态",
                _ => null
            };
        }

        /// <summary>
        /// 手动触发规则匹配（用于测试或远端命令触发）
        /// </summary>
        /// <param name="status">设备状态</param>
        /// <param name="message">消息内容</param>
        /// <param name="eventTypeHint">事件类型提示（如"远端"）</param>
        public void ManualMatch(DeviceStatus status, string? message, string? eventTypeHint = null)
        {
            var args = new DeviceEventArgs { Status = status, Message = message };
            _ = Task.Run(() => ProcessEvent(args));
        }

        /// <summary>
        /// 处理远端命令的便捷方法
        /// </summary>
        /// <param name="command">远端命令字符串</param>
        public void ProcessRemoteCommand(string command)
        {
            ManualMatch(DeviceStatus.Custom, command, "远端");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Shutdown();
                _disposed = true;
            }
        }
    }
}
