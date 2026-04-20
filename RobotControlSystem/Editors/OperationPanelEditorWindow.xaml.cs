using System;
using System.Collections.Generic;
using System.IO;                // 保留 using，但调用时使用全限定名
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using RobotControlSystem.Models;
using RobotControlSystem.Services;

namespace RobotControlSystem.Editors
{
    public partial class OperationPanelEditorWindow : Window
    {
        private DatabaseHelper _db = new DatabaseHelper();
        private List<Template> _templates;
        private Template _currentTemplate;
        private PanelElement _selectedElement;
        private bool _isDragging;
        private Point _dragOffset;
        private const double PixelsPerMm = 10;
        private const double AlignThreshold = 5;
        private const long MaxImageSize = 5 * 1024 * 1024; // 5MB
        private Dictionary<string, int> _idCounters = new Dictionary<string, int>
        {
            { "Button", 0 },
            { "Knob", 0 },
            { "Lamp", 0 }
        };

        private CheckBox _chkCustomSize;
        private TextBox _txtImageWidth;
        private TextBox _txtImageHeight;
        private ComboBox _modeCombo;

        // 编辑模式标识（true=可编辑，false=只读执行模式）
        private bool _isEditMode = true;

        // 图片保存目录（相对于应用程序基目录）
        private readonly string _imageFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");

        public OperationPanelEditorWindow()
        {
            InitializeComponent();
            LoadTemplateList();
            ElemTypeCombo.SelectedIndex = 0;
            UpdateEditModeUI();

            // 确保图片目录存在
            if (!Directory.Exists(_imageFolder))
                Directory.CreateDirectory(_imageFolder);
        }

        private void LoadTemplateList()
        {
            _templates = _db.GetTemplates();
            TemplateListBox.ItemsSource = _templates;
        }

        private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateListBox.SelectedItem is Template t)
            {
                LoadTemplate(t.Id);
            }
        }

        private void LoadTemplate(int id)
        {
            _currentTemplate = _db.GetTemplate(id);
            if (_currentTemplate == null) return;

            // 兼容旧数据：将裁剪模式转换为等比缩放完整（Uniform）
            if (_currentTemplate.BackgroundImageMode == "UniformToFill")
            {
                _currentTemplate.BackgroundImageMode = "Uniform";
            }

            RefreshIdCounters();
            RedrawCanvas();
            UpdatePropertyPanel();
            TxtStatus.Text = $"已加载: {_currentTemplate.Name}";
            UpdateScrollBars();

            // 加载已保存模板后自动进入只读模式（可修改，根据需求调整）
            _isEditMode = false;
            UpdateEditModeUI();
        }

        private void RefreshIdCounters()
        {
            _idCounters["Button"] = 0;
            _idCounters["Knob"] = 0;
            _idCounters["Lamp"] = 0;
            if (_currentTemplate?.Elements == null) return;
            foreach (var elem in _currentTemplate.Elements)
            {
                string prefix = elem.Type switch
                {
                    "Button" => "BTN",
                    "Knob" => "KNOB",
                    "Lamp" => "LAMP",
                    _ => ""
                };
                if (string.IsNullOrEmpty(prefix)) continue;
                if (elem.Id.StartsWith(prefix + "_"))
                {
                    if (int.TryParse(elem.Id.Substring(prefix.Length + 1), out int num))
                    {
                        if (num > _idCounters[elem.Type])
                            _idCounters[elem.Type] = num;
                    }
                }
            }
        }

        private string GenerateId(string type)
        {
            _idCounters[type]++;
            string prefix = type switch
            {
                "Button" => "BTN",
                "Knob" => "KNOB",
                "Lamp" => "LAMP",
                _ => "ELEM"
            };
            return $"{prefix}_{_idCounters[type]:D3}";
        }

        private void RedrawCanvas()
        {
            DrawingCanvas.Children.Clear();
            DrawingCanvas.Children.Add(GuideVertical);
            DrawingCanvas.Children.Add(GuideHorizontal);
            GuideVertical.Visibility = Visibility.Collapsed;
            GuideHorizontal.Visibility = Visibility.Collapsed;

            if (_currentTemplate == null) return;

            // 设置画布背景色（如果没有背景图片）
            if (string.IsNullOrEmpty(_currentTemplate.ImagePath))
            {
                try
                {
                    var converter = new BrushConverter();
                    var brush = converter.ConvertFromString(_currentTemplate.BackgroundColor) as Brush;
                    DrawingCanvas.Background = brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
                catch
                {
                    DrawingCanvas.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
            }
            else
            {
                // 绘制背景图片
                try
                {
                    string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _currentTemplate.ImagePath);
                    if (!File.Exists(fullPath))
                    {
                        // 图片文件丢失，清除路径
                        _currentTemplate.ImagePath = null;
                        RedrawCanvas();
                        return;
                    }

                    var img = new Image();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    img.Source = bitmap;

                    if (_currentTemplate.ImageWidth.HasValue && _currentTemplate.ImageHeight.HasValue)
                    {
                        img.Width = _currentTemplate.ImageWidth.Value;
                        img.Height = _currentTemplate.ImageHeight.Value;
                        img.Stretch = Stretch.Fill;
                        Canvas.SetLeft(img, 0);
                        Canvas.SetTop(img, 0);
                    }
                    else
                    {
                        if (_currentTemplate.BackgroundImageMode == "Fill")
                        {
                            img.Width = DrawingCanvas.Width;
                            img.Height = DrawingCanvas.Height;
                            img.Stretch = Stretch.Fill;
                            Canvas.SetLeft(img, 0);
                            Canvas.SetTop(img, 0);
                        }
                        else // Uniform
                        {
                            double scaleX = DrawingCanvas.Width / bitmap.PixelWidth;
                            double scaleY = DrawingCanvas.Height / bitmap.PixelHeight;
                            double scale = Math.Min(scaleX, scaleY);
                            double scaledWidth = bitmap.PixelWidth * scale;
                            double scaledHeight = bitmap.PixelHeight * scale;
                            img.Width = scaledWidth;
                            img.Height = scaledHeight;
                            img.Stretch = Stretch.Fill;
                            Canvas.SetLeft(img, (DrawingCanvas.Width - scaledWidth) / 2);
                            Canvas.SetTop(img, (DrawingCanvas.Height - scaledHeight) / 2);
                        }
                    }

                    DrawingCanvas.Children.Add(img);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"背景图片加载失败: {ex.Message}\n请检查图片文件是否存在。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // 绘制所有元素
            if (_currentTemplate.Elements != null)
            {
                foreach (var elem in _currentTemplate.Elements)
                {
                    AddElementShape(elem);
                }
            }
        }

        private void AddElementShape(PanelElement elem)
        {
            Shape shape;
            Brush fill;
            Brush stroke;
            double strokeThickness = 2;

            if (elem.Type == "Button")
            {
                shape = new Rectangle();
                fill = new SolidColorBrush(Color.FromArgb(100, 0, 120, 215));
                stroke = Brushes.DodgerBlue;
            }
            else if (elem.Type == "Knob")
            {
                shape = new Ellipse();
                fill = new SolidColorBrush(Color.FromArgb(100, 255, 165, 0));
                stroke = Brushes.Orange;
            }
            else // Lamp
            {
                shape = new Ellipse();
                fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0));
                stroke = Brushes.Yellow;
            }

            shape.Width = elem.Width * PixelsPerMm;
            shape.Height = elem.Height * PixelsPerMm;
            shape.Fill = fill;
            shape.Stroke = stroke;
            shape.StrokeThickness = strokeThickness;
            shape.Tag = elem;
            shape.MouseDown += Shape_MouseDown;
            shape.MouseMove += Shape_MouseMove;
            shape.MouseUp += Shape_MouseUp;
            Canvas.SetLeft(shape, elem.X * PixelsPerMm);
            Canvas.SetTop(shape, elem.Y * PixelsPerMm);
            DrawingCanvas.Children.Add(shape);

            var text = new TextBlock
            {
                Text = elem.Id,
                Foreground = Brushes.White,
                FontSize = 12,
                Background = Brushes.Black
            };
            Canvas.SetLeft(text, elem.X * PixelsPerMm);
            Canvas.SetTop(text, elem.Y * PixelsPerMm - 15);
            DrawingCanvas.Children.Add(text);
        }

        private void Shape_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var shape = sender as Shape;
            var elem = shape.Tag as PanelElement;

            if (_isEditMode)
            {
                // 编辑模式：选中+拖拽
                _selectedElement = elem;
                _isDragging = true;
                var pos = e.GetPosition(DrawingCanvas);
                _dragOffset = new Point(pos.X - Canvas.GetLeft(shape), pos.Y - Canvas.GetTop(shape));
                shape.CaptureMouse();
                UpdatePropertyPanel();
                StatusElement.Text = $"选中: {_selectedElement.Id}";
                e.Handled = true;
            }
            else
            {
                // 只读模式：如果是按钮，执行机器人指令
                if (elem.Type == "Button")
                {
                    ExecuteRobotCommand(elem);
                }
                e.Handled = true;
            }
        }

        private void Shape_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _selectedElement == null) return;
            var shape = sender as Shape;
            var pos = e.GetPosition(DrawingCanvas);
            double newLeft = pos.X - _dragOffset.X;
            double newTop = pos.Y - _dragOffset.Y;
            newLeft = Math.Max(0, Math.Min(newLeft, DrawingCanvas.Width - shape.Width));
            newTop = Math.Max(0, Math.Min(newTop, DrawingCanvas.Height - shape.Height));

            // 对齐检测
            double alignedLeft = newLeft;
            double alignedTop = newTop;
            bool alignX = false, alignY = false;
            double guideX = 0, guideY = 0;

            foreach (var elem in _currentTemplate.Elements)
            {
                if (elem == _selectedElement) continue;
                double ex = elem.X * PixelsPerMm;
                double ey = elem.Y * PixelsPerMm;
                double ew = elem.Width * PixelsPerMm;
                double eh = elem.Height * PixelsPerMm;

                if (Math.Abs(newLeft - ex) < AlignThreshold) { alignedLeft = ex; alignX = true; guideX = ex; }
                if (Math.Abs(newLeft + shape.Width - (ex + ew)) < AlignThreshold) { alignedLeft = ex + ew - shape.Width; alignX = true; guideX = ex + ew; }
                if (Math.Abs(newLeft + shape.Width / 2 - (ex + ew / 2)) < AlignThreshold) { alignedLeft = ex + ew / 2 - shape.Width / 2; alignX = true; guideX = ex + ew / 2; }
                if (Math.Abs(newTop - ey) < AlignThreshold) { alignedTop = ey; alignY = true; guideY = ey; }
                if (Math.Abs(newTop + shape.Height - (ey + eh)) < AlignThreshold) { alignedTop = ey + eh - shape.Height; alignY = true; guideY = ey + eh; }
                if (Math.Abs(newTop + shape.Height / 2 - (ey + eh / 2)) < AlignThreshold) { alignedTop = ey + eh / 2 - shape.Height / 2; alignY = true; guideY = ey + eh / 2; }
            }

            newLeft = alignedLeft;
            newTop = alignedTop;

            if (alignX)
            {
                GuideVertical.X1 = guideX; GuideVertical.Y1 = 0; GuideVertical.X2 = guideX; GuideVertical.Y2 = DrawingCanvas.Height;
                GuideVertical.Visibility = Visibility.Visible;
            }
            else GuideVertical.Visibility = Visibility.Collapsed;

            if (alignY)
            {
                GuideHorizontal.X1 = 0; GuideHorizontal.Y1 = guideY; GuideHorizontal.X2 = DrawingCanvas.Width; GuideHorizontal.Y2 = guideY;
                GuideHorizontal.Visibility = Visibility.Visible;
            }
            else GuideHorizontal.Visibility = Visibility.Collapsed;

            Canvas.SetLeft(shape, newLeft);
            Canvas.SetTop(shape, newTop);
            _selectedElement.X = newLeft / PixelsPerMm;
            _selectedElement.Y = newTop / PixelsPerMm;

            var text = DrawingCanvas.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == _selectedElement.Id);
            if (text != null)
            {
                Canvas.SetLeft(text, newLeft);
                Canvas.SetTop(text, newTop - 15);
            }

            StatusCoord.Text = $"坐标: ({_selectedElement.X:F1}, {_selectedElement.Y:F1}) mm";
            e.Handled = true;
        }

        private void Shape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var shape = sender as Shape;
            shape.ReleaseMouseCapture();
            _isDragging = false;
            GuideVertical.Visibility = Visibility.Collapsed;
            GuideHorizontal.Visibility = Visibility.Collapsed;
        }

        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DrawingCanvas)
            {
                _selectedElement = null;
                UpdatePropertyPanel();
                StatusElement.Text = "选中: 无";
            }
        }

        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e) { }
        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e) { }

        // 更新编辑模式UI
        private void UpdateEditModeUI()
        {
            if (_isEditMode)
            {
                BtnToggleEditMode.Content = "🔒 只读模式";
                BtnToggleEditMode.Background = Brushes.LightGreen;
                TxtMode.Text = "编辑模式";
                TxtMode.Foreground = Brushes.Green;
                BtnAddElement.IsEnabled = true;
            }
            else
            {
                BtnToggleEditMode.Content = "🔓 编辑模式";
                BtnToggleEditMode.Background = Brushes.Orange;
                TxtMode.Text = "只读模式（点击按钮执行）";
                TxtMode.Foreground = Brushes.Red;
                BtnAddElement.IsEnabled = false;
            }
        }

        // 模式切换按钮点击事件
        private void BtnToggleEditMode_Click(object sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            UpdateEditModeUI();
        }

        // 执行机器人指令
        private async void ExecuteRobotCommand(PanelElement button)
        {
            string taskName = button.Parameters?.ContainsKey("TaskName") == true
                ? button.Parameters["TaskName"]?.ToString()
                : null;
            string robotIP = button.Parameters?.ContainsKey("RobotIP") == true
                ? button.Parameters["RobotIP"]?.ToString()
                : null;

            if (string.IsNullOrEmpty(taskName))
            {
                MessageBox.Show("按钮未配置任务名称，请在属性面板中设置", "参数缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtStatus.Text = $"正在执行 {button.Id} ...";
            StatusElement.Text = $"执行中: {button.Id}";

            try
            {
                using (var service = new ElibotRobotService())
                {
                    var (taskSuccess, status) = await service.RunTaskAndGetStatusAsync(taskName, robotIP);
                    string resultMsg = taskSuccess ? $"✅ 任务启动成功，状态：{status}" : $"❌ 任务启动失败，状态：{status}";
                    MessageBox.Show(resultMsg, "执行结果", MessageBoxButton.OK,
                        taskSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    TxtStatus.Text = taskSuccess ? "执行完成" : "执行失败";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"执行异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "执行异常";
            }
            finally
            {
                StatusElement.Text = "选中: 无";
            }
        }

        private void UpdatePropertyPanel()
        {
            TemplatePropertyPanel.Children.Clear();
            if (_currentTemplate != null)
            {
                AddPropertyRow(TemplatePropertyPanel, "模板名称", _currentTemplate.Name,
                    (s) => _currentTemplate.Name = s, false);
                AddPropertyRow(TemplatePropertyPanel, "品牌", _currentTemplate.Brand ?? "",
                    (s) => _currentTemplate.Brand = s, false);
                AddPropertyRow(TemplatePropertyPanel, "型号", _currentTemplate.Model ?? "",
                    (s) => _currentTemplate.Model = s, false);

                // 背景图片设置按钮
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                var btnSelectImage = new Button { Content = "选择背景图片", Width = 120, Margin = new Thickness(0, 0, 5, 0) };
                btnSelectImage.Click += BtnSelectImage_Click;
                var btnClearImage = new Button { Content = "清除背景", Width = 80 };
                btnClearImage.Click += BtnClearImage_Click;
                btnPanel.Children.Add(btnSelectImage);
                btnPanel.Children.Add(btnClearImage);
                TemplatePropertyPanel.Children.Add(btnPanel);

                // 图片模式选择
                var modePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                modePanel.Children.Add(new TextBlock { Text = "图片模式:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                _modeCombo = new ComboBox { Width = 150 };
                _modeCombo.Items.Add(new ComboBoxItem { Content = "拉伸填充", Tag = "Fill" });
                _modeCombo.Items.Add(new ComboBoxItem { Content = "等比例完整缩放", Tag = "Uniform" });
                _modeCombo.SelectionChanged += (s, e) =>
                {
                    if (_modeCombo.SelectedItem is ComboBoxItem item)
                    {
                        _currentTemplate.BackgroundImageMode = item.Tag.ToString();
                        RedrawCanvas();
                        UpdateScrollBars();
                    }
                };
                if (_currentTemplate.BackgroundImageMode == "Fill")
                    _modeCombo.SelectedIndex = 0;
                else
                    _modeCombo.SelectedIndex = 1;

                modePanel.Children.Add(_modeCombo);
                TemplatePropertyPanel.Children.Add(modePanel);

                // 自定义尺寸复选框
                var customPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                _chkCustomSize = new CheckBox { Content = "自定义图片尺寸", IsChecked = _currentTemplate.ImageWidth.HasValue && _currentTemplate.ImageHeight.HasValue, VerticalAlignment = VerticalAlignment.Center, Width = 120 };
                _chkCustomSize.Checked += (s, e) => EnableCustomSize(true);
                _chkCustomSize.Unchecked += (s, e) => EnableCustomSize(false);
                customPanel.Children.Add(_chkCustomSize);
                TemplatePropertyPanel.Children.Add(customPanel);

                // 宽度输入
                var widthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                widthPanel.Children.Add(new TextBlock { Text = "宽度(px):", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                _txtImageWidth = new TextBox { Width = 150, Text = _currentTemplate.ImageWidth?.ToString() ?? "" };
                _txtImageWidth.LostFocus += (s, e) => UpdateCustomSize();
                _txtImageWidth.KeyDown += (s, e) => { if (e.Key == Key.Enter) UpdateCustomSize(); };
                widthPanel.Children.Add(_txtImageWidth);
                TemplatePropertyPanel.Children.Add(widthPanel);

                // 高度输入
                var heightPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                heightPanel.Children.Add(new TextBlock { Text = "高度(px):", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                _txtImageHeight = new TextBox { Width = 150, Text = _currentTemplate.ImageHeight?.ToString() ?? "" };
                _txtImageHeight.LostFocus += (s, e) => UpdateCustomSize();
                _txtImageHeight.KeyDown += (s, e) => { if (e.Key == Key.Enter) UpdateCustomSize(); };
                heightPanel.Children.Add(_txtImageHeight);
                TemplatePropertyPanel.Children.Add(heightPanel);

                EnableCustomSize(_chkCustomSize.IsChecked == true);

                // 背景色设置
                var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                colorPanel.Children.Add(new TextBlock { Text = "背景色:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                var colorBox = new TextBox { Text = _currentTemplate.BackgroundColor, Width = 150 };
                colorBox.LostFocus += (s, e) => { _currentTemplate.BackgroundColor = colorBox.Text; RedrawCanvas(); };
                colorBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { _currentTemplate.BackgroundColor = colorBox.Text; RedrawCanvas(); } };
                colorPanel.Children.Add(colorBox);
                TemplatePropertyPanel.Children.Add(colorPanel);

                var presetPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                presetPanel.Children.Add(new TextBlock { Text = "预设:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                var btnDark = new Button { Content = "深灰", Width = 40, Margin = new Thickness(0, 0, 5, 0) };
                btnDark.Click += (s, e) => { colorBox.Text = "#FF1E1E1E"; _currentTemplate.BackgroundColor = "#FF1E1E1E"; RedrawCanvas(); };
                var btnBlack = new Button { Content = "黑色", Width = 40, Margin = new Thickness(0, 0, 5, 0) };
                btnBlack.Click += (s, e) => { colorBox.Text = "#FF000000"; _currentTemplate.BackgroundColor = "#FF000000"; RedrawCanvas(); };
                var btnWhite = new Button { Content = "白色", Width = 40 };
                btnWhite.Click += (s, e) => { colorBox.Text = "#FFFFFFFF"; _currentTemplate.BackgroundColor = "#FFFFFFFF"; RedrawCanvas(); };
                presetPanel.Children.Add(btnDark);
                presetPanel.Children.Add(btnBlack);
                presetPanel.Children.Add(btnWhite);
                TemplatePropertyPanel.Children.Add(presetPanel);
            }

            // 元素属性编辑
            if (_selectedElement == null)
            {
                NoSelectionPanel.Visibility = Visibility.Visible;
                ElementPropertyPanel.Visibility = Visibility.Collapsed;
                return;
            }

            NoSelectionPanel.Visibility = Visibility.Collapsed;
            ElementPropertyPanel.Visibility = Visibility.Visible;
            ElementPropertyPanel.Children.Clear();
            var elem = _selectedElement;
            AddPropertyRow(ElementPropertyPanel, "ID", elem.Id, (s) => elem.Id = s, true);
            AddPropertyRow(ElementPropertyPanel, "类型", elem.Type, null, true);
            AddPropertyRow(ElementPropertyPanel, "X (mm)", elem.X.ToString("F1"), (s) => { if (double.TryParse(s, out double v)) { elem.X = v; RedrawCanvas(); } });
            AddPropertyRow(ElementPropertyPanel, "Y (mm)", elem.Y.ToString("F1"), (s) => { if (double.TryParse(s, out double v)) { elem.Y = v; RedrawCanvas(); } });
            AddPropertyRow(ElementPropertyPanel, "宽度 (mm)", elem.Width.ToString("F1"), (s) => { if (double.TryParse(s, out double v)) { elem.Width = v; RedrawCanvas(); } });
            AddPropertyRow(ElementPropertyPanel, "高度 (mm)", elem.Height.ToString("F1"), (s) => { if (double.TryParse(s, out double v)) { elem.Height = v; RedrawCanvas(); } });

            if (elem.Type == "Button")
            {
                AddPropertyRow(ElementPropertyPanel, "压力 (N)", elem.Pressure?.ToString("F1") ?? "1.0", (s) => { if (double.TryParse(s, out double v)) elem.Pressure = v; });
                AddPropertyRow(ElementPropertyPanel, "时长 (ms)", elem.PressDuration?.ToString() ?? "500", (s) => { if (int.TryParse(s, out int v)) elem.PressDuration = v; });
                AddPropertyRow(ElementPropertyPanel, "任务名称",
                    elem.Parameters?.ContainsKey("TaskName") == true ? elem.Parameters["TaskName"].ToString() : "",
                    (s) => {
                        if (elem.Parameters == null) elem.Parameters = new Dictionary<string, object>();
                        elem.Parameters["TaskName"] = s;
                    });
                AddPropertyRow(ElementPropertyPanel, "机器人IP",
                    elem.Parameters?.ContainsKey("RobotIP") == true ? elem.Parameters["RobotIP"].ToString() : "",
                    (s) => {
                        if (elem.Parameters == null) elem.Parameters = new Dictionary<string, object>();
                        elem.Parameters["RobotIP"] = s;
                    });
            }
            else if (elem.Type == "Knob")
            {
                AddPropertyRow(ElementPropertyPanel, "角度 (°)", elem.Angle?.ToString("F1") ?? "90", (s) => { if (double.TryParse(s, out double v)) elem.Angle = v; });
                AddPropertyRow(ElementPropertyPanel, "扭矩 (N·m)", elem.Torque?.ToString("F1") ?? "1.0", (s) => { if (double.TryParse(s, out double v)) elem.Torque = v; });
            }
            else if (elem.Type == "Lamp")
            {
                AddPropertyRow(ElementPropertyPanel, "颜色 (HEX)", elem.Color ?? "#ffff00", (s) => elem.Color = s);
            }

            var btnDeleteElem = new Button { Content = "删除元素", Margin = new Thickness(0, 10, 0, 0), Background = Brushes.Red, Foreground = Brushes.White };
            btnDeleteElem.Click += (s, e) => DeleteSelectedElement();
            ElementPropertyPanel.Children.Add(btnDeleteElem);
        }

        private void EnableCustomSize(bool enable)
        {
            _txtImageWidth.IsEnabled = enable;
            _txtImageHeight.IsEnabled = enable;
            _modeCombo.IsEnabled = !enable;
            if (!enable)
            {
                _currentTemplate.ImageWidth = null;
                _currentTemplate.ImageHeight = null;
                RedrawCanvas();
                UpdateScrollBars();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_txtImageWidth.Text))
                    _txtImageWidth.Text = DrawingCanvas.Width.ToString();
                if (string.IsNullOrWhiteSpace(_txtImageHeight.Text))
                    _txtImageHeight.Text = DrawingCanvas.Height.ToString();
                UpdateCustomSize();
            }
        }

        private void UpdateCustomSize()
        {
            if (!_chkCustomSize.IsChecked.HasValue || _chkCustomSize.IsChecked.Value == false) return;
            if (double.TryParse(_txtImageWidth.Text, out double w) && double.TryParse(_txtImageHeight.Text, out double h))
            {
                _currentTemplate.ImageWidth = w;
                _currentTemplate.ImageHeight = h;
                RedrawCanvas();
                UpdateScrollBars();
            }
        }

        private void UpdateScrollBars()
        {
            if (_currentTemplate == null) return;

            if (_currentTemplate.ImageWidth.HasValue && _currentTemplate.ImageHeight.HasValue)
            {
                CanvasScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                CanvasScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else
            {
                CanvasScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                CanvasScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
        }

        private void BtnSelectImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null) return;
            var openFileDialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "选择背景图片"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > MaxImageSize)
                    {
                        MessageBox.Show($"图片大小不能超过5MB。当前大小: {fileInfo.Length / 1024.0:F2}KB", "文件过大", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    string ext = fileInfo.Extension.ToLower();
                    if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".bmp" && ext != ".gif")
                    {
                        MessageBox.Show("不支持的图片格式。", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 生成唯一文件名
                    string guid = Guid.NewGuid().ToString();
                    string fileName = guid + ext;
                    string relativePath = System.IO.Path.Combine("Images", fileName);
                    string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                    // 复制文件到 Images 目录
                    File.Copy(openFileDialog.FileName, fullPath, true);

                    // 如果之前有图片，删除旧文件
                    if (!string.IsNullOrEmpty(_currentTemplate.ImagePath))
                    {
                        string oldFullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _currentTemplate.ImagePath);
                        if (File.Exists(oldFullPath))
                        {
                            try { File.Delete(oldFullPath); } catch { }
                        }
                    }

                    _currentTemplate.ImagePath = relativePath;
                    RedrawCanvas();
                    TxtStatus.Text = "背景图片已更新";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"图片加载失败: {ex.Message}");
                }
            }
        }

        private void BtnClearImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null) return;

            // 删除图片文件
            if (!string.IsNullOrEmpty(_currentTemplate.ImagePath))
            {
                string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _currentTemplate.ImagePath);
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); } catch { }
                }
            }

            _currentTemplate.ImagePath = null;
            RedrawCanvas();
            TxtStatus.Text = "背景图片已清除";
        }

        private void AddPropertyRow(StackPanel panel, string label, string initialValue, Action<string> onChanged = null, bool isReadOnly = false)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label + ":", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var textBox = new TextBox { Text = initialValue, IsReadOnly = isReadOnly, Width = 150 };
            if (onChanged != null)
            {
                textBox.LostFocus += (s, e) => onChanged(textBox.Text);
                textBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) onChanged(textBox.Text); };
            }
            row.Children.Add(textBox);
            panel.Children.Add(row);
        }

        private void DeleteSelectedElement()
        {
            if (_selectedElement == null) return;
            _currentTemplate.Elements.Remove(_selectedElement);
            _selectedElement = null;
            RefreshIdCounters();
            RedrawCanvas();
            UpdatePropertyPanel();
            StatusElement.Text = "选中: 无";
            StatusCoord.Text = "坐标: —";
        }

        private void BtnNewTemplate_Click(object sender, RoutedEventArgs e)
        {
            _currentTemplate = new Template
            {
                Id = 0,
                Name = "新模板",
                Brand = "",
                Model = "",
                BackgroundImageMode = "Fill",
                BackgroundColor = "#FF1E1E1E",
                ImageWidth = null,
                ImageHeight = null,
                ImagePath = null,
                Elements = new List<PanelElement>()
            };
            _idCounters = new Dictionary<string, int> { { "Button", 0 }, { "Knob", 0 }, { "Lamp", 0 } };
            RedrawCanvas();
            UpdatePropertyPanel();
            UpdateScrollBars();
            TxtStatus.Text = "新建模板";
            _isEditMode = true;
            UpdateEditModeUI();
        }

        private void BtnOpenTemplate_Click(object sender, RoutedEventArgs e)
        {
            LoadTemplateList();
        }

        private void BtnSaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null) return;
            try
            {
                _currentTemplate.Id = _db.SaveTemplate(_currentTemplate);
                LoadTemplateList();
                TxtStatus.Text = "保存成功";
                if (_isEditMode)
                {
                    _isEditMode = false;
                    UpdateEditModeUI();
                }
                MessageBox.Show("模板保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                // 关键：设置对话框结果为 true 并关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddElement_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
            {
                MessageBox.Show("请先新建或打开一个模板");
                return;
            }
            var selectedItem = ElemTypeCombo.SelectedItem as ComboBoxItem;
            string type = selectedItem?.Tag.ToString() ?? "Button";
            var newElem = new PanelElement
            {
                Id = GenerateId(type),
                Type = type,
                X = 5,
                Y = 5,
                Width = type == "Button" ? 2 : 3,
                Height = type == "Button" ? 2 : 3,
            };
            if (type == "Button")
            {
                newElem.Pressure = 1.0;
                newElem.PressDuration = 500;
            }
            else if (type == "Knob")
            {
                newElem.Angle = 90;
                newElem.Torque = 1.0;
            }
            else if (type == "Lamp")
            {
                newElem.Color = "#ffff00";
            }
            _currentTemplate.Elements.Add(newElem);
            AddElementShape(newElem);
            TxtStatus.Text = $"添加了 {newElem.Id}";
        }

        private void BtnDeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null || _currentTemplate.Id == 0)
            {
                MessageBox.Show("没有可删除的模板（新建未保存）");
                return;
            }
            if (MessageBox.Show($"确定要删除模板“{_currentTemplate.Name}”吗？此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    _db.DeleteTemplate(_currentTemplate.Id);
                    _currentTemplate = null;
                    _selectedElement = null;
                    RedrawCanvas();
                    UpdatePropertyPanel();
                    LoadTemplateList();
                    TxtStatus.Text = "模板已删除";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}