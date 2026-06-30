using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json;
using RobotControlSystem.Models;
using RobotControlSystem.Services;
using RobotControlSystem.Editors;
using RobotControlSystem.Interfaces;

namespace RobotControlSystem
{
    public partial class MainWindow : Window
    {
        private HttpClient _httpClient = new HttpClient();
        private AgvHttpService _agvHttpService;
        private ElibotRobotService _robotService;
        private IUserDevice _userDevice;
        private DatabaseHelper _db = new DatabaseHelper();
        private List<Template> _templates;
        private List<Recipe> _recipes;
        private List<DeviceConfig> _devices;
        private Template _currentTemplate;
        private string _currentVehicle = "AMB-01";
        private bool _robotConnected = false;

        // Web API 服务
        private Services.WebApiService _webApiService;

        // 任务计数器（用于站点导航）
        private int _ap3Counter = 4;
        private int _ap4Counter = 3;

        // 地图数据内部类
        private class MapPoint { public double x { get; set; } public double y { get; set; } }
        private class AdvancedPoint { public MapPoint pos { get; set; } }
        private class MapData { public MapHeader header { get; set; } public List<MapPoint> normalPosList { get; set; } public List<AdvancedPoint> advancedPointList { get; set; } }
        private class MapHeader { public MapPoint minPos { get; set; } public MapPoint maxPos { get; set; } }

        // 预览画布毫米与像素比例
        private const double PixelsPerMm = 10;

        // 小车图标控件
        private Ellipse _agvIcon;

        // 地图坐标转换参数（用于将机器人实际坐标转换为画布像素坐标）
        private double _mapMinX, _mapMinY, _mapMaxX, _mapMaxY;
        private double _mapOffsetX, _mapOffsetY, _mapScale;
        private bool _mapLoaded = false;

        // 消防实时监控窗口引用
        private FireMonitorWindow _fireMonitorWindow;

        // 配方编辑器窗口引用
        private RecipeFlowEditorWindow _recipeFlowEditorWindow;

        // 事件处理器
        private Services.EventProcessor _eventProcessor;

        // 实时位置轮询定时器
        private DispatcherTimer _robotStatusTimer;
        private const string RobotStatusUrl = "http://127.0.0.1:8088/robotsStatus";

        private void OnUserDeviceStatusChanged(object sender, DeviceEventArgs e)
        {
            // 现在由 EventProcessor 统一处理事件匹配和配方执行
            // 这里仅做 UI 连接状态更新
            Dispatcher.Invoke(() =>
            {
                if (e.Status == DeviceStatus.Online)
                {
                    borderFireAlarm.Background = Brushes.LightGreen;
                    txtFireAlarmStatus.Text = "无火警·正常";
                }
                else if (e.Status == DeviceStatus.Offline)
                {
                    borderFireAlarm.Background = Brushes.Gray;
                    txtFireAlarmStatus.Text = "设备离线";
                }
            });
        }
        public MainWindow()
        {
            InitializeComponent();
            _robotService = new ElibotRobotService(new MainWindowLogService(this));
            _agvHttpService = new AgvHttpService("http://127.0.0.1:8088");

            LoadTemplatesAndRecipes();
            LoadDevices();

            // 初始化模板下拉框
            cmbTemplate.ItemsSource = _templates;
            if (_templates != null && _templates.Count > 0)
                cmbTemplate.SelectedIndex = 0;
            // 消防监听相关功能已移除
        }

        private void LoadTemplatesAndRecipes()
        {
            _templates = _db.GetTemplates();
            _recipes = _db.GetRecipes();
            if (_templates != null && _templates.Count > 0)
                _currentTemplate = _templates[0];
            // 更新配方按钮列表
            RecipeItemsControl.ItemsSource = _recipes;
        }

        private void LoadDevices()
        {
            _devices = _db.GetDeviceConfigs();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadMapAsync(txtMapVehicle.Text, txtMapName.Text);
            DrawPanelPreview();
            StartRobotStatusPolling();

            // ========== 新增：初始化用户传输装置插件 ==========
            _userDevice = DeviceSingleton.Instance.UserDevice;
            if (_userDevice != null)
            {
                // 从数据库读取配置（示例：监听端口7799，设备ID为GST200）
                string config = "{\"listenPort\":7799,\"deviceId\":\"GST200\"}";
                _userDevice.SetParameters(config);

                // 订阅状态变化事件
                _userDevice.StatusChanged += OnUserDeviceStatusChanged;

                // ========== 初始化事件处理器 ==========
                _eventProcessor = new Services.EventProcessor(_userDevice);
                _eventProcessor.RuleMatched += (rule, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] 规则匹配: {rule.EventName} -> 执行配方: {rule.RecipeName}");
                        TryExecuteRecipeByName(rule.RecipeName);

                        if (rule.EventType == Models.EventType.火警报警器)
                        {
                            borderFireAlarm.Background = Brushes.Red;
                            txtFireAlarmStatus.Text = $"🚨 {rule.EventName}！{args.Message}";
                            txtAgvLockStatus.Text = $"火警: {args.Message}";
                        }
                    });
                };

                // 启动连接和监控
                await _userDevice.ConnectAsync();
                _userDevice.StartMonitoring();

                System.Diagnostics.Debug.WriteLine("✅ 用户传输装置插件已启动");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 未找到用户传输装置插件");
            }

            // ========== 新增：启动 Web API 服务 ==========
            try
            {
                _webApiService = new Services.WebApiService(_agvHttpService, _robotService);
                await _webApiService.StartAsync(System.Threading.CancellationToken.None);
                System.Diagnostics.Debug.WriteLine("✅ Web API 服务已启动，监听 http://0.0.0.0:5000");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Web API 启动失败: {ex.Message}");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // ========== 新增：停止 Web API 服务 ==========
            if (_webApiService != null)
            {
                try
                {
                    _webApiService.StopAsync(System.Threading.CancellationToken.None).Wait();
                    System.Diagnostics.Debug.WriteLine("🛑 Web API 服务已停止");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"停止 Web API 异常: {ex.Message}");
                }
            }

            // 停止定时器
            _robotStatusTimer?.Stop();

            try
            {
                if (_robotConnected)
                {
                    if (MessageBox.Show("机器人仍在连接中，确认要退出吗？", "确认退出",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _robotService?.Dispose();
                    }
                    else
                    {
                        e.Cancel = true;
                    }
                }
            }
            catch { }
            _agvHttpService?.Dispose();
            _httpClient?.Dispose();
        }

        // ==================== 打开独立编辑器 ====================
        private void BtnOpenPanelEditor_Click(object sender, RoutedEventArgs e)
        {
            var editor = new OperationPanelEditorWindow();
            editor.Owner = this;
            if (editor.ShowDialog() == true)
            {
                LoadTemplatesAndRecipes();
                cmbTemplate.ItemsSource = _templates;
                if (_templates != null && _templates.Count > 0)
                    cmbTemplate.SelectedIndex = 0;
                DrawPanelPreview();
            }
        }

        private void BtnOpenRecipeEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_recipeFlowEditorWindow == null || !_recipeFlowEditorWindow.IsVisible)
            {
                _recipeFlowEditorWindow = new RecipeFlowEditorWindow();
                _recipeFlowEditorWindow.Owner = this;
                _recipeFlowEditorWindow.Closed += (s, args) => _recipeFlowEditorWindow = null;
                _recipeFlowEditorWindow.Show();
            }
            else
            {
                _recipeFlowEditorWindow.Activate();
            }
        }

        // ==================== 消防实时监控 ====================
        private void BtnEventRuleDesigner_Click(object sender, RoutedEventArgs e)
        {
            var designer = new Editors.EventRuleDesignerWindow();
            designer.Owner = this;
            designer.Show();
        }

        private void BtnFireMonitor_Click(object sender, RoutedEventArgs e)
        {
            if (_fireMonitorWindow == null || !_fireMonitorWindow.IsVisible)
            {
                _fireMonitorWindow = new FireMonitorWindow();
                _fireMonitorWindow.Owner = this;

                // ✅ 恢复委托设置：让 FireMonitorWindow 可以调用主窗口的配方执行方法
                _fireMonitorWindow.ExecuteRecipeByName = TryExecuteRecipeByName;

                _fireMonitorWindow.Closed += (s, args) => _fireMonitorWindow = null;
                _fireMonitorWindow.Show();
            }
            else
            {
                _fireMonitorWindow.Activate();
            }
        }

        public void OnFireAlarmReceived(string alarmText)
        {
            TryExecuteRecipeByName(alarmText);
        }

        // ==================== 根据火警文字查找并执行同名配方 ====================
        public async void TryExecuteRecipeByName(string alarmText)
        {
            if (string.IsNullOrEmpty(alarmText))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 火警文字为空，无法匹配配方");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🔍 查找与火警文字 '{alarmText}' 同名的配方");

            try
            {
                // 获取所有配方
                var recipes = _db.GetRecipes();

                // 查找名称完全匹配的配方（忽略大小写）
                var matchedRecipe = recipes.FirstOrDefault(r =>
                    r.Name.Equals(alarmText, StringComparison.OrdinalIgnoreCase));

                if (matchedRecipe != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 找到同名配方: {matchedRecipe.Name} (ID: {matchedRecipe.Id})");

                    // 更新主窗口状态显示
                    Dispatcher.Invoke(() =>
                    {
                        txtAgvLockStatus.Text = $"执行火警配方: {matchedRecipe.Name}";
                        borderFireAlarm.Background = Brushes.Red;
                        txtFireAlarmStatus.Text = $"🚨 火警！执行配方: {matchedRecipe.Name}";
                    });

                    // 统一使用配方编辑器的执行引擎（与手动点击“执行”一致）
                    if (_recipeFlowEditorWindow == null)
                    {
                        _recipeFlowEditorWindow = new RecipeFlowEditorWindow();
                        _recipeFlowEditorWindow.Owner = this;
                        _recipeFlowEditorWindow.Closed += (s, args) => _recipeFlowEditorWindow = null;
                    }

                    await _recipeFlowEditorWindow.ExecuteRecipeById(matchedRecipe.Id);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 未找到与火警文字 '{alarmText}' 同名的配方");

                    // 列出所有可用的配方名称供调试
                    var recipeNames = string.Join(", ", recipes.Select(r => r.Name));
                    System.Diagnostics.Debug.WriteLine($"可用的配方: {recipeNames}");

                    Dispatcher.Invoke(() =>
                    {
                        txtAgvLockStatus.Text = $"未找到同名配方: {alarmText}";
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 执行配方失败: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    txtAgvLockStatus.Text = $"执行配方失败: {ex.Message}";
                });
            }
        }

        // 直接执行配方（不依赖编辑器窗口）
        private async Task ExecuteRecipeDirectly(int recipeId)
        {
            System.Diagnostics.Debug.WriteLine($"🚀 开始执行配方 ID: {recipeId}");

            try
            {
                // 获取完整的配方数据
                var fullRecipe = _db.GetRecipe(recipeId);
                if (fullRecipe == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 配方数据为空");
                    Dispatcher.Invoke(() =>
                    {
                        txtAgvLockStatus.Text = "配方数据加载失败";
                    });
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"配方名称: {fullRecipe.Name}, 节点数: {fullRecipe.Nodes.Count}");

                // 获取起始节点（没有输入连接的节点）
                var startNodes = fullRecipe.Nodes
                    .Where(n => !fullRecipe.Connections.Any(c => c.ToNodeId == n.Id))
                    .ToList();

                if (!startNodes.Any())
                    startNodes = fullRecipe.Nodes.Take(1).ToList();

                if (startNodes.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 没有可执行的起始节点");
                    Dispatcher.Invoke(() =>
                    {
                        txtAgvLockStatus.Text = "配方没有可执行的节点";
                    });
                    return;
                }

                // 执行流程
                var executionStack = new Stack<FlowNode>();
                foreach (var start in startNodes)
                    executionStack.Push(start);

                int nodeIndex = 0;
                while (executionStack.Count > 0)
                {
                    var node = executionStack.Pop();
                    nodeIndex++;

                    System.Diagnostics.Debug.WriteLine($"▶️ 执行节点 {nodeIndex}: {node.Type}");

                    Dispatcher.Invoke(() =>
                    {
                        txtAgvLockStatus.Text = $"执行配方: {fullRecipe.Name} - 步骤 {nodeIndex}: {node.Type}";
                    });

                    bool success = await ExecuteNodeAction(node);

                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 节点执行失败: {node.Type}");
                        Dispatcher.Invoke(() =>
                        {
                            txtAgvLockStatus.Text = $"配方执行失败: {node.Type}";
                        });
                        break;
                    }

                    // 获取下一个节点
                    var nextIds = fullRecipe.Connections
                        .Where(c => c.FromNodeId == node.Id)
                        .Select(c => c.ToNodeId)
                        .ToList();

                    foreach (var nextId in nextIds)
                    {
                        var nextNode = fullRecipe.Nodes.FirstOrDefault(n => n.Id == nextId);
                        if (nextNode != null)
                        {
                            executionStack.Push(nextNode);
                        }
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    txtAgvLockStatus.Text = $"配方执行完成: {fullRecipe.Name}";
                    txtFireAlarmStatus.Text = "配方执行完成";
                });

                System.Diagnostics.Debug.WriteLine($"✅ 配方执行完成: {fullRecipe.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 执行配方异常: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    txtAgvLockStatus.Text = $"配方执行异常: {ex.Message}";
                });
            }
        }

        // 执行具体的节点动作
        private async Task<bool> ExecuteNodeAction(FlowNode node)
        {
            try
            {
                switch (node.Type)
                {
                    case "AGV导航":
                        string targetSite = node.Parameters.ContainsKey("targetSite") ? node.Parameters["targetSite"]?.ToString() : "";
                        if (string.IsNullOrEmpty(targetSite))
                        {
                            System.Diagnostics.Debug.WriteLine("❌ AGV导航缺少目标站点");
                            return false;
                        }

                        System.Diagnostics.Debug.WriteLine($"📦 创建运单，目标站点: {targetSite}");

                        string orderId = Guid.NewGuid().ToString();
                        string blockId = $"{orderId}_block";

                        var orderData = new
                        {
                            id = orderId,
                            vehicle = "AMB-01",
                            blocks = new[]
                            {
                                new { blockId = blockId, location = targetSite }
                            },
                            complete = true
                        };

                        string jsonPayload = JsonConvert.SerializeObject(orderData);

                        using (var httpClient = new HttpClient())
                        {
                            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                            HttpResponseMessage response = await httpClient.PostAsync("http://127.0.0.1:8088/setOrder", content);
                            if (!response.IsSuccessStatusCode)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ 创建运单失败: {response.StatusCode}");
                                return false;
                            }
                            System.Diagnostics.Debug.WriteLine("✅ 运单创建成功");
                        }
                        break;

                    case "AGV停止":
                        System.Diagnostics.Debug.WriteLine("⏸️ 暂停 AGV 导航...");
                        // 实现AGV停止逻辑
                        break;

                    case "AGV继续导航":
                        System.Diagnostics.Debug.WriteLine("▶️ 继续 AGV 导航...");
                        // 实现AGV继续导航逻辑
                        break;

                    case "机械臂按压":
                        string buttonId = node.Parameters.ContainsKey("buttonId") ? node.Parameters["buttonId"]?.ToString() : "";
                        if (string.IsNullOrEmpty(buttonId))
                        {
                            System.Diagnostics.Debug.WriteLine("❌ 机械臂按压缺少按钮ID");
                            return false;
                        }

                        // 获取按钮关联的任务名
                        string taskName = null;
                        if (node.Parameters.ContainsKey("TaskName"))
                            taskName = node.Parameters["TaskName"]?.ToString();

                        if (string.IsNullOrEmpty(taskName))
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ 按钮 {buttonId} 未配置任务名");
                            return false;
                        }

                        string robotIp = node.Parameters.ContainsKey("robotIp") ? node.Parameters["robotIp"]?.ToString() : "192.168.1.140";
                        int robotPort = node.Parameters.ContainsKey("robotPort") ? Convert.ToInt32(node.Parameters["robotPort"]) : 29999;

                        System.Diagnostics.Debug.WriteLine($"🤖 连接机械臂 {robotIp}:{robotPort}");
                        using (var robot = new ElibotRobotService())
                        {
                            robot.RobotIP = robotIp;
                            robot.RobotPort = robotPort;
                            bool connected = await robot.ConnectAsync();
                            if (!connected)
                            {
                                System.Diagnostics.Debug.WriteLine("❌ 机械臂连接失败");
                                return false;
                            }

                            await robot.PowerOnAsync();
                            await robot.ReleaseBrakeAsync();
                            await robot.RunTaskAsync(taskName);
                            System.Diagnostics.Debug.WriteLine("✅ 机械臂任务完成");
                        }
                        break;

                    case "机械臂旋转":
                    case "机械臂复位":
                        string manualTaskName = node.Parameters.ContainsKey("taskName") ? node.Parameters["taskName"]?.ToString() : "";
                        if (string.IsNullOrEmpty(manualTaskName))
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ {node.Type} 缺少任务名参数");
                            return false;
                        }

                        string nodeRobotIp = node.Parameters.ContainsKey("robotIp") ? node.Parameters["robotIp"]?.ToString() : "192.168.1.140";
                        int nodeRobotPort = node.Parameters.ContainsKey("robotPort") ? Convert.ToInt32(node.Parameters["robotPort"]) : 29999;

                        System.Diagnostics.Debug.WriteLine($"🤖 连接机械臂 {nodeRobotIp}:{nodeRobotPort}");
                        using (var robot = new ElibotRobotService())
                        {
                            robot.RobotIP = nodeRobotIp;
                            robot.RobotPort = nodeRobotPort;
                            bool connected = await robot.ConnectAsync();
                            if (!connected)
                            {
                                System.Diagnostics.Debug.WriteLine("❌ 机械臂连接失败");
                                return false;
                            }

                            await robot.PowerOnAsync();
                            await robot.ReleaseBrakeAsync();
                            await robot.RunTaskAsync(manualTaskName);
                            System.Diagnostics.Debug.WriteLine("✅ 机械臂任务完成");
                        }
                        break;

                    case "延时":
                        int ms = node.Parameters.ContainsKey("milliseconds") ? Convert.ToInt32(node.Parameters["milliseconds"]) : 1000;
                        System.Diagnostics.Debug.WriteLine($"⏳ 延时 {ms} 毫秒...");
                        await Task.Delay(ms);
                        break;

                    case "日志保存":
                        string msg = node.Parameters.ContainsKey("message") ? node.Parameters["message"]?.ToString() : "";
                        System.Diagnostics.Debug.WriteLine($"📝 日志: {msg}");
                        Dispatcher.Invoke(() =>
                        {
                            txtAgvLockStatus.Text = msg;
                        });
                        break;

                    default:
                        System.Diagnostics.Debug.WriteLine($"⚠️ 未知节点类型: {node.Type}");
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 执行 {node.Type} 时异常: {ex.Message}");
                return false;
            }
        }

        // ==================== 地图 ====================
        private async Task LoadMapAsync(string vehicle, string mapName)
        {
            try
            {
                var request = new { vehicle, map = mapName };
                string json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("http://127.0.0.1:8088/robotSmap", content);
                if (!response.IsSuccessStatusCode) return;
                string responseJson = await response.Content.ReadAsStringAsync();
                var mapData = JsonConvert.DeserializeObject<MapData>(responseJson);
                RenderMap(mapData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"地图加载失败: {ex.Message}");
            }
        }

        private void RenderMap(MapData mapData)
        {
            mapOverlayCanvas.Children.Clear();
            if (mapData?.header == null || mapData.normalPosList == null) return;

            var header = mapData.header;
            _mapMinX = header.minPos.x;
            _mapMinY = header.minPos.y;
            _mapMaxX = header.maxPos.x;
            _mapMaxY = header.maxPos.y;
            double mapWidthM = _mapMaxX - _mapMinX, mapHeightM = _mapMaxY - _mapMinY;

            int canvasWidth = 800, canvasHeight = 600;
            double scaleX = canvasWidth / mapWidthM, scaleY = canvasHeight / mapHeightM;
            _mapScale = Math.Min(scaleX, scaleY);
            _mapOffsetX = (canvasWidth - mapWidthM * _mapScale) / 2;
            _mapOffsetY = (canvasHeight - mapHeightM * _mapScale) / 2;

            var wb = new WriteableBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Bgra32, null);
            wb.Lock();
            unsafe
            {
                byte* buffer = (byte*)wb.BackBuffer;
                int stride = wb.BackBufferStride;

                for (int y = 0; y < canvasHeight; y++)
                    for (int x = 0; x < canvasWidth; x++)
                    {
                        int idx = y * stride + x * 4;
                        buffer[idx] = 220; buffer[idx + 1] = 220; buffer[idx + 2] = 220; buffer[idx + 3] = 255;
                    }

                foreach (var point in mapData.normalPosList)
                {
                    double cx = _mapOffsetX + (point.x - _mapMinX) * _mapScale;
                    double cy = _mapOffsetY + (_mapMaxY - point.y) * _mapScale;
                    int px = (int)Math.Round(cx), py = (int)Math.Round(cy);
                    if (px >= 0 && px < canvasWidth && py >= 0 && py < canvasHeight)
                    {
                        int idx = py * stride + px * 4;
                        buffer[idx] = 0; buffer[idx + 1] = 0; buffer[idx + 2] = 0; buffer[idx + 3] = 255;
                    }
                }
            }
            wb.AddDirtyRect(new Int32Rect(0, 0, canvasWidth, canvasHeight));
            wb.Unlock();

            mapImage.Source = wb;
            mapImage.Width = canvasWidth;
            mapImage.Height = canvasHeight;
            mapOverlayCanvas.Width = canvasWidth;
            mapOverlayCanvas.Height = canvasHeight;

            // 绘制高级点（红色圆点）
            if (mapData.advancedPointList != null)
            {
                foreach (var ap in mapData.advancedPointList)
                {
                    if (ap.pos == null) continue;
                    double cx = _mapOffsetX + (ap.pos.x - _mapMinX) * _mapScale;
                    double cy = _mapOffsetY + (_mapMaxY - ap.pos.y) * _mapScale;
                    var ellipse = new Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = Brushes.Red,
                        Stroke = Brushes.White,
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(ellipse, cx - 5);
                    Canvas.SetTop(ellipse, cy - 5);
                    mapOverlayCanvas.Children.Add(ellipse);
                }
            }

            // 绘制小车图标（初始位置放在地图中心）
            double iconX = (canvasWidth / 2) - 10;
            double iconY = (canvasHeight / 2) - 10;
            _agvIcon = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.Gray,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            Canvas.SetLeft(_agvIcon, iconX);
            Canvas.SetTop(_agvIcon, iconY);
            Panel.SetZIndex(_agvIcon, 20);
            mapOverlayCanvas.Children.Add(_agvIcon);

            _mapLoaded = true;
            UpdateAgvStatusIcon(); // 根据设备配置更新初始颜色
        }

        // 将机器人实际坐标转换为画布像素坐标（x）
        private double ConvertMapXToCanvasX(double robotX)
        {
            return _mapOffsetX + (robotX - _mapMinX) * _mapScale;
        }

        // 将机器人实际坐标转换为画布像素坐标（y）
        private double ConvertMapYToCanvasY(double robotY)
        {
            return _mapOffsetY + (_mapMaxY - robotY) * _mapScale;
        }

        // 启动实时位置轮询
        private void StartRobotStatusPolling()
        {
            if (_robotStatusTimer != null) return;
            _robotStatusTimer = new DispatcherTimer();
            _robotStatusTimer.Interval = TimeSpan.FromSeconds(1);
            _robotStatusTimer.Tick += async (s, e) => await UpdateRobotStatusAsync();
            _robotStatusTimer.Start();
        }

        // 更新机器人状态（位置、在线状态）
        private async Task UpdateRobotStatusAsync()
        {
            if (!_mapLoaded || _agvIcon == null) return;

            try
            {
                string selectedVehicle = (cmbVehicle.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "AMB-01";
                // 请求指定机器人的信息（性能更好）
                string url = $"{RobotStatusUrl}?vehicles={selectedVehicle}";
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var root = JsonConvert.DeserializeObject<RobotsStatusResponse>(json);
                    var robot = root?.report?.FirstOrDefault(r => r.vehicle_id == selectedVehicle);

                    if (robot != null)
                    {
                        // 更新位置
                        double canvasX = ConvertMapXToCanvasX(robot.rbk_report.x);
                        double canvasY = ConvertMapYToCanvasY(robot.rbk_report.y);
                        Canvas.SetLeft(_agvIcon, canvasX - _agvIcon.Width / 2);
                        Canvas.SetTop(_agvIcon, canvasY - _agvIcon.Height / 2);

                        // 在线状态（机器人数据存在即在线）
                        _agvIcon.Fill = Brushes.Green;
                        txtAgvLockStatus.Text = "AGV: 在线";
                    }
                    else
                    {
                        // 机器人不在返回数据中 → 离线
                        _agvIcon.Fill = Brushes.Red;
                        txtAgvLockStatus.Text = "AGV: 离线";
                    }
                }
                else
                {
                    // HTTP 请求失败 → 离线
                    _agvIcon.Fill = Brushes.Red;
                    txtAgvLockStatus.Text = "AGV: 离线（接口异常）";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新机器人状态失败: {ex.Message}");
                _agvIcon.Fill = Brushes.Red;
                txtAgvLockStatus.Text = "AGV: 离线（连接失败）";
            }
        }

        // 机器人状态响应模型
        private class RobotsStatusResponse
        {
            public List<RobotInfo> report { get; set; }
        }

        private class RobotInfo
        {
            public string vehicle_id { get; set; }
            public RbkReport rbk_report { get; set; }
        }

        private class RbkReport
        {
            public double x { get; set; }
            public double y { get; set; }
        }

        // 查询并渲染地图按钮事件（补充缺失的方法）
        private async void BtnQueryMap_Click(object sender, RoutedEventArgs e)
        {
            await LoadMapAsync(txtMapVehicle.Text, txtMapName.Text);
        }

        // ==================== 操作面板预览 ====================
        private void DrawPanelPreview()
        {
            panelCanvas.Children.Clear();
            if (_currentTemplate == null) return;

            // 设置背景
            if (!string.IsNullOrEmpty(_currentTemplate.ImagePath))
            {
                string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _currentTemplate.ImagePath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var imageBrush = new ImageBrush();
                        var bitmap = new BitmapImage(new Uri(fullPath, UriKind.Absolute));
                        imageBrush.ImageSource = bitmap;
                        if (_currentTemplate.BackgroundImageMode == "Fill")
                        {
                            imageBrush.Stretch = Stretch.Fill;
                        }
                        else // Uniform
                        {
                            imageBrush.Stretch = Stretch.Uniform;
                        }
                        panelCanvas.Background = imageBrush;
                    }
                    catch
                    {
                        SetCanvasBackgroundFromColor();
                    }
                }
                else
                {
                    SetCanvasBackgroundFromColor();
                }
            }
            else
            {
                SetCanvasBackgroundFromColor();
            }

            // 绘制所有元素
            foreach (var elem in _currentTemplate.Elements)
            {
                Shape shape;
                Brush fill;
                if (elem.Type == "Button")
                {
                    shape = new Rectangle();
                    fill = new SolidColorBrush(Color.FromArgb(100, 0, 120, 215));
                }
                else if (elem.Type == "Knob")
                {
                    shape = new Ellipse();
                    fill = new SolidColorBrush(Color.FromArgb(100, 255, 165, 0));
                }
                else
                {
                    shape = new Ellipse();
                    fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0));
                }

                shape.Width = elem.Width * PixelsPerMm;
                shape.Height = elem.Height * PixelsPerMm;
                shape.Fill = fill;
                shape.Stroke = Brushes.White;
                shape.StrokeThickness = 1;
                shape.Tag = elem;
                shape.MouseDown += PanelElement_MouseDown;
                Canvas.SetLeft(shape, elem.X * PixelsPerMm);
                Canvas.SetTop(shape, elem.Y * PixelsPerMm);
                panelCanvas.Children.Add(shape);

                var text = new TextBlock
                {
                    Text = elem.Id,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
                };
                Canvas.SetLeft(text, elem.X * PixelsPerMm);
                Canvas.SetTop(text, elem.Y * PixelsPerMm - 12);
                panelCanvas.Children.Add(text);
            }
        }

        private void SetCanvasBackgroundFromColor()
        {
            try
            {
                var converter = new BrushConverter();
                var brush = converter.ConvertFromString(_currentTemplate.BackgroundColor) as Brush;
                panelCanvas.Background = brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 30));
            }
            catch
            {
                panelCanvas.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            }
        }

        private async void PanelElement_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Shape shape && shape.Tag is PanelElement elem && elem.Type == "Button")
            {
                string taskName = elem.Parameters?.ContainsKey("TaskName") == true ? elem.Parameters["TaskName"]?.ToString() : null;
                string robotIP = elem.Parameters?.ContainsKey("RobotIP") == true ? elem.Parameters["RobotIP"]?.ToString() : null;

                if (string.IsNullOrEmpty(taskName))
                {
                    MessageBox.Show($"按钮 {elem.Id} 未配置任务名称", "提示");
                    return;
                }

                string originalIP = _robotService.RobotIP;
                if (!string.IsNullOrEmpty(robotIP))
                    _robotService.RobotIP = robotIP;

                try
                {
                    if (!_robotConnected)
                    {
                        bool connected = await _robotService.ConnectAsync();
                        if (!connected)
                        {
                            MessageBox.Show("无法连接到机器人", "错误");
                            return;
                        }
                        _robotConnected = true;
                        UpdateRobotStatusUI();
                    }

                    bool success = await _robotService.RunTaskAsync(taskName);
                    if (success)
                    {
                        MessageBox.Show($"任务 {taskName} 启动成功", "执行结果");
                    }
                    else
                    {
                        MessageBox.Show($"任务 {taskName} 启动失败", "执行结果");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"执行异常: {ex.Message}", "错误");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(robotIP))
                        _robotService.RobotIP = originalIP;
                }
            }
        }

        private void PanelCanvas_MouseDown(object sender, MouseButtonEventArgs e) { }

        private void BtnRefreshPanel_Click(object sender, RoutedEventArgs e)
        {
            DrawPanelPreview();
        }

        private void CmbTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTemplate.SelectedItem is Template t)
            {
                _currentTemplate = t;
                DrawPanelPreview();
            }
        }

        // ==================== AGV 控制 ====================
        private string GetSelectedVehicle()
        {
            return (cmbVehicle.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "AMB-01";
        }

        private async void BtnGetAgvStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vehicle = GetSelectedVehicle();
                var (success, content, msg) = await _agvHttpService.GetRobotsStatusAsync(vehicle);
                if (success)
                {
                    MessageBox.Show($"AGV状态: {content}", "获取成功");
                }
                else
                {
                    MessageBox.Show($"获取失败: {msg}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"异常: {ex.Message}");
            }
            finally
            {
                await RefreshDeviceStatuses();
                UpdateAgvStatusIcon();
            }
        }

        private async Task RefreshDeviceStatuses()
        {
            LoadDevices();
        }

        private async void BtnLockAgv_Click(object sender, RoutedEventArgs e)
        {
            await LockVehicle(false);
        }

        private async void BtnForceLockAgv_Click(object sender, RoutedEventArgs e)
        {
            var vehicle = GetSelectedVehicle();
            try
            {
                await _agvHttpService.UnlockAsync(vehicle);
                await Task.Delay(500);
                await LockVehicle(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"强制抢夺异常: {ex.Message}");
            }
        }

        private async Task LockVehicle(bool showSuccess = true)
        {
            var vehicle = GetSelectedVehicle();
            try
            {
                var (success, msg) = await _agvHttpService.LockAsync(vehicle);
                if (success)
                {
                    txtAgvLockStatus.Text = $"AGV: 已锁定 {vehicle}";
                    txtAgvLockStatus.Foreground = Brushes.LightGreen;
                    if (showSuccess)
                        MessageBox.Show("控制权获取成功");
                }
                else
                {
                    MessageBox.Show($"锁定失败: {msg}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"异常: {ex.Message}");
            }
        }

        private async void BtnUnlockAgv_Click(object sender, RoutedEventArgs e)
        {
            var vehicle = GetSelectedVehicle();
            try
            {
                var (success, msg) = await _agvHttpService.UnlockAsync(vehicle);
                if (success)
                {
                    txtAgvLockStatus.Text = "AGV: 未锁定";
                    txtAgvLockStatus.Foreground = Brushes.Orange;
                    MessageBox.Show("控制权已释放");
                }
                else
                {
                    MessageBox.Show($"释放失败: {msg}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"异常: {ex.Message}");
            }
        }

        private async void BtnClearOrders_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清除所有运单吗？此操作不可恢复。", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var (success, msg) = await _agvHttpService.DeleteAllOrdersAsync();
                if (success)
                    MessageBox.Show("所有运单已清除");
                else
                    MessageBox.Show($"清除失败: {msg}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"异常: {ex.Message}");
            }
        }

        private async void BtnNavigate_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSite.SelectedItem is ComboBoxItem item)
            {
                string site = item.Tag.ToString();
                await NavigateToSite(site);
            }
        }

        private async Task NavigateToSite(string site)
        {
            var vehicle = GetSelectedVehicle();
            string blockId = "a" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string taskId = "a" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                var (success, msg) = await _agvHttpService.CreateOrderAsync(site, vehicle, taskId, blockId, true);
                if (success)
                {
                    MessageBox.Show($"已发送导航到{site}命令，任务ID: {taskId}");
                }
                else
                {
                    MessageBox.Show($"导航失败: {msg}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"异常: {ex.Message}");
            }
        }

        // ==================== 机器人控制 ====================
        private async void BtnConnectRobot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                btnConnectRobot.IsEnabled = false;

                _robotService.RobotIP = txtRobotIP.Text.Trim();
                if (int.TryParse(txtRobotPort.Text.Trim(), out int port))
                    _robotService.RobotPort = port;
                else
                {
                    MessageBox.Show("端口号格式错误");
                    return;
                }

                bool success = await _robotService.ConnectAsync();
                if (success)
                {
                    _robotConnected = true;
                    UpdateRobotStatusUI();
                    btnPowerOn.IsEnabled = true;
                    btnQueryStatus.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show("连接失败，请检查机器人IP和网络");
                }
            }
            finally
            {
                btnConnectRobot.IsEnabled = true;
            }
        }

        private async void BtnPowerOn_Click(object sender, RoutedEventArgs e)
        {
            if (!_robotConnected) return;
            try
            {
                btnPowerOn.IsEnabled = false;
                bool ok = await _robotService.PowerOnAsync();
                if (ok)
                {
                    btnReleaseBrake.IsEnabled = true;
                }
            }
            finally
            {
                btnPowerOn.IsEnabled = _robotConnected;
            }
        }

        private async void BtnReleaseBrake_Click(object sender, RoutedEventArgs e)
        {
            if (!_robotConnected) return;
            try
            {
                btnReleaseBrake.IsEnabled = false;
                bool ok = await _robotService.ReleaseBrakeAsync();
                if (ok)
                {
                    btnRunTask.IsEnabled = true;
                }
            }
            finally
            {
                btnReleaseBrake.IsEnabled = _robotConnected;
            }
        }

        private async void BtnRunTask_Click(object sender, RoutedEventArgs e)
        {
            if (!_robotConnected) return;
            try
            {
                btnRunTask.IsEnabled = false;
                bool ok = await _robotService.RunTaskAsync("动作3.task");
            }
            finally
            {
                btnRunTask.IsEnabled = _robotConnected;
            }
        }

        private async void BtnQueryStatus_Click(object sender, RoutedEventArgs e)
        {
            if (!_robotConnected) return;
            try
            {
                string status = await _robotService.QueryTaskStatusAsync();
                MessageBox.Show($"机器人状态: {status}", "查询结果");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}");
            }
        }

        private void UpdateRobotStatusUI()
        {
            txtRobotStatus.Text = _robotConnected ? "机器人: 已连接" : "机器人: 未连接";
            txtRobotStatus.Foreground = _robotConnected ? Brushes.LightGreen : Brushes.Orange;
        }

        private void UpdateAgvStatusIcon()
        {
            if (_agvIcon == null) return;

            string selectedVehicle = (cmbVehicle.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "AMB-01";
            var agvDevice = _devices?.FirstOrDefault(d => d.DeviceType == "AGV" && d.Name == selectedVehicle);
            if (agvDevice == null)
            {
                _agvIcon.Fill = Brushes.Gray;
                return;
            }

            switch (agvDevice.ConnectionStatus)
            {
                case "在线":
                    _agvIcon.Fill = Brushes.Green;
                    break;
                case "离线":
                    _agvIcon.Fill = Brushes.Red;
                    break;
                default:
                    _agvIcon.Fill = Brushes.Gray;
                    break;
            }
        }

        private void CmbVehicle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAgvStatusIcon();
        }

        // ==================== 缩放处理 ====================
        private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;
                var mousePos = e.GetPosition(MapContainer);
                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
                double newScaleX = MapScaleTransform.ScaleX * zoomFactor;
                double newScaleY = MapScaleTransform.ScaleY * zoomFactor;

                if (newScaleX < 0.2 || newScaleX > 5.0) return;

                MapScaleTransform.ScaleX = newScaleX;
                MapScaleTransform.ScaleY = newScaleY;

                double newX = mousePos.X * zoomFactor;
                double newY = mousePos.Y * zoomFactor;
                MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset + (newX - mousePos.X));
                MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset + (newY - mousePos.Y));
            }
        }

        private void PanelScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;
                var mousePos = e.GetPosition(PanelContainer);
                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
                double newScaleX = PanelScaleTransform.ScaleX * zoomFactor;
                double newScaleY = PanelScaleTransform.ScaleY * zoomFactor;

                if (newScaleX < 0.2 || newScaleX > 5.0) return;

                PanelScaleTransform.ScaleX = newScaleX;
                PanelScaleTransform.ScaleY = newScaleY;

                double newX = mousePos.X * zoomFactor;
                double newY = mousePos.Y * zoomFactor;
                PanelScrollViewer.ScrollToHorizontalOffset(PanelScrollViewer.HorizontalOffset + (newX - mousePos.X));
                PanelScrollViewer.ScrollToVerticalOffset(PanelScrollViewer.VerticalOffset + (newY - mousePos.Y));
            }
        }

        // ==================== 配方快速执行 ====================
        private async void RecipeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            if (!int.TryParse(button.Tag?.ToString(), out int recipeId))
            {
                txtAgvLockStatus.Text = "配方参数无效";
                return;
            }

            var recipe = _db.GetRecipe(recipeId);
            if (recipe == null)
            {
                txtAgvLockStatus.Text = "未找到配方";
                return;
            }

            try
            {
                txtAgvLockStatus.Text = $"开始执行配方: {recipe.Name}";

                if (_recipeFlowEditorWindow == null)
                {
                    _recipeFlowEditorWindow = new RecipeFlowEditorWindow();
                    _recipeFlowEditorWindow.Owner = this;
                    _recipeFlowEditorWindow.Closed += (s, args) => _recipeFlowEditorWindow = null;
                }

                await _recipeFlowEditorWindow.ExecuteRecipeById(recipeId);
                txtAgvLockStatus.Text = $"配方执行完成: {recipe.Name}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 快速执行配方失败: {ex.Message}");
                txtAgvLockStatus.Text = $"配方执行失败: {ex.Message}";
            }
        }
    }

    public class MainWindowLogService : ILogService
    {
        private MainWindow _window;
        public MainWindowLogService(MainWindow w) { _window = w; }
        public void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}