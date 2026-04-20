using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobotControlSystem.Models;
using RobotControlSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobotControlSystem.Editors
{
    public partial class RecipeFlowEditorWindow : Window
    {
        private DatabaseHelper _db = new DatabaseHelper();
        private List<Recipe> _recipes;
        private Recipe _currentRecipe;
        private Template _currentTemplate;               // 当前配方使用的模板
        private List<Template> _templates;
        private FlowNode _selectedNode;
        private bool _isDraggingNode;
        private Point _dragOffset;
        private bool _isConnecting;
        private string _connectingFromNodeId;
        private int _connectingFromOutput;

        // AGV 配置
        private AgvHttpService _agvService;
        private string _agvBaseUrl;
        private string _agvVehicleName;

        // 机械臂全局默认配置（从数据库加载）
        private string _defaultRobotIp;
        private int _defaultRobotPort;

        // 预定义位置（用于AGV导航快捷选择）
        private class PositionPreset
        {
            public string Name { get; set; }
            public string SiteName { get; set; }
        }

        private List<PositionPreset> _positionPresets = new List<PositionPreset>
        {
            new PositionPreset { Name = "充电桩", SiteName = "ChargingStation" },
            new PositionPreset { Name = "工位1", SiteName = "Workstation1" },
            new PositionPreset { Name = "工位2", SiteName = "Workstation2" },
            new PositionPreset { Name = "待机区", SiteName = "StandbyArea" }
        };

        // 节点类型默认参数
        private Dictionary<string, Dictionary<string, object>> _defaultParams = new()
        {
            ["AGV导航"] = new Dictionary<string, object> { ["targetSite"] = "ChargingStation" },
            ["AGV停止"] = new Dictionary<string, object>(),
            ["AGV继续导航"] = new Dictionary<string, object>(),
            ["机械臂按压"] = new Dictionary<string, object> { ["buttonId"] = "", ["robotIp"] = "", ["robotPort"] = 0 },
            ["机械臂旋转"] = new Dictionary<string, object> { ["knobId"] = "", ["angle"] = 90.0, ["torque"] = 1.0, ["taskName"] = "", ["robotIp"] = "", ["robotPort"] = 0 },
            ["机械臂复位"] = new Dictionary<string, object> { ["taskName"] = "", ["robotIp"] = "", ["robotPort"] = 0 },
            ["日志保存"] = new Dictionary<string, object> { ["message"] = "" },
            ["延时"] = new Dictionary<string, object> { ["milliseconds"] = 1000 },
            ["for循环"] = new Dictionary<string, object> { ["variable"] = "i", ["start"] = 0, ["end"] = 10, ["step"] = 1 },
            ["if条件"] = new Dictionary<string, object> { ["condition"] = "", ["value"] = "" }
        };

        public RecipeFlowEditorWindow()
        {
            InitializeComponent();
            LoadTemplatesList();       // 加载模板列表到下拉框
            LoadRecipes();
            LoadDeviceConfigs();
        }

        private void LoadTemplatesList()
        {
            _templates = _db.GetTemplates();
            CboTemplateSelect.ItemsSource = _templates;
            if (_templates.Any())
                CboTemplateSelect.SelectedIndex = 0;
        }

        private void LoadDeviceConfigs()
        {
            var devices = _db.GetDeviceConfigs();
            var agv = devices.FirstOrDefault(d => d.DeviceType == "AGV" && d.IsEnabled);
            if (agv != null)
            {
                _agvBaseUrl = $"http://{agv.IPAddress}:{agv.Port}";
                _agvVehicleName = agv.Name;
                _agvService = new AgvHttpService(_agvBaseUrl, _agvVehicleName);
            }
            else
            {
                _agvBaseUrl = "http://127.0.0.1:8088";
                _agvVehicleName = "AMB-01";
                _agvService = new AgvHttpService(_agvBaseUrl, _agvVehicleName);
            }

            var robot = devices.FirstOrDefault(d => d.DeviceType == "RoboticArm" && d.IsEnabled);
            if (robot != null)
            {
                _defaultRobotIp = robot.IPAddress;
                _defaultRobotPort = robot.Port;
            }
            else
            {
                _defaultRobotIp = "192.168.1.140";
                _defaultRobotPort = 29999;
            }
        }

        private void LoadRecipes()
        {
            _recipes = _db.GetRecipes();
            CboRecipeSelect.ItemsSource = _recipes;
        }

        private void CboRecipeSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboRecipeSelect.SelectedItem is Recipe r)
            {
                LoadRecipe(r.Id);
            }
        }

        private void LoadRecipe(int id)
        {
            _currentRecipe = _db.GetRecipe(id);
            if (_currentRecipe == null) return;

            // 根据配方的 TemplateId 设置下拉框选中项
            if (_currentRecipe.TemplateId > 0)
            {
                var template = _templates?.FirstOrDefault(t => t.Id == _currentRecipe.TemplateId);
                if (template != null)
                {
                    CboTemplateSelect.SelectedItem = template;
                    _currentTemplate = template;
                }
                else
                {
                    // 如果找不到对应模板，加载完整模板对象
                    _currentTemplate = _db.GetTemplate(_currentRecipe.TemplateId);
                }
            }
            else
            {
                CboTemplateSelect.SelectedIndex = -1;
                _currentTemplate = null;
            }

            TxtRecipeName.Text = _currentRecipe.Name;
            RedrawCanvas();
            StatusStep.Text = $"步骤数: {_currentRecipe.Nodes.Count}";
            TxtStatus.Text = $"已加载: {_currentRecipe.Name}";
        }

        // 模板选择变化时更新配方关联
        private void CboTemplateSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentRecipe == null) return;

            if (CboTemplateSelect.SelectedItem is Template t)
            {
                _currentRecipe.TemplateId = t.Id;
                _currentTemplate = t;
                TxtStatus.Text = $"已关联模板: {t.Name}";
            }
            else
            {
                _currentRecipe.TemplateId = 0;
                _currentTemplate = null;
                TxtStatus.Text = "未关联模板";
            }

            // 如果当前选中的节点是机械臂按压，刷新属性面板以更新按钮列表
            if (_selectedNode != null && _selectedNode.Type == "机械臂按压")
            {
                UpdatePropertyPanel();
            }
        }

        private void RedrawCanvas()
        {
            FlowCanvas.Children.Clear();
            if (_currentRecipe == null) return;

            foreach (var conn in _currentRecipe.Connections)
                DrawConnection(conn);

            foreach (var node in _currentRecipe.Nodes)
                DrawNode(node);
        }

        private void DrawNode(FlowNode node)
        {
            var border = new Border
            {
                Width = 120,
                Height = 70,
                Background = GetNodeColor(node.Type),
                CornerRadius = new CornerRadius(8),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Tag = node
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = node.Type, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(5) });
            sp.Children.Add(new TextBlock { Text = GetNodeSummary(node), Foreground = Brushes.LightGray, FontSize = 10, Margin = new Thickness(5, 0, 5, 5) });
            border.Child = sp;

            Canvas.SetLeft(border, node.X);
            Canvas.SetTop(border, node.Y);
            FlowCanvas.Children.Add(border);

            var inputDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Orange };
            Canvas.SetLeft(inputDot, node.X - 5);
            Canvas.SetTop(inputDot, node.Y + 20);
            FlowCanvas.Children.Add(inputDot);
            Panel.SetZIndex(inputDot, 10);

            if (node.Type == "if条件")
            {
                var trueDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Green };
                Canvas.SetLeft(trueDot, node.X + 115);
                Canvas.SetTop(trueDot, node.Y + 15);
                FlowCanvas.Children.Add(trueDot);
                Panel.SetZIndex(trueDot, 10);

                var falseDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Red };
                Canvas.SetLeft(falseDot, node.X + 115);
                Canvas.SetTop(falseDot, node.Y + 45);
                FlowCanvas.Children.Add(falseDot);
                Panel.SetZIndex(falseDot, 10);
            }
            else
            {
                var outputDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Green };
                Canvas.SetLeft(outputDot, node.X + 115);
                Canvas.SetTop(outputDot, node.Y + 30);
                FlowCanvas.Children.Add(outputDot);
                Panel.SetZIndex(outputDot, 10);
            }
        }

        private Brush GetNodeColor(string type)
        {
            if (type.Contains("AGV")) return new SolidColorBrush(Color.FromRgb(74, 144, 226));
            if (type.Contains("机械臂")) return new SolidColorBrush(Color.FromRgb(226, 74, 74));
            if (type == "延时") return new SolidColorBrush(Color.FromRgb(100, 100, 100));
            if (type == "for循环") return new SolidColorBrush(Color.FromRgb(150, 100, 200));
            if (type == "if条件") return new SolidColorBrush(Color.FromRgb(200, 150, 50));
            return Brushes.Gray;
        }

        private string GetNodeSummary(FlowNode node)
        {
            try
            {
                if (node.Type == "AGV导航")
                    return $"目标:{node.Parameters["targetSite"]}";
                if (node.Type == "机械臂按压")
                    return $"按钮:{node.Parameters["buttonId"]}";
                if (node.Type == "延时")
                    return $"{node.Parameters["milliseconds"]}ms";
                if (node.Type == "for循环")
                    return $"{node.Parameters["variable"]}={node.Parameters["start"]}..{node.Parameters["end"]}";
                if (node.Type == "if条件")
                    return $"{node.Parameters["condition"]} {node.Parameters["value"]}";
            }
            catch { }
            return "";
        }

        private void DrawConnection(Connection conn)
        {
            var fromNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == conn.FromNodeId);
            var toNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == conn.ToNodeId);
            if (fromNode == null || toNode == null) return;

            double fromY;
            if (fromNode.Type == "if条件")
                fromY = fromNode.Y + (conn.FromOutput == 0 ? 15 : 45);
            else
                fromY = fromNode.Y + 30;

            var line = new Line
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 2,
                X1 = fromNode.X + 120,
                Y1 = fromY,
                X2 = toNode.X,
                Y2 = toNode.Y + 20 + conn.ToInput * 25,
                Tag = conn
            };
            FlowCanvas.Children.Add(line);
        }

        // ========== 鼠标事件 ==========
        private void FlowCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(FlowCanvas);

            foreach (var node in _currentRecipe.Nodes)
            {
                var outputs = new List<(double X, double Y, int OutputIndex)>();
                if (node.Type == "if条件")
                {
                    outputs.Add((node.X + 115 + 5, node.Y + 15 + 5, 0));
                    outputs.Add((node.X + 115 + 5, node.Y + 45 + 5, 1));
                }
                else
                {
                    outputs.Add((node.X + 115 + 5, node.Y + 30 + 5, 0));
                }

                foreach (var (ox, oy, idx) in outputs)
                {
                    double dist = Math.Sqrt(Math.Pow(pos.X - ox, 2) + Math.Pow(pos.Y - oy, 2));
                    if (dist <= 10)
                    {
                        _isConnecting = true;
                        _connectingFromNodeId = node.Id;
                        _connectingFromOutput = idx;
                        FlowCanvas.CaptureMouse();
                        return;
                    }
                }
            }

            foreach (var node in _currentRecipe.Nodes)
            {
                if (pos.X >= node.X && pos.X <= node.X + 120 &&
                    pos.Y >= node.Y && pos.Y <= node.Y + 70)
                {
                    _selectedNode = node;
                    _isDraggingNode = true;
                    _dragOffset = new Point(pos.X - node.X, pos.Y - node.Y);
                    FlowCanvas.CaptureMouse();
                    UpdatePropertyPanel();
                    return;
                }
            }

            _selectedNode = null;
            UpdatePropertyPanel();
        }

        private void FlowCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(FlowCanvas);

            if (_isConnecting)
            {
                RedrawCanvas();
                var fromNode = _currentRecipe.Nodes.First(n => n.Id == _connectingFromNodeId);
                double fromY = fromNode.Type == "if条件" ? fromNode.Y + (_connectingFromOutput == 0 ? 15 : 45) : fromNode.Y + 30;

                var tempLine = new Line
                {
                    Stroke = Brushes.Orange,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 },
                    X1 = fromNode.X + 120,
                    Y1 = fromY,
                    X2 = pos.X,
                    Y2 = pos.Y
                };
                FlowCanvas.Children.Add(tempLine);
            }
            else if (_isDraggingNode && _selectedNode != null)
            {
                _selectedNode.X = pos.X - _dragOffset.X;
                _selectedNode.Y = pos.Y - _dragOffset.Y;
                _selectedNode.X = Math.Max(0, Math.Min(_selectedNode.X, FlowCanvas.Width - 120));
                _selectedNode.Y = Math.Max(0, Math.Min(_selectedNode.Y, FlowCanvas.Height - 70));
                RedrawCanvas();
            }
        }

        private void FlowCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isConnecting)
            {
                var pos = e.GetPosition(FlowCanvas);
                foreach (var node in _currentRecipe.Nodes)
                {
                    if (node.Id == _connectingFromNodeId) continue;

                    double inputX = node.X - 5 + 5;
                    double inputY = node.Y + 20 + 5;
                    double dist = Math.Sqrt(Math.Pow(pos.X - inputX, 2) + Math.Pow(pos.Y - inputY, 2));
                    if (dist <= 10)
                    {
                        var newConn = new Connection
                        {
                            FromNodeId = _connectingFromNodeId,
                            FromOutput = _connectingFromOutput,
                            ToNodeId = node.Id,
                            ToInput = 0
                        };
                        _currentRecipe.Connections.Add(newConn);
                        break;
                    }
                }
                _isConnecting = false;
                _connectingFromNodeId = null;
                RedrawCanvas();
            }
            _isDraggingNode = false;
            FlowCanvas.ReleaseMouseCapture();
        }

        // ========== 强制终止 ==========
        private async void BtnAgvEmergencyStop_Click(object sender, RoutedEventArgs e)
        {
            LogToExecutionLog("🔄 正在获取机器人控制权...");
            var lockResult = await _agvService.LockAsync();
            if (!lockResult.success)
            {
                LogToExecutionLog($"❌ 获取控制权失败: {lockResult.msg}");
                return;
            }
            LogToExecutionLog($"✅ 控制权获取成功");

            LogToExecutionLog("🛑 正在设置软急停...");
            var stopResult = await _agvService.SetSoftEmergencyStopAsync(true);
            if (!stopResult.success)
            {
                LogToExecutionLog($"❌ 软急停失败: {stopResult.msg}");
                return;
            }
            LogToExecutionLog($"✅ AGV 已紧急停止");
        }

        private void BtnArmEmergencyStop_Click(object sender, RoutedEventArgs e)
        {
            LogToExecutionLog("🛑 机械臂紧急停止指令已发送（需根据实际 SDK 实现）");
        }

        private void LogToExecutionLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                ExecutionLog.AppendText($"[{timestamp}] {message}\n");
                ExecutionLog.ScrollToEnd();
            });
        }

        // ========== 组件双击添加节点 ==========
        private void ComponentItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            string type = btn?.Tag?.ToString();
            if (string.IsNullOrEmpty(type)) return;
            if (_currentRecipe == null) BtnNewRecipe_Click(null, null);
            var newNode = new FlowNode
            {
                Id = "node_" + DateTime.Now.Ticks % 10000,
                Type = type,
                X = 100,
                Y = 100,
                Parameters = _defaultParams.ContainsKey(type)
                    ? new Dictionary<string, object>(_defaultParams[type])
                    : new Dictionary<string, object>()
            };
            _currentRecipe.Nodes.Add(newNode);
            DrawNode(newNode);
            StatusStep.Text = $"步骤数: {_currentRecipe.Nodes.Count}";
        }

        // ========== 右侧属性面板更新 ==========
        private void UpdatePropertyPanel()
        {
            var node = _selectedNode;
            if (node == null)
            {
                NoSelectionPanel.Visibility = Visibility.Visible;
                NodePropertyPanel.Visibility = Visibility.Collapsed;
                return;
            }

            NoSelectionPanel.Visibility = Visibility.Collapsed;
            NodePropertyPanel.Visibility = Visibility.Visible;
            NodePropertyPanel.Children.Clear();

            if (node.Type == "AGV导航")
            {
                var siteCombo = new ComboBox
                {
                    Width = 150,
                    IsEditable = true,
                    DisplayMemberPath = "Name",
                    SelectedValuePath = "SiteName",
                    ItemsSource = _positionPresets
                };

                string currentSite = GetParamString(node, "targetSite");
                siteCombo.Text = currentSite;

                siteCombo.SelectionChanged += (s, args) =>
                {
                    if (node == null) return;

                    if (siteCombo.SelectedItem is PositionPreset preset)
                    {
                        node.Parameters["targetSite"] = preset.SiteName;
                    }
                    else
                    {
                        node.Parameters["targetSite"] = siteCombo.Text;
                    }
                    RedrawCanvas();
                };

                siteCombo.LostFocus += (s, args) =>
                {
                    if (node == null) return;

                    if (siteCombo.SelectedItem == null ||
                        siteCombo.Text != (siteCombo.SelectedItem as PositionPreset)?.SiteName)
                    {
                        node.Parameters["targetSite"] = siteCombo.Text;
                        RedrawCanvas();
                    }
                };

                AddPropertyRow("目标站点", siteCombo);
            }
            else if (node.Type == "机械臂按压")
            {
                var buttonList = new List<PanelElement>();
                if (_currentTemplate != null && _currentTemplate.Elements != null)
                {
                    buttonList = _currentTemplate.Elements.Where(e => e.Type == "Button").ToList();
                }

                var buttonCombo = new ComboBox
                {
                    Width = 150,
                    DisplayMemberPath = "Id",
                    SelectedValuePath = "Id",
                    ItemsSource = buttonList,
                    IsEnabled = buttonList.Any()
                };

                string currentButtonId = GetParamString(node, "buttonId");
                buttonCombo.SelectedValue = currentButtonId;

                buttonCombo.SelectionChanged += (s, args) =>
                {
                    if (node == null) return;
                    node.Parameters["buttonId"] = buttonCombo.SelectedValue?.ToString();
                    RedrawCanvas();
                };

                AddPropertyRow("选择按钮", buttonCombo);

                if (!buttonList.Any())
                {
                    var tip = new TextBlock
                    {
                        Text = "当前配方未关联模板，或模板中没有按钮。请先编辑操作面板模板并关联到配方。",
                        Foreground = Brushes.Red,
                        FontSize = 10,
                        Margin = new Thickness(0, 5, 0, 0)
                    };
                    NodePropertyPanel.Children.Add(tip);
                }

                AddParamTextBox(node, "机械臂IP", GetParamString(node, "robotIp", ""), v => node.Parameters["robotIp"] = v);
                AddParamTextBox(node, "机械臂端口", GetParamString(node, "robotPort", "0"), v =>
                {
                    if (int.TryParse(v, out int port))
                        node.Parameters["robotPort"] = port;
                });
            }
            else if (node.Type == "机械臂旋转")
            {
                AddParamTextBox(node, "旋钮ID", GetParamString(node, "knobId"), v => node.Parameters["knobId"] = v);
                AddParamTextBox(node, "角度", GetParamString(node, "angle", "90"), v =>
                {
                    if (double.TryParse(v, out double angle))
                        node.Parameters["angle"] = angle;
                });
                AddParamTextBox(node, "扭矩", GetParamString(node, "torque", "1.0"), v =>
                {
                    if (double.TryParse(v, out double torque))
                        node.Parameters["torque"] = torque;
                });
                AddParamTextBox(node, "任务名（.task）", GetParamString(node, "taskName"), v => node.Parameters["taskName"] = v);
                AddParamTextBox(node, "机械臂IP", GetParamString(node, "robotIp", ""), v => node.Parameters["robotIp"] = v);
                AddParamTextBox(node, "机械臂端口", GetParamString(node, "robotPort", "0"), v =>
                {
                    if (int.TryParse(v, out int port))
                        node.Parameters["robotPort"] = port;
                });
            }
            else if (node.Type == "机械臂复位")
            {
                AddParamTextBox(node, "任务名（.task）", GetParamString(node, "taskName"), v => node.Parameters["taskName"] = v);
                AddParamTextBox(node, "机械臂IP", GetParamString(node, "robotIp", ""), v => node.Parameters["robotIp"] = v);
                AddParamTextBox(node, "机械臂端口", GetParamString(node, "robotPort", "0"), v =>
                {
                    if (int.TryParse(v, out int port))
                        node.Parameters["robotPort"] = port;
                });
            }
            else if (node.Type == "日志保存")
            {
                AddParamTextBox(node, "消息", GetParamString(node, "message"), v => node.Parameters["message"] = v);
            }
            else if (node.Type == "延时")
            {
                AddParamTextBox(node, "延时毫秒", GetParamString(node, "milliseconds", "1000"), v =>
                {
                    if (int.TryParse(v, out int ms))
                        node.Parameters["milliseconds"] = ms;
                });
            }
            else if (node.Type == "for循环")
            {
                AddParamTextBox(node, "循环变量名", GetParamString(node, "variable", "i"), v => node.Parameters["variable"] = v);
                AddParamTextBox(node, "起始值", GetParamString(node, "start", "0"), v =>
                {
                    if (int.TryParse(v, out int start))
                        node.Parameters["start"] = start;
                });
                AddParamTextBox(node, "结束值", GetParamString(node, "end", "10"), v =>
                {
                    if (int.TryParse(v, out int end))
                        node.Parameters["end"] = end;
                });
                AddParamTextBox(node, "步长", GetParamString(node, "step", "1"), v =>
                {
                    if (int.TryParse(v, out int step))
                        node.Parameters["step"] = step;
                });
            }
            else if (node.Type == "if条件")
            {
                AddParamTextBox(node, "条件表达式", GetParamString(node, "condition"), v => node.Parameters["condition"] = v);
                AddParamTextBox(node, "比较值", GetParamString(node, "value"), v => node.Parameters["value"] = v);
                NodePropertyPanel.Children.Add(new TextBlock
                {
                    Text = "支持变量：currentValue（循环变量）",
                    Foreground = Brushes.LightGray,
                    FontSize = 10,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }
            else
            {
                NodePropertyPanel.Children.Add(new TextBlock { Text = "无可配置参数", Foreground = Brushes.Gray, Margin = new Thickness(5) });
            }

            var deleteButton = new Button
            {
                Content = "🗑️ 删除步骤",
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(5),
                Background = new SolidColorBrush(Color.FromRgb(217, 83, 79)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            deleteButton.Click += (s, e) => DeleteSelectedNode(node);
            NodePropertyPanel.Children.Add(deleteButton);
        }

        private string GetParamString(FlowNode node, string key, string defaultValue = "")
        {
            return node != null && node.Parameters.ContainsKey(key) ? node.Parameters[key]?.ToString() ?? defaultValue : defaultValue;
        }

        private void AddParamTextBox(FlowNode node, string label, string initialValue, Action<string> onChanged)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(new TextBlock { Text = label + ":", Width = 80, VerticalAlignment = VerticalAlignment.Center });

            var tb = new TextBox { Width = 150 };
            tb.Text = initialValue;
            tb.TextChanged += (s, e) =>
            {
                if (node == null) return;
                onChanged(tb.Text);
            };
            tb.LostFocus += (s, e) =>
            {
                if (node == null) return;
                onChanged(tb.Text);
            };
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && node != null)
                {
                    onChanged(tb.Text);
                }
            };
            panel.Children.Add(tb);
            NodePropertyPanel.Children.Add(panel);
        }

        private void AddPropertyRow(string label, UIElement editor)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(new TextBlock { Text = label + ":", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(editor);
            NodePropertyPanel.Children.Add(panel);
        }

        private void DeleteSelectedNode()
        {
            if (_selectedNode == null) return;
            DeleteSelectedNode(_selectedNode);
        }

        private void DeleteSelectedNode(FlowNode node)
        {
            if (node == null || _currentRecipe == null) return;

            string nodeId = node.Id;
            _currentRecipe.Connections = _currentRecipe.Connections
                .Where(c => c.FromNodeId != nodeId && c.ToNodeId != nodeId)
                .ToList();
            _currentRecipe.Nodes = _currentRecipe.Nodes
                .Where(n => n.Id != nodeId)
                .ToList();
            if (_selectedNode == node)
            {
                _selectedNode = null;
            }
            RedrawCanvas();
            UpdatePropertyPanel();
            StatusStep.Text = $"步骤数: {_currentRecipe.Nodes.Count}";
        }

        // ========== 按钮命令 ==========
        private void BtnNewRecipe_Click(object sender, RoutedEventArgs e)
        {
            _currentRecipe = new Recipe { Id = 0, Name = "新配方", TemplateId = 0, Nodes = new List<FlowNode>(), Connections = new List<Connection>() };
            _currentTemplate = null;
            TxtRecipeName.Text = "新配方";
            CboTemplateSelect.SelectedIndex = -1;
            RedrawCanvas();
        }

        private void BtnSaveRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            Keyboard.ClearFocus();

            _currentRecipe.Name = TxtRecipeName.Text;
            _currentRecipe.Id = _db.SaveRecipe(_currentRecipe);
            LoadRecipes();
            CboRecipeSelect.SelectedValue = _currentRecipe.Id;

            _selectedNode = null;
            UpdatePropertyPanel();

            TxtStatus.Text = "保存成功";
        }

        private void BtnDeleteRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null || _currentRecipe.Id == 0)
            {
                MessageBox.Show("没有可删除的配方（新建未保存）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"确定要删除配方“{_currentRecipe.Name}”吗？此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    _db.DeleteRecipe(_currentRecipe.Id);
                    _currentRecipe = null;
                    _currentTemplate = null;
                    _selectedNode = null;
                    LoadRecipes();
                    CboRecipeSelect.SelectedIndex = -1;
                    RedrawCanvas();
                    UpdatePropertyPanel();
                    StatusStep.Text = "步骤数: 0";
                    TxtStatus.Text = "配方已删除";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 🔧 修复：添加 BtnSimulate_Click 方法，调用 ExecuteCurrentRecipe
        private async void BtnSimulate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteCurrentRecipe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"执行过程中发生错误: {ex.Message}", "运行错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== 公共执行方法（供外部调用） ==========
        public async Task ExecuteRecipeById(int recipeId)
        {
            try
            {
                var recipe = _db.GetRecipe(recipeId);
                if (recipe != null)
                {
                    // 切换到该配方
                    _currentRecipe = recipe;
                    TxtRecipeName.Text = recipe.Name;

                    // 刷新画布显示
                    RedrawCanvas();

                    // 清空执行日志
                    //ExecutionLog.Clear();
                    LogToExecutionLog($"🔥 执行火警配方: {recipe.Name}");

                    // 执行配方
                    await ExecuteCurrentRecipe();
                }
                else
                {
                    LogToExecutionLog($"❌ 未找到配方 ID: {recipeId}");
                }
            }
            catch (Exception ex)
            {
                LogToExecutionLog($"❌ 执行配方异常: {ex.Message}");
                throw;
            }
        }

        public async Task ExecuteRecipeByName(string recipeName)
        {
            try
            {
                var recipe = _db.GetRecipes().FirstOrDefault(r =>
                    r.Name.Equals(recipeName, StringComparison.OrdinalIgnoreCase));

                if (recipe != null)
                {
                    var fullRecipe = _db.GetRecipe(recipe.Id);
                    if (fullRecipe != null)
                    {
                        _currentRecipe = fullRecipe;
                        TxtRecipeName.Text = fullRecipe.Name;
                        RedrawCanvas();
                        ExecutionLog.Clear();
                        LogToExecutionLog($"🔥 执行火警配方: {fullRecipe.Name}");
                        await ExecuteCurrentRecipe();
                    }
                }
                else
                {
                    LogToExecutionLog($"❌ 未找到配方: {recipeName}");
                }
            }
            catch (Exception ex)
            {
                LogToExecutionLog($"❌ 执行配方异常: {ex.Message}");
                throw;
            }
        }

        // ========== 执行配方（核心逻辑） ==========
        private async Task ExecuteCurrentRecipe()
        {
            if (_currentRecipe == null || !_currentRecipe.Nodes.Any())
            {
                LogToExecutionLog("没有可执行的配方");
                return;
            }

            LogToExecutionLog($"🚀 开始执行配方: {_currentRecipe.Name}");

            // 确保模板已加载（当配方有关联模板且当前模板为空时）
            if (_currentRecipe.TemplateId > 0 && _currentTemplate == null)
            {
                _currentTemplate = _db.GetTemplate(_currentRecipe.TemplateId);
            }

            var startNodes = _currentRecipe.Nodes
                .Where(n => !_currentRecipe.Connections.Any(c => c.ToNodeId == n.Id))
                .ToList();
            if (!startNodes.Any()) startNodes = _currentRecipe.Nodes.Take(1).ToList();

            var executionStack = new Stack<ExecutionContext>();
            foreach (var start in startNodes)
                executionStack.Push(new ExecutionContext { Node = start, LoopState = null });

            while (executionStack.Count > 0)
            {
                var ctx = executionStack.Pop();

                if (ctx.LoopState != null && ctx.LoopState.IsInsideLoop)
                {
                    bool shouldContinue = await ExecuteNode(ctx.Node, ctx.LoopState);
                    if (!shouldContinue) break;

                    var nextIds = _currentRecipe.Connections
                        .Where(c => c.FromNodeId == ctx.Node.Id)
                        .Select(c => c.ToNodeId)
                        .ToList();
                    foreach (var nextId in nextIds)
                    {
                        var nextNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == nextId);
                        if (nextNode != null)
                        {
                            executionStack.Push(new ExecutionContext
                            {
                                Node = nextNode,
                                LoopState = ctx.LoopState
                            });
                        }
                    }
                    continue;
                }

                if (ctx.Node.Type == "for循环")
                {
                    int start = Convert.ToInt32(ctx.Node.Parameters["start"]);
                    int end = Convert.ToInt32(ctx.Node.Parameters["end"]);
                    int step = Convert.ToInt32(ctx.Node.Parameters["step"]);
                    string varName = ctx.Node.Parameters["variable"].ToString();

                    var bodyStartIds = _currentRecipe.Connections
                        .Where(c => c.FromNodeId == ctx.Node.Id)
                        .Select(c => c.ToNodeId)
                        .ToList();

                    if (!bodyStartIds.Any())
                    {
                        LogToExecutionLog($"⚠️ for循环 '{varName}' 没有循环体，跳过");
                        continue;
                    }

                    for (int i = end; i >= start; i += step)
                    {
                        var bodyNodes = new List<FlowNode>();
                        foreach (var bodyId in bodyStartIds)
                        {
                            var bodyNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == bodyId);
                            if (bodyNode != null)
                                bodyNodes.Add(bodyNode);
                        }
                        for (int j = bodyNodes.Count - 1; j >= 0; j--)
                        {
                            executionStack.Push(new ExecutionContext
                            {
                                Node = bodyNodes[j],
                                LoopState = new LoopState
                                {
                                    IsInsideLoop = true,
                                    LoopVariable = varName,
                                    CurrentValue = i,
                                    LoopStartNodeId = ctx.Node.Id
                                }
                            });
                        }
                    }
                }
                else if (ctx.Node.Type == "if条件")
                {
                    string condition = ctx.Node.Parameters["condition"]?.ToString() ?? "";
                    string value = ctx.Node.Parameters["value"]?.ToString() ?? "";
                    bool conditionMet = EvaluateCondition(condition, value, ctx.LoopState);

                    int branchOutput = conditionMet ? 0 : 1;
                    var nextIds = _currentRecipe.Connections
                        .Where(c => c.FromNodeId == ctx.Node.Id && c.FromOutput == branchOutput)
                        .Select(c => c.ToNodeId)
                        .ToList();

                    foreach (var nextId in nextIds)
                    {
                        var nextNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == nextId);
                        if (nextNode != null)
                        {
                            executionStack.Push(new ExecutionContext { Node = nextNode, LoopState = ctx.LoopState });
                        }
                    }
                }
                else
                {
                    bool success = await ExecuteNode(ctx.Node, ctx.LoopState);
                    if (!success) break;

                    var nextIds = _currentRecipe.Connections
                        .Where(c => c.FromNodeId == ctx.Node.Id && c.FromOutput == 0)
                        .Select(c => c.ToNodeId)
                        .ToList();

                    for (int i = nextIds.Count - 1; i >= 0; i--)
                    {
                        var nextNode = _currentRecipe.Nodes.FirstOrDefault(n => n.Id == nextIds[i]);
                        if (nextNode != null)
                        {
                            executionStack.Push(new ExecutionContext
                            {
                                Node = nextNode,
                                LoopState = ctx.LoopState
                            });
                        }
                    }
                }
            }

            LogToExecutionLog("✅ 配方执行完成");
        }

        private bool EvaluateCondition(string condition, string value, LoopState loopState)
        {
            if (loopState != null && condition == "currentValue")
            {
                if (double.TryParse(value, out double val))
                {
                    return loopState.CurrentValue == val;
                }
            }
            return false;
        }

        private async Task<bool> ExecuteNode(FlowNode node, LoopState loopState = null)
        {
            LogToExecutionLog($"▶️ 执行: {node.Type}");

            try
            {
                switch (node.Type)
                {
                    case "AGV导航":
                        string targetSite = node.Parameters["targetSite"]?.ToString();
                        if (string.IsNullOrEmpty(targetSite))
                        {
                            LogToExecutionLog("❌ AGV导航缺少目标站点");
                            return false;
                        }

                        LogToExecutionLog($"🔄 获取控制权...");
                        var lockRes = await _agvService.LockAsync();
                        if (!lockRes.success)
                        {
                            LogToExecutionLog($"❌ 获取控制权失败: {lockRes.msg}");
                            return false;
                        }
                        LogToExecutionLog($"✅ 控制权获取成功");

                        LogToExecutionLog($"🗑️ 清除运单缓存...");
                        var clearRes = await _agvService.ClearCacheAsync();
                        if (!clearRes.success)
                        {
                            LogToExecutionLog($"⚠️ 清除缓存警告: {clearRes.msg}");
                        }

                        LogToExecutionLog($"📦 创建运单，目标站点: {targetSite}");

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
                        LogToExecutionLog($"发送运单数据: {jsonPayload}");

                        using (var httpClient = new HttpClient())
                        {
                            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                            try
                            {
                                HttpResponseMessage response = await httpClient.PostAsync("http://127.0.0.1:8088/setOrder", content);
                                if (response.IsSuccessStatusCode)
                                {
                                    string responseBody = await response.Content.ReadAsStringAsync();
                                    LogToExecutionLog($"✅ 运单创建成功，AGV 开始导航。响应：{responseBody}");
                                }
                                else
                                {
                                    LogToExecutionLog($"❌ 创建运单失败，HTTP状态码：{response.StatusCode}");
                                    return false;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToExecutionLog($"❌ 创建运单异常：{ex.Message}");
                                return false;
                            }
                        }

                        // ========== 新增：轮询运单状态直到完成 ==========
                        const int maxRetries = 300;      // 最大重试次数（假设每次等待2秒，共10分钟）
                        const int retryDelayMs = 2000;   // 每次重试间隔2秒
                        int retryCount = 0;
                        bool orderFinished = false;

                        while (retryCount < maxRetries && !orderFinished)
                        {
                            await Task.Delay(retryDelayMs);
                            retryCount++;

                            LogToExecutionLog($"🔍 查询运单状态 (第{retryCount}次)...");

                            using (var client = new HttpClient())
                            {
                                try
                                {
                                    string orderDetailsUrl = $"http://127.0.0.1:8088/orderDetails/{orderId}";
                                    HttpResponseMessage detailsResponse = await client.GetAsync(orderDetailsUrl);
                                    if (detailsResponse.IsSuccessStatusCode)
                                    {
                                        string detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                                        var details = JObject.Parse(detailsJson);
                                        string state = details["state"]?.ToString();

                                        LogToExecutionLog($"📊 运单状态: {state}");

                                        if (state == "FINISHED")
                                        {
                                            orderFinished = true;
                                            LogToExecutionLog($"✅ AGV 运单已完成，继续执行后续步骤");
                                        }
                                        else if (state == "FAILED")
                                        {
                                            LogToExecutionLog($"❌ AGV 运单失败，停止执行");
                                            return false;
                                        }
                                        else
                                        {
                                            // 其他状态如 RUNNING, CREATED 等，继续等待
                                            LogToExecutionLog($"⏳ 运单状态 {state}，等待中...");
                                        }
                                    }
                                    else
                                    {
                                        LogToExecutionLog($"⚠️ 查询运单状态失败，HTTP状态码：{detailsResponse.StatusCode}，将重试");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogToExecutionLog($"⚠️ 查询运单状态异常：{ex.Message}，将重试");
                                }
                            }
                        }

                        if (!orderFinished)
                        {
                            LogToExecutionLog($"❌ 等待运单完成超时（{maxRetries * retryDelayMs / 1000}秒），停止执行");
                            return false;
                        }

                        break;

                    case "AGV停止":
                        LogToExecutionLog($"⏸️ 暂停 AGV 导航...");
                        var pauseRes = await _agvService.PauseAsync();
                        if (!pauseRes.success)
                            LogToExecutionLog($"⚠️ 暂停失败: {pauseRes.msg}");
                        else
                            LogToExecutionLog($"✅ AGV 已暂停");
                        break;

                    case "AGV继续导航":
                        LogToExecutionLog($"▶️ 继续 AGV 导航...");
                        var resumeRes = await _agvService.ResumeAsync();
                        if (!resumeRes.success)
                            LogToExecutionLog($"⚠️ 继续失败: {resumeRes.msg}");
                        else
                            LogToExecutionLog($"✅ AGV 已继续");
                        break;

                    case "机械臂按压":
                        //
                        string buttonId = node.Parameters["buttonId"]?.ToString();
                        if (string.IsNullOrEmpty(buttonId))
                        {
                            LogToExecutionLog("❌ 机械臂按压缺少按钮ID");
                            return false;
                        }
                        if (_currentTemplate == null)
                        {
                            LogToExecutionLog("❌ 当前配方未关联模板，无法获取按钮任务");
                            return false;
                        }

                        var buttonElem = _currentTemplate.Elements?.FirstOrDefault(e => e.Type == "Button" && e.Id == buttonId);
                        if (buttonElem == null)
                        {
                            LogToExecutionLog($"❌ 未找到按钮 {buttonId} 对应的元素");
                            return false;
                        }

                        string taskName = buttonElem.Parameters?.ContainsKey("TaskName") == true
                            ? buttonElem.Parameters["TaskName"]?.ToString()
                            : null;

                        if (string.IsNullOrEmpty(taskName))
                        {
                            LogToExecutionLog($"❌ 按钮 {buttonId} 未配置任务名");
                            return false;
                        }

                        string robotIp = GetNodeRobotIp(node);
                        int robotPort = GetNodeRobotPort(node);
                        LogToExecutionLog($"🤖 连接机械臂 {robotIp}:{robotPort}");
                        using (var robot = new ElibotRobotService())
                        {
                            robot.RobotIP = robotIp;
                            robot.RobotPort = robotPort;
                            bool connected = await robot.ConnectAsync();
                    
                            LogToExecutionLog($"🎬 运行任务: {taskName}");
                            bool taskStarted = await robot.RunTaskAsync(taskName);
                            if (!taskStarted)
                            {
                                LogToExecutionLog($"❌ 任务启动失败");
                                return false;
                            }
                            LogToExecutionLog($"✅ 机械臂任务完成");
                        }
                        break;

                    case "机械臂旋转":
                    case "机械臂复位":
                        string manualTaskName = node.Parameters["taskName"]?.ToString();
                        if (string.IsNullOrEmpty(manualTaskName))
                        {
                            LogToExecutionLog($"❌ {node.Type} 缺少任务名参数");
                            return false;
                        }

                        string nodeRobotIp = GetNodeRobotIp(node);
                        int nodeRobotPort = GetNodeRobotPort(node);
                        LogToExecutionLog($"🤖 连接机械臂 {nodeRobotIp}:{nodeRobotPort}");
                        using (var robot = new ElibotRobotService())
                        {
                            robot.RobotIP = nodeRobotIp;
                            robot.RobotPort = nodeRobotPort;
                            bool connected = await robot.ConnectAsync();
                            if (!connected)
                            {
                                LogToExecutionLog($"❌ 机械臂连接失败");
                                return false;
                            }
                            LogToExecutionLog($"🔌 机械臂上电...");
                            bool powered = await robot.PowerOnAsync();
                            if (!powered)
                            {
                                LogToExecutionLog($"❌ 上电失败");
                                return false;
                            }
                            LogToExecutionLog($"🕹️ 释放抱闸...");
                            bool brakeReleased = await robot.ReleaseBrakeAsync();
                            if (!brakeReleased)
                            {
                                LogToExecutionLog($"❌ 释放抱闸失败");
                                return false;
                            }
                            LogToExecutionLog($"🎬 运行任务: {manualTaskName}");
                            bool taskStarted = await robot.RunTaskAsync(manualTaskName);
                            if (!taskStarted)
                            {
                                LogToExecutionLog($"❌ 任务启动失败");
                                return false;
                            }
                            LogToExecutionLog($"✅ 机械臂任务完成");
                        }
                        break;

                    case "延时":
                        int ms = Convert.ToInt32(node.Parameters["milliseconds"]);
                        LogToExecutionLog($"⏳ 延时 {ms} 毫秒...");
                        await Task.Delay(ms);
                        break;

                    case "日志保存":
                        string msg = node.Parameters["message"]?.ToString() ?? "";
                        LogToExecutionLog($"📝 日志: {msg}");
                        break;

                    default:
                        LogToExecutionLog($"⚠️ 未知节点类型: {node.Type}");
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogToExecutionLog($"❌ 执行 {node.Type} 时异常: {ex.Message}");
                return false;
            }
        }

        private string GetNodeRobotIp(FlowNode node)
        {
            if (node.Parameters.TryGetValue("robotIp", out var ipObj) && ipObj is string ip && !string.IsNullOrWhiteSpace(ip))
                return ip;
            return _defaultRobotIp;
        }

        private int GetNodeRobotPort(FlowNode node)
        {
            if (node.Parameters.TryGetValue("robotPort", out var portObj))
            {
                if (portObj is int port && port > 0)
                    return port;
                if (portObj is string portStr && int.TryParse(portStr, out int portVal) && portVal > 0)
                    return portVal;
            }
            return _defaultRobotPort;
        }

        // 执行上下文辅助类
        private class ExecutionContext
        {
            public FlowNode Node { get; set; }
            public LoopState LoopState { get; set; }
        }

        private class LoopState
        {
            public bool IsInsideLoop { get; set; }
            public string LoopVariable { get; set; }
            public double CurrentValue { get; set; }
            public string LoopStartNodeId { get; set; }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

        }
    }
}