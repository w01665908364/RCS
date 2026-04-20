using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
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

            Dispatcher.Invoke(() =>
            {
                LastUpdateTime = DateTime.Now;

                switch (e.Status)
                {
                    case DeviceStatus.Online:
                        StatusText = "连接成功";
                        break;
                    case DeviceStatus.Offline:
                        StatusText = "已断开";
                        break;
                    case DeviceStatus.Error:
                        StatusText = $"错误: {e.Message}";
                        break;
                    case DeviceStatus.FireAlarm:
                        StatusText = "🚨 收到火警";
                        break;
                }

                if (!string.IsNullOrEmpty(e.Message) && e.Message.TrimStart().StartsWith("{"))
                {
                    ParseAndAddData(e.Message);
                }

                if (e.Status == DeviceStatus.FireAlarm && ExecuteRecipeByName != null)
                {
                    ExecuteRecipeByName(e.Message);
                }
            });
        }

        private void ParseAndAddData(string jsonMessage)
        {
            try
            {
                var json = JObject.Parse(jsonMessage);

                var data = new FireMonitorData
                {
                    Timestamp = DateTime.Now,
                    DeviceCode = json["userCode"]?.ToString() ?? json["deviceCode"]?.ToString() ?? "",
                    StatusName = json["statusName"]?.ToString() ?? "",
                    Address = json["address"]?.ToString() ?? "",
                    RawJson = jsonMessage
                };

                if (data.StatusName.Contains("火警"))
                {
                    data.StatusCode = 1;
                    data.MessageType = "报警";
                }
                else if (data.StatusName.Contains("故障"))
                {
                    data.StatusCode = 0;
                    data.MessageType = "故障";
                }
                else
                {
                    data.StatusCode = 2;
                    data.MessageType = "设备状态";
                }

                Dispatcher.Invoke(() =>
                {
                    AllDataList.Insert(0, data);

                    if (data.StatusCode != 2)
                    {
                        AlertDataList.Insert(0, data);
                    }

                    System.Diagnostics.Debug.WriteLine($"[数据已添加] StatusName={data.StatusName}, DeviceCode={data.DeviceCode}, AllDataList={AllDataList.Count}");

                    RefreshAllBindings();
                });

                while (AllDataList.Count > 1000) AllDataList.RemoveAt(AllDataList.Count - 1);
                while (AlertDataList.Count > 200) AlertDataList.RemoveAt(AlertDataList.Count - 1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析数据失败: {ex.Message}");
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
