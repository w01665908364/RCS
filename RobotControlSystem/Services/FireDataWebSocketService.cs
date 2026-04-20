using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RobotControlSystem.Services
{
    public class FireDeviceData : INotifyPropertyChanged
    {
        private string _deviceCode;
        private string _statusName;
        private string _address;
        private int _statusCode;
        private DateTime _timestamp;
        private string _messageType;

        public string DeviceCode
        {
            get => _deviceCode;
            set => SetProperty(ref _deviceCode, value);
        }

        public string StatusName
        {
            get => _statusName;
            set => SetProperty(ref _statusName, value);
        }

        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        public int StatusCode
        {
            get => _statusCode;
            set => SetProperty(ref _statusCode, value);
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class FireDataWebSocketService : INotifyPropertyChanged, IDisposable
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly string _webSocketUrl;

        private bool _isConnected;
        private string _connectionStatus;
        private int _totalMessagesReceived;

        private ObservableCollection<FireDeviceData> _deviceDataList = new ObservableCollection<FireDeviceData>();
        private ObservableCollection<FireDeviceData> _alertDataList = new ObservableCollection<FireDeviceData>();
        private DateTime _lastUpdateTime;

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        public int TotalMessagesReceived
        {
            get => _totalMessagesReceived;
            private set => SetProperty(ref _totalMessagesReceived, value);
        }

        public ObservableCollection<FireDeviceData> DeviceDataList
        {
            get => _deviceDataList;
            private set => SetProperty(ref _deviceDataList, value);
        }

        public ObservableCollection<FireDeviceData> AlertDataList
        {
            get => _alertDataList;
            private set => SetProperty(ref _alertDataList, value);
        }

        public DateTime LastUpdateTime
        {
            get => _lastUpdateTime;
            private set => SetProperty(ref _lastUpdateTime, value);
        }

        public int DeviceStatusCount { get; private set; }
        public int FireMainCount { get; private set; }
        public int AnalogCount { get; private set; }
        public int AlertCount { get; private set; }

        public event EventHandler<string> ConnectionStatusChanged;
        public event EventHandler<FireDeviceData> NewDataReceived;
        public event EventHandler<FireDeviceData> AlertReceived;
        // 新增：火警事件，传递火警文字信息
        public event EventHandler<string> FireAlarmReceived;
        public event PropertyChangedEventHandler PropertyChanged;

        public FireDataWebSocketService() : this("ws://192.168.1.66:16000/fire-data-ws")
        {
        }

        public FireDataWebSocketService(string webSocketUrl)
        {
            _webSocketUrl = webSocketUrl;
            ConnectionStatus = "未连接";
            _lastUpdateTime = DateTime.MinValue;
        }

        public async Task ConnectAsync()
        {
            try
            {
                if (_webSocket != null && (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.Connecting))
                {
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                ConnectionStatus = "正在连接...";
                IsConnected = false;

                await _webSocket.ConnectAsync(new Uri(_webSocketUrl), _cancellationTokenSource.Token);

                ConnectionStatus = "已连接";
                IsConnected = true;

                _ = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));
                ConnectionStatusChanged?.Invoke(this, "连接成功");
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"连接失败: {ex.Message}";
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(this, $"连接失败: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket != null)
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "客户端主动关闭", CancellationToken.None);
                    }

                    _webSocket.Dispose();
                    _webSocket = null;
                }

                ConnectionStatus = "已断开";
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(this, "连接已断开");
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"断开连接时出错: {ex.Message}";
                ConnectionStatusChanged?.Invoke(this, $"断开连接时出错: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            while (_webSocket != null && _webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"接收消息错误: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private void ProcessMessage(string message)
        {
            try
            {
                TotalMessagesReceived++;

                dynamic jsonData = JsonConvert.DeserializeObject(message);

                var deviceData = new FireDeviceData
                {
                    MessageType = jsonData.messageType,
                    Timestamp = DateTime.Now
                };

                if (jsonData.deviceCode != null)
                    deviceData.DeviceCode = jsonData.deviceCode;
                else if (jsonData.userCode != null)
                    deviceData.DeviceCode = jsonData.userCode;

                if (jsonData.statusName != null)
                    deviceData.StatusName = jsonData.statusName;

                if (jsonData.address != null)
                    deviceData.Address = jsonData.address;
                else if (jsonData.partExplain != null)
                    deviceData.Address = jsonData.partExplain;

                if (jsonData.statusCode != null)
                    deviceData.StatusCode = (int)jsonData.statusCode;
                else if (jsonData.status != null)
                    deviceData.StatusCode = (int)jsonData.status;
                else
                    deviceData.StatusCode = 2;

                UpdateStatistics(jsonData.messageType.ToString(), deviceData.StatusCode);

                App.Current.Dispatcher.Invoke(() =>
                {
                    LastUpdateTime = DateTime.Now;

                    if (DeviceDataList.Count > 100)
                        DeviceDataList.RemoveAt(DeviceDataList.Count - 1);

                    DeviceDataList.Insert(0, deviceData);

                    // 判断是否为火警/故障
                    bool isAlert = deviceData.StatusCode == 0 || deviceData.StatusCode == 1 ||
                        (deviceData.StatusName?.Contains("火警") == true) ||
                        (deviceData.StatusName?.Contains("故障") == true);

                    if (isAlert)
                    {
                        if (AlertDataList.Count > 50)
                            AlertDataList.RemoveAt(AlertDataList.Count - 1);

                        AlertDataList.Insert(0, deviceData);
                        AlertReceived?.Invoke(this, deviceData);

                        // 🚨 获取火警文字信息（优先使用 statusName，其次使用 deviceCode）
                        string alarmText = deviceData.StatusName ?? deviceData.DeviceCode ?? "";
                        if (!string.IsNullOrEmpty(alarmText))
                        {
                            System.Diagnostics.Debug.WriteLine($"🔥 检测到火警文字: {alarmText}");
                            FireAlarmReceived?.Invoke(this, alarmText);
                        }
                    }

                    NewDataReceived?.Invoke(this, deviceData);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理消息时出错: {ex.Message}, 消息: {message}");
            }
        }

        private void UpdateStatistics(string messageType, int statusCode)
        {
            switch (messageType)
            {
                case "deviceStatus":
                    DeviceStatusCount++;
                    break;
                case "fireMainStatus":
                    FireMainCount++;
                    break;
                case "analogData":
                    AnalogCount++;
                    break;
            }

            if (statusCode == 0 || statusCode == 1)
            {
                AlertCount++;
            }

            OnPropertyChanged(nameof(DeviceStatusCount));
            OnPropertyChanged(nameof(FireMainCount));
            OnPropertyChanged(nameof(AnalogCount));
            OnPropertyChanged(nameof(AlertCount));
        }

        public void ClearData()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                DeviceDataList.Clear();
                AlertDataList.Clear();

                TotalMessagesReceived = 0;
                DeviceStatusCount = 0;
                FireMainCount = 0;
                AnalogCount = 0;
                AlertCount = 0;
            });
        }

        public async Task ReconnectAsync()
        {
            await DisconnectAsync();
            await Task.Delay(1000);
            await ConnectAsync();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _webSocket?.Dispose();
        }
    }
}