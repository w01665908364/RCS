using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using RobotControlSystem.Interfaces;
using RobotControlSystem.Models;
using Newtonsoft.Json.Linq;

namespace RobotControlSystem
{
    public partial class FireMonitorWindow : Window, INotifyPropertyChanged
    {
        private IUserDevice _userDevice;
        private string _statusText = "未连接";
        private DateTime _lastUpdateTime;
        private DispatcherTimer _refreshTimer;

        public ObservableCollection<FireMonitorData> AllDataList { get; }
        public ObservableCollection<FireMonitorData> AlertDataList { get; }

        public int TotalMessagesReceived => AllDataList.Count;
        public int DeviceStatusCount => AllDataList.Count(d => d.StatusCode == 2);
        public int FireMainCount => AllDataList.Count(d => d.MessageType == "消防主机");
        public int AnalogCount => AllDataList.Count(d => d.MessageType == "模拟量");
        public int AlertCount => AlertDataList.Count;
        public bool IsConnected => _userDevice?.IsConnected ?? false;

        public DateTime LastUpdateTime
        {
            get => _lastUpdateTime;
            set { _lastUpdateTime = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public Action<string> ExecuteRecipeByName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RefreshAllBindings()
        {
            OnPropertyChanged(nameof(TotalMessagesReceived));
            OnPropertyChanged(nameof(DeviceStatusCount));
            OnPropertyChanged(nameof(FireMainCount));
            OnPropertyChanged(nameof(AnalogCount));
            OnPropertyChanged(nameof(AlertCount));
            OnPropertyChanged(nameof(IsConnected));
        }

        public FireMonitorWindow()
        {
            InitializeComponent();
            DataContext = this;

            AllDataList = new ObservableCollection<FireMonitorData>();
            AlertDataList = new ObservableCollection<FireMonitorData>();

            // 创建定时器，每秒刷新一次
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += (s, e) =>
            {
                RefreshAllBindings();
                System.Diagnostics.Debug.WriteLine($"[定时刷新] AllDataList={AllDataList.Count}, AlertDataList={AlertDataList.Count}");
            };
            _refreshTimer.Start();

            _userDevice = GlobalServices.UserDevice;
            if (_userDevice != null)
            {
                _userDevice.StatusChanged += OnDeviceStatusChanged;
            }

            Closed += (s, e) =>
            {
                _refreshTimer?.Stop();
                if (_userDevice != null)
                {
                    _userDevice.StatusChanged -= OnDeviceStatusChanged;
                    _userDevice.StopMonitoring();
                    _userDevice.Disconnect();
                }
            };
        }

        private void OnDeviceStatusChanged(object sender, DeviceEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[收到事件] Status={e.Status}, Message={e.Message}");

            // ✅ 关键改动：将处理逻辑放到后台线程，避免卡死 UI
            _ = Task.Run(() =>
            {
                try
                {
                    Dispatcher.Invoke(() => LastUpdateTime = DateTime.Now);

                    if (!string.IsNullOrEmpty(e.Message) && e.Message.TrimStart().StartsWith("{"))
                    {
                        string deviceCode = ParseAndAddData(e.Message);

                        // 使用事件匹配引擎匹配规则
                        var matchedRules = Services.EventMatchEngine.Instance.MatchRules(e.Status, e.Message);
                        foreach (var rule in matchedRules)
                        {
                            System.Diagnostics.Debug.WriteLine($"[事件匹配] {rule.EventName} -> {rule.RecipeName}");
                            if (ExecuteRecipeByName != null)
                                Dispatcher.Invoke(() => ExecuteRecipeByName(rule.RecipeName));
                        }

                        // 没匹配到规则时，仍按旧逻辑用 deviceCode 尝试
                        if (matchedRules.Count == 0 && ExecuteRecipeByName != null && !string.IsNullOrEmpty(deviceCode))
                        {
                            System.Diagnostics.Debug.WriteLine($"[信号触发] 未匹配规则，按DeviceCode尝试: {deviceCode}");
                            Dispatcher.Invoke(() => ExecuteRecipeByName(deviceCode));
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        switch (e.Status)
                        {
                            case DeviceStatus.Online: StatusText = "连接成功"; break;
                            case DeviceStatus.Offline: StatusText = "已断开"; break;
                            case DeviceStatus.Error: StatusText = $"错误: {e.Message}"; break;
                            case DeviceStatus.FireAlarm: StatusText = "🚨 收到火警"; break;
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[后台处理异常] {ex.Message}");
                }
            });
        }

        private string ParseAndAddData(string jsonMessage)
        {
            try
            {
                var json = JObject.Parse(jsonMessage);

                var data = new FireMonitorData
                {
                    Timestamp = DateTime.Now,
                    // ✅ 优先使用 deviceCode（完整编码）
                    DeviceCode = json["deviceCode"]?.ToString() ?? json["userCode"]?.ToString() ?? "",
                    StatusName = json["statusName"]?.ToString() ?? json["status"]?.ToString() ?? "",
                    Address = json["address"]?.ToString() ?? json["partExplain"]?.ToString() ?? "",
                    RawJson = jsonMessage,
                    MessageType = json["messageType"]?.ToString() ?? "设备状态"
                };

                // ✅ 修复：loopNo 和 nodeNo 是字符串类型，需要正确转换
                if (json["loopNo"] != null)
                {
                    if (int.TryParse(json["loopNo"].ToString(), out int loopNo))
                        data.LoopNo = loopNo;
                }
                if (json["nodeNo"] != null)
                {
                    if (int.TryParse(json["nodeNo"].ToString(), out int nodeNo))
                        data.NodeNo = nodeNo;
                }
                // addressNo 也可能是字符串
                if (json["addressNo"] != null)
                {
                    if (int.TryParse(json["addressNo"].ToString(), out int addrNo))
                        data.NodeNo = addrNo;  // 如果 NodeNo 还没设置
                }
                if (json["time"] != null) data.EventTime = json["time"].ToString();

                if (!string.IsNullOrEmpty(data.StatusName) && data.StatusName.Contains("火警"))
                {
                    data.StatusCode = 1;
                    data.MessageType = "报警";
                }
                else if (!string.IsNullOrEmpty(data.StatusName) && data.StatusName.Contains("故障"))
                {
                    data.StatusCode = 0;
                    data.MessageType = "故障";
                }
                else
                {
                    if (json["statusCode"] != null)
                        data.StatusCode = (int)json["statusCode"];
                    else if (json["status"] != null && json["status"].Type == JTokenType.Integer)
                        data.StatusCode = (int)json["status"];
                    else
                        data.StatusCode = 2;

                    if (string.IsNullOrEmpty(data.MessageType))
                        data.MessageType = "设备状态";
                }

                Dispatcher.Invoke(() =>
                {
                    AllDataList.Insert(0, data);

                    if (data.StatusCode != 2)
                    {
                        AlertDataList.Insert(0, data);
                    }

                    System.Diagnostics.Debug.WriteLine($"[数据已添加] StatusName={data.StatusName}, DeviceCode={data.DeviceCode}");
                    RefreshAllBindings();
                });

                while (AllDataList.Count > 1000) AllDataList.RemoveAt(AllDataList.Count - 1);
                while (AlertDataList.Count > 200) AlertDataList.RemoveAt(AlertDataList.Count - 1);

                return data.DeviceCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析数据失败: {ex.Message}");
                return null;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (_userDevice != null)
            {
                _ = _userDevice.ConnectAsync();
                _userDevice.StartMonitoring();
            }
        }

        private void BtnClearData_Click(object sender, RoutedEventArgs e)
        {
            AllDataList.Clear();
            AlertDataList.Clear();
            RefreshAllBindings();
            StatusText = "数据已清空";
            System.Diagnostics.Debug.WriteLine("[数据已清空]");
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_userDevice != null)
            {
                _userDevice.StopMonitoring();
                _userDevice.Disconnect();
            }
        }
    }
}
