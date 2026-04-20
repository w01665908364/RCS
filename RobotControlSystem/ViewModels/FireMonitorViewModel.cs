using RobotControlSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace RobotControlSystem.ViewModels
{
    public class FireMonitorViewModel : ViewModelBase
    {
        private readonly FireDataWebSocketService _webSocketService;
        private string _statusMessage;
        private bool _autoScroll = true;
        private bool _showAlertsOnly;
        private string _filterDeviceCode = string.Empty;

        public FireDataWebSocketService WebSocketService => _webSocketService;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public bool ShowAlertsOnly
        {
            get => _showAlertsOnly;
            set
            {
                SetProperty(ref _showAlertsOnly, value);
                UpdateFilteredData();
            }
        }

        public string FilterDeviceCode
        {
            get => _filterDeviceCode;
            set
            {
                SetProperty(ref _filterDeviceCode, value);
                UpdateFilteredData();
            }
        }

        public ObservableCollection<FireDeviceData> FilteredData { get; } = new ObservableCollection<FireDeviceData>();

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ReconnectCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand TestCommand { get; }

        public FireMonitorViewModel()
        {
            _webSocketService = new FireDataWebSocketService();

            ConnectCommand = new RelayCommand(async () => await Connect());
            DisconnectCommand = new RelayCommand(async () => await Disconnect(), () => _webSocketService.IsConnected);
            ReconnectCommand = new RelayCommand(async () => await Reconnect());
            ClearCommand = new RelayCommand(Clear);
            TestCommand = new RelayCommand(SendTestMessage);

            _webSocketService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _webSocketService.NewDataReceived += OnNewDataReceived;
            _webSocketService.AlertReceived += OnAlertReceived;

            StatusMessage = "准备连接...";
        }

        private async Task Connect()
        {
            StatusMessage = "正在连接...";
            await _webSocketService.ConnectAsync();
        }

        private async Task Disconnect()
        {
            await _webSocketService.DisconnectAsync();
        }

        private async Task Reconnect()
        {
            await _webSocketService.ReconnectAsync();
        }

        private void Clear()
        {
            _webSocketService.ClearData();
            FilteredData.Clear();
            StatusMessage = "数据已清空";
        }

        private void SendTestMessage()
        {
            StatusMessage = "发送测试消息...";
        }

        private void OnConnectionStatusChanged(object sender, string status)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"连接状态: {status}";
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnNewDataReceived(object sender, FireDeviceData data)
        {
            UpdateFilteredData();
        }

        private void OnAlertReceived(object sender, FireDeviceData data)
        {
            ShowAlertNotification(data);
        }

        private void UpdateFilteredData()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                FilteredData.Clear();

                foreach (var item in _webSocketService.DeviceDataList)
                {
                    var shouldAdd = true;

                    if (ShowAlertsOnly && item.StatusCode != 0 && item.StatusCode != 1)
                    {
                        shouldAdd = false;
                    }

                    if (!string.IsNullOrEmpty(FilterDeviceCode) &&
                        !item.DeviceCode.Contains(FilterDeviceCode))
                    {
                        shouldAdd = false;
                    }

                    if (shouldAdd)
                    {
                        FilteredData.Add(item);
                    }
                }
            });
        }

        private void ShowAlertNotification(FireDeviceData data)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (App.Current.MainWindow != null)
                {
                    var originalTitle = App.Current.MainWindow.Title;
                    if (!originalTitle.Contains("【报警】"))
                    {
                        App.Current.MainWindow.Title = $"【报警】{data.DeviceCode} - {data.StatusName} | Robot控制系统";

                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                        timer.Tick += (s, e) =>
                        {
                            App.Current.MainWindow.Title = originalTitle;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
            });
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;

        public void Execute(object parameter) => _execute((T)parameter);
    }
}
